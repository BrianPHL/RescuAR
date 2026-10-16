import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
import test from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';

const validationRoot = process.env.RESCUAR_AUDIT_VALIDATION_ROOT
  || fileURLToPath(new URL('../../node_modules/.cache/rescuar-audit-validation/', import.meta.url));
const { PGlite } = createRequire(pathToFileURL(resolve(validationRoot, 'package.json')))('@electric-sql/pglite');
const readSql = (path) => readFile(new URL(path, import.meta.url), 'utf8');
const baseline = await readSql('./fixtures/step1-baseline.sql');
const reportBaseline = await readSql('./fixtures/report-moderation-baseline.sql');
const foundation = await readSql('../migrations/001_web_audit_foundation.sql');
const migration = await readSql('../migrations/002_report_moderation_audit.sql');
const foundationVerification = await readSql('../verify-web-audit-foundation.sql');
const moderationVerification = await readSql('../verify-report-moderation-audit.sql');
const inspection = await readSql('../inspect-report-moderation-prerequisites.sql');
const admin = '00000000-0000-0000-0000-000000000001';
const resident = '00000000-0000-0000-0000-000000000002';
const otherAdmin = '00000000-0000-0000-0000-000000000003';
const operation = '10000000-0000-0000-0000-000000000001';
const operation2 = '10000000-0000-0000-0000-000000000002';
const reportId = 'report-001';

async function fixture(apply = true) {
  const database = new PGlite();
  try {
    await database.exec(baseline);
    await database.exec(foundation);
    await database.exec(reportBaseline);
    await database.query('INSERT INTO auth.users(id) VALUES ($1), ($2), ($3)', [admin, resident, otherAdmin]);
    await database.query('INSERT INTO public.admin_roles(user_id) VALUES ($1), ($1), ($2)', [admin, otherAdmin]);
    await database.query(`INSERT INTO public.community_reports(id, title, description, address, media_url, comments_json)
      VALUES ($1, 'FIXTURE_SECRET_TITLE', 'FIXTURE_SECRET_DESCRIPTION', 'FIXTURE_SECRET_ADDRESS',
        'FIXTURE_SECRET_MEDIA', 'FIXTURE_SECRET_COMMENT'), ('report-002', 'Other fixture', NULL, NULL, NULL, '[]')`, [reportId]);
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

const moderate = (database, expected = 'Pending', next = 'Approved', op = operation, id = reportId) =>
  database.query('SELECT * FROM public.admin_moderate_report($1, $2, $3, $4)', [op, id, expected, next]);
const rejectsCode = (promise, code) => assert.rejects(promise, (error) => error.code === code);
const events = async (database) => (await database.query('SELECT * FROM audit_private.events ORDER BY recorded_at, id')).rows;
const reportRow = async (database, id = reportId) =>
  (await database.query('SELECT * FROM public.community_reports WHERE id = $1', [id])).rows[0];

test('moderation migration rejects missing foundation, drift and repeated installation without partial changes', async () => {
  const empty = new PGlite();
  try {
    await assert.rejects(empty.exec(migration), /Report table contract/);
    await empty.exec('ROLLBACK');
    assert.equal((await empty.query("SELECT to_regprocedure('public.admin_moderate_report(uuid,text,text,text)') AS rpc")).rows[0].rpc, null);
  } finally {
    await empty.close();
  }
  const database = await fixture(false);
  try {
    await database.exec('ALTER TABLE public.community_reports ALTER COLUMN status TYPE varchar');
    await assert.rejects(database.exec(migration), /Report table contract/);
    await database.exec('ROLLBACK');
    await database.exec('ALTER TABLE public.community_reports ALTER COLUMN status TYPE text');
    await database.exec("ALTER FUNCTION audit_private.require_admin() SET search_path = 'public'");
    await assert.rejects(database.exec(migration), /Reviewed audit helper changed/);
    await database.exec('ROLLBACK');
    await database.exec("ALTER FUNCTION audit_private.require_admin() SET search_path = ''");
    await database.exec(migration);
    await assert.rejects(database.exec(migration), /already exists or name conflicts/);
    await database.exec('ROLLBACK');
    assert.equal((await events(database)).length, 0);
  } finally {
    await database.close();
  }
});

test('moderation RPC denies anonymous, resident, missing identity, service and revoked administrators', async () => {
  const database = await fixture();
  try {
    const before = await reportRow(database);
    for (const [role, actor] of [['anon', null], ['anon', admin], ['authenticated', resident],
      ['authenticated', null], ['service_role', admin]]) {
      await asActor(database, role, actor, () => rejectsCode(moderate(database), '42501'));
    }
    await database.query('DELETE FROM public.admin_roles WHERE user_id = $1', [admin]);
    await asActor(database, 'authenticated', admin, () => rejectsCode(moderate(database), '42501'));
    assert.deepEqual(await reportRow(database), before);
    assert.deepEqual(await events(database), []);
  } finally {
    await database.close();
  }
});

test('approve, reject, resolve and unchanged decisions return redacted database receipts', async () => {
  const database = await fixture();
  try {
    const before = await reportRow(database);
    await asActor(database, 'authenticated', admin, async () => {
      const receipt = (await moderate(database)).rows[0];
      assert.equal(receipt.report_id, reportId);
      assert.equal(receipt.before_status, 'Pending');
      assert.equal(receipt.after_status, 'Approved');
      assert.equal(receipt.outcome, 'succeeded');
      assert.ok(receipt.audit_event_id);
      assert.ok(Number.isFinite(Date.parse(receipt.recorded_at)));
      await moderate(database, 'Approved', 'Resolved', operation2);
      await moderate(database, 'Resolved', 'Rejected', '10000000-0000-0000-0000-000000000003');
      await moderate(database, 'Rejected', 'Rejected', '10000000-0000-0000-0000-000000000004');
    });
    assert.deepEqual(await reportRow(database), { ...before, status: 'Rejected' });
    const history = await events(database);
    assert.deepEqual(history.map((event) => event.event_type), ['report.approved', 'report.resolved', 'report.rejected', 'report.rejected']);
    assert.deepEqual(history.map((event) => event.outcome), ['succeeded', 'succeeded', 'succeeded', 'no_change']);
    assert.deepEqual(history[0].details, { before_status: 'Pending', after_status: 'Approved' });
    assert.ok(history.every((event) => event.actor_id === admin && event.source === 'database'
      && event.module === 'community-reports-moderation' && event.target_type === 'report' && event.target_id === reportId));
    assert.ok(!JSON.stringify(history).includes('FIXTURE_SECRET'));
    await database.query('UPDATE public.community_reports SET status = NULL WHERE id = $1', [reportId]);
    await asActor(database, 'authenticated', admin, () =>
      moderate(database, null, 'Approved', '10000000-0000-0000-0000-000000000005'));
    assert.equal((await events(database)).at(-1).details.before_status, null);
  } finally {
    await database.close();
  }
});

test('identical retries return the original receipt without reverting later decisions or recreating deleted reports', async () => {
  const database = await fixture();
  try {
    let first;
    await asActor(database, 'authenticated', admin, async () => {
      first = (await moderate(database)).rows;
      assert.deepEqual((await moderate(database)).rows, first);
      await moderate(database, 'Approved', 'Resolved', operation2);
      assert.deepEqual((await moderate(database)).rows, first);
    });
    assert.equal((await reportRow(database)).status, 'Resolved');
    assert.equal((await events(database)).length, 2);
    await database.query('DELETE FROM public.community_reports WHERE id = $1', [reportId]);
    await asActor(database, 'authenticated', admin, async () => {
      assert.deepEqual((await moderate(database)).rows, first);
      await rejectsCode(moderate(database, 'Resolved', 'Rejected', '10000000-0000-0000-0000-000000000003'), 'P0002');
    });
    assert.equal(await reportRow(database), undefined);
    assert.equal((await events(database)).length, 2);
    await database.query('DELETE FROM public.admin_roles WHERE user_id = $1', [admin]);
    await database.query('DELETE FROM auth.users WHERE id = $1', [admin]);
    await asActor(database, 'authenticated', otherAdmin, async () => {
      const history = (await database.query("SELECT * FROM public.get_web_audit_logs(p_module => 'community-reports-moderation')")).rows;
      assert.equal(history.length, 2);
      assert.ok(history.every((event) => event.actor_id === admin));
    });
  } finally {
    await database.close();
  }
});

test('conflicting operation reuse, invalid values and stale decisions never mutate or create success evidence', async () => {
  const database = await fixture();
  try {
    await asActor(database, 'authenticated', admin, async () => {
      await moderate(database);
      await rejectsCode(moderate(database, 'Pending', 'Rejected'), '22023');
      await rejectsCode(moderate(database, 'Approved', 'Approved'), '22023');
      await rejectsCode(moderate(database, 'Pending', 'Approved', operation, 'report-002'), '22023');
      await rejectsCode(moderate(database, 'Pending', 'Rejected', operation2), '40001');
      await rejectsCode(moderate(database, null, 'Rejected', operation2), '40001');
      for (const args of [[null, reportId, 'Approved', 'Rejected'], [operation2, null, 'Approved', 'Rejected'],
        [operation2, 'bad/id', 'Approved', 'Rejected'], [operation2, reportId, 'FIXTURE_SECRET_STATUS', 'Rejected'],
        [operation2, reportId, 'Approved', 'Pending'], [operation2, reportId, 'Approved', null]]) {
        await rejectsCode(database.query('SELECT * FROM public.admin_moderate_report($1, $2, $3, $4)', args), '22023');
      }
      await rejectsCode(moderate(database, 'Pending', 'Approved', operation2, 'missing-report'), 'P0002');
    });
    await asActor(database, 'authenticated', otherAdmin, () => rejectsCode(moderate(database), '40001'));
    assert.equal((await reportRow(database)).status, 'Approved');
    assert.equal((await reportRow(database, 'report-002')).status, 'Pending');
    assert.equal((await events(database)).length, 1);
  } finally {
    await database.close();
  }
});

test('audit failures and transaction rollbacks keep report changes and evidence atomic', async () => {
  const database = await fixture();
  try {
    const before = await reportRow(database);
    await database.exec('ALTER TABLE audit_private.events ADD CONSTRAINT fixture_reject CHECK (false) NOT VALID');
    await asActor(database, 'authenticated', admin, () => rejectsCode(moderate(database), '23514'));
    assert.deepEqual(await reportRow(database), before);
    assert.deepEqual(await events(database), []);
    await database.exec('ALTER TABLE audit_private.events DROP CONSTRAINT fixture_reject');
    await asActor(database, 'authenticated', admin, async () => {
      await database.exec('BEGIN');
      await moderate(database);
      await database.exec('ROLLBACK');
    });
    assert.deepEqual(await reportRow(database), before);
    assert.deepEqual(await events(database), []);
    await database.exec(`CREATE FUNCTION public.fixture_suppress_change() RETURNS trigger LANGUAGE plpgsql AS $$
      BEGIN RETURN NULL; END $$;
      CREATE TRIGGER fixture_suppress BEFORE UPDATE ON public.community_reports
        FOR EACH ROW EXECUTE FUNCTION public.fixture_suppress_change();`);
    await asActor(database, 'authenticated', admin, () => rejectsCode(moderate(database), '55000'));
    assert.deepEqual(await reportRow(database), before);
    assert.deepEqual(await events(database), []);
  } finally {
    await database.close();
  }
});

test('shared policies, grants, mobile full-row writes and the installed foundation remain unchanged', async () => {
  const database = await fixture(false);
  try {
    const inspect = async () => JSON.parse((await database.query(inspection)).rows[0].report_moderation_schema_report);
    const before = await inspect();
    await database.exec(migration);
    const after = await inspect();
    assert.deepEqual(after.relations, before.relations);
    assert.deepEqual(after.policies, before.policies);
    assert.deepEqual(after.constraints, before.constraints);
    for (const role of ['anon', 'authenticated']) {
      await asActor(database, role, role === 'anon' ? null : resident, async () => {
        const record = await reportRow(database);
        const columns = Object.keys(record);
        const assignments = columns.map((column, index) => `${column} = $${index + 1}`).join(', ');
        await database.query(`UPDATE public.community_reports SET ${assignments} WHERE id = $${columns.length + 1}`,
          [...columns.map((column) => column === 'like_count' ? 2 : column === 'comments_json' ? 'FIXTURE_SECRET_NEW_COMMENT' : record[column]), reportId]);
        await database.query("INSERT INTO public.community_reports(id, title) VALUES ($1, 'Mobile fixture')", [`mobile-${role}`]);
      });
    }
    assert.equal((await reportRow(database)).like_count, 2);
    assert.equal((await reportRow(database)).comments_json, 'FIXTURE_SECRET_NEW_COMMENT');
    assert.equal((await reportRow(database)).status, 'Pending');
    assert.deepEqual(await events(database), []);
    await database.exec('BEGIN READ ONLY');
    const verification = JSON.parse((await database.query(foundationVerification)).rows[0].audit_foundation_report);
    await database.exec('ROLLBACK');
    assert.equal(verification.all_checks_passed, true, JSON.stringify(verification.checks));
  } finally {
    await database.close();
  }
});

test('deployment verification reads metadata only and detects missing, changed or over-permitted RPCs', async () => {
  const database = await fixture(false);
  try {
    const verify = async () => {
      await database.exec('BEGIN READ ONLY');
      try {
        return JSON.parse((await database.query(moderationVerification)).rows[0].report_moderation_audit_report);
      } finally {
        await database.exec('ROLLBACK');
      }
    };
    assert.equal((await verify()).all_checks_passed, false);
    await database.exec(migration);
    await asActor(database, 'authenticated', admin, () => moderate(database));
    const beforeReport = await reportRow(database);
    const beforeEvents = await events(database);
    let verification = await verify();
    assert.equal(verification.all_checks_passed, true, JSON.stringify(verification.checks));
    assert.equal(Object.keys(verification.checks).length, 13);
    assert.equal(verification.functions.length, 11);
    assert.ok(verification.functions.every((routine) => routine.exists && routine.body_matches));
    assert.ok(!JSON.stringify(verification).includes('FIXTURE_SECRET'));
    assert.deepEqual(await reportRow(database), beforeReport);
    assert.deepEqual(await events(database), beforeEvents);
    await database.exec('GRANT EXECUTE ON FUNCTION public.admin_moderate_report(uuid,text,text,text) TO PUBLIC');
    assert.equal((await verify()).checks.function_execution_is_restricted, false);
    await database.exec('REVOKE EXECUTE ON FUNCTION public.admin_moderate_report(uuid,text,text,text) FROM PUBLIC');
    await database.exec("ALTER FUNCTION public.admin_moderate_report(uuid,text,text,text) SET search_path = 'public'");
    assert.equal((await verify()).checks.functions_match_reviewed_contracts, false);
    await database.exec("ALTER FUNCTION public.admin_moderate_report(uuid,text,text,text) SET search_path = ''");
    await database.exec('REVOKE TRUNCATE ON public.community_reports FROM anon');
    assert.equal((await verify()).checks.shared_report_grants_preserved, false);
    await database.exec('GRANT TRUNCATE ON public.community_reports TO anon');
    await database.exec('ALTER POLICY "Allow public update access" ON public.community_reports USING (false)');
    assert.equal((await verify()).checks.shared_report_policies_preserved, false);
    await database.exec('ALTER POLICY "Allow public update access" ON public.community_reports USING (true)');
    await database.exec(`CREATE OR REPLACE FUNCTION public.admin_moderate_report(
      p_operation_id uuid, p_report_id text, p_expected_status text, p_new_status text
    ) RETURNS TABLE (report_id text, before_status text, after_status text,
      outcome text, audit_event_id uuid, recorded_at timestamptz)
      LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$ BEGIN RETURN; END $$;`);
    verification = await verify();
    assert.equal(verification.checks.functions_match_reviewed_contracts, false);
    assert.deepEqual(await reportRow(database), beforeReport);
    assert.deepEqual(await events(database), beforeEvents);
  } finally {
    await database.close();
  }
});
