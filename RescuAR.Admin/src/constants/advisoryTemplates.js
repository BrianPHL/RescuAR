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
    description: 'Earthquake activity below magnitude 3.0 is usually not felt, but it may still be recorded by monitoring instruments.',
    recommendedAction: 'No immediate protective action is normally needed. Check official earthquake updates and stay alert for any reported aftershocks or local advisories.',
    escalationActions: 'If stronger shaking or aftershocks occur, move away from shelves, glass, and other falling hazards. Follow updated official earthquake advisories.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Earthquake Advisories',
    id: 'earthquake-minor',
    title: 'Minor (3.0 - 3.9)',
    category: 'Earthquake',
    severity: 'Low',
    description: 'Light shaking may be felt by some people, but significant structural damage is unlikely.',
    recommendedAction: 'Stay calm and check your surroundings for minor hazards. Monitor official updates and be prepared for possible aftershocks.',
    escalationActions: 'If stronger shaking or aftershocks occur, move away from shelves, glass, and other falling hazards. Follow updated official earthquake advisories.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Earthquake Advisories',
    id: 'earthquake-light',
    title: 'Light (4.0 - 4.9)',
    category: 'Earthquake',
    severity: 'Moderate',
    description: 'Noticeable shaking may be felt indoors, and hanging or unsecured objects may move or fall.',
    recommendedAction: 'During shaking, Drop, Cover, and Hold On. Stay away from windows and falling objects, then check your surroundings for hazards after the shaking stops.',
    escalationActions: 'If stronger aftershocks occur or you see cracks, falling debris, or utility damage, leave unsafe areas after shaking stops and move to an open, safe location.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Earthquake Advisories',
    id: 'earthquake-moderate',
    title: 'Moderate (5.0 - 5.9)',
    category: 'Earthquake',
    severity: 'Moderate',
    description: 'Strong shaking may damage weak structures, move heavy objects, and create falling-object hazards in affected areas.',
    recommendedAction: 'Drop, Cover, and Hold On until shaking stops. Afterward, check for injuries, fire, damaged utilities, and unsafe structures before moving to a safer location.',
    escalationActions: 'If aftershocks continue or damage is visible, leave damaged buildings after shaking stops. Avoid power lines and possible gas leaks, and follow evacuation instructions.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Earthquake Advisories',
    id: 'earthquake-strong',
    title: 'Strong (6.0 - 6.9)',
    category: 'Earthquake',
    severity: 'High',
    description: 'Severe shaking may cause major damage to buildings, roads, utilities, and other infrastructure in populated areas.',
    recommendedAction: 'Drop, Cover, and Hold On during shaking. Afterward, leave visibly damaged buildings, avoid downed power lines, and follow evacuation or emergency instructions.',
    escalationActions: 'If buildings, roads, or utilities are badly damaged, move to a designated safe area and stay away from unsafe structures. Follow emergency route and shelter updates.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Earthquake Advisories',
    id: 'earthquake-major',
    title: 'Major (7.0 - 7.9)',
    category: 'Earthquake',
    severity: 'Critical',
    description: 'Very strong shaking may cause widespread structural damage, blocked roads, utility failures, and dangerous debris across a large area.',
    recommendedAction: 'Protect yourself during shaking, then evacuate unsafe buildings when it is safe to move. Expect aftershocks and follow official evacuation routes and emergency instructions.',
    escalationActions: 'Remain in the designated safe or evacuation area and expect strong aftershocks. Do not re-enter damaged buildings until authorities declare them safe.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Earthquake Advisories',
    id: 'earthquake-great',
    title: 'Great (8.0+)',
    category: 'Earthquake',
    severity: 'Critical',
    description: 'Extreme shaking may cause catastrophic and widespread destruction, major infrastructure failure, and prolonged disruption across affected communities.',
    recommendedAction: 'Protect yourself during shaking and move away from severely damaged areas afterward. Follow authorities immediately, expect strong aftershocks, and use designated evacuation or safe areas.',
    escalationActions: 'Remain in the designated safe or evacuation area and expect strong aftershocks. Do not re-enter damaged buildings until authorities declare them safe.',
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
    description: 'Light to moderate rain may affect parts of Marikina City, making roads wet and causing brief water buildup in low-lying areas.',
    recommendedAction: 'Bring rain protection and use caution on wet roads. Monitor official weather updates, especially if you are near flood-prone areas.',
    escalationActions: 'If rain becomes heavier or water begins collecting, expect a Heavy Rain Warning. Limit travel, avoid low-lying roads, and keep emergency supplies ready.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Weather Advisories',
    id: 'weather-occasional-rain',
    title: 'Occasional Rain',
    category: 'Weather',
    severity: 'Low',
    description: 'Cloudy skies with occasional light rain may affect the city and make roads slippery in some areas.',
    recommendedAction: 'Carry rain protection and travel carefully on wet roads. Continue checking official weather updates for any change in conditions.',
    escalationActions: 'If rain becomes persistent or water starts collecting, limit travel, avoid low-lying roads, and prepare emergency supplies. Follow any Heavy Rain Warning issued for your area.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Weather Advisories',
    id: 'weather-heavy-rain',
    title: 'Heavy Rain Warning',
    category: 'Weather',
    severity: 'Moderate',
    description: 'Heavy rain may cause water to collect quickly on roads and in low-lying areas, with possible localized flooding.',
    recommendedAction: 'Avoid unnecessary travel and do not enter flooded roads. Keep essential items ready and monitor official advisories for worsening conditions.',
    escalationActions: 'If rain intensifies or flooding begins, stay indoors when possible, avoid flood-prone roads, and prepare to move to higher ground. Follow any higher weather alert immediately.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Weather Advisories',
    id: 'weather-thunderstorm-mod',
    title: 'Thunderstorm Warning (Moderate)',
    category: 'Weather',
    severity: 'Moderate',
    description: 'Intense rain, lightning, and strong winds may affect Marikina and can cause sudden flooding in vulnerable areas.',
    recommendedAction: 'Stay indoors and away from windows during the storm. Avoid flood-prone roads and open areas, and follow official safety updates.',
    escalationActions: 'If lightning, wind, or rain becomes severe, stay indoors, avoid windows and flood-prone areas, and prepare for a higher alert or evacuation instruction.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Weather Advisories',
    id: 'weather-strong-wind',
    title: 'Strong Wind Warning',
    category: 'Weather',
    severity: 'Moderate',
    description: 'Gusty winds may affect exposed areas and can move unsecured objects, tree branches, or lightweight materials.',
    recommendedAction: 'Secure loose outdoor objects and stay away from trees, damaged structures, and power lines. Remain indoors if winds become stronger.',
    escalationActions: 'If trees, roofs, or power lines begin to fail, stay indoors in a secure room and keep away from windows and electrical hazards. Follow any higher wind warning immediately.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Weather Advisories',
    id: 'weather-thunderstorm-high',
    title: 'Thunderstorm Warning (High)',
    category: 'Weather',
    severity: 'High',
    description: 'Intense rain, lightning, and strong winds may affect Marikina and can cause sudden flooding in vulnerable areas.',
    recommendedAction: 'Stay indoors and away from windows during the storm. Avoid flood-prone roads and open areas, and follow official safety updates.',
    escalationActions: 'If flooding becomes dangerous, winds cause damage, or evacuation is announced, move to a safer location immediately and follow LGU instructions.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Weather Advisories',
    id: 'weather-extreme-heat',
    title: 'Extreme Heat Warning',
    category: 'Weather',
    severity: 'High',
    description: 'High temperatures may increase the risk of heat exhaustion or heat-related illness, especially during prolonged outdoor activity.',
    recommendedAction: 'Drink water regularly and reduce strenuous outdoor activity. Stay in shaded or cool areas and seek help if you feel dizzy, weak, or unwell.',
    escalationActions: 'If you or someone nearby develops dizziness, confusion, fainting, or severe weakness, move to a cool place and seek medical help. Follow emergency heat instructions if issued.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Weather Advisories',
    id: 'weather-damaging-wind',
    title: 'Damaging Wind Warning',
    category: 'Weather',
    severity: 'High',
    description: 'Strong winds may damage lightweight structures, trees, and power lines and may make outdoor travel unsafe.',
    recommendedAction: 'Stay indoors and away from windows where possible. Keep clear of trees, signs, damaged structures, and fallen or hanging power lines.',
    escalationActions: 'If widespread damage occurs or your shelter becomes unsafe, move to a safer indoor location or designated shelter when instructed. Avoid fallen power lines, trees, and debris.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Weather Advisories',
    id: 'weather-severe',
    title: 'Severe Weather Warning',
    category: 'Weather',
    severity: 'Critical',
    description: 'Severe rain, thunderstorms, and strong winds may create dangerous flooding and unsafe travel conditions across affected areas.',
    recommendedAction: 'Move to a safe location before conditions worsen. Avoid floodwaters and follow LGU evacuation or emergency instructions immediately.',
    escalationActions: 'Remain in a safe location or evacuation area while dangerous conditions continue. Stay away from floodwaters, damaged structures, and power lines until authorities issue an all-clear.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'Weather Advisories',
    id: 'weather-torrential-rain',
    title: 'Torrential Rain Warning',
    category: 'Weather',
    severity: 'Critical',
    description: 'Torrential rain may cause rapid and dangerous flooding, especially near rivers, drainage channels, and low-lying communities.',
    recommendedAction: 'Move to higher ground before routes become flooded. Do not cross floodwaters and follow evacuation instructions from local authorities.',
    escalationActions: 'Remain on higher ground or in the evacuation area while flooding is dangerous. Do not cross moving floodwater or return through flooded roads until authorities say it is safe.',
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
    description: 'The river has reached the 1st Alarm level. Water is elevated and may continue rising if rain persists.',
    recommendedAction: 'Monitor official river and weather updates closely. Prepare medicines, documents, food, water, and other emergency supplies in case evacuation becomes necessary.',
    escalationActions: 'If the river reaches 16.00 m — 2nd Alarm, prepare to evacuate, move valuables and essential items higher, and follow LGU instructions for your area.',
    affectedAreas: 'Batasan, San Jose, Tumana'
  },
  {
    group: 'River Monitoring: Batasan',
    id: 'river-batasan-2nd',
    title: 'Batasan: 2nd Alarm (16.0m)',
    category: 'Monitoring',
    severity: 'High',
    description: 'The river has reached the 2nd Alarm level. Continued rise may cause flooding in nearby low-lying and riverside areas.',
    recommendedAction: 'Prepare to evacuate and move important belongings to a higher place. Keep evacuation routes clear and follow LGU instructions for your area.',
    escalationActions: 'If the river reaches 18.00 m — 3rd Alarm, evacuate when instructed and proceed to the designated safe area. Do not walk or drive through floodwater.',
    affectedAreas: 'Batasan, San Jose, Tumana'
  },
  {
    group: 'River Monitoring: Batasan',
    id: 'river-batasan-3rd',
    title: 'Batasan: 3rd Alarm (18.0m)',
    category: 'Monitoring',
    severity: 'Critical',
    description: 'The river has reached the 3rd Alarm level. Flooding risk is critical and nearby communities may be in immediate danger.',
    recommendedAction: 'Evacuate immediately when instructed and proceed to the designated safe area. Do not cross floodwaters or return until authorities say it is safe.',
    escalationActions: 'Remain in the designated safe or evacuation area while flooding remains dangerous. Do not return home until authorities confirm that water levels are receding and it is safe to return.',
    affectedAreas: 'Batasan, San Jose, Tumana'
  },

  // Nangka Station
  {
    group: 'River Monitoring: Nangka',
    id: 'river-nangka-1st',
    title: 'Nangka: 1st Alarm (16.5m)',
    category: 'Monitoring',
    severity: 'Moderate',
    description: 'The river has reached the 1st Alarm level. Water is elevated and may continue rising if rain persists.',
    recommendedAction: 'Monitor official river and weather updates closely. Prepare medicines, documents, food, water, and other emergency supplies in case evacuation becomes necessary.',
    escalationActions: 'If the river reaches 17.10 m — 2nd Alarm, prepare to evacuate, move valuables and essential items higher, and follow LGU instructions for your area.',
    affectedAreas: 'Nangka'
  },
  {
    group: 'River Monitoring: Nangka',
    id: 'river-nangka-2nd',
    title: 'Nangka: 2nd Alarm (17.1m)',
    category: 'Monitoring',
    severity: 'High',
    description: 'The river has reached the 2nd Alarm level. Continued rise may cause flooding in nearby low-lying and riverside areas.',
    recommendedAction: 'Prepare to evacuate and move important belongings to a higher place. Keep evacuation routes clear and follow LGU instructions for your area.',
    escalationActions: 'If the river reaches 17.70 m — 3rd Alarm, evacuate when instructed and proceed to the designated safe area. Do not walk or drive through floodwater.',
    affectedAreas: 'Nangka'
  },
  {
    group: 'River Monitoring: Nangka',
    id: 'river-nangka-3rd',
    title: 'Nangka: 3rd Alarm (17.7m)',
    category: 'Monitoring',
    severity: 'Critical',
    description: 'The river has reached the 3rd Alarm level. Flooding risk is critical and nearby communities may be in immediate danger.',
    recommendedAction: 'Evacuate immediately when instructed and proceed to the designated safe area. Do not cross floodwaters or return until authorities say it is safe.',
    escalationActions: 'Remain in the designated safe or evacuation area while flooding remains dangerous. Do not return home until authorities confirm that water levels are receding and it is safe to return.',
    affectedAreas: 'Nangka'
  },

  // Rodriguez Station
  {
    group: 'River Monitoring: Rodriguez',
    id: 'river-rodriguez-1st',
    title: 'Rodriguez: 1st Alarm (28.8m)',
    category: 'Monitoring',
    severity: 'Moderate',
    description: 'The river has reached the 1st Alarm level. Water is elevated and may continue rising if rain persists.',
    recommendedAction: 'Monitor official river and weather updates closely. Prepare medicines, documents, food, water, and other emergency supplies in case evacuation becomes necessary.',
    escalationActions: 'If the river reaches 29.80 m — 2nd Alarm, prepare to evacuate, move valuables and essential items higher, and follow LGU instructions for your area.',
    affectedAreas: 'Burgos, Rodriguez'
  },
  {
    group: 'River Monitoring: Rodriguez',
    id: 'river-rodriguez-2nd',
    title: 'Rodriguez: 2nd Alarm (29.8m)',
    category: 'Monitoring',
    severity: 'High',
    description: 'The river has reached the 2nd Alarm level. Continued rise may cause flooding in nearby low-lying and riverside areas.',
    recommendedAction: 'Prepare to evacuate and move important belongings to a higher place. Keep evacuation routes clear and follow LGU instructions for your area.',
    escalationActions: 'If the river reaches 30.70 m — 3rd Alarm, evacuate when instructed and proceed to the designated safe area. Do not walk or drive through floodwater.',
    affectedAreas: 'Burgos, Rodriguez'
  },
  {
    group: 'River Monitoring: Rodriguez',
    id: 'river-rodriguez-3rd',
    title: 'Rodriguez: 3rd Alarm (30.7m)',
    category: 'Monitoring',
    severity: 'Critical',
    description: 'The river has reached the 3rd Alarm level. Flooding risk is critical and nearby communities may be in immediate danger.',
    recommendedAction: 'Evacuate immediately when instructed and proceed to the designated safe area. Do not cross floodwaters or return until authorities say it is safe.',
    escalationActions: 'Remain in the designated safe or evacuation area while flooding remains dangerous. Do not return home until authorities confirm that water levels are receding and it is safe to return.',
    affectedAreas: 'Burgos, Rodriguez'
  },

  // San Jose Station
  {
    group: 'River Monitoring: San Jose',
    id: 'river-sanjose-1st',
    title: 'San Jose: 1st Alarm (22.4m)',
    category: 'Monitoring',
    severity: 'Moderate',
    description: 'The river has reached the 1st Alarm level. Water is elevated and may continue rising if rain persists.',
    recommendedAction: 'Monitor official river and weather updates closely. Prepare medicines, documents, food, water, and other emergency supplies in case evacuation becomes necessary.',
    escalationActions: 'If the river reaches 23.00 m — 2nd Alarm, prepare to evacuate, move valuables and essential items higher, and follow LGU instructions for your area.',
    affectedAreas: 'San Jose'
  },
  {
    group: 'River Monitoring: San Jose',
    id: 'river-sanjose-2nd',
    title: 'San Jose: 2nd Alarm (23.0m)',
    category: 'Monitoring',
    severity: 'High',
    description: 'The river has reached the 2nd Alarm level. Continued rise may cause flooding in nearby low-lying and riverside areas.',
    recommendedAction: 'Prepare to evacuate and move important belongings to a higher place. Keep evacuation routes clear and follow LGU instructions for your area.',
    escalationActions: 'If the river reaches 23.60 m — 3rd Alarm, evacuate when instructed and proceed to the designated safe area. Do not walk or drive through floodwater.',
    affectedAreas: 'San Jose'
  },
  {
    group: 'River Monitoring: San Jose',
    id: 'river-sanjose-3rd',
    title: 'San Jose: 3rd Alarm (23.6m)',
    category: 'Monitoring',
    severity: 'Critical',
    description: 'The river has reached the 3rd Alarm level. Flooding risk is critical and nearby communities may be in immediate danger.',
    recommendedAction: 'Evacuate immediately when instructed and proceed to the designated safe area. Do not cross floodwaters or return until authorities say it is safe.',
    escalationActions: 'Remain in the designated safe or evacuation area while flooding remains dangerous. Do not return home until authorities confirm that water levels are receding and it is safe to return.',
    affectedAreas: 'San Jose'
  },

  // Sto. Niño Station
  {
    group: 'River Monitoring: Sto. Niño',
    id: 'river-stonino-1st',
    title: 'Sto. Niño: 1st Alarm (15.0m)',
    category: 'Monitoring',
    severity: 'Moderate',
    description: 'The river has reached the 1st Alarm level. Water is elevated and may continue rising if rain persists.',
    recommendedAction: 'Monitor official river and weather updates closely. Prepare medicines, documents, food, water, and other emergency supplies in case evacuation becomes necessary.',
    escalationActions: 'If the river reaches 16.00 m — 2nd Alarm, prepare to evacuate, move valuables and essential items higher, and follow LGU instructions for your area.',
    affectedAreas: 'Sto. Niño'
  },
  {
    group: 'River Monitoring: Sto. Niño',
    id: 'river-stonino-2nd',
    title: 'Sto. Niño: 2nd Alarm (16.0m)',
    category: 'Monitoring',
    severity: 'High',
    description: 'The river has reached the 2nd Alarm level. Continued rise may cause flooding in nearby low-lying and riverside areas.',
    recommendedAction: 'Prepare to evacuate and move important belongings to a higher place. Keep evacuation routes clear and follow LGU instructions for your area.',
    escalationActions: 'If the river reaches 18.00 m — 3rd Alarm, evacuate when instructed and proceed to the designated safe area. Do not walk or drive through floodwater.',
    affectedAreas: 'Sto. Niño'
  },
  {
    group: 'River Monitoring: Sto. Niño',
    id: 'river-stonino-3rd',
    title: 'Sto. Niño: 3rd Alarm (18.0m)',
    category: 'Monitoring',
    severity: 'Critical',
    description: 'The river has reached the 3rd Alarm level. Flooding risk is critical and nearby communities may be in immediate danger.',
    recommendedAction: 'Evacuate immediately when instructed and proceed to the designated safe area. Do not cross floodwaters or return until authorities say it is safe.',
    escalationActions: 'Remain in the designated safe or evacuation area while flooding remains dangerous. Do not return home until authorities confirm that water levels are receding and it is safe to return.',
    affectedAreas: 'Sto. Niño'
  },

  // Tumana Station
  {
    group: 'River Monitoring: Tumana',
    id: 'river-tumana-1st',
    title: 'Tumana: 1st Alarm (15.0m)',
    category: 'Monitoring',
    severity: 'Moderate',
    description: 'The river has reached the 1st Alarm level. Water is elevated and may continue rising if rain persists.',
    recommendedAction: 'Monitor official river and weather updates closely. Prepare medicines, documents, food, water, and other emergency supplies in case evacuation becomes necessary.',
    escalationActions: 'If the river reaches 16.00 m — 2nd Alarm, prepare to evacuate, move valuables and essential items higher, and follow LGU instructions for your area.',
    affectedAreas: 'Tumana'
  },
  {
    group: 'River Monitoring: Tumana',
    id: 'river-tumana-2nd',
    title: 'Tumana: 2nd Alarm (16.0m)',
    category: 'Monitoring',
    severity: 'High',
    description: 'The river has reached the 2nd Alarm level. Continued rise may cause flooding in nearby low-lying and riverside areas.',
    recommendedAction: 'Prepare to evacuate and move important belongings to a higher place. Keep evacuation routes clear and follow LGU instructions for your area.',
    escalationActions: 'If the river reaches 18.00 m — 3rd Alarm, evacuate when instructed and proceed to the designated safe area. Do not walk or drive through floodwater.',
    affectedAreas: 'Tumana'
  },
  {
    group: 'River Monitoring: Tumana',
    id: 'river-tumana-3rd',
    title: 'Tumana: 3rd Alarm (18.0m)',
    category: 'Monitoring',
    severity: 'Critical',
    description: 'The river has reached the 3rd Alarm level. Flooding risk is critical and nearby communities may be in immediate danger.',
    recommendedAction: 'Evacuate immediately when instructed and proceed to the designated safe area. Do not cross floodwaters or return until authorities say it is safe.',
    escalationActions: 'Remain in the designated safe or evacuation area while flooding remains dangerous. Do not return home until authorities confirm that water levels are receding and it is safe to return.',
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
    description: 'Minor flooding may develop in low-lying or poorly drained areas, and some roads may begin to hold water.',
    recommendedAction: 'Monitor official updates and nearby water levels. Avoid flood-prone roads and keep essential items ready in case conditions worsen.',
    escalationActions: 'If water begins covering roads or entering low areas in Marikina City, limit travel, secure important belongings, and prepare emergency supplies. Follow the next Flood Advisory immediately.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'City & District Flood Advisories',
    id: 'flood-city-advisory',
    title: 'Flood Advisory: Marikina City',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding is developing in vulnerable areas and may begin affecting roads, homes, or normal travel.',
    recommendedAction: 'Limit travel and avoid flooded streets. Secure valuables, prepare emergency supplies, and be ready to move if water continues rising.',
    escalationActions: 'If water keeps rising or roads in Marikina City become difficult to pass, move valuables and vehicles to higher ground and prepare to evacuate. Follow any Flood Warning immediately.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'City & District Flood Advisories',
    id: 'flood-city-warning',
    title: 'Flood Warning: Marikina City',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect Marikina City, making some roads unsafe and increasing the need to move to higher ground.',
    recommendedAction: 'Move vehicles, valuables, and essential items to higher ground. Prepare to evacuate and use only safe routes identified by local authorities.',
    escalationActions: 'If water reaches homes or an evacuation order is issued for Marikina City, leave immediately using designated safe routes. Do not walk or drive through floodwater.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'City & District Flood Advisories',
    id: 'flood-city-emergency',
    title: 'Flood Emergency: Marikina City',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting Marikina City and may threaten homes, roads, and access routes; evacuation may be required.',
    recommendedAction: 'Evacuate immediately when instructed and proceed to the designated safe area. Never walk or drive through floodwaters, and follow LGU emergency instructions.',
    escalationActions: 'Stay in the designated evacuation or safe area while flooding in Marikina City remains dangerous. Do not return home until authorities issue an all-clear and confirm routes are safe.',
    affectedAreas: 'All Marikina City'
  },
  {
    group: 'City & District Flood Advisories',
    id: 'flood-d1-watch',
    title: 'Flood Watch: District 1',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop in low-lying or poorly drained areas, and some roads may begin to hold water.',
    recommendedAction: 'Monitor official updates and nearby water levels. Avoid flood-prone roads and keep essential items ready in case conditions worsen.',
    escalationActions: 'If water begins covering roads or entering low areas in District 1, limit travel, secure important belongings, and prepare emergency supplies. Follow the next Flood Advisory immediately.',
    affectedAreas: d1Barangays
  },
  {
    group: 'City & District Flood Advisories',
    id: 'flood-d1-advisory',
    title: 'Flood Advisory: District 1',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding is developing in vulnerable areas and may begin affecting roads, homes, or normal travel.',
    recommendedAction: 'Limit travel and avoid flooded streets. Secure valuables, prepare emergency supplies, and be ready to move if water continues rising.',
    escalationActions: 'If water keeps rising or roads in District 1 become difficult to pass, move valuables and vehicles to higher ground and prepare to evacuate. Follow any Flood Warning immediately.',
    affectedAreas: d1Barangays
  },
  {
    group: 'City & District Flood Advisories',
    id: 'flood-d1-warning',
    title: 'Flood Warning: District 1',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect District 1, making some roads unsafe and increasing the need to move to higher ground.',
    recommendedAction: 'Move vehicles, valuables, and essential items to higher ground. Prepare to evacuate and use only safe routes identified by local authorities.',
    escalationActions: 'If water reaches homes or an evacuation order is issued for District 1, leave immediately using designated safe routes. Do not walk or drive through floodwater.',
    affectedAreas: d1Barangays
  },
  {
    group: 'City & District Flood Advisories',
    id: 'flood-d1-emergency',
    title: 'Flood Emergency: District 1',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting District 1 and may threaten homes, roads, and access routes; evacuation may be required.',
    recommendedAction: 'Evacuate immediately when instructed and proceed to the designated safe area. Never walk or drive through floodwaters, and follow LGU emergency instructions.',
    escalationActions: 'Stay in the designated evacuation or safe area while flooding in District 1 remains dangerous. Do not return home until authorities issue an all-clear and confirm routes are safe.',
    affectedAreas: d1Barangays
  },
  {
    group: 'City & District Flood Advisories',
    id: 'flood-d2-watch',
    title: 'Flood Watch: District 2',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop in low-lying or poorly drained areas, and some roads may begin to hold water.',
    recommendedAction: 'Monitor official updates and nearby water levels. Avoid flood-prone roads and keep essential items ready in case conditions worsen.',
    escalationActions: 'If water begins covering roads or entering low areas in District 2, limit travel, secure important belongings, and prepare emergency supplies. Follow the next Flood Advisory immediately.',
    affectedAreas: d2Barangays
  },
  {
    group: 'City & District Flood Advisories',
    id: 'flood-d2-advisory',
    title: 'Flood Advisory: District 2',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding is developing in vulnerable areas and may begin affecting roads, homes, or normal travel.',
    recommendedAction: 'Limit travel and avoid flooded streets. Secure valuables, prepare emergency supplies, and be ready to move if water continues rising.',
    escalationActions: 'If water keeps rising or roads in District 2 become difficult to pass, move valuables and vehicles to higher ground and prepare to evacuate. Follow any Flood Warning immediately.',
    affectedAreas: d2Barangays
  },
  {
    group: 'City & District Flood Advisories',
    id: 'flood-d2-warning',
    title: 'Flood Warning: District 2',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect District 2, making some roads unsafe and increasing the need to move to higher ground.',
    recommendedAction: 'Move vehicles, valuables, and essential items to higher ground. Prepare to evacuate and use only safe routes identified by local authorities.',
    escalationActions: 'If water reaches homes or an evacuation order is issued for District 2, leave immediately using designated safe routes. Do not walk or drive through floodwater.',
    affectedAreas: d2Barangays
  },
  {
    group: 'City & District Flood Advisories',
    id: 'flood-d2-emergency',
    title: 'Flood Emergency: District 2',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting District 2 and may threaten homes, roads, and access routes; evacuation may be required.',
    recommendedAction: 'Evacuate immediately when instructed and proceed to the designated safe area. Never walk or drive through floodwaters, and follow LGU emergency instructions.',
    escalationActions: 'Stay in the designated evacuation or safe area while flooding in District 2 remains dangerous. Do not return home until authorities issue an all-clear and confirm routes are safe.',
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
    description: 'Minor flooding may develop in low-lying or poorly drained areas, and some roads may begin to hold water.',
    recommendedAction: 'Monitor official updates and nearby water levels. Avoid flood-prone roads and keep essential items ready in case conditions worsen.',
    escalationActions: 'If water begins covering roads or entering low areas in Barangka, limit travel, secure important belongings, and prepare emergency supplies. Follow the next Flood Advisory immediately.',
    affectedAreas: 'Barangka'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-barangka-mod',
    title: 'Flood Advisory: Barangka',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding is developing in vulnerable areas and may begin affecting roads, homes, or normal travel.',
    recommendedAction: 'Limit travel and avoid flooded streets. Secure valuables, prepare emergency supplies, and be ready to move if water continues rising.',
    escalationActions: 'If water keeps rising or roads in Barangka become difficult to pass, move valuables and vehicles to higher ground and prepare to evacuate. Follow any Flood Warning immediately.',
    affectedAreas: 'Barangka'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-barangka-high',
    title: 'Flood Warning: Barangka',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect Barangka, making some roads unsafe and increasing the need to move to higher ground.',
    recommendedAction: 'Move vehicles, valuables, and essential items to higher ground. Prepare to evacuate and use only safe routes identified by local authorities.',
    escalationActions: 'If water reaches homes or an evacuation order is issued for Barangka, leave immediately using designated safe routes. Do not walk or drive through floodwater.',
    affectedAreas: 'Barangka'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-barangka-crit',
    title: 'Flood Emergency: Barangka',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting Barangka and may threaten homes, roads, and access routes; evacuation may be required.',
    recommendedAction: 'Evacuate immediately when instructed and proceed to the designated safe area. Never walk or drive through floodwaters, and follow LGU emergency instructions.',
    escalationActions: 'Stay in the designated evacuation or safe area while flooding in Barangka remains dangerous. Do not return home until authorities issue an all-clear and confirm routes are safe.',
    affectedAreas: 'Barangka'
  },

  // Industrial Valley Complex
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-ivc-low',
    title: 'Flood Watch: Industrial Valley Complex',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop in low-lying or poorly drained areas, and some roads may begin to hold water.',
    recommendedAction: 'Monitor official updates and nearby water levels. Avoid flood-prone roads and keep essential items ready in case conditions worsen.',
    escalationActions: 'If water begins covering roads or entering low areas in Industrial Valley Complex, limit travel, secure important belongings, and prepare emergency supplies. Follow the next Flood Advisory immediately.',
    affectedAreas: 'Industrial Valley Complex'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-ivc-mod',
    title: 'Flood Advisory: Industrial Valley Complex',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding is developing in vulnerable areas and may begin affecting roads, homes, or normal travel.',
    recommendedAction: 'Limit travel and avoid flooded streets. Secure valuables, prepare emergency supplies, and be ready to move if water continues rising.',
    escalationActions: 'If water keeps rising or roads in Industrial Valley Complex become difficult to pass, move valuables and vehicles to higher ground and prepare to evacuate. Follow any Flood Warning immediately.',
    affectedAreas: 'Industrial Valley Complex'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-ivc-high',
    title: 'Flood Warning: Industrial Valley Complex',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect Industrial Valley Complex, making some roads unsafe and increasing the need to move to higher ground.',
    recommendedAction: 'Move vehicles, valuables, and essential items to higher ground. Prepare to evacuate and use only safe routes identified by local authorities.',
    escalationActions: 'If water reaches homes or an evacuation order is issued for Industrial Valley Complex, leave immediately using designated safe routes. Do not walk or drive through floodwater.',
    affectedAreas: 'Industrial Valley Complex'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-ivc-crit',
    title: 'Flood Emergency: Industrial Valley Complex',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting Industrial Valley Complex and may threaten homes, roads, and access routes; evacuation may be required.',
    recommendedAction: 'Evacuate immediately when instructed and proceed to the designated safe area. Never walk or drive through floodwaters, and follow LGU emergency instructions.',
    escalationActions: 'Stay in the designated evacuation or safe area while flooding in Industrial Valley Complex remains dangerous. Do not return home until authorities issue an all-clear and confirm routes are safe.',
    affectedAreas: 'Industrial Valley Complex'
  },

  // Jesus dela Peña
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-jdp-low',
    title: 'Flood Watch: Jesus dela Peña',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop in low-lying or poorly drained areas, and some roads may begin to hold water.',
    recommendedAction: 'Monitor official updates and nearby water levels. Avoid flood-prone roads and keep essential items ready in case conditions worsen.',
    escalationActions: 'If water begins covering roads or entering low areas in Jesus dela Peña, limit travel, secure important belongings, and prepare emergency supplies. Follow the next Flood Advisory immediately.',
    affectedAreas: 'Jesus dela Peña'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-jdp-mod',
    title: 'Flood Advisory: Jesus dela Peña',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding is developing in vulnerable areas and may begin affecting roads, homes, or normal travel.',
    recommendedAction: 'Limit travel and avoid flooded streets. Secure valuables, prepare emergency supplies, and be ready to move if water continues rising.',
    escalationActions: 'If water keeps rising or roads in Jesus dela Peña become difficult to pass, move valuables and vehicles to higher ground and prepare to evacuate. Follow any Flood Warning immediately.',
    affectedAreas: 'Jesus dela Peña'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-jdp-high',
    title: 'Flood Warning: Jesus dela Peña',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect Jesus dela Peña, making some roads unsafe and increasing the need to move to higher ground.',
    recommendedAction: 'Move vehicles, valuables, and essential items to higher ground. Prepare to evacuate and use only safe routes identified by local authorities.',
    escalationActions: 'If water reaches homes or an evacuation order is issued for Jesus dela Peña, leave immediately using designated safe routes. Do not walk or drive through floodwater.',
    affectedAreas: 'Jesus dela Peña'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-jdp-crit',
    title: 'Flood Emergency: Jesus dela Peña',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting Jesus dela Peña and may threaten homes, roads, and access routes; evacuation may be required.',
    recommendedAction: 'Evacuate immediately when instructed and proceed to the designated safe area. Never walk or drive through floodwaters, and follow LGU emergency instructions.',
    escalationActions: 'Stay in the designated evacuation or safe area while flooding in Jesus dela Peña remains dangerous. Do not return home until authorities issue an all-clear and confirm routes are safe.',
    affectedAreas: 'Jesus dela Peña'
  },

  // Kalumpang
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-kalumpang-low',
    title: 'Flood Watch: Kalumpang',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop in low-lying or poorly drained areas, and some roads may begin to hold water.',
    recommendedAction: 'Monitor official updates and nearby water levels. Avoid flood-prone roads and keep essential items ready in case conditions worsen.',
    escalationActions: 'If water begins covering roads or entering low areas in Kalumpang, limit travel, secure important belongings, and prepare emergency supplies. Follow the next Flood Advisory immediately.',
    affectedAreas: 'Kalumpang (Calumpang)'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-kalumpang-mod',
    title: 'Flood Advisory: Kalumpang',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding is developing in vulnerable areas and may begin affecting roads, homes, or normal travel.',
    recommendedAction: 'Limit travel and avoid flooded streets. Secure valuables, prepare emergency supplies, and be ready to move if water continues rising.',
    escalationActions: 'If water keeps rising or roads in Kalumpang become difficult to pass, move valuables and vehicles to higher ground and prepare to evacuate. Follow any Flood Warning immediately.',
    affectedAreas: 'Kalumpang (Calumpang)'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-kalumpang-high',
    title: 'Flood Warning: Kalumpang',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect Kalumpang, making some roads unsafe and increasing the need to move to higher ground.',
    recommendedAction: 'Move vehicles, valuables, and essential items to higher ground. Prepare to evacuate and use only safe routes identified by local authorities.',
    escalationActions: 'If water reaches homes or an evacuation order is issued for Kalumpang, leave immediately using designated safe routes. Do not walk or drive through floodwater.',
    affectedAreas: 'Kalumpang (Calumpang)'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-kalumpang-crit',
    title: 'Flood Emergency: Kalumpang',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting Kalumpang and may threaten homes, roads, and access routes; evacuation may be required.',
    recommendedAction: 'Evacuate immediately when instructed and proceed to the designated safe area. Never walk or drive through floodwaters, and follow LGU emergency instructions.',
    escalationActions: 'Stay in the designated evacuation or safe area while flooding in Kalumpang remains dangerous. Do not return home until authorities issue an all-clear and confirm routes are safe.',
    affectedAreas: 'Kalumpang (Calumpang)'
  },

  // Malanday
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-malanday-low',
    title: 'Flood Watch: Malanday',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop in low-lying or poorly drained areas, and some roads may begin to hold water.',
    recommendedAction: 'Monitor official updates and nearby water levels. Avoid flood-prone roads and keep essential items ready in case conditions worsen.',
    escalationActions: 'If water begins covering roads or entering low areas in Malanday, limit travel, secure important belongings, and prepare emergency supplies. Follow the next Flood Advisory immediately.',
    affectedAreas: 'Malanday'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-malanday-mod',
    title: 'Flood Advisory: Malanday',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding is developing in vulnerable areas and may begin affecting roads, homes, or normal travel.',
    recommendedAction: 'Limit travel and avoid flooded streets. Secure valuables, prepare emergency supplies, and be ready to move if water continues rising.',
    escalationActions: 'If water keeps rising or roads in Malanday become difficult to pass, move valuables and vehicles to higher ground and prepare to evacuate. Follow any Flood Warning immediately.',
    affectedAreas: 'Malanday'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-malanday-high',
    title: 'Flood Warning: Malanday',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect Malanday, making some roads unsafe and increasing the need to move to higher ground.',
    recommendedAction: 'Move vehicles, valuables, and essential items to higher ground. Prepare to evacuate and use only safe routes identified by local authorities.',
    escalationActions: 'If water reaches homes or an evacuation order is issued for Malanday, leave immediately using designated safe routes. Do not walk or drive through floodwater.',
    affectedAreas: 'Malanday'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-malanday-crit',
    title: 'Flood Emergency: Malanday',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting Malanday and may threaten homes, roads, and access routes; evacuation may be required.',
    recommendedAction: 'Evacuate immediately when instructed and proceed to the designated safe area. Never walk or drive through floodwaters, and follow LGU emergency instructions.',
    escalationActions: 'Stay in the designated evacuation or safe area while flooding in Malanday remains dangerous. Do not return home until authorities issue an all-clear and confirm routes are safe.',
    affectedAreas: 'Malanday'
  },

  // San Roque
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-sanroque-low',
    title: 'Flood Watch: San Roque',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop in low-lying or poorly drained areas, and some roads may begin to hold water.',
    recommendedAction: 'Monitor official updates and nearby water levels. Avoid flood-prone roads and keep essential items ready in case conditions worsen.',
    escalationActions: 'If water begins covering roads or entering low areas in San Roque, limit travel, secure important belongings, and prepare emergency supplies. Follow the next Flood Advisory immediately.',
    affectedAreas: 'San Roque'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-sanroque-mod',
    title: 'Flood Advisory: San Roque',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding is developing in vulnerable areas and may begin affecting roads, homes, or normal travel.',
    recommendedAction: 'Limit travel and avoid flooded streets. Secure valuables, prepare emergency supplies, and be ready to move if water continues rising.',
    escalationActions: 'If water keeps rising or roads in San Roque become difficult to pass, move valuables and vehicles to higher ground and prepare to evacuate. Follow any Flood Warning immediately.',
    affectedAreas: 'San Roque'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-sanroque-high',
    title: 'Flood Warning: San Roque',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect San Roque, making some roads unsafe and increasing the need to move to higher ground.',
    recommendedAction: 'Move vehicles, valuables, and essential items to higher ground. Prepare to evacuate and use only safe routes identified by local authorities.',
    escalationActions: 'If water reaches homes or an evacuation order is issued for San Roque, leave immediately using designated safe routes. Do not walk or drive through floodwater.',
    affectedAreas: 'San Roque'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-sanroque-crit',
    title: 'Flood Emergency: San Roque',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting San Roque and may threaten homes, roads, and access routes; evacuation may be required.',
    recommendedAction: 'Evacuate immediately when instructed and proceed to the designated safe area. Never walk or drive through floodwaters, and follow LGU emergency instructions.',
    escalationActions: 'Stay in the designated evacuation or safe area while flooding in San Roque remains dangerous. Do not return home until authorities issue an all-clear and confirm routes are safe.',
    affectedAreas: 'San Roque'
  },

  // Sta. Elena
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-staelena-low',
    title: 'Flood Watch: Sta. Elena',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop in low-lying or poorly drained areas, and some roads may begin to hold water.',
    recommendedAction: 'Monitor official updates and nearby water levels. Avoid flood-prone roads and keep essential items ready in case conditions worsen.',
    escalationActions: 'If water begins covering roads or entering low areas in Sta. Elena, limit travel, secure important belongings, and prepare emergency supplies. Follow the next Flood Advisory immediately.',
    affectedAreas: 'Sta. Elena'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-staelena-mod',
    title: 'Flood Advisory: Sta. Elena',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding is developing in vulnerable areas and may begin affecting roads, homes, or normal travel.',
    recommendedAction: 'Limit travel and avoid flooded streets. Secure valuables, prepare emergency supplies, and be ready to move if water continues rising.',
    escalationActions: 'If water keeps rising or roads in Sta. Elena become difficult to pass, move valuables and vehicles to higher ground and prepare to evacuate. Follow any Flood Warning immediately.',
    affectedAreas: 'Sta. Elena'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-staelena-high',
    title: 'Flood Warning: Sta. Elena',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect Sta. Elena, making some roads unsafe and increasing the need to move to higher ground.',
    recommendedAction: 'Move vehicles, valuables, and essential items to higher ground. Prepare to evacuate and use only safe routes identified by local authorities.',
    escalationActions: 'If water reaches homes or an evacuation order is issued for Sta. Elena, leave immediately using designated safe routes. Do not walk or drive through floodwater.',
    affectedAreas: 'Sta. Elena'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-staelena-crit',
    title: 'Flood Emergency: Sta. Elena',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting Sta. Elena and may threaten homes, roads, and access routes; evacuation may be required.',
    recommendedAction: 'Evacuate immediately when instructed and proceed to the designated safe area. Never walk or drive through floodwaters, and follow LGU emergency instructions.',
    escalationActions: 'Stay in the designated evacuation or safe area while flooding in Sta. Elena remains dangerous. Do not return home until authorities issue an all-clear and confirm routes are safe.',
    affectedAreas: 'Sta. Elena'
  },

  // Sto. Niño
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-stonino-low',
    title: 'Flood Watch: Sto. Niño',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop in low-lying or poorly drained areas, and some roads may begin to hold water.',
    recommendedAction: 'Monitor official updates and nearby water levels. Avoid flood-prone roads and keep essential items ready in case conditions worsen.',
    escalationActions: 'If water begins covering roads or entering low areas in Sto. Niño, limit travel, secure important belongings, and prepare emergency supplies. Follow the next Flood Advisory immediately.',
    affectedAreas: 'Sto. Niño'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-stonino-mod',
    title: 'Flood Advisory: Sto. Niño',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding is developing in vulnerable areas and may begin affecting roads, homes, or normal travel.',
    recommendedAction: 'Limit travel and avoid flooded streets. Secure valuables, prepare emergency supplies, and be ready to move if water continues rising.',
    escalationActions: 'If water keeps rising or roads in Sto. Niño become difficult to pass, move valuables and vehicles to higher ground and prepare to evacuate. Follow any Flood Warning immediately.',
    affectedAreas: 'Sto. Niño'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-stonino-high',
    title: 'Flood Warning: Sto. Niño',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect Sto. Niño, making some roads unsafe and increasing the need to move to higher ground.',
    recommendedAction: 'Move vehicles, valuables, and essential items to higher ground. Prepare to evacuate and use only safe routes identified by local authorities.',
    escalationActions: 'If water reaches homes or an evacuation order is issued for Sto. Niño, leave immediately using designated safe routes. Do not walk or drive through floodwater.',
    affectedAreas: 'Sto. Niño'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-stonino-crit',
    title: 'Flood Emergency: Sto. Niño',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting Sto. Niño and may threaten homes, roads, and access routes; evacuation may be required.',
    recommendedAction: 'Evacuate immediately when instructed and proceed to the designated safe area. Never walk or drive through floodwaters, and follow LGU emergency instructions.',
    escalationActions: 'Stay in the designated evacuation or safe area while flooding in Sto. Niño remains dangerous. Do not return home until authorities issue an all-clear and confirm routes are safe.',
    affectedAreas: 'Sto. Niño'
  },

  // Tañong
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-tanong-low',
    title: 'Flood Watch: Tañong',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop in low-lying or poorly drained areas, and some roads may begin to hold water.',
    recommendedAction: 'Monitor official updates and nearby water levels. Avoid flood-prone roads and keep essential items ready in case conditions worsen.',
    escalationActions: 'If water begins covering roads or entering low areas in Tañong, limit travel, secure important belongings, and prepare emergency supplies. Follow the next Flood Advisory immediately.',
    affectedAreas: 'Tañong'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-tanong-mod',
    title: 'Flood Advisory: Tañong',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding is developing in vulnerable areas and may begin affecting roads, homes, or normal travel.',
    recommendedAction: 'Limit travel and avoid flooded streets. Secure valuables, prepare emergency supplies, and be ready to move if water continues rising.',
    escalationActions: 'If water keeps rising or roads in Tañong become difficult to pass, move valuables and vehicles to higher ground and prepare to evacuate. Follow any Flood Warning immediately.',
    affectedAreas: 'Tañong'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-tanong-high',
    title: 'Flood Warning: Tañong',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect Tañong, making some roads unsafe and increasing the need to move to higher ground.',
    recommendedAction: 'Move vehicles, valuables, and essential items to higher ground. Prepare to evacuate and use only safe routes identified by local authorities.',
    escalationActions: 'If water reaches homes or an evacuation order is issued for Tañong, leave immediately using designated safe routes. Do not walk or drive through floodwater.',
    affectedAreas: 'Tañong'
  },
  {
    group: 'Barangay Flood Advisories: District 1',
    id: 'flood-tanong-crit',
    title: 'Flood Emergency: Tañong',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting Tañong and may threaten homes, roads, and access routes; evacuation may be required.',
    recommendedAction: 'Evacuate immediately when instructed and proceed to the designated safe area. Never walk or drive through floodwaters, and follow LGU emergency instructions.',
    escalationActions: 'Stay in the designated evacuation or safe area while flooding in Tañong remains dangerous. Do not return home until authorities issue an all-clear and confirm routes are safe.',
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
    description: 'Minor flooding may develop in low-lying or poorly drained areas, and some roads may begin to hold water.',
    recommendedAction: 'Monitor official updates and nearby water levels. Avoid flood-prone roads and keep essential items ready in case conditions worsen.',
    escalationActions: 'If water begins covering roads or entering low areas in Concepcion I, limit travel, secure important belongings, and prepare emergency supplies. Follow the next Flood Advisory immediately.',
    affectedAreas: 'Concepcion I (Concepcion Uno)'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-concepcion1-mod',
    title: 'Flood Advisory: Concepcion I',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding is developing in vulnerable areas and may begin affecting roads, homes, or normal travel.',
    recommendedAction: 'Limit travel and avoid flooded streets. Secure valuables, prepare emergency supplies, and be ready to move if water continues rising.',
    escalationActions: 'If water keeps rising or roads in Concepcion I become difficult to pass, move valuables and vehicles to higher ground and prepare to evacuate. Follow any Flood Warning immediately.',
    affectedAreas: 'Concepcion I (Concepcion Uno)'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-concepcion1-high',
    title: 'Flood Warning: Concepcion I',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect Concepcion I, making some roads unsafe and increasing the need to move to higher ground.',
    recommendedAction: 'Move vehicles, valuables, and essential items to higher ground. Prepare to evacuate and use only safe routes identified by local authorities.',
    escalationActions: 'If water reaches homes or an evacuation order is issued for Concepcion I, leave immediately using designated safe routes. Do not walk or drive through floodwater.',
    affectedAreas: 'Concepcion I (Concepcion Uno)'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-concepcion1-crit',
    title: 'Flood Emergency: Concepcion I',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting Concepcion I and may threaten homes, roads, and access routes; evacuation may be required.',
    recommendedAction: 'Evacuate immediately when instructed and proceed to the designated safe area. Never walk or drive through floodwaters, and follow LGU emergency instructions.',
    escalationActions: 'Stay in the designated evacuation or safe area while flooding in Concepcion I remains dangerous. Do not return home until authorities issue an all-clear and confirm routes are safe.',
    affectedAreas: 'Concepcion I (Concepcion Uno)'
  },

  // Concepcion II
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-concepcion2-low',
    title: 'Flood Watch: Concepcion II',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop in low-lying or poorly drained areas, and some roads may begin to hold water.',
    recommendedAction: 'Monitor official updates and nearby water levels. Avoid flood-prone roads and keep essential items ready in case conditions worsen.',
    escalationActions: 'If water begins covering roads or entering low areas in Concepcion II, limit travel, secure important belongings, and prepare emergency supplies. Follow the next Flood Advisory immediately.',
    affectedAreas: 'Concepcion II (Concepcion Dos)'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-concepcion2-mod',
    title: 'Flood Advisory: Concepcion II',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding is developing in vulnerable areas and may begin affecting roads, homes, or normal travel.',
    recommendedAction: 'Limit travel and avoid flooded streets. Secure valuables, prepare emergency supplies, and be ready to move if water continues rising.',
    escalationActions: 'If water keeps rising or roads in Concepcion II become difficult to pass, move valuables and vehicles to higher ground and prepare to evacuate. Follow any Flood Warning immediately.',
    affectedAreas: 'Concepcion II (Concepcion Dos)'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-concepcion2-high',
    title: 'Flood Warning: Concepcion II',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect Concepcion II, making some roads unsafe and increasing the need to move to higher ground.',
    recommendedAction: 'Move vehicles, valuables, and essential items to higher ground. Prepare to evacuate and use only safe routes identified by local authorities.',
    escalationActions: 'If water reaches homes or an evacuation order is issued for Concepcion II, leave immediately using designated safe routes. Do not walk or drive through floodwater.',
    affectedAreas: 'Concepcion II (Concepcion Dos)'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-concepcion2-crit',
    title: 'Flood Emergency: Concepcion II',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting Concepcion II and may threaten homes, roads, and access routes; evacuation may be required.',
    recommendedAction: 'Evacuate immediately when instructed and proceed to the designated safe area. Never walk or drive through floodwaters, and follow LGU emergency instructions.',
    escalationActions: 'Stay in the designated evacuation or safe area while flooding in Concepcion II remains dangerous. Do not return home until authorities issue an all-clear and confirm routes are safe.',
    affectedAreas: 'Concepcion II (Concepcion Dos)'
  },

  // Fortune
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-fortune-low',
    title: 'Flood Watch: Fortune',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop in low-lying or poorly drained areas, and some roads may begin to hold water.',
    recommendedAction: 'Monitor official updates and nearby water levels. Avoid flood-prone roads and keep essential items ready in case conditions worsen.',
    escalationActions: 'If water begins covering roads or entering low areas in Fortune, limit travel, secure important belongings, and prepare emergency supplies. Follow the next Flood Advisory immediately.',
    affectedAreas: 'Fortune'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-fortune-mod',
    title: 'Flood Advisory: Fortune',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding is developing in vulnerable areas and may begin affecting roads, homes, or normal travel.',
    recommendedAction: 'Limit travel and avoid flooded streets. Secure valuables, prepare emergency supplies, and be ready to move if water continues rising.',
    escalationActions: 'If water keeps rising or roads in Fortune become difficult to pass, move valuables and vehicles to higher ground and prepare to evacuate. Follow any Flood Warning immediately.',
    affectedAreas: 'Fortune'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-fortune-high',
    title: 'Flood Warning: Fortune',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect Fortune, making some roads unsafe and increasing the need to move to higher ground.',
    recommendedAction: 'Move vehicles, valuables, and essential items to higher ground. Prepare to evacuate and use only safe routes identified by local authorities.',
    escalationActions: 'If water reaches homes or an evacuation order is issued for Fortune, leave immediately using designated safe routes. Do not walk or drive through floodwater.',
    affectedAreas: 'Fortune'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-fortune-crit',
    title: 'Flood Emergency: Fortune',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting Fortune and may threaten homes, roads, and access routes; evacuation may be required.',
    recommendedAction: 'Evacuate immediately when instructed and proceed to the designated safe area. Never walk or drive through floodwaters, and follow LGU emergency instructions.',
    escalationActions: 'Stay in the designated evacuation or safe area while flooding in Fortune remains dangerous. Do not return home until authorities issue an all-clear and confirm routes are safe.',
    affectedAreas: 'Fortune'
  },

  // Marikina Heights
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-mh-low',
    title: 'Flood Watch: Marikina Heights',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop in low-lying or poorly drained areas, and some roads may begin to hold water.',
    recommendedAction: 'Monitor official updates and nearby water levels. Avoid flood-prone roads and keep essential items ready in case conditions worsen.',
    escalationActions: 'If water begins covering roads or entering low areas in Marikina Heights, limit travel, secure important belongings, and prepare emergency supplies. Follow the next Flood Advisory immediately.',
    affectedAreas: 'Marikina Heights'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-mh-mod',
    title: 'Flood Advisory: Marikina Heights',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding is developing in vulnerable areas and may begin affecting roads, homes, or normal travel.',
    recommendedAction: 'Limit travel and avoid flooded streets. Secure valuables, prepare emergency supplies, and be ready to move if water continues rising.',
    escalationActions: 'If water keeps rising or roads in Marikina Heights become difficult to pass, move valuables and vehicles to higher ground and prepare to evacuate. Follow any Flood Warning immediately.',
    affectedAreas: 'Marikina Heights'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-mh-high',
    title: 'Flood Warning: Marikina Heights',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect Marikina Heights, making some roads unsafe and increasing the need to move to higher ground.',
    recommendedAction: 'Move vehicles, valuables, and essential items to higher ground. Prepare to evacuate and use only safe routes identified by local authorities.',
    escalationActions: 'If water reaches homes or an evacuation order is issued for Marikina Heights, leave immediately using designated safe routes. Do not walk or drive through floodwater.',
    affectedAreas: 'Marikina Heights'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-mh-crit',
    title: 'Flood Emergency: Marikina Heights',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting Marikina Heights and may threaten homes, roads, and access routes; evacuation may be required.',
    recommendedAction: 'Evacuate immediately when instructed and proceed to the designated safe area. Never walk or drive through floodwaters, and follow LGU emergency instructions.',
    escalationActions: 'Stay in the designated evacuation or safe area while flooding in Marikina Heights remains dangerous. Do not return home until authorities issue an all-clear and confirm routes are safe.',
    affectedAreas: 'Marikina Heights'
  },

  // Nangka
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-nangka-low',
    title: 'Flood Watch: Nangka',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop in low-lying or poorly drained areas, and some roads may begin to hold water.',
    recommendedAction: 'Monitor official updates and nearby water levels. Avoid flood-prone roads and keep essential items ready in case conditions worsen.',
    escalationActions: 'If water begins covering roads or entering low areas in Nangka, limit travel, secure important belongings, and prepare emergency supplies. Follow the next Flood Advisory immediately.',
    affectedAreas: 'Nangka'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-nangka-mod',
    title: 'Flood Advisory: Nangka',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding is developing in vulnerable areas and may begin affecting roads, homes, or normal travel.',
    recommendedAction: 'Limit travel and avoid flooded streets. Secure valuables, prepare emergency supplies, and be ready to move if water continues rising.',
    escalationActions: 'If water keeps rising or roads in Nangka become difficult to pass, move valuables and vehicles to higher ground and prepare to evacuate. Follow any Flood Warning immediately.',
    affectedAreas: 'Nangka'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-nangka-high',
    title: 'Flood Warning: Nangka',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect Nangka, making some roads unsafe and increasing the need to move to higher ground.',
    recommendedAction: 'Move vehicles, valuables, and essential items to higher ground. Prepare to evacuate and use only safe routes identified by local authorities.',
    escalationActions: 'If water reaches homes or an evacuation order is issued for Nangka, leave immediately using designated safe routes. Do not walk or drive through floodwater.',
    affectedAreas: 'Nangka'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-nangka-crit',
    title: 'Flood Emergency: Nangka',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting Nangka and may threaten homes, roads, and access routes; evacuation may be required.',
    recommendedAction: 'Evacuate immediately when instructed and proceed to the designated safe area. Never walk or drive through floodwaters, and follow LGU emergency instructions.',
    escalationActions: 'Stay in the designated evacuation or safe area while flooding in Nangka remains dangerous. Do not return home until authorities issue an all-clear and confirm routes are safe.',
    affectedAreas: 'Nangka'
  },

  // Parang
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-parang-low',
    title: 'Flood Watch: Parang',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop in low-lying or poorly drained areas, and some roads may begin to hold water.',
    recommendedAction: 'Monitor official updates and nearby water levels. Avoid flood-prone roads and keep essential items ready in case conditions worsen.',
    escalationActions: 'If water begins covering roads or entering low areas in Parang, limit travel, secure important belongings, and prepare emergency supplies. Follow the next Flood Advisory immediately.',
    affectedAreas: 'Parang'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-parang-mod',
    title: 'Flood Advisory: Parang',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding is developing in vulnerable areas and may begin affecting roads, homes, or normal travel.',
    recommendedAction: 'Limit travel and avoid flooded streets. Secure valuables, prepare emergency supplies, and be ready to move if water continues rising.',
    escalationActions: 'If water keeps rising or roads in Parang become difficult to pass, move valuables and vehicles to higher ground and prepare to evacuate. Follow any Flood Warning immediately.',
    affectedAreas: 'Parang'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-parang-high',
    title: 'Flood Warning: Parang',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect Parang, making some roads unsafe and increasing the need to move to higher ground.',
    recommendedAction: 'Move vehicles, valuables, and essential items to higher ground. Prepare to evacuate and use only safe routes identified by local authorities.',
    escalationActions: 'If water reaches homes or an evacuation order is issued for Parang, leave immediately using designated safe routes. Do not walk or drive through floodwater.',
    affectedAreas: 'Parang'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-parang-crit',
    title: 'Flood Emergency: Parang',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting Parang and may threaten homes, roads, and access routes; evacuation may be required.',
    recommendedAction: 'Evacuate immediately when instructed and proceed to the designated safe area. Never walk or drive through floodwaters, and follow LGU emergency instructions.',
    escalationActions: 'Stay in the designated evacuation or safe area while flooding in Parang remains dangerous. Do not return home until authorities issue an all-clear and confirm routes are safe.',
    affectedAreas: 'Parang'
  },

  // Tumana
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-tumana-low',
    title: 'Flood Watch: Tumana',
    category: 'Flood',
    severity: 'Low',
    description: 'Minor flooding may develop in low-lying or poorly drained areas, and some roads may begin to hold water.',
    recommendedAction: 'Monitor official updates and nearby water levels. Avoid flood-prone roads and keep essential items ready in case conditions worsen.',
    escalationActions: 'If water begins covering roads or entering low areas in Tumana, limit travel, secure important belongings, and prepare emergency supplies. Follow the next Flood Advisory immediately.',
    affectedAreas: 'Tumana'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-tumana-mod',
    title: 'Flood Advisory: Tumana',
    category: 'Flood',
    severity: 'Moderate',
    description: 'Flooding is developing in vulnerable areas and may begin affecting roads, homes, or normal travel.',
    recommendedAction: 'Limit travel and avoid flooded streets. Secure valuables, prepare emergency supplies, and be ready to move if water continues rising.',
    escalationActions: 'If water keeps rising or roads in Tumana become difficult to pass, move valuables and vehicles to higher ground and prepare to evacuate. Follow any Flood Warning immediately.',
    affectedAreas: 'Tumana'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-tumana-high',
    title: 'Flood Warning: Tumana',
    category: 'Flood',
    severity: 'High',
    description: 'Significant flooding may affect Tumana, making some roads unsafe and increasing the need to move to higher ground.',
    recommendedAction: 'Move vehicles, valuables, and essential items to higher ground. Prepare to evacuate and use only safe routes identified by local authorities.',
    escalationActions: 'If water reaches homes or an evacuation order is issued for Tumana, leave immediately using designated safe routes. Do not walk or drive through floodwater.',
    affectedAreas: 'Tumana'
  },
  {
    group: 'Barangay Flood Advisories: District 2',
    id: 'flood-tumana-crit',
    title: 'Flood Emergency: Tumana',
    category: 'Flood',
    severity: 'Critical',
    description: 'Dangerous flooding is affecting Tumana and may threaten homes, roads, and access routes; evacuation may be required.',
    recommendedAction: 'Evacuate immediately when instructed and proceed to the designated safe area. Never walk or drive through floodwaters, and follow LGU emergency instructions.',
    escalationActions: 'Stay in the designated evacuation or safe area while flooding in Tumana remains dangerous. Do not return home until authorities issue an all-clear and confirm routes are safe.',
    affectedAreas: 'Tumana'
  }
];
