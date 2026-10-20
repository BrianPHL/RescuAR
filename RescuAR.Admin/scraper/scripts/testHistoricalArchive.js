import { fetchPagasaStationHistory } from '../lib/pagasaClient.js';
import { findPagasaStation } from '../config/pagasaStations.js';

const ymdhm = process.argv[2] || '202011121020';
const identifiers = process.argv.slice(3).length
  ? process.argv.slice(3)
  : ['sto_nino_mmda', 'sto_nino_secondary', 'nangka', 'montalban', 'rodriguez'];

for (const identifier of identifiers) {
  const station = findPagasaStation(identifier);
  if (!station) {
    console.warn(`[Archive Test] Unknown station: ${identifier}`);
    continue;
  }

  try {
    const rows = await fetchPagasaStationHistory(station.station_code, ymdhm);
    const matchingYear = rows.filter((row) => String(row?.timestr || '').startsWith(ymdhm.slice(0, 4)));
    console.log(`\n[Archive Test] ${station.display_name} (${station.station_code}) @ ${ymdhm}`);
    console.log(`Returned: ${rows.length} row(s); same-year rows: ${matchingYear.length}.`);
    console.table(rows.slice(0, 8).map((row) => ({
      timestr: row.timestr,
      wl: row.wl,
      alertwl: row.alertwl,
      alarmwl: row.alarmwl,
      criticalwl: row.criticalwl
    })));
  } catch (error) {
    console.error(`[Archive Test] ${station.display_name}: ${error.message}`);
  }
}
