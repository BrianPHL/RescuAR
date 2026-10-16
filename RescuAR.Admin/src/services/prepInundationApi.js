// Full prediction endpoint. The external model owns every calculation and evidence label.
export const PREP_API_URL = import.meta.env?.VITE_PREP_API_URL?.trim() || '';

export async function predictPrepInundation({
  riverLevel,
  mode,
  inputSource,
  observedAt = null,
  signal,
  endpoint = PREP_API_URL,
  fetchImpl = globalThis.fetch,
  timeoutMs = 15000,
}) {
  if (!endpoint) throw new Error('Configure VITE_PREP_API_URL with the external model prediction endpoint');
  if (typeof riverLevel !== 'number' || !Number.isFinite(riverLevel) || riverLevel < 0) {
    throw new Error('A valid river level is required');
  }
  if (!['live', 'simulation'].includes(mode) || typeof inputSource !== 'string' || !inputSource.trim()) {
    throw new Error('Prediction mode and input source are required');
  }
  if (mode === 'live' && (!observedAt || !Number.isFinite(Date.parse(observedAt)))) {
    throw new Error('Live predictions require the observation timestamp');
  }

  const controller = new AbortController();
  const cancel = () => controller.abort(signal?.reason);
  if (signal?.aborted) cancel();
  else signal?.addEventListener('abort', cancel, { once: true });
  let timedOut = false;
  const timer = setTimeout(() => { timedOut = true; controller.abort(); }, timeoutMs);

  try {
    const response = await fetchImpl(endpoint, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify({ riverLevel, mode, inputSource, observedAt }),
      signal: controller.signal,
    });
    if (!response.ok) throw new Error(`External model returned HTTP ${response.status}`);
    const result = await response.json();
    const alarm = result?.alarm;
    const inundation = result?.inundation;
    if (!alarm || !['NORMAL', 'ALARM_1', 'ALARM_2', 'ALARM_3'].includes(alarm.code)
      || typeof alarm.level !== 'number' || !Number.isInteger(alarm.level) || alarm.level < 0 || alarm.level > 3
      || typeof alarm.label !== 'string' || !alarm.label.trim()
      || !inundation || typeof inundation.has_triggered_rule !== 'boolean'
      || !Array.isArray(inundation.affected_barangays)
      || inundation.affected_barangays.some((name) => typeof name !== 'string')
      || !Array.isArray(inundation.triggered_thresholds)
      || typeof inundation.evidence_status !== 'string' || !inundation.evidence_status.trim()) {
      throw new Error('External model response does not match the Admin prediction contract');
    }
    if (alarm.code !== (alarm.level === 0 ? 'NORMAL' : `ALARM_${alarm.level}`)) {
      throw new Error('External model returned inconsistent alarm fields');
    }
    const population = result.exposure?.total_population_affected;
    if (population != null && (typeof population !== 'number' || !Number.isFinite(population) || population < 0)) {
      throw new Error('External model returned an invalid population estimate');
    }
    return result;
  } catch (error) {
    // The screen suppresses caller AbortError on unmount; a timeout must clear old results.
    if (timedOut && !signal?.aborted) throw new Error('External model request timed out');
    throw error;
  } finally {
    clearTimeout(timer);
    signal?.removeEventListener('abort', cancel);
  }
}
