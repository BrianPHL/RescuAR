-- RBAC foundation. Run in Supabase SQL Editor as project owner after reviewing deployed RLS.
-- Before running: replace BOOTSTRAP_UUID in the last SELECT with the existing admin's auth.users UUID.
BEGIN;
CREATE TABLE IF NOT EXISTS public.rbac_roles(
 id text PRIMARY KEY CHECK(id ~ '^[a-z][a-z0-9_]{2,60}$'),
 label text NOT NULL,
 system_role boolean NOT NULL DEFAULT false
);
CREATE TABLE IF NOT EXISTS public.rbac_role_permissions(
 role_id text NOT NULL REFERENCES public.rbac_roles(id) ON DELETE CASCADE,
 permission text NOT NULL CHECK(permission ~ '^[a-z][a-z0-9_.]{2,100}$'),
 PRIMARY KEY(role_id, permission)
);
CREATE TABLE IF NOT EXISTS public.rbac_admin_assignments(
 user_id uuid NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
 role_id text NOT NULL REFERENCES public.rbac_roles(id) ON DELETE RESTRICT,
 assigned_at timestamptz NOT NULL DEFAULT now(),
 assigned_by uuid REFERENCES auth.users(id),
 PRIMARY KEY(user_id,role_id)
);
CREATE TABLE IF NOT EXISTS public.rbac_audit_log(
 id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
 actor uuid,
 action text NOT NULL,
 target_user uuid,
 detail jsonb NOT NULL DEFAULT '{}'::jsonb,
 created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS public.rbac_test_runs(
 id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
 actor uuid NOT NULL REFERENCES auth.users(id),
 kind text NOT NULL CHECK(kind IN ('river_level','advisory_preview')),
 payload jsonb NOT NULL,
 created_at timestamptz NOT NULL DEFAULT now()
);
ALTER TABLE public.rbac_roles ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.rbac_role_permissions ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.rbac_admin_assignments ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.rbac_audit_log ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.rbac_test_runs ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON public.rbac_roles,public.rbac_role_permissions,public.rbac_admin_assignments,public.rbac_audit_log,public.rbac_test_runs FROM PUBLIC,anon,authenticated;
GRANT USAGE ON SCHEMA public TO authenticated;
INSERT INTO public.rbac_roles(id,label,system_role) VALUES
 ('superadmin','Superadmin',true),('research_head','Research Head',true),
 ('operations_admin','Operations Admin',true),('community_moderator','Community Moderator',true),
 ('data_analyst','Data Analyst',true)
ON CONFLICT(id) DO NOTHING;
INSERT INTO public.rbac_role_permissions(role_id,permission) VALUES
 ('superadmin','system.all'),
 ('research_head','dashboard.view'),('research_head','monitoring.view'),('research_head','hazard.view'),('research_head','research.view'),('research_head','research.manage'),('research_head','analytics.view'),
 ('operations_admin','dashboard.view'),('operations_admin','monitoring.view'),('operations_admin','hazard.view'),('operations_admin','advisory.manage'),('operations_admin','evacuation.manage'),('operations_admin','hotline.manage'),('operations_admin','analytics.view'),
 ('community_moderator','dashboard.view'),('community_moderator','monitoring.view'),('community_moderator','reports.moderate'),('community_moderator','residents.manage'),
 ('data_analyst','dashboard.view'),('data_analyst','monitoring.view'),('data_analyst','hazard.view'),('data_analyst','analytics.view'),('data_analyst','research.view')
ON CONFLICT DO NOTHING;
CREATE OR REPLACE FUNCTION public.rbac_can(p_permission text)
RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER SET search_path=''
AS $$
 SELECT EXISTS(
  SELECT 1 FROM public.rbac_admin_assignments a
  JOIN public.rbac_role_permissions p ON p.role_id=a.role_id
  WHERE a.user_id=(SELECT auth.uid())
  AND (p.permission='system.all' OR p.permission=p_permission)
 )
$$;
REVOKE ALL ON FUNCTION public.rbac_can(text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.rbac_can(text) TO authenticated;

CREATE OR REPLACE FUNCTION public.rbac_my_access()
RETURNS jsonb LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=''
AS $$
DECLARE result jsonb;
BEGIN
 IF auth.uid() IS NULL THEN RAISE EXCEPTION 'Authentication required' USING ERRCODE='42501'; END IF;
 SELECT jsonb_build_object(
  'roles',COALESCE((SELECT jsonb_agg(a.role_id) FROM public.rbac_admin_assignments a WHERE a.user_id=auth.uid()),'[]'::jsonb),
  'permissions',COALESCE((SELECT jsonb_agg(DISTINCT rp.permission)
   FROM public.rbac_admin_assignments a JOIN public.rbac_role_permissions rp ON rp.role_id=a.role_id
   WHERE a.user_id=auth.uid()),'[]'::jsonb)
 ) INTO result;
 RETURN result;
END $$;
REVOKE ALL ON FUNCTION public.rbac_my_access() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.rbac_my_access() TO authenticated;

CREATE OR REPLACE FUNCTION public.rbac_list_accounts()
RETURNS TABLE(user_id uuid,email text,role_id text,assigned_at timestamptz)
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=''
AS $$
BEGIN
 IF NOT public.rbac_can('system.all') THEN RAISE EXCEPTION 'Forbidden' USING ERRCODE='42501'; END IF;
 RETURN QUERY SELECT a.user_id,u.email::text,a.role_id,a.assigned_at
 FROM public.rbac_admin_assignments a JOIN auth.users u ON u.id=a.user_id
 ORDER BY u.email,a.role_id;
END $$;
REVOKE ALL ON FUNCTION public.rbac_list_accounts() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.rbac_list_accounts() TO authenticated;

CREATE OR REPLACE FUNCTION public.rbac_set_assignment(p_email text,p_role text,p_enabled boolean)
RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=''
AS $$
DECLARE target uuid; current_actor uuid:=auth.uid();
BEGIN
 IF current_actor IS NULL OR NOT public.rbac_can('system.all') THEN RAISE EXCEPTION 'Forbidden' USING ERRCODE='42501'; END IF;
 SELECT id INTO target FROM auth.users WHERE lower(email)=lower(trim(p_email));
 IF target IS NULL THEN RAISE EXCEPTION 'Auth account not found. Invite or create the account in Supabase Auth first.'; END IF;
 IF NOT EXISTS(SELECT 1 FROM public.rbac_roles WHERE id=p_role) THEN RAISE EXCEPTION 'Unknown role'; END IF;
 IF NOT p_enabled AND p_role='superadmin' THEN
  IF target=current_actor THEN RAISE EXCEPTION 'Cannot remove your own Superadmin role'; END IF;
  IF (SELECT count(DISTINCT user_id) FROM public.rbac_admin_assignments WHERE role_id='superadmin') <= 1 THEN
   RAISE EXCEPTION 'Cannot remove the last Superadmin';
  END IF;
 END IF;
 IF p_enabled THEN
  INSERT INTO public.rbac_admin_assignments(user_id,role_id,assigned_by)
  VALUES(target,p_role,current_actor) ON CONFLICT(user_id,role_id) DO NOTHING;
 ELSE
  DELETE FROM public.rbac_admin_assignments WHERE user_id=target AND role_id=p_role;
 END IF;
 INSERT INTO public.rbac_audit_log(actor,action,target_user,detail)
 VALUES(current_actor,'assignment_changed',target,jsonb_build_object('role',p_role,'enabled',p_enabled));
END $$;
REVOKE ALL ON FUNCTION public.rbac_set_assignment(text,text,boolean) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.rbac_set_assignment(text,text,boolean) TO authenticated;

CREATE OR REPLACE FUNCTION public.rbac_log_test(p_kind text,p_payload jsonb)
RETURNS bigint LANGUAGE plpgsql SECURITY DEFINER SET search_path=''
AS $$
DECLARE result bigint;
BEGIN
 IF NOT public.rbac_can('system.all') THEN RAISE EXCEPTION 'Forbidden' USING ERRCODE='42501'; END IF;
 IF p_kind NOT IN ('river_level','advisory_preview') OR pg_catalog.length(p_payload::text)>12000 THEN
   RAISE EXCEPTION 'Invalid simulation record';
 END IF;
 INSERT INTO public.rbac_test_runs(actor,kind,payload)
 VALUES(auth.uid(),p_kind,p_payload) RETURNING id INTO result;
 RETURN result;
END $$;
REVOKE ALL ON FUNCTION public.rbac_log_test(text,jsonb) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.rbac_log_test(text,jsonb) TO authenticated;

CREATE OR REPLACE FUNCTION public.rbac_list_tests()
RETURNS TABLE(id bigint,kind text,payload jsonb,created_at timestamptz,actor uuid)
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=''
AS $$
BEGIN
 IF NOT public.rbac_can('system.all') THEN RAISE EXCEPTION 'Forbidden' USING ERRCODE='42501'; END IF;
 RETURN QUERY SELECT t.id,t.kind,t.payload,t.created_at,t.actor FROM public.rbac_test_runs t ORDER BY t.id DESC LIMIT 100;
END $$;
REVOKE ALL ON FUNCTION public.rbac_list_tests() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.rbac_list_tests() TO authenticated;


CREATE OR REPLACE FUNCTION public.rbac_create_custom_role(p_id text,p_label text,p_permissions text[])
RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=''
AS $$
DECLARE perm text;
BEGIN
 IF NOT public.rbac_can('system.all') THEN RAISE EXCEPTION 'Forbidden' USING ERRCODE='42501'; END IF;
 IF p_id !~ '^custom_[a-z0-9_]{3,45}$' OR pg_catalog.length(trim(p_label))<3 THEN RAISE EXCEPTION 'Invalid custom role name'; END IF;
 IF coalesce(array_length(p_permissions,1),0)>40 THEN RAISE EXCEPTION 'Too many permissions'; END IF;
 IF EXISTS(SELECT 1 FROM unnest(p_permissions) AS p
    WHERE p NOT IN ('dashboard.view','monitoring.view','hazard.view','research.view','research.manage',
     'analytics.view','advisory.manage','evacuation.manage','hotline.manage','reports.moderate','residents.manage')) THEN
   RAISE EXCEPTION 'Unsupported or privileged permission';
 END IF;
 INSERT INTO public.rbac_roles(id,label,system_role) VALUES(p_id,trim(p_label),false);
 FOREACH perm IN ARRAY p_permissions LOOP
  INSERT INTO public.rbac_role_permissions(role_id,permission) VALUES(p_id,perm) ON CONFLICT DO NOTHING;
 END LOOP;
 INSERT INTO public.rbac_audit_log(actor,action,detail)
 VALUES(auth.uid(),'custom_role_created',jsonb_build_object('role',p_id,'permissions',to_jsonb(p_permissions)));
END $$;
REVOKE ALL ON FUNCTION public.rbac_create_custom_role(text,text,text[]) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.rbac_create_custom_role(text,text,text[]) TO authenticated;

CREATE OR REPLACE FUNCTION public.rbac_list_roles()
RETURNS TABLE(id text,label text,system_role boolean)
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=''
AS $$
BEGIN
 IF NOT public.rbac_can('system.all') THEN RAISE EXCEPTION 'Forbidden' USING ERRCODE='42501'; END IF;
 RETURN QUERY SELECT r.id,r.label,r.system_role FROM public.rbac_roles r ORDER BY r.id;
END $$;
REVOKE ALL ON FUNCTION public.rbac_list_roles() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.rbac_list_roles() TO authenticated;

-- Existing older privilege-bearing RPCs must be reviewed and updated separately:
-- public.admin_toggle_suspend_user, public.admin_delete_user, public.admin_update_user,
-- public.get_all_users_with_roles, public.admin_moderate_report and any operational CRUD tables.
-- DO NOT treat this foundation as full database authorization of legacy functions.
COMMIT;
