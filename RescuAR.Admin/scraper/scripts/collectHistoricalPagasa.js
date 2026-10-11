import axios from "axios";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const BASE_URL =
  "https://pasig-marikina-tullahanffws.pagasa.dost.gov.ph";

const STATIONS = {
  sto_nino_mmda: {
    obscd: "11104204",
    name: "Sto Nino (MMDA)"
  },
  sto_nino: {
    obscd: "11104201",
    name: "Sto Nino"
  },
  nangka: {
    obscd: "11103202",
    name: "Nangka"
  },
  rodriguez: {
    obscd: "11102201",
    name: "Rodriguez"
  },
  montalban: {
    obscd: "11102202",
    name: "Montalban"
  },
  tumana: {
    obscd: "11103203",
    name: "Tumana Bridge"
  }
};

function cleanPagasaNumber(value) {
  if (value == null || value === "") return null;

  const original = String(value).trim();

  const cleaned = original
    .replace(/\(\*\)/g, "")
    .trim();

  const numeric = Number(cleaned);

  return {
    raw: original,
    value: Number.isFinite(numeric) ? numeric : null,
    marked: original.includes("(*)")
  };
}

async function fetchHistory(obscd, ymdhm) {
  const body = new URLSearchParams({
    obscd,
    ymdhm
  });

  const response = await axios.post(
    `${BASE_URL}/water/detail_list.do`,
    body.toString(),
    {
      timeout: 20000,
      headers: {
        "Content-Type":
          "application/x-www-form-urlencoded; charset=UTF-8",
        "Referer":
          `${BASE_URL}/water/table.do`,
        "User-Agent":
          "RescuAR-Historical-Research/1.0"
      }
    }
  );

  return Array.isArray(response.data)
    ? response.data
    : [];
}

async function main() {
  const station = STATIONS.sto_nino_mmda;

  const queryTime = "202011121020";

  console.log(
    `Fetching ${station.name} at ${queryTime}`
  );

  const rows = await fetchHistory(
    station.obscd,
    queryTime
  );

  const normalized = rows.map(row => {
    const wl = cleanPagasaNumber(row.wl);

    return {
      station_code: station.obscd,
      station_name: station.name,
      observed_at: row.timestr,
      water_level_m: wl?.value ?? null,
      raw_water_level: wl?.raw ?? null,
      pagasa_marked: wl?.marked ?? false,
      alert_threshold_m:
        cleanPagasaNumber(row.alertwl)?.value ?? null,
      alarm_threshold_m:
        cleanPagasaNumber(row.alarmwl)?.value ?? null,
      critical_threshold_m:
        cleanPagasaNumber(row.criticalwl)?.value ?? null,
      source: "PAGASA_FFWWS"
    };
  });

  const outputDir = path.resolve(
    __dirname,
    "../data/historical/pagasa"
  );

  fs.mkdirSync(outputDir, {
    recursive: true
  });

  fs.writeFileSync(
    path.join(
      outputDir,
      "ulysses_sto_nino.json"
    ),
    JSON.stringify(normalized, null, 2)
  );

  console.log(
    `Saved ${normalized.length} observations.`
  );
}

main().catch(error => {
  console.error(error);
  process.exit(1);
});