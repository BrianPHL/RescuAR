// Dependency-free CSV, XLSX (Office Open XML), and PDF exports.
const encoder = new TextEncoder();
const text = value => value == null ? '' : Array.isArray(value) ? value.join(', ') : String(value);
const xml = value => text(value).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&apos;');
const filename = (name, ext) => `${name.toLowerCase().replace(/[^a-z0-9]+/g, '-')}-${new Date().toISOString().slice(0, 10)}.${ext}`;
const download = (content, mime, name) => {
  const url = URL.createObjectURL(new Blob([content], { type: mime }));
  const link = document.createElement('a');
  link.href = url;
  link.download = name;
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
};
const tabular = (items, columns) => [columns.map(c => c.label), ...items.map(item => columns.map(c => text(item[c.key])))];

export function exportCsv(name, items, columns) {
  const csv = tabular(items, columns).map(row => row.map(value => `"${value.replace(/"/g, '""')}"`).join(',')).join('\r\n');
  download('\ufeff' + csv, 'text/csv;charset=utf-8', filename(name, 'csv'));
}

// ZIP store method (no external packages), including required CRC-32 and central directory.
const crcTable = Array.from({ length: 256 }, (_, index) => {
  let crc = index;
  for (let bit = 0; bit < 8; bit++) crc = crc & 1 ? 0xedb88320 ^ (crc >>> 1) : crc >>> 1;
  return crc >>> 0;
});
const checksum = data => {
  let crc = 0xffffffff;
  for (const byte of data) crc = crcTable[(crc ^ byte) & 255] ^ (crc >>> 8);
  return (crc ^ 0xffffffff) >>> 0;
};
const write16 = (data, offset, value) => { data[offset] = value & 255; data[offset + 1] = (value >>> 8) & 255; };
const write32 = (data, offset, value) => { write16(data, offset, value & 65535); write16(data, offset + 2, value >>> 16); };
function zip(files) {
  const local = [];
  const central = [];
  let offset = 0;
  for (const [path, contents] of Object.entries(files)) {
    const name = encoder.encode(path);
    const bytes = encoder.encode(contents);
    const crc = checksum(bytes);
    const header = new Uint8Array(30 + name.length);
    write32(header, 0, 0x04034b50);
    write16(header, 4, 20);
    write16(header, 6, 0x0800);
    write32(header, 14, crc);
    write32(header, 18, bytes.length);
    write32(header, 22, bytes.length);
    write16(header, 26, name.length);
    header.set(name, 30);
    local.push(header, bytes);
    const record = new Uint8Array(46 + name.length);
    write32(record, 0, 0x02014b50);
    write16(record, 4, 20);
    write16(record, 6, 20);
    write16(record, 8, 0x0800);
    write32(record, 16, crc);
    write32(record, 20, bytes.length);
    write32(record, 24, bytes.length);
    write16(record, 28, name.length);
    write32(record, 42, offset);
    record.set(name, 46);
    central.push(record);
    offset += header.length + bytes.length;
  }
  const centralSize = central.reduce((sum, part) => sum + part.length, 0);
  const end = new Uint8Array(22);
  write32(end, 0, 0x06054b50);
  write16(end, 8, central.length);
  write16(end, 10, central.length);
  write32(end, 12, centralSize);
  write32(end, 16, offset);
  return new Blob([...local, ...central, end], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
}
const colName = i => { let result = ''; for (let n = i + 1; n > 0; n = Math.floor((n - 1) / 26)) result = String.fromCharCode(65 + (n - 1) % 26) + result; return result; };
export function exportXlsx(name, items, columns) {
  const rows = tabular(items, columns);
  const lastCell = `${colName(columns.length - 1)}${rows.length}`;
  const sheetRows = rows.map((row, index) => `<row r="${index + 1}">${row.map((v, col) => `<c r="${colName(col)}${index + 1}" t="inlineStr"${index === 0 ? ' s="1"' : ''}><is><t xml:space="preserve">${xml(v)}</t></is></c>`).join('')}</row>`).join('');
  const files = {
    '[Content_Types].xml': '<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>',
    '_rels/.rels': '<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>',
    'xl/workbook.xml': '<?xml version="1.0" encoding="UTF-8"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Data" sheetId="1" r:id="rId1"/></sheets></workbook>',
    'xl/_rels/workbook.xml.rels': '<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>',
    'xl/styles.xml': '<?xml version="1.0" encoding="UTF-8"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="2"><font/><font><b/></font></fonts><fills count="1"><fill><patternFill patternType="none"/></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="2"><xf xfId="0" fontId="0" fillId="0" borderId="0"/><xf xfId="0" fontId="1" fillId="0" borderId="0" applyFont="1"/></cellXfs></styleSheet>',
    'xl/worksheets/sheet1.xml': `<?xml version="1.0" encoding="UTF-8"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><dimension ref="A1:${lastCell}"/><sheetViews><sheetView workbookViewId="0"/></sheetViews><sheetData>${sheetRows}</sheetData><autoFilter ref="A1:${lastCell}"/></worksheet>`
  };
  download(zip(files), 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', filename(name, 'xlsx'));
}

// Minimal, downloadable PDF with wrapped multi-page textual records; font coverage is standard Latin-1.
const pdfSafe = value => text(value).normalize('NFKD').replace(/[^\x20-\x7e\xa0-\xff]/g, '?').replace(/[\\()]/g, '\\$&');
const wrap = (value, max = 97) => {
  const words = text(value).split(/\s+/);
  const lines = [];
  let line = '';
  for (let word of words) {
    while (word.length > max) {
      if (line) { lines.push(line); line = ''; }
      lines.push(word.slice(0, max)); word = word.slice(max);
    }
    if ((line + ' ' + word).trim().length > max) { lines.push(line); line = word; }
    else line = line ? line + ' ' + word : word;
  }
  if (line) lines.push(line);
  return lines.length ? lines : [''];
};
export function exportPdf(name, items, columns) {
  const lines = [];
  const add = (v, bold = false) => wrap(v).forEach(t => lines.push({ text: t, bold }));
  add(name, true);
  add(`Exported: ${new Date().toLocaleString()} | Records: ${items.length}`);
  lines.push({ text: '' });
  items.forEach((item, idx) => {
    add(`Record ${idx + 1}`, true);
    columns.forEach(col => add(`${col.label}: ${text(item[col.key])}`));
    lines.push({ text: '' });
  });
  if (!items.length) add('No records match the current filters.');
  const perPage = 52;
  const pages = [];
  for (let i = 0; i < lines.length; i += perPage) pages.push(lines.slice(i, i + perPage));
  const objects = [null, '<< /Type /Catalog /Pages 2 0 R >>', '', '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>', '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>'];
  const pageIds = [];
  pages.forEach((page, pageIndex) => {
    const contentId = objects.length;
    const stream = ['BT', '/F1 10 Tf', '45 795 Td', '13 TL'];
    page.forEach(line => {
      stream.push(`${line.bold ? '/F2' : '/F1'} 10 Tf`);
      stream.push(`(${pdfSafe(line.text)}) Tj`, 'T*');
    });
    stream.push('/F1 9 Tf', `(${pageIndex + 1} / ${pages.length}) Tj`, 'ET');
    const content = stream.join('\n') + '\n';
    objects.push(`<< /Length ${content.length} >>\nstream\n${content}endstream`);
    const pageId = objects.length;
    objects.push(`<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 842] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents ${contentId} 0 R >>`);
    pageIds.push(pageId);
  });
  objects[2] = `<< /Type /Pages /Kids [${pageIds.map(id => `${id} 0 R`).join(' ')}] /Count ${pages.length} >>`;
  let body = '%PDF-1.4\n';
  const offsets = [0];
  for (let i = 1; i < objects.length; i++) {
    offsets.push(body.length);
    body += `${i} 0 obj\n${objects[i]}\nendobj\n`;
  }
  const xref = body.length;
  body += `xref\n0 ${objects.length}\n0000000000 65535 f \n`;
  for (let i = 1; i < offsets.length; i++) body += `${String(offsets[i]).padStart(10, '0')} 00000 n \n`;
  body += `trailer\n<< /Size ${objects.length} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF`;
  download(new Uint8Array([...body].map(char => char.charCodeAt(0))), 'application/pdf', filename(name, 'pdf'));
}
