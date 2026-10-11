import fs from 'node:fs';

function escapeCsv(value) {
  if (value === null || value === undefined) return '';
  const text = String(value);
  if (/[",\n\r]/.test(text)) return `"${text.replace(/"/g, '""')}"`;
  return text;
}

export function writeCsv(filePath, rows, headers = null) {
  const columns = headers || (rows[0] ? Object.keys(rows[0]) : []);
  const lines = [columns.map(escapeCsv).join(',')];
  for (const row of rows) {
    lines.push(columns.map((column) => escapeCsv(row[column])).join(','));
  }
  fs.writeFileSync(filePath, `${lines.join('\n')}\n`, 'utf8');
}

export function readCsv(filePath) {
  const text = fs.readFileSync(filePath, 'utf8').replace(/^\uFEFF/, '');
  const rows = [];
  let current = '';
  let record = [];
  let inQuotes = false;

  for (let i = 0; i < text.length; i += 1) {
    const char = text[i];
    if (inQuotes) {
      if (char === '"' && text[i + 1] === '"') {
        current += '"';
        i += 1;
      } else if (char === '"') {
        inQuotes = false;
      } else {
        current += char;
      }
    } else if (char === '"') {
      inQuotes = true;
    } else if (char === ',') {
      record.push(current);
      current = '';
    } else if (char === '\n') {
      record.push(current.replace(/\r$/, ''));
      rows.push(record);
      record = [];
      current = '';
    } else {
      current += char;
    }
  }

  if (current.length || record.length) {
    record.push(current.replace(/\r$/, ''));
    rows.push(record);
  }

  if (!rows.length) return [];
  const headers = rows.shift();
  return rows
    .filter((row) => row.some((value) => value !== ''))
    .map((row) => Object.fromEntries(headers.map((header, index) => [header, row[index] ?? ''])));
}
