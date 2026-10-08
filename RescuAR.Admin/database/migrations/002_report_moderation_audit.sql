-- Run once as postgres in the inspected Supabase project, after foundation 001.
-- Adds an RPC only. Shared report policies/grants and the current UI are unchanged.
BEGIN;

DO $preflight$
DECLARE
    routine record;
BEGIN
    IF current_user <> 'postgres' THEN
        RAISE EXCEPTION 'Run this migration as postgres in the Supabase SQL Editor';
    END IF;
    IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc p
        JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
        WHERE n.nspname = 'public' AND p.proname = 'admin_moderate_report') THEN
        RAISE EXCEPTION 'Moderation RPC already exists or name conflicts; inspect before proceeding';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class c
        WHERE c.oid = pg_catalog.to_regclass('public.community_reports')
            AND c.relkind = 'r' AND c.relrowsecurity
            AND pg_catalog.pg_get_userbyid(c.relowner) = 'postgres')
        OR EXISTS (SELECT 1 FROM (VALUES ('id'), ('status')) required(column_name)
            WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                WHERE a.attrelid = pg_catalog.to_regclass('public.community_reports')
                    AND a.attname = required.column_name AND NOT a.attisdropped
                    AND a.atttypid = 'text'::regtype AND a.attgenerated = ''))
        OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c
            WHERE c.conrelid = pg_catalog.to_regclass('public.community_reports')
                AND c.contype = 'p' AND pg_catalog.pg_get_constraintdef(c.oid) = 'PRIMARY KEY (id)')
        OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
            WHERE t.tgrelid = pg_catalog.to_regclass('public.community_reports') AND NOT t.tgisinternal) THEN
        RAISE EXCEPTION 'Report table contract or triggers changed; request a fresh moderation schema report';
    END IF;
    -- Match the supplied 2026-10-07 report, ignoring body whitespace only.
    FOR routine IN SELECT * FROM (VALUES
        ('public.web_admin_access()', true, 'ad8b8ac3c0880fb3d370908b1c24b720'),
        ('audit_private.require_admin()', false, 'fe9c3f863b99691cc89aed0b8db129e0'),
        ('audit_private.append_event(uuid,uuid,text,text,text,text,text,text,jsonb)', false, '590acd7b43b4f1c734fe4e8d255225bd')
    ) expected(signature, definer, fingerprint) LOOP
        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc p
            WHERE p.oid = pg_catalog.to_regprocedure(routine.signature)
                AND pg_catalog.pg_get_userbyid(p.proowner) = 'postgres'
                AND p.prosecdef = routine.definer AND p.proconfig = ARRAY['search_path=""']::text[]
                AND pg_catalog.md5(pg_catalog.regexp_replace(p.prosrc, '\s+', ' ', 'g')) = routine.fingerprint) THEN
            RAISE EXCEPTION 'Reviewed audit helper changed: %. Request a fresh schema report', routine.signature;
        END IF;
    END LOOP;
    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class c
        WHERE c.oid = pg_catalog.to_regclass('audit_private.events')
            AND c.relrowsecurity AND pg_catalog.pg_get_userbyid(c.relowner) = 'postgres')
        OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c
            WHERE c.conrelid = pg_catalog.to_regclass('audit_private.events')
                AND c.conname = 'events_retry_key' AND c.contype = 'u'
                AND pg_catalog.pg_get_constraintdef(c.oid) = 'UNIQUE (actor_id, operation_id, event_type, source)') THEN
        RAISE EXCEPTION 'Audit foundation storage changed or is missing; inspect before proceeding';
    END IF;
END;
$preflight$;

CREATE FUNCTION public.admin_moderate_report(
    p_operation_id uuid, p_report_id text, p_expected_status text, p_new_status text
) RETURNS TABLE (report_id text, before_status text, after_status text,
    outcome text, audit_event_id uuid, recorded_at timestamptz)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $function$
DECLARE
    actor uuid := audit_private.require_admin();
    previous_status text;
    persisted_status text;
    event_kind text;
    event_outcome text;
    event_id uuid;
    existing audit_private.events%ROWTYPE;
BEGIN
    IF p_operation_id IS NULL OR p_report_id IS NULL
        OR p_report_id !~ '^[A-Za-z0-9][A-Za-z0-9_:-]{0,127}$'
        OR p_new_status IS NULL OR p_new_status NOT IN ('Approved', 'Rejected', 'Resolved')
        OR (p_expected_status IS NOT NULL
            AND p_expected_status NOT IN ('Pending', 'Approved', 'Rejected', 'Resolved')) THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Invalid report moderation request';
    END IF;
    event_kind := CASE p_new_status WHEN 'Approved' THEN 'report.approved'
        WHEN 'Rejected' THEN 'report.rejected' ELSE 'report.resolved' END;
    -- Serialize identical/conflicting retries even when they target different rows.
    -- The lock identity is derived inside the RPC, never supplied as audit identity.
    PERFORM pg_catalog.pg_advisory_xact_lock(
        pg_catalog.hashtextextended(actor::text || ':' || p_operation_id::text, 0));
    SELECT e.* INTO existing FROM audit_private.events e
    WHERE e.actor_id = actor AND e.operation_id = p_operation_id AND e.source = 'database'
        AND e.event_type IN ('report.approved', 'report.rejected', 'report.resolved');
    IF FOUND THEN
        IF existing.module IS DISTINCT FROM 'community-reports-moderation'
            OR existing.target_type IS DISTINCT FROM 'report'
            OR existing.target_id IS DISTINCT FROM p_report_id
            OR existing.event_type IS DISTINCT FROM event_kind
            OR existing.details IS DISTINCT FROM pg_catalog.jsonb_build_object(
                'before_status', p_expected_status, 'after_status', p_new_status)
            OR existing.outcome IS DISTINCT FROM
                (CASE WHEN p_expected_status IS NOT DISTINCT FROM p_new_status
                    THEN 'no_change' ELSE 'succeeded' END) THEN
            RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Operation ID reused with different moderation data';
        END IF;
        -- Historical receipt: a retry never reapplies or overwrites a later decision.
        RETURN QUERY SELECT existing.target_id, existing.details ->> 'before_status',
            existing.details ->> 'after_status', existing.outcome, existing.id, existing.recorded_at;
        RETURN;
    END IF;

    SELECT r.status INTO previous_status FROM public.community_reports r
    WHERE r.id = p_report_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'P0002', MESSAGE = 'Report not found';
    END IF;
    IF previous_status IS DISTINCT FROM p_expected_status THEN
        RAISE EXCEPTION USING ERRCODE = '40001', MESSAGE = 'Report status changed; refresh before deciding again';
    END IF;
    event_outcome := CASE WHEN previous_status IS DISTINCT FROM p_new_status
        THEN 'succeeded' ELSE 'no_change' END;
    IF previous_status IS DISTINCT FROM p_new_status THEN
        UPDATE public.community_reports r SET status = p_new_status
        WHERE r.id = p_report_id RETURNING r.status INTO persisted_status;
        IF NOT FOUND OR persisted_status IS DISTINCT FROM p_new_status THEN
            RAISE EXCEPTION USING ERRCODE = '55000', MESSAGE = 'Report status change could not be confirmed';
        END IF;
    END IF;
    event_id := audit_private.append_event(p_operation_id, actor, event_kind,
        'community-reports-moderation', 'database', event_outcome, 'report', p_report_id,
        pg_catalog.jsonb_build_object('before_status', previous_status, 'after_status', p_new_status));
    SELECT e.* INTO STRICT existing FROM audit_private.events e WHERE e.id = event_id;
    RETURN QUERY SELECT existing.target_id, existing.details ->> 'before_status',
        existing.details ->> 'after_status', existing.outcome, existing.id, existing.recorded_at;
END;
$function$;

COMMENT ON FUNCTION public.admin_moderate_report(uuid,text,text,text) IS
    'RescuAR report moderation audit v1; guarded atomic decision and retry receipt';
REVOKE ALL ON FUNCTION public.admin_moderate_report(uuid,text,text,text)
    FROM PUBLIC, anon, authenticated, service_role;
GRANT EXECUTE ON FUNCTION public.admin_moderate_report(uuid,text,text,text) TO authenticated;
NOTIFY pgrst, 'reload schema';
COMMIT;
