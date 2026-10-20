-- Efficient chart access for long PAGASA monitoring history.
-- Daily/hourly average is computed in Supabase, not by downloading >25,000 rows.
-- Uses invoker privileges / existing river_level_history RLS.
CREATE INDEX IF NOT EXISTS idx_pagasa_river_history_chart
  ON public.river_level_history (station_code, source, observed_at);

CREATE OR REPLACE FUNCTION public.pagasa_river_chart(
  p_station_code text,
  p_since timestamptz,
  p_until timestamptz,
  p_bucket text DEFAULT 'day'
)
RETURNS TABLE(observed_at timestamptz, level double precision)
LANGUAGE sql STABLE SECURITY INVOKER
SET search_path = public
AS $$
  WITH readings AS (
    SELECT h.observed_at, h.level::double precision AS level
    FROM public.river_level_history AS h
    WHERE h.station_code = p_station_code
      AND h.source = 'PAGASA_FFWWS'
      AND h.observed_at BETWEEN p_since AND p_until
      AND h.level IS NOT NULL
  ), summary AS (
    SELECT
      CASE WHEN p_bucket = 'raw' THEN r.observed_at
        ELSE date_trunc(
          CASE WHEN p_bucket = 'hour' THEN 'hour' ELSE 'day' END,
          r.observed_at AT TIME ZONE 'Asia/Manila'
        ) AT TIME ZONE 'Asia/Manila'
      END AS bucket_at,
      AVG(r.level) AS avg_level
    FROM readings AS r
    GROUP BY 1
  ), bucket_grid AS (
    SELECT generate_series(
      (date_trunc(
        CASE WHEN p_bucket = 'hour' THEN 'hour' ELSE 'day' END,
        p_since AT TIME ZONE 'Asia/Manila'
      ) AT TIME ZONE 'Asia/Manila'),
      p_until,
      CASE WHEN p_bucket = 'hour' THEN interval '1 hour' ELSE interval '1 day' END
    ) AS bucket_at
    WHERE p_bucket IN ('day', 'hour')
  )
  SELECT s.bucket_at, s.avg_level FROM summary AS s WHERE p_bucket = 'raw'
  UNION ALL
  SELECT g.bucket_at, s.avg_level FROM bucket_grid AS g
    LEFT JOIN summary AS s ON s.bucket_at = g.bucket_at
  ORDER BY 1;
$$;
-- Still subject to existing table SELECT permission and RLS because SECURITY INVOKER.
GRANT EXECUTE ON FUNCTION public.pagasa_river_chart(text,timestamptz,timestamptz,text)
TO authenticated, anon;
