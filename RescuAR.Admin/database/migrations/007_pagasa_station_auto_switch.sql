-- Install on the EXISTING Supabase database before deploying the telemetry worker.
-- This function is called only by the server-side service_role. Switching a
-- recognized alternate preserves both station identities and their history;
-- an exact-name code replacement updates the catalog code, not historic rows.
-- Review table constraints/FKs before applying; this is not a schema bootstrap.
CREATE OR REPLACE FUNCTION public.pagasa_apply_station_switch(
  p_old_code text,
  p_new_code text,
  p_expected_source_name text,
  p_mode text
)
RETURNS boolean
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
  previous public.station_catalog%ROWTYPE;
  alternative public.station_catalog%ROWTYPE;
BEGIN
  IF auth.role() IS DISTINCT FROM 'service_role' THEN
    RAISE EXCEPTION 'Only the telemetry service may switch PAGASA station mappings'
      USING ERRCODE = '42501';
  END IF;
  IF p_old_code !~ '^\d{8}$' OR p_new_code !~ '^\d{8}$' OR
     p_old_code = p_new_code OR p_mode NOT IN ('code_change', 'alternate') OR
     NULLIF(trim(p_expected_source_name), '') IS NULL THEN
    RAISE EXCEPTION 'Invalid PAGASA station switch request';
  END IF;

  -- Serialize station mapping updates across concurrent telemetry processes.
  PERFORM pg_advisory_xact_lock(hashtext('PAGASA_FFWWS station catalog switch'));
  SELECT * INTO previous
  FROM public.station_catalog
  WHERE source = 'PAGASA_FFWWS' AND station_code = p_old_code
  FOR UPDATE;

  IF NOT FOUND OR previous.enabled IS DISTINCT FROM true OR
     previous.source_station_name IS DISTINCT FROM p_expected_source_name THEN
    RETURN false;
  END IF;

  IF p_mode = 'code_change' THEN
    -- Never take over an existing station code, even if disabled.
    IF EXISTS (
      SELECT 1 FROM public.station_catalog
      WHERE source = 'PAGASA_FFWWS' AND station_code = p_new_code
    ) THEN
      RETURN false;
    END IF;
    UPDATE public.station_catalog
      SET station_code = p_new_code
      WHERE source = 'PAGASA_FFWWS' AND station_code = p_old_code;
  ELSE
    -- Verified independent gauges for the same named monitoring point.
    IF NOT ((p_old_code = '11104201' AND p_new_code = '11104204') OR
            (p_old_code = '11104204' AND p_new_code = '11104201')) THEN
      RAISE EXCEPTION 'Unapproved PAGASA alternate station switch';
    END IF;
    SELECT * INTO alternative
    FROM public.station_catalog
    WHERE source = 'PAGASA_FFWWS' AND station_code = p_new_code
    FOR UPDATE;
    IF NOT FOUND OR alternative.enabled IS DISTINCT FROM false THEN
      RETURN false;
    END IF;
    UPDATE public.station_catalog
      SET enabled = false, is_primary = false
      WHERE source = 'PAGASA_FFWWS' AND station_code = p_old_code;
    UPDATE public.station_catalog
      SET enabled = true, is_primary = previous.is_primary
      WHERE source = 'PAGASA_FFWWS' AND station_code = p_new_code;
  END IF;
  RETURN true;
END;
$$;

REVOKE ALL ON FUNCTION public.pagasa_apply_station_switch(text,text,text,text) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.pagasa_apply_station_switch(text,text,text,text) FROM anon, authenticated;
GRANT EXECUTE ON FUNCTION public.pagasa_apply_station_switch(text,text,text,text) TO service_role;
COMMENT ON FUNCTION public.pagasa_apply_station_switch(text,text,text,text) IS
  'Atomic, service-role-only PAGASA catalog switch after worker-side source and detail validation.';
