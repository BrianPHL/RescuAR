-- RescuAR Admin: server-managed onboarding and first-login password rotation.
-- Install AFTER database/migrations/004_rbac_foundation.sql.
-- This migration does not create administrator accounts or change existing roles.
BEGIN;

CREATE TABLE IF NOT EXISTS public.rbac_password_setup (
 user_id uuid PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
 must_change_password boolean NOT NULL DEFAULT true,
 created_by uuid NOT NULL REFERENCES auth.users(id),
 temporary_password_sha256 text CHECK (temporary_password_sha256 ~ '^[0-9a-f]{64}$'),
 created_at timestamptz NOT NULL DEFAULT now(),
 completed_at timestamptz
);
ALTER TABLE public.rbac_password_setup ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON TABLE public.rbac_password_setup FROM PUBLIC, anon, authenticated;
-- No browser-accessible RLS policies: service-role backend is sole writer.

-- Read ONLY the signed-in user's own password setup status. Unknown users and
-- existing pre-migration admins have no row and are not forced to rotate.
CREATE OR REPLACE FUNCTION public.rbac_password_setup_status()
RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER SET search_path = ''
AS $$
 SELECT COALESCE((SELECT s.must_change_password
                  FROM public.rbac_password_setup s
                  WHERE s.user_id = (SELECT auth.uid())), false);
$$;
REVOKE ALL ON FUNCTION public.rbac_password_setup_status() FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.rbac_password_setup_status() TO authenticated;

-- Existing RBAC-enforced RPCs must DENY privileges until the first-login
-- password change has been completed. User cannot modify this status.
CREATE OR REPLACE FUNCTION public.rbac_can(p_permission text)
RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER SET search_path = ''
AS $$
 SELECT (SELECT auth.uid()) IS NOT NULL
 AND NOT EXISTS (
  SELECT 1 FROM public.rbac_password_setup s
  WHERE s.user_id = (SELECT auth.uid()) AND s.must_change_password
 )
 AND EXISTS (
  SELECT 1 FROM public.rbac_admin_assignments a
  JOIN public.rbac_role_permissions p ON p.role_id = a.role_id
  WHERE a.user_id = (SELECT auth.uid())
  AND (p.permission = 'system.all' OR p.permission = p_permission)
 );
$$;
REVOKE ALL ON FUNCTION public.rbac_can(text) FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.rbac_can(text) TO authenticated;

-- Backend-only atomic creation of the role, password gate, and audit entry.
-- Caller identity is verified by Supabase Auth in the Edge Function AND
-- independently rechecked in this database function. Only service_role can
-- invoke it; never grant EXECUTE to authenticated/anon.
CREATE OR REPLACE FUNCTION public.rbac_register_provisioned_admin(
 p_user_id uuid, p_role text, p_actor uuid, p_password_sha256 text
) RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path = ''
AS $$
BEGIN
 IF p_user_id IS NULL OR p_actor IS NULL OR p_role IS NULL
   OR p_role = 'superadmin' OR p_password_sha256 !~ '^[0-9a-f]{64}$' OR NOT EXISTS (
    SELECT 1 FROM public.rbac_roles WHERE id = p_role
   ) THEN
   RAISE EXCEPTION 'Invalid administrator role or user' USING ERRCODE = '22023';
 END IF;
 IF NOT EXISTS (
    SELECT 1 FROM public.rbac_admin_assignments a
    WHERE a.user_id=p_actor AND a.role_id='superadmin'
   ) OR EXISTS (
    SELECT 1 FROM public.rbac_password_setup s
    WHERE s.user_id=p_actor AND s.must_change_password
   ) THEN
    RAISE EXCEPTION 'Not an active Superadmin' USING ERRCODE='42501';
 END IF;
 IF EXISTS (SELECT 1 FROM public.rbac_admin_assignments WHERE user_id=p_user_id) THEN
    RAISE EXCEPTION 'Account already assigned' USING ERRCODE='23505';
 END IF;
 INSERT INTO public.rbac_password_setup (user_id, must_change_password, created_by, temporary_password_sha256)
 VALUES (p_user_id, true, p_actor, p_password_sha256);
 INSERT INTO public.rbac_admin_assignments (user_id,role_id,assigned_by)
 VALUES (p_user_id,p_role,p_actor);
 INSERT INTO public.rbac_audit_log(actor,action,target_user,detail)
 VALUES(p_actor,'admin_account_provisioned',p_user_id,
        pg_catalog.jsonb_build_object('role',p_role,'first_login_password_change',true));
END;
$$;
REVOKE ALL ON FUNCTION public.rbac_register_provisioned_admin(uuid,text,uuid,text) FROM PUBLIC, anon, authenticated;
GRANT EXECUTE ON FUNCTION public.rbac_register_provisioned_admin(uuid,text,uuid,text) TO service_role;

-- Called ONLY after the Edge Function successfully changes the password
-- using the server-only Supabase Auth admin API.
CREATE OR REPLACE FUNCTION public.rbac_complete_password_setup(p_user_id uuid)
RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path = ''
AS $$
BEGIN
 UPDATE public.rbac_password_setup
 SET must_change_password=false, temporary_password_sha256=NULL, completed_at=now()
 WHERE user_id=p_user_id AND must_change_password=true;
 IF NOT FOUND THEN
  RAISE EXCEPTION 'No pending password setup' USING ERRCODE='22023';
 END IF;
 INSERT INTO public.rbac_audit_log(actor,action,target_user,detail)
 VALUES(p_user_id,'first_login_password_changed',p_user_id,'{}'::jsonb);
END;
$$;
REVOKE ALL ON FUNCTION public.rbac_complete_password_setup(uuid) FROM PUBLIC, anon, authenticated;
GRANT EXECUTE ON FUNCTION public.rbac_complete_password_setup(uuid) TO service_role;
COMMIT;
