export const PREP_EXPOSURE_API_URL = String(
  import.meta.env.VITE_PREP_EXPOSURE_API_URL || ""
)
  .trim()
  .replace(/\/+$/, "");

export const isPrepExposureApiConfigured = () =>
  Boolean(PREP_EXPOSURE_API_URL);

function toFiniteNumber(value) {
  if (
    value === null ||
    value === undefined ||
    value === ''
  ) {
    return null;
  }

  const number = Number(value);

  return Number.isFinite(number)
    ? number
    : null;
}

function normalizeSummary(summary = {}, barangays = [], facilities = []) {
  return {
    // Preserve the original API response fields.
    ...summary,

    // Normalized frontend fields.
    returnPeriodYears: toFiniteNumber(
      summary.return_period_years
    ),

    scenarioLabel:
      summary.scenario_label ?? null,

    annualExceedanceProbabilityPct: toFiniteNumber(
      summary.annual_exceedance_probability_pct
    ),

    minimumHazardCounted:
      summary.minimum_hazard_counted ?? null,

    minimumDepthThreshold:
      summary.minimum_depth_threshold ?? null,

    estimatedAffectedPopulation:
      toFiniteNumber(
        summary.estimated_affected_population
      ),

    estimatedAffectedPopulationPct: toFiniteNumber(
      summary.estimated_affected_population_pct
    ),

    estimatedAffectedChildren:
      toFiniteNumber(
        summary.estimated_affected_children
      ),

    estimatedAffectedChildrenPct: toFiniteNumber(
      summary.estimated_affected_children_pct
    ),

    barangaysWithExposure:
      toFiniteNumber(
        summary.barangays_with_mapped_exposure
      ) ?? barangays.length,

    cityPopulationBaseline: toFiniteNumber(
      summary.city_population_baseline
    ),

    cityChildrenBaseline: toFiniteNumber(
      summary.city_children_baseline
    ),

    // PREP returns the exposed facilities as an array.
    facilitiesInHazardArea: facilities.length,
  };
}

export async function fetchPrepExposureScenario({
  returnPeriod,
  minimumHazard,
  signal,
}) {
  if (!isPrepExposureApiConfigured()) {
    throw new Error(
      "VITE_PREP_EXPOSURE_API_URL is not configured for the RescuAR Admin deployment."
    );
  }

  const response = await fetch(
    `${PREP_EXPOSURE_API_URL}/v1/exposure/scenario`,
    {
      method: "POST",

      headers: {
        "Content-Type": "application/json",
      },

      body: JSON.stringify({
        return_period: Number(returnPeriod),
        minimum_hazard: minimumHazard,
      }),

      signal,
    }
  );

  let payload = null;

  try {
    payload = await response.json();
  } catch {
    payload = null;
  }

  if (!response.ok) {
    const apiMessage =
      payload?.message ||
      payload?.error ||
      `HTTP ${response.status}`;

    throw new Error(
      `PREP exposure request failed: ${apiMessage}`
    );
  }

  if (!payload || payload.ok === false) {
    throw new Error(
      payload?.message ||
        payload?.error ||
        "PREP exposure service returned an invalid response."
    );
  }

  const barangays = Array.isArray(payload.barangays)
    ? payload.barangays
    : [];

  const facilities = Array.isArray(payload.facilities)
    ? payload.facilities
    : [];

  // Plumber may serialize a one-row R data.frame as a one-element JSON
  // array. Normalize that shape to a plain object before reading summary
  // metrics such as affected population and affected children.
  const rawSummary = Array.isArray(payload.summary)
    ? (payload.summary[0] ?? {})
    : (payload.summary ?? {});

  return {
    ...payload,

    barangays,
    facilities,

    summary: normalizeSummary(
      rawSummary,
      barangays,
      facilities
    ),
  };
}

// Compatibility alias in case another component currently imports
// fetchExposureScenario instead.
export const fetchExposureScenario =
  fetchPrepExposureScenario;