/**
 * Official Marikina City Barangays grouped by Legislative District
 */
export const MARIKINA_DISTRICTS = [
  {
    name: 'District 1',
    barangays: [
      'Barangka',
      'Industrial Valley Complex',
      'Jesus dela Peña',
      'Kalumpang (Calumpang)',
      'Malanday',
      'San Roque',
      'Sta. Elena',
      'Sto. Niño',
      'Tañong'
    ]
  },
  {
    name: 'District 2',
    barangays: [
      'Concepcion I (Concepcion Uno)',
      'Concepcion II (Concepcion Dos)',
      'Fortune',
      'Marikina Heights',
      'Nangka',
      'Parang',
      'Tumana'
    ]
  }
];

export const ALL_DISTRICT_1_BARANGAYS = MARIKINA_DISTRICTS[0].barangays;
export const ALL_DISTRICT_2_BARANGAYS = MARIKINA_DISTRICTS[1].barangays;
export const ALL_MARIKINA_BARANGAYS = [
  ...ALL_DISTRICT_1_BARANGAYS,
  ...ALL_DISTRICT_2_BARANGAYS
];
