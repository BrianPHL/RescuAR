import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
import test from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';

const validationRoot = process.env.RESCUAR_AUDIT_VALIDATION_ROOT
  || fileURLToPath(new URL('../../node_modules/.cache/rescuar-audit-validation/', import.meta.url));
const { PGlite } = createRequire(pathToFileURL(resolve(validationRoot, 'package.json')))('@electric-sql/pglite');
const migration = await readFile(new URL('../migrations/001_web_audit_foundation.sql', import.meta.url), 'utf8');
const baseline = await readFile(new URL('./fixtures/step1-baseline.sql', import.meta.url), 'utf8');
const verification = await readFile(new URL('../verify-web-audit-foundation.sql', import.meta.url), 'utf8');
const admin = '00000000-0000-0000-0000-000000000001';
const resident = '00000000-0000-0000-0000-000000000002';
const other = '00000000-0000-0000-0000-000000000003';
const missing = '00000000-0000-0000-0000-000000000099';
const operation = '10000000-0000-0000-0000-000000000001';

async function fixture(apply = true) {
  const database = new PGlite();
  try {
    await database.exec(baseline);
    await database.exec(`
      INSERT INTO auth.users(id, email) VALUES
        ('${admin}', 'FIXTURE_SECRET_ADMIN'), ('${resident}', 'FIXTURE_SECRET_EMAIL'), ('${other}', 'FIXTURE_SECRET_OTHER');
      INSERT INTO public.users(id, username, first_name, last_name, phone_number, address, allergies) VALUES
        ('${admin}', 'operator', 'Operator', 'One', '123', 'Office', 'FIXTURE_SECRET_HEALTH'),
        ('${resident}', 'resident', 'FIXTURE_SECRET_NAME', 'Resident', '456', 'FIXTURE_SECRET_ADDRESS', 'FIXTURE_SECRET_HEALTH'),
        ('${other}', 'other', 'Other', 'User', '789', 'Other address', NULL);
      INSERT INTO public.admin_roles(user_id) VALUES ('${admin}'), ('${admin}');
    `);
    if (apply) await database.exec(migration);
    return database;
  } catch (error) {
    await database.close();
    throw error;
  }
}

async function asActor(database, role, actor, task) {
  assert.ok(['anon', 'authenticated', 'service_role'].includes(role));
  await database.exec(`SET ROLE ${role}`);
  await database.query("SELECT set_config('request.jwt.claim.sub', $1, false)", [actor || '']);
  try {
    return await task();
  } finally {
    await database.exec('RESET ROLE');
    await database.query("SELECT set_config('request.jwt.claim.sub', '', false)");
  }
}

const rejectsCode = (request, code) => assert.rejects(request, (error) => error.code === code);
const events = async (database) => (await database.query('SELECT * FROM audit_private.events ORDER BY recorded_at, id')).rows;
const profile = async (database, id = resident) => (await database.query('SELECT * FROM public.users WHERE id = $1', [id])).rows[0];
const record = (database, event = 'module.opened', module = 'dashboard', details = {}, op = operation) => database.query(
  'SELECT public.record_web_audit_event($1, $2, $3, NULL, NULL, $4) AS id', [op, event, module, JSON.stringify(details)]
);

test('migration rejects schema drift and repeat application without partial changes', async () => {
  const database = await fixture(false);
  try {
    await database.exec("ALTER FUNCTION public.admin_delete_user(uuid) SET search_path = 'public'");
    await assert.rejects(database.exec(migration), /Inspected RPC definition changed/);
    await database.exec('ROLLBACK');
    assert.equal((await database.query("SELECT to_regnamespace('audit_private') AS schema")).rows[0].schema, null);
    assert.equal((await database.query("SELECT has_function_privilege('anon', 'public.admin_delete_user(uuid)', 'EXECUTE') AS allowed")).rows[0].allowed, true);
  } finally {
    await database.close();
  }
  const applied = await fixture();
  try {
    await assert.rejects(applied.exec(migration), /already exists or names conflict/);
    await applied.exec('ROLLBACK');
    assert.equal((await events(applied)).length, 0);
  } finally {
    await applied.close();
  }
});

test('anonymous, non-admin, missing-identity and service callers cannot use privileged entry points', async () => {
  const database = await fixture();
  try {
    const calls = [
      'SELECT public.get_all_users_with_roles()',
      `SELECT public.admin_update_user('${resident}', '{"first_name":"Changed"}')`,
      `SELECT public.admin_toggle_suspend_user('${resident}', 'suspended')`,
      `SELECT public.admin_delete_user('${resident}')`,
      'SELECT public.get_web_audit_logs()',
      `SELECT public.record_web_audit_event('${operation}', 'module.opened', 'dashboard')`
    ];
    const before = await profile(database);
    for (const [role, actor] of [['anon', null], ['authenticated', resident], ['authenticated', null], ['service_role', null]]) {
      await asActor(database, role, actor, async () => {
        for (const sql of calls) await rejectsCode(database.query(sql), '42501');
      });
    }
    await asActor(database, 'authenticated', resident, async () => {
      assert.equal((await database.query('SELECT public.web_admin_access() AS allowed')).rows[0].allowed, false);
    });
    await asActor(database, 'authenticated', admin, async () => {
      assert.equal((await database.query('SELECT public.web_admin_access() AS allowed')).rows[0].allowed, true);
      for (const sql of [
        'SELECT * FROM audit_private.events',
        "INSERT INTO audit_private.events DEFAULT VALUES",
        "UPDATE audit_private.events SET details = '{}'",
        'DELETE FROM audit_private.events', 'TRUNCATE audit_private.events',
        'SELECT audit_private.require_admin()', 'TRUNCATE public.admin_roles'
      ]) await rejectsCode(database.query(sql), '42501');
    });
    assert.deepEqual(await profile(database), before);
    assert.equal((await events(database)).length, 0);
  } finally {
    await database.close();
  }
});

test('migration rejects changed user columns or RPC owners before creating audit objects', async () => {
  for (const drift of ['ALTER TABLE public.users DROP COLUMN status',
    'ALTER FUNCTION public.admin_delete_user(uuid) OWNER TO service_role']) {
    const database = await fixture(false);
    try {
      await database.exec(drift);
      await assert.rejects(database.exec(migration), /contract changed|definition changed/);
      await database.exec('ROLLBACK');
      assert.equal((await database.query("SELECT to_regnamespace('audit_private') AS schema")).rows[0].schema, null);
    } finally {
      await database.close();
    }
  }
});

test('client events use server identity/time, reject raw content and trusted claims, and deduplicate retries', async () => {
  const database = await fixture();
  try {
    await asActor(database, 'authenticated', admin, async () => {
      const first = await record(database, 'records.queried', 'dashboard', { page: 1, filter_applied: true });
      const retry = await record(database, 'records.queried', 'dashboard', { filter_applied: true, page: 1 });
      assert.equal(first.rows[0].id, retry.rows[0].id);
      await rejectsCode(record(database, 'records.queried', 'dashboard', { page: 2 }), '22023');
      await rejectsCode(record(database, 'user.deleted'), '22023');
      for (const details of [{ actor_id: resident }, { source: 'database' }, { recorded_at: '2000-01-01' },
        { raw_query: 'FIXTURE_SECRET_NAME' }, { page: -1 }, { page: 1.5 }, { page: null },
        { mode: 'unknown' }, { filter_applied: 'yes' }, { template_id: { nested: 'value' } }]) {
        await rejectsCode(record(database, 'module.opened', 'dashboard', details), '22023');
      }
      await rejectsCode(database.query(`SELECT public.record_web_audit_event(
        '${operation}', 'module.opened', 'dashboard', p_actor_id => '${resident}'::uuid)`), '42883');
      await rejectsCode(record(database, 'module.opened', 'invalid-module'), '23514');
      await rejectsCode(record(database, 'module.opened', 'dashboard', {}, null), '22023');
    });
    const rows = await events(database);
    assert.equal(rows.length, 1);
    assert.equal(rows[0].actor_id, admin);
    assert.equal(rows[0].source, 'client_observed');
    assert.equal(rows[0].outcome, 'observed');
    assert.ok(Number.isFinite(Date.parse(rows[0].recorded_at)));
    assert.ok(!JSON.stringify(rows).includes('FIXTURE_SECRET'));
  } finally {
    await database.close();
  }
});

test('guarded user RPCs retain their contracts and record redacted confirmed outcomes', async () => {
  const database = await fixture();
  try {
    await asActor(database, 'authenticated', admin, async () => {
      const directory = (await database.query('SELECT * FROM public.get_all_users_with_roles()')).rows;
      assert.equal(directory.length, 3, 'duplicate membership must not duplicate directory rows');
      assert.equal(directory.find((user) => user.id === admin).role, 'admin');
      assert.equal(directory.find((user) => user.id === resident).email, 'FIXTURE_SECRET_EMAIL');
      assert.equal(Object.hasOwn(directory[0], 'status'), false, 'existing return shape stays compatible');
      await database.query('SELECT public.admin_update_user($1, $2)', [resident, '{"first_name":"FIXTURE_SECRET_NEW_NAME"}']);
      await database.query('SELECT public.admin_update_user($1, $2)', [resident, '{}']);
      await database.query('SELECT public.admin_toggle_suspend_user($1, $2)', [resident, 'suspended']);
      await database.query('SELECT public.admin_toggle_suspend_user($1, $2)', [resident, 'suspended']);
      await rejectsCode(database.query('SELECT public.admin_update_user($1, $2)', [resident, '{"status":"active"}']), '22023');
      await rejectsCode(database.query('SELECT public.admin_update_user($1, $2)', [resident, '{"first_name":123}']), '22023');
      await rejectsCode(database.query('SELECT public.admin_toggle_suspend_user($1, $2)', [resident, 'unsupported']), '22023');
      await rejectsCode(database.query('SELECT public.admin_update_user($1, $2)', [missing, '{}']), 'P0002');
      await rejectsCode(database.query('SELECT public.admin_delete_user($1)', [missing]), 'P0002');
    });
    const updated = await profile(database);
    assert.equal(updated.first_name, 'FIXTURE_SECRET_NEW_NAME');
    assert.equal(updated.last_name, 'Resident', 'omitted fields must be preserved');
    assert.equal(updated.address, 'FIXTURE_SECRET_ADDRESS');
    assert.equal(updated.status, 'suspended');
    let rows = await events(database);
    assert.equal(rows.length, 5);
    assert.deepEqual(rows.map((event) => event.outcome), ['succeeded', 'succeeded', 'no_change', 'succeeded', 'no_change']);
    assert.deepEqual(rows[1].details, { changed_fields: ['first_name'] });
    assert.deepEqual(rows[3].details, { before_status: 'active', after_status: 'suspended' });
    assert.equal(rows[0].details.returned_count, 3);
    assert.ok(rows.every((event) => event.source === 'database' && event.actor_id === admin));
    assert.ok(!JSON.stringify(rows).includes('FIXTURE_SECRET'));
    await asActor(database, 'authenticated', admin, () => database.query('SELECT public.admin_delete_user($1)', [resident]));
    assert.equal(await profile(database), undefined);
    rows = await events(database);
    assert.equal(rows.at(-1).event_type, 'user.deleted');
    assert.equal(rows.at(-1).target_id, resident);
    assert.equal(rows.length, 6, 'prior target history survives cascading account deletion');
  } finally {
    await database.close();
  }
});

test('audit failures roll back mutations, cascades and directory access instead of silently losing evidence', async () => {
  const database = await fixture();
  try {
    const before = await profile(database);
    await database.exec("ALTER TABLE audit_private.events ADD CONSTRAINT fixture_reject CHECK (false) NOT VALID");
    await asActor(database, 'authenticated', admin, async () => {
      await rejectsCode(database.query('SELECT public.admin_update_user($1, $2)', [resident, '{"first_name":"Changed"}']), '23514');
      await rejectsCode(database.query('SELECT public.admin_toggle_suspend_user($1, $2)', [resident, 'suspended']), '23514');
      await rejectsCode(database.query('SELECT public.admin_delete_user($1)', [resident]), '23514');
      await rejectsCode(database.query('SELECT * FROM public.get_all_users_with_roles()'), '23514');
    });
    assert.deepEqual(await profile(database), before);
    assert.equal((await database.query('SELECT count(*)::int AS count FROM auth.users')).rows[0].count, 3);
    assert.equal((await events(database)).length, 0);
    await database.exec('ALTER TABLE audit_private.events DROP CONSTRAINT fixture_reject');
    await asActor(database, 'authenticated', admin, async () => {
      await database.exec('BEGIN');
      await database.query('SELECT public.admin_update_user($1, $2)', [resident, '{"first_name":"Changed"}']);
      await database.exec('ROLLBACK');
      await rejectsCode(database.query('SELECT public.admin_delete_user($1)', [admin]), '23503');
    });
    assert.deepEqual(await profile(database), before);
    assert.equal((await events(database)).length, 0);
  } finally {
    await database.close();
  }
});

test('history resists accidental owner edits and survives deletion/revocation of its actor', async () => {
  const database = await fixture();
  try {
    await asActor(database, 'authenticated', admin, () => record(database));
    const before = await events(database);
    for (const sql of ["UPDATE audit_private.events SET outcome = 'unknown'", 'DELETE FROM audit_private.events', 'TRUNCATE audit_private.events']) {
      await rejectsCode(database.query(sql), '55000');
    }
    await database.query('DELETE FROM public.admin_roles WHERE user_id = $1', [admin]);
    await database.query('DELETE FROM auth.users WHERE id = $1', [admin]);
    assert.deepEqual(await events(database), before);
    await asActor(database, 'authenticated', admin, async () => {
      await rejectsCode(database.query('SELECT * FROM public.get_web_audit_logs()'), '42501');
    });
    await database.query('INSERT INTO public.admin_roles(user_id) VALUES ($1)', [other]);
    await asActor(database, 'authenticated', other, async () => {
      assert.equal((await database.query('SELECT * FROM public.get_web_audit_logs()')).rows[0].actor_id, admin);
    });
  } finally {
    await database.close();
  }
});

test('audit reader paginates tied timestamps consistently, applies filters, and never writes recursively', async () => {
  const database = await fixture();
  try {
    await database.exec(`INSERT INTO audit_private.events
      (id, recorded_at, operation_id, actor_id, event_type, module, source, outcome) VALUES
      ('20000000-0000-0000-0000-000000000001', '2026-10-01', gen_random_uuid(), '${admin}', 'module.opened', 'dashboard', 'client_observed', 'observed'),
      ('20000000-0000-0000-0000-000000000002', '2026-10-01', gen_random_uuid(), '${admin}', 'records.queried', 'dashboard', 'client_observed', 'observed'),
      ('20000000-0000-0000-0000-000000000003', '2026-10-02', gen_random_uuid(), '${other}', 'module.opened', 'documentation', 'client_observed', 'observed');`);
    await asActor(database, 'authenticated', admin, async () => {
      await database.exec('BEGIN READ ONLY');
      const first = (await database.query('SELECT * FROM public.get_web_audit_logs(2)')).rows;
      assert.deepEqual(first.map((event) => event.id.slice(-1)), ['3', '2']);
      const second = (await database.query('SELECT * FROM public.get_web_audit_logs(2, $1, $2)', [first[1].recorded_at, first[1].id])).rows;
      assert.deepEqual(second.map((event) => event.id.slice(-1)), ['1']);
      assert.equal((await database.query("SELECT * FROM public.get_web_audit_logs(p_module => 'dashboard', p_actor_id => $1, p_event_type => 'records.queried')", [admin])).rows.length, 1);
      await database.exec('ROLLBACK');
      for (const limit of [null, 0, -1, 201]) {
        await rejectsCode(database.query('SELECT * FROM public.get_web_audit_logs($1)', [limit]), '22023');
      }
      await rejectsCode(database.query('SELECT * FROM public.get_web_audit_logs(p_before_id => $1)', [operation]), '22023');
    });
    assert.equal((await events(database)).length, 3);
  } finally {
    await database.close();
  }
});

test('verification reads catalogs only and detects missing installation or a changed guard', async () => {
  const database = await fixture(false);
  try {
    const inspect = async () => {
      await database.exec('BEGIN READ ONLY');
      try {
        return JSON.parse((await database.query(verification)).rows[0].audit_foundation_report);
      } finally {
        await database.exec('ROLLBACK');
      }
    };
    assert.equal((await inspect()).all_checks_passed, false);
    await database.exec(migration);
    await asActor(database, 'authenticated', admin, () => record(database));
    const before = await events(database);
    const report = await inspect();
    assert.equal(report.all_checks_passed, true, JSON.stringify(report.checks));
    assert.equal(Object.keys(report.checks).length, 9);
    assert.ok(!JSON.stringify(report).includes('FIXTURE_SECRET'));
    assert.deepEqual(await events(database), before);
    await database.exec('CREATE OR REPLACE FUNCTION public.web_admin_access() RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER SET search_path = \'\' AS $$ SELECT true $$');
    assert.equal((await inspect()).checks.functions_match_reviewed_foundation, false);
  } finally {
    await database.close();
  }
});
