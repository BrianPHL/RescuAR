/**
 * PAGASA Pasig-Marikina-Tullahan FFWWS station mappings verified from
 * /water/table_list.do on 2026-10-08.
 *
 * Keep source names/codes intact. Do not create aliases for a station unless
 * its observation code has been verified against the official PAGASA feed.
 */
export const PAGASA_STATIONS = [
  {
    key: 'rodriguez',
    station_code: '11102201',
    source_station_name: 'Rodriguez',
    display_name: 'Rodriguez Station',
    enabled_live: true,
    include_historical: true
  },
  {
    key: 'montalban',
    station_code: '11102202',
    source_station_name: 'Montalban',
    display_name: 'Montalban Station',
    enabled_live: true,
    include_historical: true
  },
  {
    key: 'nangka',
    station_code: '11103202',
    source_station_name: 'Nangka',
    display_name: 'Nangka Station',
    enabled_live: true,
    include_historical: true
  },
  {
    key: 'sto_nino_mmda',
    station_code: '11104204',
    source_station_name: 'Sto Nino(MMDA)',
    display_name: 'Sto. Niño Station',
    enabled_live: true,
    include_historical: true
  },
  {
    key: 'tumana',
    station_code: '11103203',
    source_station_name: 'Tumana Bridge',
    display_name: 'Tumana Station',
    enabled_live: true,
    include_historical: true
  },
  {
    key: 'sto_nino_secondary',
    station_code: '11104201',
    source_station_name: 'Sto Nino',
    display_name: 'Sto. Niño Secondary',
    enabled_live: false,
    include_historical: true
  }
];

export const LIVE_PAGASA_STATIONS = PAGASA_STATIONS.filter((station) => station.enabled_live);
export const HISTORICAL_PAGASA_STATIONS = PAGASA_STATIONS.filter((station) => station.include_historical);

export const PAGASA_STATION_BY_CODE = new Map(
  PAGASA_STATIONS.map((station) => [station.station_code, station])
);

export function findPagasaStation(identifier) {
  const needle = String(identifier || '').trim().toLowerCase();
  if (!needle) return null;

  return PAGASA_STATIONS.find((station) =>
    station.station_code === needle ||
    station.key.toLowerCase() === needle ||
    station.source_station_name.toLowerCase() === needle ||
    station.display_name.toLowerCase() === needle
  ) || null;
}
