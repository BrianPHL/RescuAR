import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
import test from 'node:test';
import { pathToFileURL } from 'node:url';

const validationRoot = process.env.RESCUAR_AUDIT_VALIDATION_ROOT;
if (!validationRoot) {
  throw new Error('Set RESCUAR_AUDIT_VALIDATION_ROOT to the temporary PGlite installation; see database/README.md.');
}
const validationRequire = createRequire(pathToFileURL(resolve(validationRoot, 'package.json')));
const { PGlite } = validationRequire('@electric-sql/pglite');
const inspection = await readFile(new URL('../inspect-audit-prerequisites.sql', import.meta.url), 'utf8');

async function inspect(database) {
  await database.exec('BEGIN READ ONLY');
  try {
    const result = await database.query(inspection);
    assert.equal(result.rows.length, 1);
    return JSON.parse(result.rows[0].audit_schema_report);
  } finally {
    await database.exec('ROLLBACK');
  }
}

test('inspection tolerates missing application tables and Supabase roles', async () => {
  const database = new PGlite();
  try {
    const report = await inspect(database);
    assert.equal(report.report_version, 1);
    assert.equal(report.expected_relations.length, 11);
    assert.ok(report.expected_relations.every((relation) => !relation.exists));
    assert.deepEqual(report.relations, []);
    assert.deepEqual(report.functions, []);
    assert.deepEqual(report.schema_access, []);
  } finally {
    await database.close();
  }
});

test('inspection finds security metadata and does not expose or mutate fixture records', async () => {
  const database = new PGlite();
  try {
    await database.exec(`
      CREATE ROLE anon;
      CREATE ROLE authenticated;
      CREATE ROLE service_role BYPASSRLS;
      CREATE ROLE inherited_reader;
      GRANT inherited_reader TO authenticated;
      CREATE SCHEMA auth;
      CREATE SCHEMA audit;
      CREATE TYPE public.admin_role AS ENUM ('admin', 'moderator');
      CREATE TABLE auth.users (id uuid PRIMARY KEY, email text NOT NULL);
      CREATE TABLE public.users (
        id uuid PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
        full_name text, allergies text
      );
      CREATE TABLE public.admin_roles (
        user_id uuid PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
        role public.admin_role NOT NULL
      );
      CREATE TABLE public.advisories (
        id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
        title text NOT NULL CHECK (length(title) > 0),
        status text NOT NULL DEFAULT 'Active'
      );
      CREATE INDEX advisories_status_idx ON public.advisories(status);
      CREATE TABLE audit.events (id bigint GENERATED ALWAYS AS IDENTITY, operation text);
      CREATE TABLE public.other_notes (
        advisory_id bigint REFERENCES public.advisories(id) ON DELETE SET NULL
      );
      CREATE FUNCTION public.can_review_records() RETURNS boolean
        LANGUAGE sql STABLE AS $$ SELECT true $$;
      ALTER TABLE public.advisories ENABLE ROW LEVEL SECURITY;
      CREATE POLICY review_advisories ON public.advisories FOR SELECT
        TO authenticated USING (public.can_review_records());
      GRANT USAGE ON SCHEMA public TO anon, authenticated;
      GRANT SELECT ON public.advisories TO inherited_reader;
      GRANT UPDATE (status) ON public.advisories TO authenticated;
      CREATE FUNCTION public.record_change() RETURNS trigger
        LANGUAGE plpgsql AS $$
        BEGIN
          INSERT INTO audit.events(operation) VALUES (TG_OP);
          RETURN NEW;
        END $$;
      CREATE TRIGGER advisory_changes AFTER INSERT OR UPDATE ON public.advisories
        FOR EACH ROW EXECUTE FUNCTION public.record_change();
      CREATE FUNCTION public.admin_delete_user(target_user_id uuid) RETURNS void
        LANGUAGE sql SECURITY DEFINER SET search_path = ''
        AS $$ DELETE FROM auth.users WHERE id = target_user_id $$;
      REVOKE ALL ON FUNCTION public.admin_delete_user(uuid) FROM PUBLIC;
      GRANT EXECUTE ON FUNCTION public.admin_delete_user(uuid) TO authenticated;
      CREATE FUNCTION public.admin_delete_user(target_user_id uuid, dry_run boolean) RETURNS boolean
        LANGUAGE sql AS $$ SELECT dry_run $$;
      INSERT INTO auth.users VALUES ('00000000-0000-0000-0000-000000000001', 'ROW_ONLY_SECRET_EMAIL');
      INSERT INTO public.users VALUES ('00000000-0000-0000-0000-000000000001', 'ROW_ONLY_SECRET_NAME', 'ROW_ONLY_SECRET_MEDICAL');
      INSERT INTO public.advisories(title) VALUES ('ROW_ONLY_SECRET_TITLE');
    `);
    const snapshot = async () => (await database.query(`
      SELECT
        (SELECT jsonb_agg(to_jsonb(r)) FROM auth.users AS r) AS auth_rows,
        (SELECT jsonb_agg(to_jsonb(r)) FROM public.users AS r) AS profile_rows,
        (SELECT jsonb_agg(to_jsonb(r)) FROM public.advisories AS r) AS advisory_rows,
        (SELECT jsonb_agg(to_jsonb(r)) FROM audit.events AS r) AS audit_rows,
        (SELECT count(*)::int FROM pg_catalog.pg_class) AS object_count
    `)).rows;
    const before = await snapshot();
    const report = await inspect(database);
    assert.deepEqual(await snapshot(), before);
    assert.ok(!JSON.stringify(report).includes('ROW_ONLY_SECRET'));

    const advisories = report.relations.find((relation) => relation.name === 'advisories');
    assert.equal(advisories.rls_enabled, true);
    assert.equal(advisories.columns.find((column) => column.name === 'id').identity, 'a');
    assert.ok(advisories.indexes.some((index) => index.includes('advisories_status_idx')));
    assert.equal(advisories.role_access.find((role) => role.role === 'authenticated').select, true);
    assert.ok(!advisories.grants.some((grant) => grant.grantee === 'authenticated' && grant.privilege === 'SELECT'));
    assert.ok(advisories.column_grants.some((grant) => grant.column === 'status' && grant.privilege === 'UPDATE'));
    assert.equal(advisories.triggers[0].name, 'advisory_changes');
    assert.ok(report.relations.some((relation) => relation.schema === 'audit' && relation.name === 'events'));
    assert.ok(report.constraints.some((constraint) => constraint.relation === 'other_notes' && constraint.definition.includes('SET NULL')));
    assert.ok(report.functions.some((routine) => routine.name === 'record_change'));
    assert.ok(report.functions.some((routine) => routine.name === 'can_review_records'));
    const deleteRoutines = report.functions.filter((routine) => routine.name === 'admin_delete_user');
    assert.equal(deleteRoutines.length, 2);
    const privilegedDelete = deleteRoutines.find((routine) => routine.security_definer);
    assert.ok(privilegedDelete.settings.some((setting) => setting.startsWith('search_path=')));
    assert.equal(privilegedDelete.role_access.find((role) => role.role === 'anon').execute, false);
    assert.equal(privilegedDelete.role_access.find((role) => role.role === 'authenticated').execute, true);
    assert.deepEqual(report.relations.find((relation) => relation.name === 'admin_roles')
      .columns.find((column) => column.name === 'role').enum_values, ['admin', 'moderator']);
    assert.ok(report.schema_access.some((access) => access.role === 'service_role' && access.bypasses_rls));

    await database.exec('SET ROLE anon');
    await assert.rejects(database.query('SELECT title FROM public.advisories'), /permission denied/);
    const limitedReport = await inspect(database);
    assert.ok(limitedReport.relations.some((relation) => relation.name === 'advisories'));
    assert.ok(!JSON.stringify(limitedReport).includes('ROW_ONLY_SECRET'));
    await database.exec('RESET ROLE');
    assert.deepEqual(await snapshot(), before);
  } finally {
    await database.close();
  }
});
