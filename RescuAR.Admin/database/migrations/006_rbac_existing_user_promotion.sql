-- RescuAR / RBAC: optional temporary credential during promotion of an EXISTING
-- and email-confirmed Supabase Auth user who has NO administrator role.
-- Install after 004_rbac_foundation.sql and 005_rbac_admin_provisioning.sql.
-- Do not run as a role-assignment script; this migration creates guarded RPCs.
BEGIN;

-- Atomic permission gate + role assignment before the Auth password is changed.
-- Callable only from the trusted Edge Function using the service-role key.
-- Ownership of the email address must be checked independently before promotion.
CREATE OR REPLACE FUNCTION public.rbac_prepare_existing_admin_promotion(
  p_email text, p_role text, p_actor uuid, p_password_sha256 text
) RETURNS uuid
LANGUAGE plpgsql SECURITY DEFINER SET search_path = ''
AS $$
DECLARE
  target uuid;
  target_confirmed timestamptz;
BEGIN
  IF p_actor IS NULL OR p_email IS NULL OR p_role IS NULL
    OR p_password_sha256 IS NULL
    OR p_password_sha256 !~ '^[0-9a-f]{64}$'
    OR pg_catalog.length(p_email) > 254
    OR p_email !~ '^[^[:space:]@]+@[^[:space:]@]+\.[^[:space:]@]+$'
    OR p_role = 'superadmin' OR p_role !~ '^[a-z][a-z0-9_]{2,60}$' THEN
    RAISE EXCEPTION 'Invalid promotion request' USING ERRCODE = '22023';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM public.rbac_admin_assignments a
    WHERE a.user_id = p_actor AND a.role_id = 'superadmin'
  ) OR EXISTS (
    SELECT 1 FROM public.rbac_password_setup s
    WHERE s.user_id = p_actor AND s.must_change_password
  ) THEN
    RAISE EXCEPTION 'Active Superadmin role required' USING ERRCODE = '42501';
  END IF;

  IF NOT EXISTS (SELECT 1 FROM public.rbac_roles WHERE id = p_role) THEN
    RAISE EXCEPTION 'Unknown role' USING ERRCODE = '22023';
  END IF;

  -- Row lock serializes concurrent promotions of the same Auth user.
  SELECT u.id, u.email_confirmed_at INTO target, target_confirmed
  FROM auth.users u
  WHERE lower(u.email) = lower(trim(p_email))
  FOR UPDATE;

  IF target IS NULL OR target = p_actor THEN
    RAISE EXCEPTION 'No eligible existing Auth account found' USING ERRCODE = '22023';
  END IF;
  IF target_confirmed IS NULL THEN
    RAISE EXCEPTION 'Existing Auth email must be confirmed before promotion' USING ERRCODE = '22023';
  END IF;
  IF EXISTS (SELECT 1 FROM public.rbac_admin_assignments a WHERE a.user_id = target)
    OR EXISTS (SELECT 1 FROM public.rbac_password_setup s WHERE s.user_id = target) THEN
    RAISE EXCEPTION 'Account already has administrator access or a password setup record' USING ERRCODE = '23505';
  END IF;

  -- Keep operations together; failures roll back both inserts and the audit.
  INSERT INTO public.rbac_password_setup (
    user_id, must_change_password, created_by, temporary_password_sha256
  ) VALUES (target, true, p_actor, p_password_sha256);
  INSERT INTO public.rbac_admin_assignments (user_id, role_id, assigned_by)
  VALUES (target, p_role, p_actor);
  INSERT INTO public.rbac_audit_log (actor, action, target_user, detail)
  VALUES (p_actor, 'existing_user_admin_promotion_prepared', target,
    pg_catalog.jsonb_build_object('role', p_role, 'pending_password_change', true));
  RETURN target;
END;
$$;
REVOKE ALL ON FUNCTION public.rbac_prepare_existing_admin_promotion(text,text,uuid,text) FROM PUBLIC, anon, authenticated;
GRANT EXECUTE ON FUNCTION public.rbac_prepare_existing_admin_promotion(text,text,uuid,text) TO service_role;

-- Compensate if Supabase Auth rejects the password update. Verify ownership
-- of the pending operation with actor+role+hash before removing its assignments.
CREATE OR REPLACE FUNCTION public.rbac_abort_existing_admin_promotion(
  p_user_id uuid, p_role text, p_actor uuid, p_password_sha256 text
) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path = ''
AS $$
DECLARE
  setup_found uuid;
BEGIN
  SELECT s.user_id INTO setup_found
  FROM public.rbac_password_setup s
  WHERE s.user_id = p_user_id
    AND s.must_change_password = true
    AND s.created_by = p_actor
    AND s.temporary_password_sha256 = p_password_sha256
  FOR UPDATE;

  IF setup_found IS NULL OR NOT EXISTS (
    SELECT 1 FROM public.rbac_admin_assignments a
    WHERE a.user_id = p_user_id
      AND a.role_id = p_role
      AND a.assigned_by = p_actor
  ) THEN
    RAISE EXCEPTION 'Promotion operation could not be safely rolled back' USING ERRCODE = '42501';
  END IF;

  DELETE FROM public.rbac_admin_assignments
  WHERE user_id = p_user_id AND role_id = p_role AND assigned_by = p_actor;
  DELETE FROM public.rbac_password_setup
  WHERE user_id = p_user_id AND must_change_password AND created_by = p_actor
    AND temporary_password_sha256 = p_password_sha256;
  INSERT INTO public.rbac_audit_log (actor, action, target_user, detail)
  VALUES (p_actor, 'existing_user_admin_promotion_rolled_back', p_user_id,
    pg_catalog.jsonb_build_object('role', p_role, 'reason', 'auth_password_update_failed'));
END;
$$;
REVOKE ALL ON FUNCTION public.rbac_abort_existing_admin_promotion(uuid,text,uuid,text) FROM PUBLIC, anon, authenticated;
GRANT EXECUTE ON FUNCTION public.rbac_abort_existing_admin_promotion(uuid,text,uuid,text) TO service_role;

COMMIT;
