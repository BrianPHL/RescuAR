import React from 'react';
import { Download } from 'lucide-react';
import { exportCsv, exportXlsx, exportPdf } from '../utils/exportData';

const formats = [
  { label: 'XLSX', handler: exportXlsx },
  { label: 'CSV', handler: exportCsv },
  { label: 'PDF', handler: exportPdf },
];

export default function ExportActions({ title, rows, columns, disabled = false }) {
  return (
    <div className="export-actions" role="group" aria-label={`Export ${title} data`}>
      <span className="export-actions-label"><Download size={15} aria-hidden="true" /> Export</span>
      <div className="export-actions-formats">
        {formats.map(({ label, handler }) => (
          <button className="export-format-btn" key={label} type="button"
            disabled={disabled} title={`Download ${title} as ${label}`}
            onClick={() => handler(title, rows, columns)}>{label}</button>
        ))}
      </div>
    </div>
  );
}
