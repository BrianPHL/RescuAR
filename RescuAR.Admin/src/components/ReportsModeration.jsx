import React, { useState, useEffect } from 'react';
import { ChevronLeft, ChevronRight, RefreshCw, CheckCircle, AlertTriangle, XCircle, MapPin, User, Clock, ShieldAlert } from 'lucide-react';
import { MapContainer, TileLayer, Marker, Popup, useMap } from 'react-leaflet';
import 'leaflet/dist/leaflet.css';
import L from 'leaflet';
import { supabase } from '../supabaseClient';

// Custom Leaflet Icons for pin markers per report
const selectedPinIcon = new L.Icon({
  iconUrl: 'https://raw.githubusercontent.com/pointhi/leaflet-color-markers/master/img/marker-icon-2x-red.png',
  shadowUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.7.1/images/marker-shadow.png',
  iconSize: [25, 41],
  iconAnchor: [12, 41],
  popupAnchor: [1, -34],
  shadowSize: [41, 41]
});

const reportPinIcon = new L.Icon({
  iconUrl: 'https://raw.githubusercontent.com/pointhi/leaflet-color-markers/master/img/marker-icon-2x-blue.png',
  shadowUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.7.1/images/marker-shadow.png',
  iconSize: [25, 41],
  iconAnchor: [12, 41],
  popupAnchor: [1, -34],
  shadowSize: [41, 41]
});

// Helper component to auto-pan the map when selecting a report
function MapRecenter({ lat, lng }) {
  const map = useMap();
  useEffect(() => {
    if (lat && lng && !isNaN(lat) && !isNaN(lng)) {
      map.setView([lat, lng], 14, { animate: true });
    }
  }, [lat, lng, map]);
  return null;
}

export default function ReportsModeration() {
  const [reports, setReports] = useState([]);
  const [selectedReport, setSelectedReport] = useState(null);
  const [loading, setLoading] = useState(true);
  const [searchQuery, setSearchQuery] = useState('');
  const [lastUpdated, setLastUpdated] = useState(new Date().toLocaleString());

  const fetchReports = async () => {
    setLoading(true);
    try {
      const { data, error } = await supabase
        .from('community_reports')
        .select('*')
        .order('created_at', { ascending: false });

      if (error) {
        console.error('Error fetching Supabase reports:', error);
      } else if (data) {
        setReports(data);
        if (data.length > 0 && !selectedReport) {
          setSelectedReport(data[0]);
        }
      }
    } catch (err) {
      console.error('Fetch error:', err);
    } finally {
      setLoading(false);
      setLastUpdated(new Date().toLocaleString());
    }
  };

  useEffect(() => {
    fetchReports();

    // Realtime listener for incoming mobile reports
    const channel = supabase
      .channel('public:community_reports')
      .on('postgres_changes', { event: '*', schema: 'public', table: 'community_reports' }, () => {
        fetchReports();
      })
      .subscribe();

    return () => {
      supabase.removeChannel(channel);
    };
  }, []);

  const maskName = (name) => {
    if (!name) return 'Us*** U.';
    const parts = name.trim().split(/\s+/);
    const firstName = parts[0] || '';
    const first2 = firstName.length >= 2 ? firstName.substring(0, 2) : firstName;
    let surnameInitial = '';
    if (parts.length > 1) {
      surnameInitial = parts[parts.length - 1][0].toUpperCase() + '.';
    }
    return `${first2}*** ${surnameInitial}`.trim();
  };

  const handleUpdateStatus = async (reportId, newStatus) => {
    try {
      const { error } = await supabase
        .from('community_reports')
        .update({ status: newStatus })
        .eq('id', reportId);

      if (error) {
        alert('Failed to update report status: ' + error.message);
      } else {
        setReports(reports.map(r => r.id === reportId ? { ...r, status: newStatus } : r));
        if (selectedReport && selectedReport.id === reportId) {
          setSelectedReport({ ...selectedReport, status: newStatus });
        }
      }
    } catch (err) {
      alert('Error updating status: ' + err.message);
    }
  };

  const filteredReports = reports.filter(r => {
    if (!searchQuery) return true;
    const q = searchQuery.toLowerCase();
    return (
      (r.title && r.title.toLowerCase().includes(q)) ||
      (r.description && r.description.toLowerCase().includes(q)) ||
      (r.posted_by && r.posted_by.toLowerCase().includes(q)) ||
      (r.category && r.category.toLowerCase().includes(q)) ||
      (r.address && r.address.toLowerCase().includes(q))
    );
  });

  const defaultCenter = [
    selectedReport?.latitude || (reports.length > 0 && reports[0].latitude) || 14.6340,
    selectedReport?.longitude || (reports.length > 0 && reports[0].longitude) || 121.0990
  ];

  return (
    <div className="main-view">
      {/* View Header */}
      <div className="view-header">
        <div className="view-title-container">
          <h1>Reports Moderation</h1>
          <span className="view-subtitle">Last updated: {lastUpdated}</span>
        </div>
      </div>

      <div className="stations-split-layout" style={{ marginTop: '24px', alignItems: 'flex-start' }}>

        {/* Left Column: Queue & Map */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: '32px', flex: 1 }}>

          {/* Reports Queue Section */}
          <div>
            <h2 style={{ fontSize: '18px', fontWeight: '600', marginBottom: '16px', color: 'var(--text-main)' }}>
              Reports Queue ({filteredReports.length})
            </h2>

            {/* Search Bar */}
            <div style={{ marginBottom: '24px' }}>
              <input
                type="text"
                placeholder="Search for a report by title, category, or user..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                style={{
                  width: '100%',
                  padding: '12px 16px',
                  borderRadius: '8px',
                  border: '1px solid var(--color-border)',
                  backgroundColor: '#fff',
                  fontSize: '14px',
                  color: 'var(--text-main)',
                  outline: 'none',
                  boxShadow: '0 1px 2px rgba(0,0,0,0.05)'
                }}
              />
            </div>

            {/* Table Card */}
            <div className="stations-card" style={{ padding: '0', display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
              <div className="table-container" style={{ margin: '0' }}>
                <table className="data-table" style={{ width: '100%', borderCollapse: 'collapse' }}>
                  <thead style={{ backgroundColor: '#f1f5f9', borderBottom: '1px solid var(--color-border)' }}>
                    <tr>
                      <th style={{ padding: '12px 24px', textAlign: 'left', fontWeight: '700', color: 'var(--text-main)', fontSize: '13px' }}>Report Title</th>
                      <th style={{ padding: '12px 24px', textAlign: 'left', fontWeight: '700', color: 'var(--text-main)', fontSize: '13px' }}>User</th>
                      <th style={{ padding: '12px 24px', textAlign: 'left', fontWeight: '700', color: 'var(--text-main)', fontSize: '13px' }}>Category</th>
                      <th style={{ padding: '12px 24px', textAlign: 'left', fontWeight: '700', color: 'var(--text-main)', fontSize: '13px' }}>Attachment</th>
                      <th style={{ padding: '12px 24px', textAlign: 'left', fontWeight: '700', color: 'var(--text-main)', fontSize: '13px' }}>Status</th>
                    </tr>
                  </thead>
                  <tbody>
                    {loading ? (
                      <tr>
                        <td colSpan="5" style={{ padding: '24px', textAlign: 'center', color: '#64748b' }}>
                          Loading live Supabase reports...
                        </td>
                      </tr>
                    ) : filteredReports.length === 0 ? (
                      <tr>
                        <td colSpan="5" style={{ padding: '24px', textAlign: 'center', color: '#64748b' }}>
                          No community reports submitted yet.
                        </td>
                      </tr>
                    ) : (
                      filteredReports.map((report) => (
                        <tr
                          key={report.id}
                          onClick={() => setSelectedReport(report)}
                          className="table-row-hover"
                          style={{
                            borderBottom: '1px solid var(--color-border)',
                            cursor: 'pointer',
                            backgroundColor: selectedReport?.id === report.id ? '#f0fdf4' : 'transparent'
                          }}
                        >
                          <td style={{ padding: '16px 24px', fontSize: '14px', fontWeight: '600', color: 'var(--text-main)' }}>
                            {report.title || 'Untitled Report'}
                          </td>
                          <td style={{ padding: '16px 24px', fontSize: '14px', color: 'var(--text-main)' }}>
                            {maskName(report.posted_by)}
                          </td>
                          <td style={{ padding: '16px 24px', fontSize: '14px', fontWeight: '600', color: '#0284c7' }}>
                            {report.category || 'General'}
                          </td>
                          <td style={{ padding: '16px 24px', fontSize: '13px' }}>
                            {report.media_url ? (
                              <a
                                href={report.media_url}
                                target="_blank"
                                rel="noreferrer"
                                onClick={(e) => e.stopPropagation()}
                                style={{ display: 'inline-flex', alignItems: 'center', gap: '4px', padding: '4px 10px', borderRadius: '6px', backgroundColor: '#f0fdf4', color: '#16a34a', fontWeight: '700', textDecoration: 'none', fontSize: '12px' }}
                              >
                                View Media
                              </a>
                            ) : (
                              <span style={{ color: '#94a3b8', fontSize: '12px' }}>No media</span>
                            )}
                          </td>
                          <td style={{ padding: '16px 24px', fontSize: '14px', fontWeight: '600' }}>
                            <span style={{
                              padding: '4px 8px',
                              borderRadius: '6px',
                              fontSize: '12px',
                              backgroundColor: report.status === 'Approved' ? '#dcfce7' : report.status === 'Resolved' ? '#e0f2fe' : report.status === 'Rejected' ? '#fee2e2' : '#fef3c7',
                              color: report.status === 'Approved' ? '#15803d' : report.status === 'Resolved' ? '#0369a1' : report.status === 'Rejected' ? '#dc2626' : '#b45309'
                            }}>
                              {report.status || 'Pending'}
                            </span>
                          </td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>

              {/* Pagination Footer */}
              <div style={{
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                padding: '16px 24px',
                borderTop: '1px solid var(--color-border)',
                backgroundColor: '#fff'
              }}>
                <span style={{ fontSize: '13px', color: 'var(--text-muted)' }}>
                  {filteredReports.length} {filteredReports.length === 1 ? 'record' : 'records'} total
                </span>
                <button
                  onClick={fetchReports}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '6px',
                    padding: '6px 12px',
                    borderRadius: '6px',
                    border: '1px solid var(--color-border)',
                    backgroundColor: '#fff',
                    fontSize: '12px',
                    cursor: 'pointer',
                    fontWeight: '600',
                    color: 'var(--text-main)'
                  }}
                >
                  <RefreshCw size={14} /> Refresh Data
                </button>
              </div>
            </div>
          </div>

          {/* Interactive Report Location Map Section */}
          <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px' }}>
              <h2 style={{ fontSize: '18px', fontWeight: '600', color: 'var(--text-main)', margin: 0 }}>
                Report Location Map ({reports.filter(r => r.latitude && r.longitude).length} Pinned)
              </h2>
              <span style={{ fontSize: '12px', color: '#64748b' }}>
                🔴 Red Pin = Selected Report • 🔵 Blue Pins = Community Reports
              </span>
            </div>

            <div className="stations-card" style={{ padding: 0, overflow: 'hidden', height: '420px', backgroundColor: '#e5e7eb', borderRadius: '16px', position: 'relative' }}>
              <MapContainer
                center={defaultCenter}
                zoom={13}
                style={{ height: '100%', width: '100%', zIndex: 1 }}
              >
                <TileLayer
                  attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
                  url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
                />

                <MapRecenter lat={selectedReport?.latitude} lng={selectedReport?.longitude} />

                {/* Render Pinned Location Markers for All Reports */}
                {reports.map((rpt) => {
                  const lat = parseFloat(rpt.latitude);
                  const lng = parseFloat(rpt.longitude);

                  if (isNaN(lat) || isNaN(lng)) return null;

                  const isSelected = selectedReport?.id === rpt.id;

                  return (
                    <Marker
                      key={rpt.id}
                      position={[lat, lng]}
                      icon={isSelected ? selectedPinIcon : reportPinIcon}
                      eventHandlers={{
                        click: () => setSelectedReport(rpt)
                      }}
                    >
                      <Popup>
                        <div style={{ padding: '4px', maxWidth: '200px' }}>
                          <span style={{
                            display: 'inline-block',
                            fontSize: '10px',
                            fontWeight: '700',
                            padding: '2px 6px',
                            borderRadius: '4px',
                            backgroundColor: rpt.status === 'Approved' ? '#dcfce7' : rpt.status === 'Resolved' ? '#e0f2fe' : rpt.status === 'Rejected' ? '#fee2e2' : '#fef3c7',
                            color: rpt.status === 'Approved' ? '#15803d' : rpt.status === 'Resolved' ? '#0369a1' : rpt.status === 'Rejected' ? '#dc2626' : '#b45309',
                            marginBottom: '4px'
                          }}>
                            {rpt.status || 'Pending'}
                          </span>
                          <h4 style={{ margin: '2px 0 4px 0', fontSize: '13px', color: '#0f172a', fontWeight: '700' }}>
                            {rpt.title || 'Report Location'}
                          </h4>
                          <p style={{ margin: '0 0 6px 0', fontSize: '11px', color: '#475569' }}>
                            📍 {rpt.address || 'Marikina City'}
                          </p>
                          <p style={{ margin: '0 0 6px 0', fontSize: '11px', color: '#64748b' }}>
                            Posted by: <strong>{maskName(rpt.posted_by)}</strong>
                          </p>
                          {rpt.media_url && (
                            <img
                              src={rpt.media_url}
                              alt="Thumbnail"
                              style={{ width: '100%', height: '80px', objectFit: 'cover', borderRadius: '6px', marginTop: '4px' }}
                            />
                          )}
                        </div>
                      </Popup>
                    </Marker>
                  );
                })}
              </MapContainer>
            </div>
          </div>

        </div>

        {/* Right Column: Detailed Inspector Card */}
        <div style={{ width: '380px', display: 'flex', flexDirection: 'column', gap: '16px' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <h2 style={{ fontSize: '18px', fontWeight: '600', color: 'var(--text-main)', margin: 0 }}>
              Report Details
            </h2>
          </div>

          {selectedReport ? (
            <div className="stations-card" style={{ padding: '24px', display: 'flex', flexDirection: 'column', gap: '20px', backgroundColor: '#fff', borderRadius: '16px', border: '1px solid var(--color-border)' }}>

              {/* Category & Status Badges */}
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <span style={{ fontSize: '12px', fontWeight: '700', color: '#0284c7', backgroundColor: '#e0f2fe', padding: '4px 10px', borderRadius: '8px' }}>
                  {selectedReport.category || 'General'}
                </span>
                <span style={{ fontSize: '12px', fontWeight: '700', color: selectedReport.status === 'Approved' ? '#15803d' : selectedReport.status === 'Resolved' ? '#0369a1' : selectedReport.status === 'Rejected' ? '#dc2626' : '#b45309', backgroundColor: selectedReport.status === 'Approved' ? '#dcfce7' : selectedReport.status === 'Resolved' ? '#e0f2fe' : selectedReport.status === 'Rejected' ? '#fee2e2' : '#fef3c7', padding: '4px 10px', borderRadius: '8px' }}>
                  {selectedReport.status || 'Pending'}
                </span>
              </div>

              {/* Title & Description */}
              <div>
                <h3 style={{ fontSize: '18px', fontWeight: '700', color: '#0f172a', marginBottom: '8px' }}>
                  {selectedReport.title}
                </h3>
                <p style={{ fontSize: '14px', color: '#334155', lineHeight: '1.5', margin: 0 }}>
                  {selectedReport.description || 'No detailed description provided.'}
                </p>
              </div>

              {/* Uploaded Media Section */}
              <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                <span style={{ fontSize: '12px', fontWeight: '700', color: '#64748b', textTransform: 'uppercase' }}>
                  Uploaded Media Attachment
                </span>
                {selectedReport.media_url ? (
                  <div style={{ borderRadius: '12px', overflow: 'hidden', border: '1px solid #e2e8f0', boxShadow: '0 2px 4px rgba(0,0,0,0.05)' }}>
                    <a href={selectedReport.media_url} target="_blank" rel="noreferrer">
                      <img
                        src={selectedReport.media_url}
                        alt="Report Attachment"
                        style={{ width: '100%', maxHeight: '220px', objectFit: 'cover', display: 'block' }}
                      />
                    </a>
                  </div>
                ) : (
                  <div style={{ padding: '14px', backgroundColor: '#f8fafc', borderRadius: '10px', border: '1px dashed #cbd5e1', fontSize: '13px', color: '#94a3b8', textAlign: 'center' }}>
                    No media attached to this report.
                  </div>
                )}
              </div>

              {/* Address & Reporter Meta */}
              <div style={{ display: 'flex', flexDirection: 'column', gap: '10px', padding: '14px', backgroundColor: '#f8fafc', borderRadius: '12px', border: '1px solid #e2e8f0' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '13px', color: '#475569' }}>
                  <MapPin size={16} color="#0a8491" />
                  <span style={{ fontWeight: '600', color: '#0f172a' }}>{selectedReport.address || 'Marikina City'}</span>
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '13px', color: '#475569' }}>
                  <User size={16} color="#64748b" />
                  <span>Posted by: <strong>{maskName(selectedReport.posted_by)}</strong></span>
                </div>
                {selectedReport.latitude && selectedReport.longitude && (
                  <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '12px', color: '#0284c7' }}>
                    <MapPin size={14} />
                    <span>GPS Coordinates: {parseFloat(selectedReport.latitude).toFixed(4)}, {parseFloat(selectedReport.longitude).toFixed(4)}</span>
                  </div>
                )}
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '12px', color: '#94a3b8' }}>
                  <Clock size={14} />
                  <span>{new Date(selectedReport.created_at || Date.now()).toLocaleString()}</span>
                </div>
              </div>

              {/* Admin Moderation Actions */}
              <div style={{ display: 'flex', flexDirection: 'column', gap: '10px', paddingTop: '10px', borderTop: '1px solid #e2e8f0' }}>
                <span style={{ fontSize: '12px', fontWeight: '700', color: '#64748b', textTransform: 'uppercase' }}>
                  Admin Actions
                </span>

                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '10px' }}>
                  <button
                    onClick={() => handleUpdateStatus(selectedReport.id, 'Approved')}
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                      gap: '6px',
                      padding: '10px',
                      borderRadius: '8px',
                      border: 'none',
                      backgroundColor: '#16a34a',
                      color: '#fff',
                      fontSize: '13px',
                      fontWeight: '600',
                      cursor: 'pointer'
                    }}
                  >
                    <CheckCircle size={16} /> Approve
                  </button>

                  <button
                    onClick={() => handleUpdateStatus(selectedReport.id, 'Resolved')}
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                      gap: '6px',
                      padding: '10px',
                      borderRadius: '8px',
                      border: 'none',
                      backgroundColor: '#0284c7',
                      color: '#fff',
                      fontSize: '13px',
                      fontWeight: '600',
                      cursor: 'pointer'
                    }}
                  >
                    <ShieldAlert size={16} /> Resolve
                  </button>
                </div>

                <button
                  onClick={() => handleUpdateStatus(selectedReport.id, 'Rejected')}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    gap: '6px',
                    padding: '10px',
                    borderRadius: '8px',
                    border: '1px solid #fee2e2',
                    backgroundColor: '#fff',
                    color: '#dc2626',
                    fontSize: '13px',
                    fontWeight: '600',
                    cursor: 'pointer'
                  }}
                >
                  <XCircle size={16} /> Reject Report
                </button>
              </div>

            </div>
          ) : (
            <div className="stations-card" style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', minHeight: '300px' }}>
              <span style={{ fontSize: '14px', color: '#9ca3af' }}>Select a report from the table queue to view details.</span>
            </div>
          )}
        </div>

      </div>

    </div>
  );
}
