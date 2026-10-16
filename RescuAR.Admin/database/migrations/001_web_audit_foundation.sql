-- Run once, as postgres, in the same Supabase project inspected in step 1.
-- The transaction rolls back completely if preconditions or creation fail.
BEGIN;

DO $preflight$
DECLARE
    routine record;
BEGIN
    IF current_user <> 'postgres' THEN
        RAISE EXCEPTION 'Run this migration as postgres in the Supabase SQL Editor';
    END IF;
    IF pg_catalog.to_regnamespace('audit_private') IS NOT NULL
        OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc p
            JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = 'public' AND p.proname IN
                ('web_admin_access', 'record_web_audit_event', 'get_web_audit_logs')) THEN
        RAISE EXCEPTION 'Audit foundation already exists or names conflict; inspect before proceeding';
    END IF;
    IF pg_catalog.to_regprocedure('auth.uid()') IS NULL THEN
        RAISE EXCEPTION 'Missing auth.uid(); inspect the deployed schema again';
    END IF;
    IF (SELECT count(*) FROM pg_catalog.pg_proc p
        JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
        WHERE n.nspname = 'public' AND p.proname IN ('admin_delete_user',
            'admin_toggle_suspend_user', 'admin_update_user', 'get_all_users_with_roles')) <> 4 THEN
        RAISE EXCEPTION 'User RPCs are missing or overloaded; inspect the deployed schema again';
    END IF;
    -- Exact inspected definitions, ignoring whitespace only. Prevent overwriting drift.
    FOR routine IN SELECT * FROM (VALUES
        ('public.admin_delete_user(uuid)', '8dcdd25fb4f7cd4d2013044b4c5af134'),
        ('public.admin_toggle_suspend_user(uuid,text)', 'a2570f927539076378ef3693bc5cada6'),
        ('public.admin_update_user(uuid,jsonb)', '7e98254734b9bdcd26a9fbbf2145770c'),
        ('public.get_all_users_with_roles()', '22c3da0cc1d915a04605ffcb67dd7f54')
    ) AS expected(signature, fingerprint) LOOP
        IF pg_catalog.to_regprocedure(routine.signature) IS NULL
            OR (SELECT pg_catalog.pg_get_userbyid(p.proowner) FROM pg_catalog.pg_proc p
                WHERE p.oid = pg_catalog.to_regprocedure(routine.signature)) <> 'postgres'
            OR pg_catalog.md5(pg_catalog.regexp_replace(
                pg_catalog.pg_get_functiondef(pg_catalog.to_regprocedure(routine.signature)),
                '\s+', ' ', 'g')) <> routine.fingerprint THEN
            RAISE EXCEPTION 'Inspected RPC definition changed: %. Request a fresh schema report', routine.signature;
        END IF;
    END LOOP;
    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute
        WHERE attrelid = pg_catalog.to_regclass('public.admin_roles')
            AND attname = 'user_id' AND atttypid = 'uuid'::regtype AND NOT attisdropped) THEN
        RAISE EXCEPTION 'Administrator membership contract changed';
    END IF;
    IF EXISTS (
        SELECT 1 FROM (VALUES
            ('auth.users', 'id', 'uuid'), ('auth.users', 'created_at', 'timestamptz'),
            ('public.users', 'id', 'uuid'), ('public.users', 'username', 'text'),
            ('public.users', 'first_name', 'text'), ('public.users', 'middle_name', 'text'),
            ('public.users', 'last_name', 'text'), ('public.users', 'phone_number', 'text'),
            ('public.users', 'address', 'text'), ('public.users', 'avatar_url', 'text'),
            ('public.users', 'blood_type', 'text'), ('public.users', 'allergies', 'text'),
            ('public.users', 'emergency_contact1_name', 'text'),
            ('public.users', 'emergency_contact1_phone', 'text'), ('public.users', 'status', 'text')
        ) AS required(relation_name, column_name, type_name)
        WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
            WHERE a.attrelid = pg_catalog.to_regclass(required.relation_name)
                AND a.attname = required.column_name AND NOT a.attisdropped
                AND a.atttypid = pg_catalog.to_regtype(required.type_name))
    ) OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
        WHERE a.attrelid = pg_catalog.to_regclass('auth.users') AND a.attname = 'email'
            AND NOT a.attisdropped AND a.atttypid IN ('text'::regtype, 'varchar'::regtype)) THEN
        RAISE EXCEPTION 'User table contract changed; inspect the deployed schema again';
    END IF;
END;
$preflight$;

CREATE SCHEMA audit_private AUTHORIZATION postgres;
COMMENT ON SCHEMA audit_private IS 'RescuAR web audit foundation v1';
REVOKE ALL ON SCHEMA audit_private FROM PUBLIC, anon, authenticated, service_role;

CREATE TABLE audit_private.events (
    id uuid PRIMARY KEY DEFAULT pg_catalog.gen_random_uuid(),
    recorded_at timestamptz NOT NULL DEFAULT pg_catalog.clock_timestamp(),
    operation_id uuid NOT NULL,
    -- Intentionally no account FK: history survives deleted accounts.
    actor_id uuid NOT NULL,
    event_type text NOT NULL CHECK (event_type ~ '^[a-z][a-z0-9_.]{1,79}$'),
    module text NOT NULL CHECK (module IN ('authentication', 'dashboard',
        'monitoring-stations', 'monitoring-river-level', 'monitoring-inundation',
        'community-reports-moderation', 'community-residents', 'community-sms-parser',
        'community-user-management', 'content-advisories', 'content-evacuation',
        'content-news', 'content-hotlines', 'system-settings', 'system-logs',
        'documentation', 'audit-logs')),
    source text NOT NULL CHECK (source IN ('client_observed', 'database')),
    outcome text NOT NULL CHECK (outcome IN ('requested', 'observed', 'succeeded',
        'failed', 'denied', 'cancelled', 'no_change', 'simulated', 'unknown')),
    target_type text CHECK (target_type IN ('user', 'advisory', 'report',
        'evacuation_center', 'hotline', 'station', 'module', 'draft', 'audit_event')),
    target_id text CHECK (target_id ~ '^[A-Za-z0-9][A-Za-z0-9_:-]{0,127}$'),
    details jsonb NOT NULL DEFAULT '{}'::jsonb CHECK (
        pg_catalog.jsonb_typeof(details) = 'object'
        AND pg_catalog.octet_length(details::text) <= 4096),
    CONSTRAINT events_retry_key UNIQUE (actor_id, operation_id, event_type, source)
);
ALTER TABLE audit_private.events ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON TABLE audit_private.events FROM PUBLIC, anon, authenticated, service_role;
CREATE INDEX events_time_idx ON audit_private.events (recorded_at DESC, id DESC);
CREATE INDEX events_actor_time_idx ON audit_private.events (actor_id, recorded_at DESC, id DESC);
CREATE INDEX events_module_time_idx ON audit_private.events (module, recorded_at DESC, id DESC);

CREATE FUNCTION audit_private.prevent_event_change() RETURNS trigger
LANGUAGE plpgsql SET search_path = '' AS $function$
BEGIN
    RAISE EXCEPTION USING ERRCODE = '55000', MESSAGE = 'Audit history is append-only';
END;
$function$;
CREATE TRIGGER events_no_update_delete BEFORE UPDATE OR DELETE ON audit_private.events
    FOR EACH ROW EXECUTE FUNCTION audit_private.prevent_event_change();
CREATE TRIGGER events_no_truncate BEFORE TRUNCATE ON audit_private.events
    FOR EACH STATEMENT EXECUTE FUNCTION audit_private.prevent_event_change();

CREATE FUNCTION public.web_admin_access() RETURNS boolean
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
    SELECT auth.uid() IS NOT NULL AND EXISTS (
        SELECT 1 FROM public.admin_roles ar WHERE ar.user_id = auth.uid()
    );
$function$;

CREATE FUNCTION audit_private.require_admin() RETURNS uuid
LANGUAGE plpgsql STABLE SET search_path = '' AS $function$
DECLARE
    actor uuid := auth.uid();
BEGIN
    IF actor IS NULL OR NOT public.web_admin_access() THEN
        RAISE EXCEPTION USING ERRCODE = '42501', MESSAGE = 'Administrator access required';
    END IF;
    RETURN actor;
END;
$function$;

CREATE FUNCTION audit_private.append_event(
    p_operation_id uuid, p_actor_id uuid, p_event_type text, p_module text,
    p_source text, p_outcome text, p_target_type text DEFAULT NULL,
    p_target_id text DEFAULT NULL, p_details jsonb DEFAULT '{}'::jsonb
) RETURNS uuid
LANGUAGE plpgsql SET search_path = '' AS $function$
DECLARE
    event_id uuid;
    existing audit_private.events%ROWTYPE;
BEGIN
    INSERT INTO audit_private.events (operation_id, actor_id, event_type, module,
        source, outcome, target_type, target_id, details)
    VALUES (p_operation_id, p_actor_id, p_event_type, p_module, p_source,
        p_outcome, p_target_type, p_target_id, p_details)
    ON CONFLICT ON CONSTRAINT events_retry_key DO NOTHING RETURNING id INTO event_id;
    IF event_id IS NULL THEN
        SELECT * INTO STRICT existing FROM audit_private.events e
        WHERE e.actor_id = p_actor_id AND e.operation_id = p_operation_id
            AND e.event_type = p_event_type AND e.source = p_source;
        IF existing.module IS DISTINCT FROM p_module
            OR existing.outcome IS DISTINCT FROM p_outcome
            OR existing.target_type IS DISTINCT FROM p_target_type
            OR existing.target_id IS DISTINCT FROM p_target_id
            OR existing.details IS DISTINCT FROM p_details THEN
            RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Operation ID reused with different event data';
        END IF;
        event_id := existing.id;
    END IF;
    RETURN event_id;
END;
$function$;

CREATE FUNCTION public.record_web_audit_event(
    p_operation_id uuid, p_event_type text, p_module text,
    p_target_type text DEFAULT NULL, p_target_id text DEFAULT NULL,
    p_details jsonb DEFAULT '{}'::jsonb
) RETURNS uuid
LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $function$
DECLARE
    actor uuid := audit_private.require_admin();
    pair record;
    event_outcome text := 'observed';
BEGIN
    IF p_operation_id IS NULL OR p_event_type IS NULL OR p_event_type NOT IN (
        'module.opened', 'records.listed', 'record.viewed', 'report.media_opened',
        'records.queried', 'module.refreshed', 'draft.opened', 'draft.template_applied',
        'task.requested', 'task.completed', 'task.failed', 'task.cancelled',
        'analysis.controls_changed', 'dashboard.export_requested', 'dashboard.export_generated',
        'prediction.requested', 'prediction.simulation_run', 'advisory.copy_requested',
        'asset.upload_requested', 'asset.upload_completed', 'settings.save_requested',
        'notification.simulation_run', 'audit.viewed', 'audit.queried',
        'audit.refreshed', 'audit.export_generated', 'auth.logout_requested') THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Unsupported client event or missing operation ID';
    END IF;
    IF p_module IS NULL OR p_details IS NULL OR pg_catalog.jsonb_typeof(p_details) <> 'object'
        OR pg_catalog.octet_length(p_details::text) > 4096 THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Invalid module or event details';
    END IF;
    -- Bounded UI context only. Never accept actor/time/source or raw user content.
    FOR pair IN SELECT * FROM pg_catalog.jsonb_each(p_details) LOOP
        IF pair.key IN ('returned_count', 'selected_count', 'page', 'page_size') THEN
            IF pg_catalog.jsonb_typeof(pair.value) <> 'number'
                OR pair.value::text !~ '^[0-9]{1,7}$' THEN
                RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Invalid numeric event detail';
            END IF;
        ELSIF pair.key = 'filter_applied' THEN
            IF pg_catalog.jsonb_typeof(pair.value) <> 'boolean' THEN
                RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Invalid filter detail';
            END IF;
        ELSIF pair.key = 'mode' THEN
            IF pair.value NOT IN ('"live"'::jsonb, '"simulation"'::jsonb) THEN
                RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Invalid mode detail';
            END IF;
        ELSIF pair.key IN ('template_id', 'sort_key') THEN
            IF pg_catalog.jsonb_typeof(pair.value) <> 'string'
                OR (pair.value #>> '{}') !~ '^[A-Za-z0-9][A-Za-z0-9_:-]{0,63}$' THEN
                RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Invalid identifier detail';
            END IF;
        ELSE
            RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Unsupported event detail';
        END IF;
    END LOOP;
    IF p_event_type IN ('task.requested', 'dashboard.export_requested', 'prediction.requested',
        'advisory.copy_requested', 'asset.upload_requested', 'settings.save_requested', 'auth.logout_requested') THEN
        event_outcome := 'requested';
    ELSIF p_event_type IN ('prediction.simulation_run', 'notification.simulation_run') THEN
        event_outcome := 'simulated';
    ELSIF p_event_type = 'task.cancelled' THEN
        event_outcome := 'cancelled';
    END IF;
    RETURN audit_private.append_event(p_operation_id, actor, p_event_type, p_module,
        'client_observed', event_outcome, p_target_type, p_target_id, p_details);
END;
$function$;

CREATE FUNCTION public.get_web_audit_logs(
    p_limit integer DEFAULT 50, p_before_at timestamptz DEFAULT NULL,
    p_before_id uuid DEFAULT NULL, p_module text DEFAULT NULL,
    p_actor_id uuid DEFAULT NULL, p_event_type text DEFAULT NULL
) RETURNS TABLE (id uuid, recorded_at timestamptz, operation_id uuid, actor_id uuid,
    event_type text, module text, source text, outcome text, target_type text,
    target_id text, details jsonb)
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = '' AS $function$
BEGIN
    PERFORM audit_private.require_admin();
    IF p_limit IS NULL OR p_limit NOT BETWEEN 1 AND 200
        OR (p_before_at IS NULL) <> (p_before_id IS NULL) THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Invalid page size or incomplete cursor';
    END IF;
    RETURN QUERY SELECT e.id, e.recorded_at, e.operation_id, e.actor_id,
        e.event_type, e.module, e.source, e.outcome, e.target_type, e.target_id, e.details
    FROM audit_private.events e
    WHERE (p_before_at IS NULL OR (e.recorded_at, e.id) < (p_before_at, p_before_id))
        AND (p_module IS NULL OR e.module = p_module)
        AND (p_actor_id IS NULL OR e.actor_id = p_actor_id)
        AND (p_event_type IS NULL OR e.event_type = p_event_type)
    ORDER BY e.recorded_at DESC, e.id DESC LIMIT p_limit;
END;
$function$;

CREATE OR REPLACE FUNCTION public.admin_update_user(target_user_id uuid, update_data jsonb)
RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $function$
DECLARE
    actor uuid := audit_private.require_admin();
    before_state jsonb;
    field_name text;
    changed_fields text[] := ARRAY[]::text[];
BEGIN
    IF target_user_id IS NULL OR update_data IS NULL OR pg_catalog.jsonb_typeof(update_data) <> 'object' THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Invalid user update';
    END IF;
    FOR field_name IN SELECT pg_catalog.jsonb_object_keys(update_data) LOOP
        IF field_name NOT IN ('first_name', 'last_name', 'phone_number', 'address')
            OR pg_catalog.jsonb_typeof(update_data -> field_name) NOT IN ('string', 'null') THEN
            RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Unsupported user field or value';
        END IF;
    END LOOP;
    SELECT pg_catalog.jsonb_build_object('first_name', u.first_name, 'last_name', u.last_name,
        'phone_number', u.phone_number, 'address', u.address)
    INTO before_state FROM public.users u WHERE u.id = target_user_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'P0002', MESSAGE = 'User profile not found';
    END IF;
    FOR field_name IN SELECT pg_catalog.jsonb_object_keys(update_data) LOOP
        IF before_state -> field_name IS DISTINCT FROM update_data -> field_name THEN
            changed_fields := pg_catalog.array_append(changed_fields, field_name);
        END IF;
    END LOOP;
    IF pg_catalog.cardinality(changed_fields) > 0 THEN
        UPDATE public.users u SET
            first_name = CASE WHEN update_data ? 'first_name' THEN update_data ->> 'first_name' ELSE u.first_name END,
            last_name = CASE WHEN update_data ? 'last_name' THEN update_data ->> 'last_name' ELSE u.last_name END,
            phone_number = CASE WHEN update_data ? 'phone_number' THEN update_data ->> 'phone_number' ELSE u.phone_number END,
            address = CASE WHEN update_data ? 'address' THEN update_data ->> 'address' ELSE u.address END
        WHERE u.id = target_user_id;
    END IF;
    PERFORM audit_private.append_event(pg_catalog.gen_random_uuid(), actor, 'user.updated',
        'community-user-management', 'database',
        CASE WHEN pg_catalog.cardinality(changed_fields) > 0 THEN 'succeeded' ELSE 'no_change' END,
        'user', target_user_id::text, pg_catalog.jsonb_build_object('changed_fields', changed_fields));
END;
$function$;

CREATE OR REPLACE FUNCTION public.admin_toggle_suspend_user(target_user_id uuid, new_status text)
RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $function$
DECLARE
    actor uuid := audit_private.require_admin();
    before_status text;
BEGIN
    IF target_user_id IS NULL OR new_status IS NULL OR new_status NOT IN ('active', 'suspended') THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Invalid user status change';
    END IF;
    SELECT u.status INTO before_status FROM public.users u WHERE u.id = target_user_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'P0002', MESSAGE = 'User profile not found';
    END IF;
    IF before_status IS DISTINCT FROM new_status THEN
        UPDATE public.users SET status = new_status WHERE id = target_user_id;
    END IF;
    PERFORM audit_private.append_event(pg_catalog.gen_random_uuid(), actor, 'user.status_changed',
        'community-user-management', 'database',
        CASE WHEN before_status IS DISTINCT FROM new_status THEN 'succeeded' ELSE 'no_change' END,
        'user', target_user_id::text, pg_catalog.jsonb_build_object(
            'before_status', CASE WHEN before_status IN ('active', 'suspended') THEN before_status
                WHEN before_status IS NULL THEN NULL ELSE 'unrecognized' END, 'after_status', new_status));
END;
$function$;

CREATE OR REPLACE FUNCTION public.admin_delete_user(target_user_id uuid)
RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $function$
DECLARE
    actor uuid := audit_private.require_admin();
BEGIN
    IF target_user_id IS NULL THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Missing target user';
    END IF;
    DELETE FROM auth.users WHERE id = target_user_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'P0002', MESSAGE = 'User account not found';
    END IF;
    PERFORM audit_private.append_event(pg_catalog.gen_random_uuid(), actor, 'user.deleted',
        'community-user-management', 'database', 'succeeded', 'user', target_user_id::text);
END;
$function$;

CREATE OR REPLACE FUNCTION public.get_all_users_with_roles()
RETURNS TABLE(id uuid, email text, username text, first_name text, middle_name text,
    last_name text, phone_number text, address text, avatar_url text, blood_type text,
    allergies text, emergency_contact1_name text, emergency_contact1_phone text,
    created_at timestamptz, role text)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $function$
DECLARE
    actor uuid := audit_private.require_admin();
    returned_count bigint;
BEGIN
    RETURN QUERY SELECT au.id, au.email::text, u.username, u.first_name, u.middle_name,
        u.last_name, u.phone_number, u.address, u.avatar_url, u.blood_type, u.allergies,
        u.emergency_contact1_name, u.emergency_contact1_phone, au.created_at,
        CASE WHEN EXISTS (SELECT 1 FROM public.admin_roles ar WHERE ar.user_id = au.id)
            THEN 'admin'::text ELSE 'user'::text END
    FROM auth.users au LEFT JOIN public.users u ON au.id = u.id;
    GET DIAGNOSTICS returned_count = ROW_COUNT;
    PERFORM audit_private.append_event(pg_catalog.gen_random_uuid(), actor, 'users.listed',
        'community-user-management', 'database', 'succeeded', 'module',
        'community-user-management', pg_catalog.jsonb_build_object('returned_count', returned_count));
END;
$function$;

-- Supabase default grants may include all API roles even for new objects.
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA audit_private FROM PUBLIC, anon, authenticated, service_role;
REVOKE ALL ON FUNCTION public.web_admin_access(),
    public.record_web_audit_event(uuid,text,text,text,text,jsonb),
    public.get_web_audit_logs(integer,timestamptz,uuid,text,uuid,text),
    public.admin_update_user(uuid,jsonb), public.admin_toggle_suspend_user(uuid,text),
    public.admin_delete_user(uuid), public.get_all_users_with_roles()
    FROM PUBLIC, anon, authenticated, service_role;
GRANT EXECUTE ON FUNCTION public.web_admin_access(),
    public.record_web_audit_event(uuid,text,text,text,text,jsonb),
    public.get_web_audit_logs(integer,timestamptz,uuid,text,uuid,text),
    public.admin_update_user(uuid,jsonb), public.admin_toggle_suspend_user(uuid,text),
    public.admin_delete_user(uuid), public.get_all_users_with_roles() TO authenticated;

-- Membership is security-sensitive; its existing row policies are unchanged.
REVOKE TRUNCATE, REFERENCES, TRIGGER ON TABLE public.admin_roles FROM PUBLIC, anon, authenticated;
NOTIFY pgrst, 'reload schema';
COMMIT;
