import React, { useState } from 'react';

export default function ResidentsDirectory() {
  const [searchTerm, setSearchTerm] = useState('');
  const residents = [
    { name: 'Kagawad Juan Dela Cruz', barangay: 'Tumana', role: 'Barangay Coordinator', phone: '+63 917 123 4567' },
    { name: 'Maria Santos', barangay: 'Nangka', role: 'Zone Captain', phone: '+63 918 234 5678' },
    { name: 'Antonio Luna', barangay: 'Provident', role: 'LGU Responder', phone: '+63 920 345 6789' },
    { name: 'Teresa Rizal', barangay: 'Malanday', role: 'Local Coordinator', phone: '+63 915 456 7890' },
    { name: 'Emilio Jacinto', barangay: 'Tumana', role: 'Zone Leader', phone: '+63 909 567 8901' }
  ];

  const filtered = residents.filter(r =>
    r.name.toLowerCase().includes(searchTerm.toLowerCase()) ||
    r.barangay.toLowerCase().includes(searchTerm.toLowerCase())
  );

  return (
    <div className="main-view">
      <div className="view-header">
        <div className="view-title-container">
          <h1>Residents Directory</h1>
          <span className="view-subtitle">Primary emergency response contacts in high-risk zones</span>
        </div>
      </div>

      <div className="stations-card">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <h2 className="stations-card-title">Emergency Coordinators List</h2>
        </div>

        <div className="search-input-wrapper">
          <input
            type="text"
            placeholder="Search by name or barangay..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
          />
        </div>

        <div className="table-container">
          <table className="data-table">
            <thead>
              <tr>
                <th>Coordinator Name</th>
                <th>Barangay Sector</th>
                <th>Designated Role</th>
                <th>Contact Info</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((r, i) => (
                <tr key={i}>
                  <td style={{ fontWeight: '600' }}>{r.name}</td>
                  <td>{r.barangay}</td>
                  <td><span style={{ backgroundColor: '#e0f2fe', color: '#0369a1', padding: '3px 8px', borderRadius: '10px', fontSize: '11px', fontWeight: '700' }}>{r.role}</span></td>
                  <td style={{ fontFamily: 'monospace' }}>{r.phone}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
