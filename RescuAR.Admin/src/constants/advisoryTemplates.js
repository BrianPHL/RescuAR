import { MARIKINA_DISTRICTS } from './marikinaData';

const d1Barangays = MARIKINA_DISTRICTS[0].barangays.join(', ');
const d2Barangays = MARIKINA_DISTRICTS[1].barangays.join(', ');

export const ADVISORY_TEMPLATES = [
  // ==========================================
  // EARTHQUAKE MAGNITUDE MONITORING ADVISORIES
  // ==========================================
  {
    group: 'Earthquake Advisories',
    id: 'earthquake-micro',
    title: 'Micro (< 3.0)',
    category: 'Earthquake',
    severity: 'Low',
    description: 'Usually not felt.',
    recommendedAction: 'No action needed. Check official updates.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Earthquake Advisories',
    id: 'earthquake-minor',
    title: 'Minor (3.0 - 3.9)',
    category: 'Earthquake',
    severity: 'Low',
    description: 'Light shaking may be felt. Damage is unlikely.',
    recommendedAction: 'Stay alert. Check official updates.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Earthquake Advisories',
    id: 'earthquake-light',
    title: 'Light (4.0 - 4.9)',
    category: 'Earthquake',
    severity: 'Moderate',
    description: 'Noticeable shaking. Objects may move.',
    recommendedAction: 'Stay calm. Keep away from falling objects.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Earthquake Advisories',
    id: 'earthquake-moderate',
    title: 'Moderate (5.0 - 5.9)',
    category: 'Earthquake',
    severity: 'Moderate',
    description: 'Strong shaking may damage weaker structures.',
    recommendedAction: 'Drop, Cover, and Hold On. Check for hazards after.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Earthquake Advisories',
    id: 'earthquake-strong',
    title: 'Strong (6.0 - 6.9)',
    category: 'Earthquake',
    severity: 'High',
    description: 'Severe shaking may damage buildings and roads.',
    recommendedAction: 'Drop, Cover, and Hold On. Move to a safe area after.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Earthquake Advisories',
    id: 'earthquake-major',
    title: 'Major (7.0 - 7.9)',
    category: 'Earthquake',
    severity: 'Critical',
    description: 'Widespread, serious damage may occur.',
    recommendedAction: 'Protect yourself. Evacuate unsafe buildings after shaking.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Earthquake Advisories',
    id: 'earthquake-great',
    title: 'Great (8.0+)',
    category: 'Earthquake',
    severity: 'Critical',
    description: 'Extreme shaking may cause widespread destruction.',
    recommendedAction: 'Protect yourself. Evacuate dangerous areas and follow authorities.',
    affectedAreas: 'All Marikina City'
  },
  // ==========================================
  // 1. WEATHER MONITORING ADVISORIES
  // ==========================================
  {
    group: 'Weather Advisories',
    id: 'weather-light-rain',
    title: 'Light to Moderate Rain',
    category: 'Weather',
    severity: 'Low',
    description: 'Light to moderate rain may affect parts of Marikina City.',
    recommendedAction: 'Bring rain protection. Monitor weather updates.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Weather Advisories',
    id: 'weather-occasional-rain',
    title: 'Occasional Rain',
    category: 'Weather',
    severity: 'Low',
    description: 'Cloudy skies with occasional light rain may affect the city.',
    recommendedAction: 'Carry rain protection. Monitor updates.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Weather Advisories',
    id: 'weather-heavy-rain',
    title: 'Heavy Rain Warning',
    category: 'Weather',
    severity: 'Moderate',
    description: 'Heavy rain may cause water buildup in low-lying areas.',
    recommendedAction: 'Limit unnecessary travel. Monitor local advisories.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Weather Advisories',
    id: 'weather-thunderstorm-mod',
    title: 'Thunderstorm Warning (Moderate)',
    category: 'Weather',
    severity: 'Moderate',
    description: 'Thunderstorms may bring moderate to heavy rain across Marikina.',
    recommendedAction: 'Stay indoors. Avoid open areas.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Weather Advisories',
    id: 'weather-strong-wind',
    title: 'Strong Wind Warning',
    category: 'Weather',
    severity: 'Moderate',
    description: 'Gusty winds may affect exposed areas and unsecured objects.',
    recommendedAction: 'Secure loose objects. Stay away from power lines.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Weather Advisories',
    id: 'weather-thunderstorm-high',
    title: 'Thunderstorm Warning (High)',
    category: 'Weather',
    severity: 'High',
    description: 'Intense rain and thunderstorms may cause rapid flooding.',
    recommendedAction: 'Stay indoors. Avoid flood-prone areas.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Weather Advisories',
    id: 'weather-extreme-heat',
    title: 'Extreme Heat Warning',
    category: 'Weather',
    severity: 'High',
    description: 'High temperatures may increase the risk of heat-related illness.',
    recommendedAction: 'Drink water. Reduce outdoor activity.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Weather Advisories',
    id: 'weather-damaging-wind',
    title: 'Damaging Wind Warning',
    category: 'Weather',
    severity: 'High',
    description: 'Strong winds may damage lightweight structures and exposed areas.',
    recommendedAction: 'Stay indoors. Keep away from trees and power lines.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Weather Advisories',
    id: 'weather-severe',
    title: 'Severe Weather Warning',
    category: 'Weather',
    severity: 'Critical',
    description: 'Severe rain and thunderstorms may cause dangerous flooding.',
    recommendedAction: 'Move to a safe area. Follow LGU instructions.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Weather Advisories',
    id: 'weather-torrential-rain',
    title: 'Torrential Rain Warning',
    category: 'Weather',
    severity: 'Critical',
    description: 'Torrential rain may cause rapid and dangerous flooding.',
    recommendedAction: 'Seek higher ground. Follow evacuation instructions.',
    affectedAreas: 'All Marikina City'
  },

  // ==========================================
  // 2. RIVER MONITORING STATIONS
  // ==========================================
  // Batasan Station
  {
    group: 'River Monitoring: Batasan',
    id: 'river-batasan-1st',
    title: 'Batasan: 1st Alarm (15.0m)',
    category: 'Monitoring',
    severity: 'Moderate',
    description: 'Water level is elevated at 15.0 m.',
    recommendedAction: 'Monitor updates. Prepare emergency supplies.',
    affectedAreas: 'Batasan, San Jose, Tumana'
  },
  {
    group: 'River Monitoring: Batasan',
    id: 'river-batasan-2nd',
    title: 'Batasan: 2nd Alarm (16.0m)',
    category: 'Monitoring',
    severity: 'High',
    description: 'Water level is 16.0 m. Water continues to rise and may affect nearby areas.',
    recommendedAction: 'Prepare for possible evacuation. Follow LGU instructions.',
    affectedAreas: 'Batasan, San Jose, Tumana'
  },
  {
    group: 'River Monitoring: Batasan',
    id: 'river-batasan-3rd',
    title: 'Batasan: 3rd Alarm (18.0m)',
    category: 'Monitoring',
    severity: 'Critical',
    description: 'Water level is 18.0 m. Flood risk is critical.',
    recommendedAction: 'Evacuate when instructed. Proceed to a safe area.',
    affectedAreas: 'Batasan, San Jose, Tumana'
  },

  // Nangka Station
  {
    group: 'River Monitoring: Nangka',
    id: 'river-nangka-1st',
    title: 'Nangka: 1st Alarm (16.5m)',
    category: 'Monitoring',
    severity: 'Moderate',
    description: 'Water level is elevated at 16.5 m.',
    recommendedAction: 'Monitor updates. Prepare emergency supplies.',
    affectedAreas: 'Nangka'
  },
  {
    group: 'River Monitoring: Nangka',
    id: 'river-nangka-2nd',
    title: 'Nangka: 2nd Alarm (17.1m)',
    category: 'Monitoring',
    severity: 'High',
    description: 'Water level is 17.1 m. Water continues to rise and may affect nearby areas.',
    recommendedAction: 'Prepare for possible evacuation. Follow LGU instructions.',
    affectedAreas: 'Nangka'
  },
  {
    group: 'River Monitoring: Nangka',
    id: 'river-nangka-3rd',
    title: 'Nangka: 3rd Alarm (17.7m)',
    category: 'Monitoring',
    severity: 'Critical',
    description: 'Water level is 17.7 m. Flood risk is critical.',
    recommendedAction: 'Evacuate when instructed. Proceed to a safe area.',
    affectedAreas: 'Nangka'
  },

  // Rodriguez Station
  {
    group: 'River Monitoring: Rodriguez',
    id: 'river-rodriguez-1st',
    title: 'Rodriguez: 1st Alarm (28.8m)',
    category: 'Monitoring',
    severity: 'Moderate',
    description: 'Water level is elevated at 28.8 m.',
    recommendedAction: 'Monitor updates. Prepare emergency supplies.',
    affectedAreas: 'Burgos, Rodriguez'
  },
  {
    group: 'River Monitoring: Rodriguez',
    id: 'river-rodriguez-2nd',
    title: 'Rodriguez: 2nd Alarm (29.8m)',
    category: 'Monitoring',
    severity: 'High',
    description: 'Water level is 29.8 m. Water continues to rise and may affect nearby areas.',
    recommendedAction: 'Prepare for possible evacuation. Follow LGU instructions.',
    affectedAreas: 'Burgos, Rodriguez'
  },
  {
    group: 'River Monitoring: Rodriguez',
    id: 'river-rodriguez-3rd',
    title: 'Rodriguez: 3rd Alarm (30.7m)',
    category: 'Monitoring',
    severity: 'Critical',
    description: 'Water level is 30.7 m. Flood risk is critical.',
    recommendedAction: 'Evacuate when instructed. Proceed to a safe area.',
    affectedAreas: 'Burgos, Rodriguez'
  },

  // San Jose Station
  {
    group: 'River Monitoring: San Jose',
    id: 'river-sanjose-1st',
    title: 'San Jose: 1st Alarm (22.4m)',
    category: 'Monitoring',
    severity: 'Moderate',
    description: 'Water level is elevated at 22.4 m.',
    recommendedAction: 'Monitor updates. Prepare emergency supplies.',
    affectedAreas: 'San Jose'
  },
  {
    group: 'River Monitoring: San Jose',
    id: 'river-sanjose-2nd',
    title: 'San Jose: 2nd Alarm (23.0m)',
    category: 'Monitoring',
    severity: 'High',
    description: 'Water level is 23.0 m. Water continues to rise and may affect nearby areas.',
    recommendedAction: 'Prepare for possible evacuation. Follow LGU instructions.',
    affectedAreas: 'San Jose'
  },
  {
    group: 'River Monitoring: San Jose',
    id: 'river-sanjose-3rd',
    title: 'San Jose: 3rd Alarm (23.6m)',
    category: 'Monitoring',
    severity: 'Critical',
    description: 'Water level is 23.6 m. Flood risk is critical.',
    recommendedAction: 'Evacuate when instructed. Proceed to a safe area.',
    affectedAreas: 'San Jose'
  },

  // Sto. Niño Station
  {
    group: 'River Monitoring: Sto. Niño',
    id: 'river-stonino-1st',
    title: 'Sto. Niño: 1st Alarm (15.0m)',
    category: 'Monitoring',
    severity: 'Moderate',
    description: 'Water level is elevated at 15.0 m.',
    recommendedAction: 'Monitor updates. Prepare emergency supplies.',
    affectedAreas: 'Sto. Niño'
  },
  {
    group: 'River Monitoring: Sto. Niño',
    id: 'river-stonino-2nd',
    title: 'Sto. Niño: 2nd Alarm (16.0m)',
    category: 'Monitoring',
    severity: 'High',
    description: 'Water level is 16.0 m. Water continues to rise and may affect nearby areas.',
    recommendedAction: 'Prepare for possible evacuation. Follow LGU instructions.',
    affectedAreas: 'Sto. Niño'
  },
  {
    group: 'River Monitoring: Sto. Niño',
    id: 'river-stonino-3rd',
    title: 'Sto. Niño: 3rd Alarm (18.0m)',
    category: 'Monitoring',
    severity: 'Critical',
    description: 'Water level is 18.0 m. Flood risk is critical.',
    recommendedAction: 'Evacuate when instructed. Proceed to a safe area.',
    affectedAreas: 'Sto. Niño'
  },

  // Tumana Station
  {
    group: 'River Monitoring: Tumana',
    id: 'river-tumana-1st',
    title: 'Tumana: 1st Alarm (15.0m)',
    category: 'Monitoring',
    severity: 'Moderate',
    description: 'Water level is elevated at 15.0 m.',
    recommendedAction: 'Monitor updates. Prepare emergency supplies.',
    affectedAreas: 'Tumana'
  },
  {
    group: 'River Monitoring: Tumana',
    id: 'river-tumana-2nd',
    title: 'Tumana: 2nd Alarm (16.0m)',
    category: 'Monitoring',
    severity: 'High',
    description: 'Water level is 16.0 m. Water continues to rise and may affect nearby areas.',
    recommendedAction: 'Prepare for possible evacuation. Follow LGU instructions.',
    affectedAreas: 'Tumana'
  },
  {
    group: 'River Monitoring: Tumana',
    id: 'river-tumana-3rd',
    title: 'Tumana: 3rd Alarm (18.0m)',
    category: 'Monitoring',
    severity: 'Critical',
    description: 'Water level is 18.0 m. Flood risk is critical.',
    recommendedAction: 'Evacuate when instructed. Proceed to a safe area.',
    affectedAreas: 'Tumana'
  },

  // ==========================================
  // 3. CITY & DISTRICT FLOOD ADVISORIES
  // ==========================================
  {
    group: 'City & District Flood Advisories',
    id: 'flood-city-watch',
    title: 'Flood Watch: Marikina City',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop in vulnerable areas.',
    recommendedAction: 'Monitor updates. Avoid flood-prone roads.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'City & District Flood Advisories',
    id: 'flood-city-advisory',
    title: 'Flood Advisory: Marikina City',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding is developing in several low-lying areas.',
    recommendedAction: 'Limit travel. Prepare emergency supplies.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'City & District Flood Advisories',
    id: 'flood-city-warning',
    title: 'Flood Warning: Marikina City',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect multiple communities.',
    recommendedAction: 'Move to a safer area. Prepare to evacuate.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'City & District Flood Advisories',
    id: 'flood-city-emergency',
    title: 'Flood Emergency: Marikina City',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting multiple areas.',
    recommendedAction: 'Evacuate when ordered. Follow LGU instructions.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'City & District Flood Advisories',
    id: 'flood-d1-watch',
    title: 'Flood Watch: District 1',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop in District 1.',
    recommendedAction: 'Monitor conditions. Avoid flood-prone streets.',
    affectedAreas: d1Barangays
  },
  {
    group: 'City & District Flood Advisories',
    id: 'flood-d1-advisory',
    title: 'Flood Advisory: District 1',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding may affect several District 1 areas.',
    recommendedAction: 'Limit travel. Prepare important belongings.',
    affectedAreas: d1Barangays
  },
  {
    group: 'City & District Flood Advisories',
    id: 'flood-d1-warning',
    title: 'Flood Warning: District 1',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect District 1 communities.',
    recommendedAction: 'Prepare to move to safer areas.',
    affectedAreas: d1Barangays
  },
  {
    group: 'City & District Flood Advisories',
    id: 'flood-d1-emergency',
    title: 'Flood Emergency: District 1',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting District 1.',
    recommendedAction: 'Evacuate when instructed by authorities.',
    affectedAreas: d1Barangays
  },
  {
    group: 'City & District Flood Advisories',
    id: 'flood-d2-watch',
    title: 'Flood Watch: District 2',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop in District 2.',
    recommendedAction: 'Monitor conditions. Avoid flood-prone streets.',
    affectedAreas: d2Barangays
  },
  {
    group: 'City & District Flood Advisories',
    id: 'flood-d2-advisory',
    title: 'Flood Advisory: District 2',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding may affect several District 2 areas.',
    recommendedAction: 'Limit travel. Prepare important belongings.',
    affectedAreas: d2Barangays
  },
  {
    group: 'City & District Flood Advisories',
    id: 'flood-d2-warning',
    title: 'Flood Warning: District 2',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect District 2 communities.',
    recommendedAction: 'Prepare to move to safer areas.',
    affectedAreas: d2Barangays
  },
  {
    group: 'City & District Flood Advisories',
    id: 'flood-d2-emergency',
    title: 'Flood Emergency: District 2',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting District 2.',
    recommendedAction: 'Evacuate when instructed by authorities.',
    affectedAreas: d2Barangays
  },

  // ==========================================
  // 4. DISTRICT 1 BARANGAYS FLOOD ADVISORIES
  // ==========================================
  // Barangka
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-barangka-low',
    title: 'Flood Watch: Barangka',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop in low-lying areas.',
    recommendedAction: 'Monitor updates. Avoid flood-prone roads.',
    affectedAreas: 'Barangka'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-barangka-mod',
    title: 'Flood Advisory: Barangka',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding is developing on vulnerable roads.',
    recommendedAction: 'Limit travel. Secure important belongings.',
    affectedAreas: 'Barangka'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-barangka-high',
    title: 'Flood Warning: Barangka',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect parts of Barangka.',
    recommendedAction: 'Move vehicles and valuables to higher ground.',
    affectedAreas: 'Barangka'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-barangka-crit',
    title: 'Flood Emergency: Barangka',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting Barangka.',
    recommendedAction: 'Evacuate when instructed. Avoid floodwaters.',
    affectedAreas: 'Barangka'
  },

  // Industrial Valley Complex
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-ivc-low',
    title: 'Flood Watch: Industrial Valley Complex',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop in vulnerable areas.',
    recommendedAction: 'Monitor updates. Avoid flood-prone roads.',
    affectedAreas: 'Industrial Valley Complex'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-ivc-mod',
    title: 'Flood Advisory: Industrial Valley Complex',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Rising water may affect local roads.',
    recommendedAction: 'Limit travel. Secure important belongings.',
    affectedAreas: 'Industrial Valley Complex'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-ivc-high',
    title: 'Flood Warning: Industrial Valley Complex',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect the area.',
    recommendedAction: 'Move vehicles and valuables to higher ground.',
    affectedAreas: 'Industrial Valley Complex'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-ivc-crit',
    title: 'Flood Emergency: Industrial Valley Complex',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting the area.',
    recommendedAction: 'Move to a safe location when instructed.',
    affectedAreas: 'Industrial Valley Complex'
  },

  // Jesus dela Peña
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-jdp-low',
    title: 'Flood Watch: Jesus dela Peña',
    category: 'Flood',
    severity: 'Low',
    description: 'Water may rise in low-lying areas.',
    recommendedAction: 'Monitor updates. Watch nearby waterways.',
    affectedAreas: 'Jesus dela Peña'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-jdp-mod',
    title: 'Flood Advisory: Jesus dela Peña',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding is developing in vulnerable areas.',
    recommendedAction: 'Avoid flooded roads. Prepare supplies.',
    affectedAreas: 'Jesus dela Peña'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-jdp-high',
    title: 'Flood Warning: Jesus dela Peña',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect the barangay.',
    recommendedAction: 'Prepare to move to higher ground.',
    affectedAreas: 'Jesus dela Peña'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-jdp-crit',
    title: 'Flood Emergency: Jesus dela Peña',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting the barangay.',
    recommendedAction: 'Evacuate when instructed by authorities.',
    affectedAreas: 'Jesus dela Peña'
  },

  // Kalumpang
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-kalumpang-low',
    title: 'Flood Watch: Kalumpang',
    category: 'Flood',
    severity: 'Low',
    description: 'Rising water may affect low-lying areas.',
    recommendedAction: 'Monitor river levels. Follow local updates.',
    affectedAreas: 'Kalumpang (Calumpang)'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-kalumpang-mod',
    title: 'Flood Advisory: Kalumpang',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding may develop near vulnerable areas.',
    recommendedAction: 'Prepare supplies. Avoid flood-prone roads.',
    affectedAreas: 'Kalumpang (Calumpang)'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-kalumpang-high',
    title: 'Flood Warning: Kalumpang',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect the barangay.',
    recommendedAction: 'Move to higher ground if conditions worsen.',
    affectedAreas: 'Kalumpang (Calumpang)'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-kalumpang-crit',
    title: 'Flood Emergency: Kalumpang',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting Kalumpang.',
    recommendedAction: 'Evacuate immediately when instructed.',
    affectedAreas: 'Kalumpang (Calumpang)'
  },

  // Malanday
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-malanday-low',
    title: 'Flood Watch: Malanday',
    category: 'Flood',
    severity: 'Low',
    description: 'Water levels may rise in low-lying areas.',
    recommendedAction: 'Monitor updates. Watch nearby waterways.',
    affectedAreas: 'Malanday'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-malanday-mod',
    title: 'Flood Advisory: Malanday',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding is developing in vulnerable areas.',
    recommendedAction: 'Secure belongings. Prepare emergency supplies.',
    affectedAreas: 'Malanday'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-malanday-high',
    title: 'Flood Warning: Malanday',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect Malanday.',
    recommendedAction: 'Prepare to move to higher ground.',
    affectedAreas: 'Malanday'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-malanday-crit',
    title: 'Flood Emergency: Malanday',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting Malanday.',
    recommendedAction: 'Evacuate when ordered and avoid floodwaters.',
    affectedAreas: 'Malanday'
  },

  // San Roque
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-sanroque-low',
    title: 'Flood Watch: San Roque',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop in vulnerable areas.',
    recommendedAction: 'Monitor updates. Check road conditions.',
    affectedAreas: 'San Roque'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-sanroque-mod',
    title: 'Flood Advisory: San Roque',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding may affect low-lying roads.',
    recommendedAction: 'Avoid flooded streets. Prepare supplies.',
    affectedAreas: 'San Roque'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-sanroque-high',
    title: 'Flood Warning: San Roque',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect parts of San Roque.',
    recommendedAction: 'Move belongings to higher areas.',
    affectedAreas: 'San Roque'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-sanroque-crit',
    title: 'Flood Emergency: San Roque',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting San Roque.',
    recommendedAction: 'Evacuate when instructed by authorities.',
    affectedAreas: 'San Roque'
  },

  // Sta. Elena
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-staelena-low',
    title: 'Flood Watch: Sta. Elena',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop during continued rain.',
    recommendedAction: 'Monitor weather. Check road conditions.',
    affectedAreas: 'Sta. Elena'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-staelena-mod',
    title: 'Flood Advisory: Sta. Elena',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Localized flooding may affect some roads.',
    recommendedAction: 'Avoid flooded areas. Limit travel.',
    affectedAreas: 'Sta. Elena'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-staelena-high',
    title: 'Flood Warning: Sta. Elena',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect vulnerable areas.',
    recommendedAction: 'Secure belongings. Prepare to relocate.',
    affectedAreas: 'Sta. Elena'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-staelena-crit',
    title: 'Flood Emergency: Sta. Elena',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting the barangay.',
    recommendedAction: 'Move to a designated safe area.',
    affectedAreas: 'Sta. Elena'
  },

  // Sto. Niño
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-stonino-low',
    title: 'Flood Watch: Sto. Niño',
    category: 'Flood',
    severity: 'Low',
    description: 'River levels may affect low-lying areas.',
    recommendedAction: 'Monitor river levels. Follow local updates.',
    affectedAreas: 'Sto. Niño'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-stonino-mod',
    title: 'Flood Advisory: Sto. Niño',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding may develop near riverside areas.',
    recommendedAction: 'Prepare supplies. Avoid the riverbank.',
    affectedAreas: 'Sto. Niño'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-stonino-high',
    title: 'Flood Warning: Sto. Niño',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect Sto. Niño.',
    recommendedAction: 'Prepare to evacuate to higher ground.',
    affectedAreas: 'Sto. Niño'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-stonino-crit',
    title: 'Flood Emergency: Sto. Niño',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting Sto. Niño.',
    recommendedAction: 'Evacuate immediately when instructed.',
    affectedAreas: 'Sto. Niño'
  },

  // Tañong
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-tanong-low',
    title: 'Flood Watch: Tañong',
    category: 'Flood',
    severity: 'Low',
    description: 'Rising water may affect riverside areas.',
    recommendedAction: 'Monitor conditions. Watch nearby waterways.',
    affectedAreas: 'Tañong'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-tanong-mod',
    title: 'Flood Advisory: Tañong',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding may develop in low-lying areas.',
    recommendedAction: 'Avoid riverside roads. Prepare supplies.',
    affectedAreas: 'Tañong'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-tanong-high',
    title: 'Flood Warning: Tañong',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect Tañong.',
    recommendedAction: 'Move belongings higher. Prepare to relocate.',
    affectedAreas: 'Tañong'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-tanong-crit',
    title: 'Flood Emergency: Tañong',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting Tañong.',
    recommendedAction: 'Evacuate when instructed by authorities.',
    affectedAreas: 'Tañong'
  },

  // ==========================================
  // 5. DISTRICT 2 BARANGAYS FLOOD ADVISORIES
  // ==========================================
  // Concepcion I
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-concepcion1-low',
    title: 'Flood Watch: Concepcion I',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop during continued rain.',
    recommendedAction: 'Monitor weather. Check drainage conditions.',
    affectedAreas: 'Concepcion I (Concepcion Uno)'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-concepcion1-mod',
    title: 'Flood Advisory: Concepcion I',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Localized flooding may affect some roads.',
    recommendedAction: 'Avoid flooded streets. Limit travel.',
    affectedAreas: 'Concepcion I (Concepcion Uno)'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-concepcion1-high',
    title: 'Flood Warning: Concepcion I',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect vulnerable areas.',
    recommendedAction: 'Secure belongings. Prepare to relocate.',
    affectedAreas: 'Concepcion I (Concepcion Uno)'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-concepcion1-crit',
    title: 'Flood Emergency: Concepcion I',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting the barangay.',
    recommendedAction: 'Evacuate when instructed by authorities.',
    affectedAreas: 'Concepcion I (Concepcion Uno)'
  },

  // Concepcion II
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-concepcion2-low',
    title: 'Flood Watch: Concepcion II',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop during continued rain.',
    recommendedAction: 'Monitor weather. Check drainage conditions.',
    affectedAreas: 'Concepcion II (Concepcion Dos)'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-concepcion2-mod',
    title: 'Flood Advisory: Concepcion II',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Localized flooding may affect some roads.',
    recommendedAction: 'Avoid flooded streets. Limit travel.',
    affectedAreas: 'Concepcion II (Concepcion Dos)'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-concepcion2-high',
    title: 'Flood Warning: Concepcion II',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect vulnerable areas.',
    recommendedAction: 'Secure belongings. Prepare to relocate.',
    affectedAreas: 'Concepcion II (Concepcion Dos)'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-concepcion2-crit',
    title: 'Flood Emergency: Concepcion II',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting the barangay.',
    recommendedAction: 'Evacuate when instructed by authorities.',
    affectedAreas: 'Concepcion II (Concepcion Dos)'
  },

  // Fortune
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-fortune-low',
    title: 'Flood Watch: Fortune',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop during heavy rain.',
    recommendedAction: 'Monitor conditions. Follow local updates.',
    affectedAreas: 'Fortune'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-fortune-mod',
    title: 'Flood Advisory: Fortune',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Localized flooding may affect low-lying roads.',
    recommendedAction: 'Limit travel. Avoid flooded areas.',
    affectedAreas: 'Fortune'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-fortune-high',
    title: 'Flood Warning: Fortune',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect vulnerable areas.',
    recommendedAction: 'Secure belongings. Prepare to move.',
    affectedAreas: 'Fortune'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-fortune-crit',
    title: 'Flood Emergency: Fortune',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting Fortune.',
    recommendedAction: 'Proceed to a safe area when instructed.',
    affectedAreas: 'Fortune'
  },

  // Marikina Heights
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-mh-low',
    title: 'Flood Watch: Marikina Heights',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop during heavy rain.',
    recommendedAction: 'Monitor weather. Check drainage conditions.',
    affectedAreas: 'Marikina Heights'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-mh-mod',
    title: 'Flood Advisory: Marikina Heights',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Localized flooding may affect some roads.',
    recommendedAction: 'Avoid flooded streets. Limit travel.',
    affectedAreas: 'Marikina Heights'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-mh-high',
    title: 'Flood Warning: Marikina Heights',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect vulnerable areas.',
    recommendedAction: 'Secure belongings. Remain prepared.',
    affectedAreas: 'Marikina Heights'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-mh-crit',
    title: 'Flood Emergency: Marikina Heights',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting parts of the area.',
    recommendedAction: 'Follow LGU evacuation instructions.',
    affectedAreas: 'Marikina Heights'
  },

  // Nangka
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-nangka-low',
    title: 'Flood Watch: Nangka',
    category: 'Flood',
    severity: 'Low',
    description: 'River levels may rise near vulnerable areas.',
    recommendedAction: 'Monitor river levels. Follow official updates.',
    affectedAreas: 'Nangka'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-nangka-mod',
    title: 'Flood Advisory: Nangka',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding may develop near low-lying areas.',
    recommendedAction: 'Prepare supplies. Avoid the riverbank.',
    affectedAreas: 'Nangka'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-nangka-high',
    title: 'Flood Warning: Nangka',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect Nangka.',
    recommendedAction: 'Prepare to move to higher ground.',
    affectedAreas: 'Nangka'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-nangka-crit',
    title: 'Flood Emergency: Nangka',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting Nangka.',
    recommendedAction: 'Evacuate immediately when instructed.',
    affectedAreas: 'Nangka'
  },

  // Parang
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-parang-low',
    title: 'Flood Watch: Parang',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop during continued rain.',
    recommendedAction: 'Monitor conditions. Follow local updates.',
    affectedAreas: 'Parang'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-parang-mod',
    title: 'Flood Advisory: Parang',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Localized flooding may affect some roads.',
    recommendedAction: 'Avoid flooded streets. Limit travel.',
    affectedAreas: 'Parang'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-parang-high',
    title: 'Flood Warning: Parang',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect vulnerable areas.',
    recommendedAction: 'Secure belongings. Prepare to relocate.',
    affectedAreas: 'Parang'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-parang-crit',
    title: 'Flood Emergency: Parang',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting parts of Parang.',
    recommendedAction: 'Follow LGU evacuation instructions.',
    affectedAreas: 'Parang'
  },

  // Tumana
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-tumana-low',
    title: 'Flood Watch: Tumana',
    category: 'Flood',
    severity: 'Low',
    description: 'River levels may rise near low-lying areas.',
    recommendedAction: 'Monitor river levels. Follow local updates.',
    affectedAreas: 'Tumana'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-tumana-mod',
    title: 'Flood Advisory: Tumana',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding may develop in vulnerable areas.',
    recommendedAction: 'Prepare supplies. Move valuables higher.',
    affectedAreas: 'Tumana'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-tumana-high',
    title: 'Flood Warning: Tumana',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect Tumana.',
    recommendedAction: 'Prepare to evacuate to higher ground.',
    affectedAreas: 'Tumana'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-tumana-crit',
    title: 'Flood Emergency: Tumana',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting Tumana.',
    recommendedAction: 'Evacuate immediately when instructed.',
    affectedAreas: 'Tumana'
  }
];
