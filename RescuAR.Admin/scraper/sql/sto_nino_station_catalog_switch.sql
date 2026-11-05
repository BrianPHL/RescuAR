-- Source switch: PAGASA MMDA Sto. Niño 11104204 -> Sto.Nino(2nd) 11104201.
-- IMPORTANT: Inspect the original station rows before applying.
-- This file does not modify river_level_history or monitoring_stations.
-- The SELECT must return a row for 11104201; if not, stop and add it
-- to station_catalog using your existing schema first.
SELECT station_code, source_station_name, display_name, enabled, is_primary
FROM public.station_catalog
WHERE source = 'PAGASA_FFWWS'
  AND station_code IN ('11104204', '11104201');

BEGIN;
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM public.station_catalog
    WHERE source = 'PAGASA_FFWWS' AND station_code = '11104201'
  ) THEN
    RAISE EXCEPTION 'Station 11104201 is not present in station_catalog. Add it first using the actual table schema.';
  END IF;
END $$;

UPDATE public.station_catalog
SET enabled = false, is_primary = false
WHERE source = 'PAGASA_FFWWS' AND station_code = '11104204';

UPDATE public.station_catalog
SET enabled = true,
    is_primary = true,
    source_station_name = 'Sto.Nino(2nd)',
    display_name = 'Sto. Niño (2nd) Station'
WHERE source = 'PAGASA_FFWWS' AND station_code = '11104201';
COMMIT;

-- Confirm one active Sto. Niño source, retaining both catalog identities.
SELECT station_code, source_station_name, display_name, enabled, is_primary
FROM public.station_catalog
WHERE source = 'PAGASA_FFWWS'
  AND station_code IN ('11104204', '11104201')
ORDER BY station_code;

-- Verify incoming readings remain correctly separated by source station code.
SELECT station_code, max(observed_at) AS last_observation_at, count(*) AS history_rows
FROM public.river_level_history
WHERE source = 'PAGASA_FFWWS'
  AND station_code IN ('11104204', '11104201')
GROUP BY station_code
ORDER BY station_code;
