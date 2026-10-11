export const PREP_EXPOSURE_API_URL = String(import.meta.env.VITE_PREP_EXPOSURE_API_URL || '').replace(/\/$/, '');

export async function fetchExposureScenario({ returnPeriod, minimumHazard, signal } = {}) {
  if (!PREP_EXPOSURE_API_URL) {
    throw new Error('VITE_PREP_EXPOSURE_API_URL is not configured for the RescuAR Admin deployment.');
  }

  const response = await fetch(`${PREP_EXPOSURE_API_URL}/v1/exposure/scenario`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      return_period: Number(returnPeriod),
      minimum_hazard: minimumHazard
    }),
    signal
  });

  if (!response.ok) {
    const body = await response.text().catch(() => '');
    throw new Error(`PREP R exposure API returned HTTP ${response.status}${body ? `: ${body.slice(0, 220)}` : ''}`);
  }

  return response.json();
}
