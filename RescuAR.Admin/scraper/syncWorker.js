import { createClient } from '@supabase/supabase-js';
import axios from 'axios';
import * as cheerio from 'cheerio';
import cron from 'node-cron';
import dotenv from 'dotenv';

dotenv.config({ path: '../.env' });

// Server-side Supabase credentials only. Never use VITE_* variables or an anon key
// in this worker: VITE_* values are browser-facing, while telemetry persistence must
// authenticate with the server-only service-role key.
const SUPABASE_URL = process.env.SUPABASE_URL;
const SUPABASE_SERVICE_ROLE_KEY = process.env.SUPABASE_SERVICE_ROLE_KEY;

if (!SUPABASE_URL || !SUPABASE_SERVICE_ROLE_KEY) {
  throw new Error(
    '[Telemetry] Missing required server configuration. Set SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY in the Railway scraper service.'
  );
}

const supabase = createClient(SUPABASE_URL, SUPABASE_SERVICE_ROLE_KEY, {
  auth: {
    persistSession: false,
    autoRefreshToken: false
  }
});

console.log('[Telemetry] Server-side Supabase client initialized for latest + historical telemetry persistence.');

const MAX_REASONABLE_LEVEL_METERS = 50;

function isValidLevel(value) {
  const level = Number(value);
  return Number.isFinite(level) && level >= 0 && level <= MAX_REASONABLE_LEVEL_METERS;
}

const STATION_MAP = {
  'Sto Nino': 'Sto. Niño Station',
  'Sto. Nino': 'Sto. Niño Station',
  'Nangka': 'Nangka Station',
  'Rodriguez': 'Rodriguez Station',
  'Burgos': 'San Jose Station',
  'Montalban': 'San Jose Station',
  'Tumana Bridge': 'Tumana Station'
};

async function fetchPagasaDirectly() {
  // Primary URL (PAGASA) and Fallback URL (BantayBaha Marikina)
  const PAGASA_BASE_URL =
    'https://pasig-marikina-tullahanffws.pagasa.dost.gov.ph';
  const PAGASA_WATER_URL =
    `${PAGASA_BASE_URL}/water/table.do`;
  const bantayBahaUrl = 'https://bantaybaha.com/gauges/1';

  // 1. Try Scraping PAGASA
  try {
    console.log(`[Scraper] PAGASA URL: ${PAGASA_WATER_URL}`);
    const response = await axios.get(PAGASA_WATER_URL, {
      timeout: 15000,
      headers: { 'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)' }
    });
    const $ = cheerio.load(response.data);
    const scrapedStations = [];

    $('table tr').each((_, row) => {
      const cols = $(row).find('td');
      if (cols.length >= 5) {
        const rawName = $(cols[0]).text().trim();
        const currentStr = $(cols[1]).text().trim().replace(/\(\*\)/g, '');
        const alertStr = $(cols[2]).text().trim();
        const alarmStr = $(cols[3]).text().trim();
        const criticalStr = $(cols[4]).text().trim();

        const currentLevel = parseFloat(currentStr);
        const alertThreshold = parseFloat(alertStr);
        const alarmThreshold = parseFloat(alarmStr);
        const criticalThreshold = parseFloat(criticalStr);

        for (const [key, mappedName] of Object.entries(STATION_MAP)) {
          if (rawName.includes(key) && isValidLevel(currentLevel)) {
            let status = 'Normal';
            if (!isNaN(criticalThreshold) && currentLevel >= criticalThreshold) {
              status = '3rd Alarm';
            } else if (!isNaN(alarmThreshold) && currentLevel >= alarmThreshold) {
              status = '2nd Alarm';
            } else if (!isNaN(alertThreshold) && currentLevel >= alertThreshold) {
              status = '1st Alarm';
            }

            scrapedStations.push({
              station_name: mappedName,
              level: currentLevel,
              status: status,
              alert_threshold: alertThreshold || null,
              alarm_threshold: alarmThreshold || null,
              critical_threshold: criticalThreshold || null,
              updated_at: new Date().toISOString(),
              source: 'PAGASA'
            });
            break;
          }
        }
      }
    });

    if (scrapedStations.length > 0) {
      console.log(`[Scraper] Successfully parsed ${scrapedStations.length} stations directly from PAGASA!`);
      return scrapedStations;
    }
  } catch (pagasaErr) {
    console.warn(`[Scraper] PAGASA direct fetch unreachable (${pagasaErr.message}). Switching to BantayBaha fallback...`);
  }

  // 2. Fallback Scraper: BantayBaha
  try {
    console.log('[Scraper] Scraping BantayBaha live feed...');
    const response = await axios.get(bantayBahaUrl, {
      timeout: 10000,
      headers: {
        'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
        'Accept': 'text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8'
      }
    });
    const $ = cheerio.load(response.data);
    const scrapedStations = [];

    // 2a. Parse main river water level (Sto. Niño)
    const pageText = $('body').text();

    // Look for numbers before "meters" (e.g. 15.5 meters)
    const stoNinoMatch = pageText.match(/(\d+\.\d+)\s*meters/i) || pageText.match(/River Water Level\s*(\d+\.\d+)/i);
    if (stoNinoMatch) {
      const levelNum = parseFloat(stoNinoMatch[1]);
      if (isValidLevel(levelNum)) {
        scrapedStations.push({
          station_name: 'Sto. Niño Station',
          level: levelNum,
          status: calculateStatus('Sto. Niño Station', levelNum),
          updated_at: new Date().toISOString(),
          source: 'BantayBaha'
        });
      }
    }

    // 2b. Parse Upstream River Water Activity Table / Text Blocks
    // Match station names followed by water level numbers (e.g. Rodriguez 29.8, San Jose 25)
    const upstreamStations = [
      { name: 'Rodriguez Station', pattern: /Rodriguez\s*(\d+\.?\d*)/i },
      { name: 'San Jose Station', pattern: /San Jose\s*(\d+\.?\d*)/i },
      { name: 'Nangka Station', pattern: /Nangka\s*(\d+\.?\d*)/i },
      { name: 'Tumana Station', pattern: /Tumana\s*(\d+\.?\d*)/i }
    ];

    for (const item of upstreamStations) {
      const match = pageText.match(item.pattern);
      if (match && match[1] && isValidLevel(parseFloat(match[1]))) {
        const levelVal = parseFloat(match[1]);
        scrapedStations.push({
          station_name: item.name,
          level: levelVal,
          status: calculateStatus(item.name, levelVal),
          updated_at: new Date().toISOString(),
          source: 'BantayBaha'
        });
      }
    }

    // 2c. Fallback parser for standard table rows if present
    $('tr').each((_, row) => {
      const text = $(row).text().trim();
      for (const [key, mappedName] of Object.entries(STATION_MAP)) {
        if (text.toLowerCase().includes(key.toLowerCase())) {
          const numbers = text.match(/(\d+\.\d+|\d+)/g);
          if (numbers && numbers.length > 0) {
            const levelVal = parseFloat(numbers[0]);
            if (isValidLevel(levelVal) && !scrapedStations.some(s => s.station_name === mappedName)) {
              scrapedStations.push({
                station_name: mappedName,
                level: levelVal,
                status: calculateStatus(mappedName, levelVal),
                updated_at: new Date().toISOString(),
                source: 'BantayBaha'
              });
            }
          }
        }
      }
    });

    if (scrapedStations.length > 0) {
      console.log(`[Scraper] Successfully parsed ${scrapedStations.length} stations from BantayBaha fallback!`);
      return scrapedStations;
    }

    // Never manufacture telemetry. If both live sources fail to yield a valid
    // reading, return no updates so Supabase retains the last verified value
    // and, importantly, its original updated_at timestamp. The UI can then
    // identify the record as stale instead of presenting a fabricated value
    // as if it were live.
    console.warn('[Scraper] BantayBaha responded, but no valid live readings were parsed. Keeping last verified Supabase values unchanged.');
    return [];
  } catch (bbErr) {
    console.error('[Scraper] BantayBaha fallback scrape error:', bbErr.message);
    return [];
  }
}

function calculateStatus(stationName, level) {
  const numLevel = parseFloat(level) || 0;
  const name = stationName.toLowerCase();
  let thresholds = { alarm1: 15.0, alarm2: 16.0, alarm3: 18.0 };

  if (name.includes('rodriguez')) thresholds = { alarm1: 28.8, alarm2: 29.8, alarm3: 30.7 };
  else if (name.includes('nangka')) thresholds = { alarm1: 16.5, alarm2: 17.1, alarm3: 17.7 };
  else if (name.includes('san jose') || name.includes('montalban') || name.includes('burgos')) thresholds = { alarm1: 22.4, alarm2: 23.0, alarm3: 23.6 };

  if (numLevel >= thresholds.alarm3) return '3rd Alarm';
  if (numLevel >= thresholds.alarm2) return '2nd Alarm';
  if (numLevel >= thresholds.alarm1) return '1st Alarm';
  return 'Normal';
}

async function syncToSupabase() {
  const stations = await fetchPagasaDirectly();
  if (stations.length === 0) {
    console.warn('[PAGASA Scraper] No verified live readings available. Skipping Supabase update; existing records and timestamps are preserved.');
    return;
  }

  console.log(`[PAGASA Scraper] Updating ${stations.length} stations in Supabase...`);
  for (const st of stations) {
    const { error } = await supabase
      .from('monitoring_stations')
      .upsert(
        {
          station_name: st.station_name,
          level: st.level,
          status: st.status,
          updated_at: st.updated_at
        },
        { onConflict: 'station_name' }
      );

    if (error) {
      console.error(`❌ Failed latest-reading update for ${st.station_name}:`, error.message);
    } else {
      console.log(`✅ Synced latest ${st.station_name}: ${st.level}m -> ${st.status}`);
    }

    // Append every verified observation to telemetry history even if the latest-row
    // upsert fails. The two writes serve different purposes and one should not block
    // the other. The history table is the source for timestamped charts/validation.
    const historyRow = {
      station_name: st.station_name,
      level: st.level,
      status: st.status,
      source: st.source || 'PAGASA',
      observed_at: st.updated_at
    };

    const { data: historyData, error: historyError } = await supabase
      .from('river_level_history')
      .insert(historyRow)
      .select('id, station_name, level, source, observed_at');

    if (historyError) {
      console.error(`❌ Failed history insert for ${st.station_name}:`, historyError.message);
    } else {
      const stored = historyData?.[0];
      console.log(
        `🕒 Stored history ${stored?.station_name ?? st.station_name}: ${stored?.level ?? st.level}m ` +
        `(${stored?.source ?? historyRow.source}) @ ${stored?.observed_at ?? st.updated_at}`
      );
    }
  }
}

// Execute immediately & run every 5 minutes
syncToSupabase();
cron.schedule('*/5 * * * *', () => syncToSupabase());
