import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
import test from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';

const validationRoot = process.env.RESCUAR_AUDIT_VALIDATION_ROOT
  || fileURLToPath(new URL('../../node_modules/.cache/rescuar-audit-validation/', import.meta.url));
const { PGlite } = createRequire(pathToFileURL(resolve(validationRoot, 'package.json')))('@electric-sql/pglite');
const inspection = await readFile(new URL('../inspect-report-moderation-prerequisites.sql', import.meta.url), 'utf8');
const baseline = await readFile(new URL('./fixtures/step1-baseline.sql', import.meta.url), 'utf8');
const foundation = await readFile(new URL('../migrations/001_web_audit_foundation.sql', import.meta.url), 'utf8');

async function inspect(database) {
  await database.exec('BEGIN READ ONLY');
  try {
    const result = await database.query(inspection);
    assert.equal(result.rows.length, 1);
    return JSON.parse(result.rows[0].report_moderation_schema_report);
  } finally {
    await database.exec('ROLLBACK');
  }
}

test('moderation inspection reports missing relations and roles without failing', async () => {
  const database = new PGlite();
  try {
    const report = await inspect(database);
    assert.equal(report.report_version, 1);
    assert.equal(report.scope, 'report_moderation_prerequisites');
    assert.ok(Number.isFinite(Date.parse(report.captured_at)));
    assert.equal(report.expected_relations.length, 3);
    assert.ok(report.expected_relations.every((relation) => !relation.exists));
    assert.deepEqual(report.relations, []);
    assert.deepEqual(report.functions, []);
    assert.deepEqual(report.schema_access, []);
  } finally {
    await database.close();
  }
});

test('moderation inspection finds shared write access, helpers and installed foundation without reading rows', async () => {
  const database = new PGlite();
  try {
    await database.exec(baseline);
    await database.exec(foundation);
    // Synthetic report shape exercises discovery; it is not a deployed-schema claim.
    await database.exec(`
      CREATE TYPE public.report_status AS ENUM ('Pending', 'Approved', 'Rejected', 'Resolved');
      CREATE TABLE public.community_reports (
        id uuid PRIMARY KEY, status public.report_status NOT NULL DEFAULT 'Pending',
        description text, comments_json text, like_count integer DEFAULT 0,
        author_id uuid REFERENCES auth.users(id)
      );
      CREATE TABLE public.report_comments (
        report_id uuid REFERENCES public.community_reports(id) ON DELETE CASCADE,
        content text
      );
      CREATE TABLE public.unrelated_business (id integer);
      REVOKE ALL ON public.community_reports FROM anon, authenticated;
      CREATE ROLE report_reader;
      GRANT report_reader TO authenticated;
      GRANT SELECT ON public.community_reports TO report_reader;
      GRANT INSERT, UPDATE, TRUNCATE ON public.community_reports TO anon;
      GRANT UPDATE(comments_json, like_count) ON public.community_reports TO authenticated;
      CREATE INDEX reports_status_idx ON public.community_reports(status);
      ALTER TABLE public.community_reports ENABLE ROW LEVEL SECURITY;
      CREATE FUNCTION public.can_read_feed() RETURNS boolean LANGUAGE sql STABLE
        AS $$ SELECT true $$;
      CREATE POLICY feed_read ON public.community_reports FOR SELECT TO authenticated
        USING (public.can_read_feed());
      CREATE POLICY public_report_update ON public.community_reports FOR UPDATE USING (true);
      INSERT INTO public.community_reports(id, description, comments_json) VALUES
        ('00000000-0000-0000-0000-000000000010', 'ROW_ONLY_SECRET_REPORT', 'ROW_ONLY_SECRET_COMMENT');
      INSERT INTO audit_private.events(operation_id, actor_id, event_type, module, source, outcome, details)
        VALUES (gen_random_uuid(), gen_random_uuid(), 'module.opened', 'dashboard', 'client_observed', 'observed',
          '{"sort_key":"ROW_ONLY_SECRET_EVENT"}');
      CREATE FUNCTION public.observe_report_change() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'Application trigger must not run during inspection'; END $$;
      CREATE TRIGGER report_change AFTER UPDATE ON public.community_reports
        FOR EACH ROW EXECUTE FUNCTION public.observe_report_change();
      CREATE FUNCTION public.legacy_decision() RETURNS void LANGUAGE plpgsql SECURITY DEFINER AS $$
        BEGIN UPDATE public.community_reports SET status = 'Approved'; END $$;
      CREATE FUNCTION public.feed_helper() RETURNS integer LANGUAGE sql RETURN 1;
      CREATE FUNCTION public.report_feed() RETURNS integer LANGUAGE sql RETURN public.feed_helper();
      CREATE FUNCTION public.report_feed(p_limit integer) RETURNS integer LANGUAGE sql AS $$ SELECT p_limit $$;
    `);
    const snapshot = async () => (await database.query(`
      SELECT (SELECT jsonb_agg(to_jsonb(r)) FROM public.community_reports r) AS report_rows,
        (SELECT jsonb_agg(to_jsonb(r)) FROM audit_private.events r) AS event_rows,
        (SELECT count(*)::int FROM pg_catalog.pg_class) AS relation_count,
        (SELECT count(*)::int FROM pg_catalog.pg_proc) AS function_count
    `)).rows;
    const before = await snapshot();
    const report = await inspect(database);
    assert.deepEqual(await snapshot(), before);
    assert.ok(!JSON.stringify(report).includes('ROW_ONLY_SECRET'));
    assert.ok(report.expected_relations.every((relation) => relation.exists));
    assert.equal(report.relations.length, 3);
    assert.ok(!report.relations.some((relation) => relation.name === 'unrelated_business'));

    const reports = report.relations.find((relation) => relation.name === 'community_reports');
    assert.equal(reports.rls_enabled, true);
    assert.ok(reports.indexes.some((index) => index.includes('reports_status_idx')));
    assert.deepEqual(reports.columns.find((column) => column.name === 'status').enum_values,
      ['Pending', 'Approved', 'Rejected', 'Resolved']);
    assert.equal(reports.role_access.find((access) => access.role === 'anon').truncate, true);
    assert.equal(reports.role_access.find((access) => access.role === 'authenticated').select, true);
    assert.equal(reports.role_access.find((access) => access.role === 'authenticated').update, false);
    const columnAccess = (name) => reports.columns.find((column) => column.name === name)
      .role_access.find((access) => access.role === 'authenticated');
    assert.equal(columnAccess('status').update, false);
    assert.equal(columnAccess('comments_json').update, true);
    assert.ok(reports.column_grants.some((grant) => grant.column === 'like_count' && grant.privilege === 'UPDATE'));
    assert.ok(report.policies.some((policy) => policy.name === 'public_report_update'
      && policy.permissive && policy.using === 'true' && policy.roles.includes('PUBLIC')));
    assert.ok(report.constraints.some((constraint) => constraint.relation === 'report_comments'
      && constraint.definition.includes('CASCADE')));
    assert.equal(reports.triggers[0].name, 'report_change');
    for (const name of ['require_admin', 'append_event', 'prevent_event_change', 'web_admin_access',
      'record_web_audit_event', 'get_web_audit_logs', 'uid', 'observe_report_change', 'can_read_feed',
      'legacy_decision', 'feed_helper']) {
      assert.ok(report.functions.some((routine) => routine.name === name), name);
    }
    assert.equal(report.functions.filter((routine) => routine.name === 'report_feed').length, 2);
    const access = report.functions.find((routine) => routine.name === 'web_admin_access');
    assert.equal(access.security_definer, true);
    assert.ok(access.settings.some((setting) => setting.startsWith('search_path=')));
    assert.equal(access.role_access.find((role) => role.role === 'anon').execute, false);
    assert.equal(access.role_access.find((role) => role.role === 'authenticated').execute, true);
    const append = report.functions.find((routine) => routine.name === 'append_event');
    assert.ok(append.role_access.every((role) => !role.execute));
    assert.equal(report.relations.find((relation) => relation.name === 'events').triggers.length, 2);
    assert.ok(report.schema_access.some((access) => access.role === 'service_role' && access.bypasses_rls));

    // Even a role denied direct row access can inspect metadata without selecting rows.
    await database.exec('SET ROLE anon');
    await assert.rejects(database.query('SELECT description FROM public.community_reports'), /permission denied/);
    const limited = await inspect(database);
    assert.equal(limited.relations.length, 3);
    assert.ok(!JSON.stringify(limited).includes('ROW_ONLY_SECRET'));
    await database.exec('RESET ROLE');
    assert.deepEqual(await snapshot(), before);
  } finally {
    await database.close();
  }
});

test('moderation inspection exposes domain status rules without assuming text or enum columns', async () => {
  const database = new PGlite();
  try {
    await database.exec(`
      CREATE DOMAIN public.moderation_status AS text NOT NULL DEFAULT 'Pending'
        CHECK (VALUE IN ('Pending', 'Approved', 'Rejected', 'Resolved'));
      CREATE TABLE public.community_reports (
        id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
        status public.moderation_status
      );
    `);
    const report = await inspect(database);
    const columns = report.relations[0].columns;
    assert.equal(columns.find((column) => column.name === 'id').identity, 'a');
    const status = columns.find((column) => column.name === 'status');
    assert.equal(status.type_kind, 'd');
    assert.equal(status.domain_base_type, 'text');
    assert.equal(status.domain_not_null, true);
    assert.ok(status.domain_default.includes('Pending'));
    assert.ok(status.domain_constraints[0].includes('Resolved'));
    assert.deepEqual(status.role_access, []);
  } finally {
    await database.close();
  }
});
