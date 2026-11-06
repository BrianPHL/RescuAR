-- Requires the existing emergency_hotlines table and 004_rbac_foundation.sql.
-- Run once in the Supabase SQL Editor BEFORE deploying the updated UI.
-- This migration does not alter read policies: hotline data may also serve the mobile app.
BEGIN;

DO $preflight$
BEGIN
  IF to_regclass('public.emergency_hotlines') IS NULL THEN
    RAISE EXCEPTION 'Missing public.emergency_hotlines; inspect the deployed schema before installing';
  END IF;
  IF to_regprocedure('public.rbac_can(text)') IS NULL THEN
    RAISE EXCEPTION 'RBAC foundation missing: install/review 004_rbac_foundation.sql first';
  END IF;
END;
$preflight$;

-- If RLS was previously disabled, keep the same read visibility as the existing
-- table GRANTs. Public/mobile readers keep working, but no new SELECT GRANT is
-- made to anonymous clients. Existing enabled RLS/SELECT policies stay intact.
DO $read_access$
BEGIN
  IF NOT (SELECT c.relrowsecurity FROM pg_catalog.pg_class c
          JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
          WHERE n.nspname='public' AND c.relname='emergency_hotlines') THEN
    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policies
                   WHERE schemaname='public' AND tablename='emergency_hotlines'
                   AND policyname='hotlines_preserve_existing_read_access') THEN
      CREATE POLICY hotlines_preserve_existing_read_access
        ON public.emergency_hotlines AS PERMISSIVE FOR SELECT TO PUBLIC USING (true);
    END IF;
  END IF;
END;
$read_access$;

ALTER TABLE public.emergency_hotlines ENABLE ROW LEVEL SECURITY;

-- A public/mobile client may read hotline numbers, but cannot mutate them.
-- Authenticated editors must pass the current DB-backed rbac_can() permission check.
REVOKE INSERT, UPDATE, DELETE ON public.emergency_hotlines FROM PUBLIC, anon;
GRANT SELECT, INSERT, UPDATE, DELETE ON public.emergency_hotlines TO authenticated;

-- The permissive policies allow authenticated editors to write when there is no
-- existing write policy; restrictive policies prevent broader legacy policies
-- (including FOR ALL) from granting writes to unrelated authenticated users.
DROP POLICY IF EXISTS hotlines_insert_allowed ON public.emergency_hotlines;
DROP POLICY IF EXISTS hotlines_insert_guard ON public.emergency_hotlines;
DROP POLICY IF EXISTS hotlines_update_allowed ON public.emergency_hotlines;
DROP POLICY IF EXISTS hotlines_update_guard ON public.emergency_hotlines;
DROP POLICY IF EXISTS hotlines_delete_allowed ON public.emergency_hotlines;
DROP POLICY IF EXISTS hotlines_delete_guard ON public.emergency_hotlines;

CREATE POLICY hotlines_insert_allowed ON public.emergency_hotlines
  AS PERMISSIVE FOR INSERT TO authenticated
  WITH CHECK ((SELECT auth.uid()) IS NOT NULL AND (SELECT public.rbac_can('hotline.manage')));
CREATE POLICY hotlines_insert_guard ON public.emergency_hotlines
  AS RESTRICTIVE FOR INSERT TO authenticated
  WITH CHECK ((SELECT auth.uid()) IS NOT NULL AND (SELECT public.rbac_can('hotline.manage')));

CREATE POLICY hotlines_update_allowed ON public.emergency_hotlines
  AS PERMISSIVE FOR UPDATE TO authenticated
  USING ((SELECT auth.uid()) IS NOT NULL AND (SELECT public.rbac_can('hotline.manage')))
  WITH CHECK ((SELECT auth.uid()) IS NOT NULL AND (SELECT public.rbac_can('hotline.manage')));
CREATE POLICY hotlines_update_guard ON public.emergency_hotlines
  AS RESTRICTIVE FOR UPDATE TO authenticated
  USING ((SELECT auth.uid()) IS NOT NULL AND (SELECT public.rbac_can('hotline.manage')))
  WITH CHECK ((SELECT auth.uid()) IS NOT NULL AND (SELECT public.rbac_can('hotline.manage')));

CREATE POLICY hotlines_delete_allowed ON public.emergency_hotlines
  AS PERMISSIVE FOR DELETE TO authenticated
  USING ((SELECT auth.uid()) IS NOT NULL AND (SELECT public.rbac_can('hotline.manage')));
CREATE POLICY hotlines_delete_guard ON public.emergency_hotlines
  AS RESTRICTIVE FOR DELETE TO authenticated
  USING ((SELECT auth.uid()) IS NOT NULL AND (SELECT public.rbac_can('hotline.manage')));

COMMIT;

-- Live acceptance: verify select/insert/update/delete with a hotline.manage user,
-- a signed-in account without that permission, and an anonymous client. Confirm
-- legacy public SELECT still returns required hotline data to the mobile app.
