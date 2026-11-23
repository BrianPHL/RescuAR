-- Phase 2: read-only server-backed System Logs foundation.
-- Apply as a privileged project owner from Supabase SQL Editor, AFTER the
-- RBAC foundation and only after confirming the tables below exist.
-- Captures committed advisory and RBAC changes; does not log failed/denied
-- attempts, SMS deliveries, or user login events. These require additional
-- server-side integrations. No browser-supplied "audit write" RPC is exposed.
BEGIN;

DO $preflight$
BEGIN
  IF pg_catalog.to_regclass('public.advisories') IS NULL
    OR pg_catalog.to_regclass('public.rbac_audit_log') IS NULL
    OR pg_catalog.to_regprocedure('public.rbac_can(text)') IS NULL THEN
    RAISE EXCEPTION 'Audit prerequisites absent: advisories, rbac_audit_log and rbac_can(text) are required';
  END IF;
END
$preflight$;

CREATE TABLE IF NOT EXISTS public.web_admin_audit_events (
  id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  recorded_at timestamptz NOT NULL DEFAULT now(),
  actor_id uuid NULL,
  source text NOT NULL CHECK (source IN ('authenticated-db-trigger', 'service-role', 'unattributed')),
  module text NOT NULL CHECK (char_length(module) BETWEEN 1 AND 90),
  event_type text NOT NULL CHECK (char_length(event_type) BETWEEN 1 AND 100),
  outcome text NOT NULL DEFAULT 'succeeded' CHECK (outcome IN ('succeeded')),
  target_type text NOT NULL,
  target_id text NULL,
  details jsonb NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(details) = 'object')
);
CREATE INDEX IF NOT EXISTS web_admin_audit_events_created_idx ON public.web_admin_audit_events (id DESC);
CREATE INDEX IF NOT EXISTS web_admin_audit_events_module_idx ON public.web_admin_audit_events (module, id DESC);

ALTER TABLE public.web_admin_audit_events ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON public.web_admin_audit_events FROM PUBLIC, anon, authenticated;
REVOKE ALL ON SEQUENCE public.web_admin_audit_events_id_seq FROM PUBLIC, anon, authenticated;
-- Supabase Edge/worker service-role writes do not need direct access. Audit
-- entries are inserted only from SECURITY DEFINER database triggers.

CREATE OR REPLACE FUNCTION public.web_admin_audit_capture()
RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $function$
DECLARE
  action_name text;
  audit_module text;
  entity_type text;
  entity_id text;
  event_actor uuid;
  event_source text;
  event_details jsonb;
BEGIN
  event_actor := auth.uid();
  event_source := CASE
    WHEN event_actor IS NOT NULL THEN 'authenticated-db-trigger'
    WHEN auth.role() = 'service_role' THEN 'service-role'
    ELSE 'unattributed' END;

  IF TG_TABLE_NAME = 'advisories' THEN
    IF TG_OP = 'UPDATE' AND pg_catalog.to_jsonb(OLD) = pg_catalog.to_jsonb(NEW) THEN RETURN NEW; END IF;
    action_name := CASE TG_OP WHEN 'INSERT' THEN 'advisory.created'
      WHEN 'UPDATE' THEN 'advisory.updated' ELSE 'advisory.deleted' END;
    audit_module := 'content-advisories';
    entity_type := 'advisory';
    entity_id := CASE WHEN TG_OP = 'DELETE' THEN OLD.id::text ELSE NEW.id::text END;
    -- No advisory text, phone numbers, passwords, or other sensitive content.
    event_details := pg_catalog.jsonb_build_object(
      'status_before', CASE WHEN TG_OP = 'INSERT' THEN NULL ELSE OLD.status END,
      'status_after', CASE WHEN TG_OP = 'DELETE' THEN NULL ELSE NEW.status END);
  ELSIF TG_TABLE_NAME = 'rbac_audit_log' AND TG_OP = 'INSERT' THEN
    action_name := 'rbac.' || NEW.action;
    audit_module := 'rbac-admins';
    entity_type := 'administrator';
    entity_id := NEW.target_user::text;
    event_actor := NEW.actor;
    event_source := CASE WHEN event_actor IS NULL THEN 'unattributed' ELSE 'authenticated-db-trigger' END;
    event_details := pg_catalog.jsonb_build_object(
      'role', NEW.detail ->> 'role',
      'enabled', NEW.detail ->> 'enabled');
  ELSE
    RAISE EXCEPTION 'Unsupported audit trigger target: %', TG_TABLE_NAME;
  END IF;

  INSERT INTO public.web_admin_audit_events (
    actor_id, source, module, event_type, target_type, target_id, details
  ) VALUES (event_actor, event_source, audit_module, action_name, entity_type, entity_id, event_details);
  RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
END
$function$;
REVOKE ALL ON FUNCTION public.web_admin_audit_capture() FROM PUBLIC, anon, authenticated;

DROP TRIGGER IF EXISTS web_admin_audit_advisories ON public.advisories;
CREATE TRIGGER web_admin_audit_advisories
AFTER INSERT OR UPDATE OR DELETE ON public.advisories
FOR EACH ROW EXECUTE FUNCTION public.web_admin_audit_capture();

DROP TRIGGER IF EXISTS web_admin_audit_rbac ON public.rbac_audit_log;
CREATE TRIGGER web_admin_audit_rbac
AFTER INSERT ON public.rbac_audit_log
FOR EACH ROW EXECUTE FUNCTION public.web_admin_audit_capture();

CREATE OR REPLACE FUNCTION public.web_admin_audit_list(
  p_limit integer DEFAULT 26,
  p_before_id bigint DEFAULT NULL,
  p_module text DEFAULT NULL,
  p_event_type text DEFAULT NULL,
  p_actor_id uuid DEFAULT NULL
)
RETURNS TABLE(
  id bigint, recorded_at timestamptz, actor_id uuid, source text,
  module text, event_type text, outcome text, target_type text,
  target_id text, details jsonb
)
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = '' AS $function$
BEGIN
  IF auth.uid() IS NULL OR NOT public.rbac_can('system.all') THEN
    RAISE EXCEPTION 'Administrator audit access required' USING ERRCODE = '42501';
  END IF;
  IF p_limit IS NULL OR p_limit < 1 OR p_limit > 100 OR (p_before_id IS NOT NULL AND p_before_id < 1) THEN
    RAISE EXCEPTION 'Invalid audit pagination request' USING ERRCODE = '22023';
  END IF;
  RETURN QUERY
  SELECT e.id, e.recorded_at, e.actor_id, e.source, e.module, e.event_type,
    e.outcome, e.target_type, e.target_id, e.details
  FROM public.web_admin_audit_events e
  WHERE (p_before_id IS NULL OR e.id < p_before_id)
    AND (p_module IS NULL OR e.module = p_module)
    AND (p_event_type IS NULL OR e.event_type = p_event_type)
    AND (p_actor_id IS NULL OR e.actor_id = p_actor_id)
  ORDER BY e.id DESC
  LIMIT p_limit;
END
$function$;
REVOKE ALL ON FUNCTION public.web_admin_audit_list(integer,bigint,text,text,uuid) FROM PUBLIC, anon, authenticated;
GRANT EXECUTE ON FUNCTION public.web_admin_audit_list(integer,bigint,text,text,uuid) TO authenticated;

COMMENT ON TABLE public.web_admin_audit_events IS
  'Committed advisory and RBAC changes from trusted database triggers; never an SMS delivery log.';
NOTIFY pgrst, 'reload schema';
COMMIT;
