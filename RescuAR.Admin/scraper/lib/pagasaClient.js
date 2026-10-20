import axios from 'axios';
import * as cheerio from 'cheerio';

export const PAGASA_BASE = 'https://pasig-marikina-tullahanffws.pagasa.dost.gov.ph';
export const PAGASA_TABLE_URL = `${PAGASA_BASE}/water/table.do`;
export const PAGASA_TABLE_LIST_URL = `${PAGASA_BASE}/water/table_list.do`;
export const PAGASA_DETAIL_LIST_URL = `${PAGASA_BASE}/water/detail_list.do`;
export const PAGASA_SOURCE_ID = 'PAGASA_FFWWS';
export const MAX_REASONABLE_LEVEL_METERS = 100;

export const PAGASA_HTTP_HEADERS = {
  'User-Agent': 'RescuAR-Telemetry/3.0 (+academic disaster monitoring)',
  Accept: 'text/html,application/json;q=0.9,*/*;q=0.8',
  'Accept-Language': 'en-US,en;q=0.9',
  Referer: PAGASA_TABLE_URL
};

export function parsePagasaNumber(raw) {
  if (raw === null || raw === undefined || raw === '') return null;
  const cleaned = String(raw).replace(/\(\*\)/g, '').trim();
  const match = cleaned.match(/-?\d+(?:\.\d+)?/);
  if (!match) return null;
  const value = Number(match[0]);
  return Number.isFinite(value) ? value : null;
}

export function hasPagasaQualityMarker(raw) {
  return typeof raw === 'string' && raw.includes('(*)');
}

export function isValidWaterLevel(value) {
  return Number.isFinite(value) && value >= 0 && value <= MAX_REASONABLE_LEVEL_METERS;
}

/** Convert PAGASA `YYYY-MM-DD HH:mm` (Asia/Manila) into an ISO timestamp. */
export function toManilaIsoFromTimestamp(value) {
  if (!value) return null;
  const match = String(value).trim().match(/^(\d{4})-(\d{2})-(\d{2})\s+(\d{1,2}):(\d{2})$/);
  if (!match) return null;
  const [, year, month, day, hour, minute] = match;
  const date = new Date(
    `${year}-${month}-${day}T${String(hour).padStart(2, '0')}:${minute}:00+08:00`
  );
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

/** Convert `YYYY-MM-DD HH:mm` into PAGASA's `YYYYMMDDHHmm` request value. */
export function ymdhmFromSearchTime(value) {
  const match = String(value || '').trim().match(/^(\d{4})-(\d{2})-(\d{2})\s+(\d{1,2}):(\d{2})$/);
  if (!match) return null;
  return `${match[1]}${match[2]}${match[3]}${String(match[4]).padStart(2, '0')}${match[5]}`;
}

/** Convert an ISO/Manila date input into PAGASA's request format. */
export function ymdhmFromDateInput(value) {
  const normalized = String(value || '').trim();
  if (/^\d{12}$/.test(normalized)) return normalized;

  const localMatch = normalized.match(/^(\d{4})-(\d{2})-(\d{2})(?:[ T](\d{1,2}):(\d{2}))?$/);
  if (localMatch) {
    return `${localMatch[1]}${localMatch[2]}${localMatch[3]}${String(localMatch[4] || '0').padStart(2, '0')}${localMatch[5] || '00'}`;
  }

  const date = new Date(normalized);
  if (Number.isNaN(date.getTime())) return null;
  // Format in Asia/Manila without depending on the machine timezone.
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Manila',
    year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', hourCycle: 'h23'
  }).formatToParts(date);
  const map = Object.fromEntries(parts.map((part) => [part.type, part.value]));
  return `${map.year}${map.month}${map.day}${map.hour}${map.minute}`;
}

export function pagasaTimestampFromYmdhm(value) {
  const text = String(value || '');
  if (!/^\d{12}$/.test(text)) return null;
  return `${text.slice(0, 4)}-${text.slice(4, 6)}-${text.slice(6, 8)} ${text.slice(8, 10)}:${text.slice(10, 12)}`;
}

export function classifyWaterLevelStatus(level, alert, alarm, critical) {
  if (!Number.isFinite(level)) return 'Unavailable';
  if (Number.isFinite(critical) && level >= critical) return 'Critical';
  if (Number.isFinite(alarm) && level >= alarm) return 'Alarm';
  if (Number.isFinite(alert) && level >= alert) return 'Alert';
  return 'Normal';
}

export function classifyWaterLevelTrend(current, previous30m, tolerance = 0.05) {
  if (!Number.isFinite(current) || !Number.isFinite(previous30m)) return 'Unknown';
  const delta = current - previous30m;
  if (delta > tolerance) return 'Rising';
  if (delta < -tolerance) return 'Falling';
  return 'Stable';
}

async function postForm(url, fields, options = {}) {
  const body = new URLSearchParams(fields);
  return axios.post(url, body.toString(), {
    timeout: options.timeout ?? 20000,
    maxRedirects: 5,
    headers: {
      ...PAGASA_HTTP_HEADERS,
      'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
      Accept: 'application/json,text/javascript,*/*;q=0.01',
      'X-Requested-With': 'XMLHttpRequest',
      ...(options.headers || {})
    }
  });
}

export async function fetchPagasaReferenceTime(options = {}) {
  const response = await axios.get(PAGASA_TABLE_URL, {
    timeout: options.timeout ?? 20000,
    maxRedirects: 5,
    headers: PAGASA_HTTP_HEADERS
  });

  const $ = cheerio.load(response.data);
  const searchTime = $('#searchtime').attr('value')?.trim();
  const ymdhm = ymdhmFromSearchTime(searchTime);
  if (!searchTime || !ymdhm) {
    throw new Error('PAGASA table.do did not expose a valid #searchtime reference value.');
  }

  return { searchTime, ymdhm, httpStatus: response.status };
}

export async function fetchPagasaStationList(ymdhm, options = {}) {
  const response = await postForm(PAGASA_TABLE_LIST_URL, { ymdhm }, options);
  if (!Array.isArray(response.data)) {
    throw new Error('PAGASA table_list.do returned an unexpected response format.');
  }
  return response.data;
}

export async function fetchPagasaStationHistory(stationCode, ymdhm, options = {}) {
  const response = await postForm(PAGASA_DETAIL_LIST_URL, {
    obscd: String(stationCode),
    ymdhm: String(ymdhm)
  }, options);
  if (!Array.isArray(response.data)) {
    throw new Error(`PAGASA detail_list.do returned an unexpected response for station ${stationCode}.`);
  }
  return response.data;
}

export function normalizeSnapshot(raw, station) {
  const level = parsePagasaNumber(raw?.wl);
  if (!isValidWaterLevel(level)) return null;

  const level30m = parsePagasaNumber(raw?.wl30m);
  const level1h = parsePagasaNumber(raw?.wl1h);
  const level2h = parsePagasaNumber(raw?.wl2h);
  const alert = parsePagasaNumber(raw?.alertwl);
  const alarm = parsePagasaNumber(raw?.alarmwl);
  const critical = parsePagasaNumber(raw?.criticalwl);

  return {
    station_code: station.station_code,
    station_name: station.display_name,
    source_station_name: raw?.obsnm || station.source_station_name,
    level,
    raw_water_level: raw?.wl == null ? null : String(raw.wl),
    quality_flag: hasPagasaQualityMarker(raw?.wl) ? 'PAGASA_MARKED' : null,
    water_level_30m_m: level30m,
    water_level_1h_m: level1h,
    water_level_2h_m: level2h,
    alert_threshold_m: alert,
    alarm_threshold_m: alarm,
    critical_threshold_m: critical,
    status: classifyWaterLevelStatus(level, alert, alarm, critical),
    trend: classifyWaterLevelTrend(level, level30m),
    delta_30m_m: Number.isFinite(level30m) ? Number((level - level30m).toFixed(3)) : null,
    source: PAGASA_SOURCE_ID
  };
}

export function normalizeHistoryRows(rawRows, station, fallbackThresholds = {}) {
  const deduped = new Map();

  for (const raw of rawRows || []) {
    const observedAt = toManilaIsoFromTimestamp(raw?.timestr);
    const level = parsePagasaNumber(raw?.wl);
    if (!observedAt || !isValidWaterLevel(level)) continue;

    const alert = parsePagasaNumber(raw?.alertwl) ?? fallbackThresholds.alert_threshold_m ?? null;
    const alarm = parsePagasaNumber(raw?.alarmwl) ?? fallbackThresholds.alarm_threshold_m ?? null;
    const critical = parsePagasaNumber(raw?.criticalwl) ?? fallbackThresholds.critical_threshold_m ?? null;

    const row = {
      station_code: station.station_code,
      station_name: station.display_name,
      source_station_name: station.source_station_name,
      level,
      raw_water_level: raw?.wl == null ? null : String(raw.wl),
      quality_flag: hasPagasaQualityMarker(raw?.wl) ? 'PAGASA_MARKED' : null,
      alert_threshold_m: alert,
      alarm_threshold_m: alarm,
      critical_threshold_m: critical,
      status: classifyWaterLevelStatus(level, alert, alarm, critical),
      source: PAGASA_SOURCE_ID,
      observed_at: observedAt
    };

    deduped.set(`${station.station_code}|${observedAt}`, row);
  }

  return [...deduped.values()].sort((a, b) => new Date(a.observed_at) - new Date(b.observed_at));
}
