import ExportActions from './ExportActions';
import React, { useState, useEffect, useRef } from 'react';
import { RefreshCw, Check, X, Clock, ShieldCheck, MapPin, User } from 'lucide-react';
import { MapContainer, TileLayer, Marker, Popup, useMap } from 'react-leaflet';
import 'leaflet/dist/leaflet.css';
import L from 'leaflet';
import { reportsModerationService } from '../services/reportsModerationClient';
import { createReportsModerationController, initialReportsState } from '../services/reportsModerationController';
import { filterReports, reportMediaUrl } from '../services/reportsModerationService';

const noOp = () => {};

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

function ReportMarker({ report, selected, selectionOperation, onInspect, children }) {
  const markerRef = useRef(null);
  useEffect(() => {
    if (selected && selectionOperation) markerRef.current?.openPopup();
  }, [selected, selectionOperation]);
  return (
    <Marker ref={markerRef} position={[Number(report.latitude), Number(report.longitude)]}
      icon={selected ? selectedPinIcon : reportPinIcon} eventHandlers={{ click: onInspect }}>
      {selected && selectionOperation ? children : null}
    </Marker>
  );
}

export default function ReportsModeration({ service = reportsModerationService, onAccessDenied = noOp }) {
  const [state, setState] = useState(initialReportsState);
  const [searchQuery, setSearchQuery] = useState('');
  const controllerRef = useRef(null);
  const initialOperationRef = useRef(null);
  const { reports, loading, lastUpdated } = state;
  const selectedReport = reports.find((report) => report.id === state.selectedId) || null;
  const pending = state.busy || loading;

  useEffect(() => {
    if (!initialOperationRef.current) initialOperationRef.current = service.newOperationId();
    const controller = createReportsModerationController(service, setState, onAccessDenied);
    controllerRef.current = controller;
    void controller.loadInitial(initialOperationRef.current);
    const unsubscribe = service.subscribe(() => { void controller.automaticRefresh(); });
    return () => {
      controller.dispose();
      unsubscribe();
    };
  }, [service, onAccessDenied]);

  const selectReport = (report) => {
    if (!pending) void controllerRef.current.inspect(report.id);
  };
  const openMedia = (event, report) => {
    event.preventDefault();
    event.stopPropagation();
    if (pending) return;
    const url = reportMediaUrl(report);
    if (!url) { controllerRef.current.showError('This attachment could not be opened.'); return; }
    const popup = window.open('about:blank', '_blank');
    if (!popup) { controllerRef.current.showError('Allow pop-ups for this site to open attachments.'); return; }
    popup.opener = null;
    void controllerRef.current.openMedia(report.id, () => {
      if (popup.closed) throw new Error('The attachment window was closed. Open it again to retry.');
      popup.location.replace(url);
    }).then((opened) => { if (!opened) popup.close(); });
  };

  const maskName = (name) => {
    if (!name) return 'Anonymous Citizen';
    return name;
  };

  const renderStatusPill = (status) => {
    const s = status || 'Pending';
    let bg = '#fef3c7';
    let color = '#b45309';
    let IconComponent = Clock;

    if (s === 'Approved') {
      bg = '#dcfce7';
      color = '#15803d';
      IconComponent = Check;
    } else if (s === 'Resolved') {
      bg = '#e0f2fe';
      color = '#0369a1';
      IconComponent = ShieldCheck;
    } else if (s === 'Rejected') {
      bg = '#fee2e2';
      color = '#dc2626';
      IconComponent = X;
    }

    return (
      <span
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: '6px',
          padding: '4px 10px',
          borderRadius: '12px',
          backgroundColor: bg,
          color: color,
          fontSize: '12px',
          fontWeight: '600'
        }}
      >
        <IconComponent size={14} color={color} strokeWidth={2.5} />
        {s}
      </span>
    );
  };

  const filteredReports = filterReports(reports, state.query);

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
          <span className="view-subtitle">{lastUpdated ? `Last updated: ${lastUpdated}` : 'Loading reports...'}</span>
        </div>
        <ExportActions title="reports-moderation" rows={filteredReports} columns={[{ label: 'ID', key: 'id' }, { label: 'Title', key: 'title' }, { label: 'Description', key: 'description' }, { label: 'Reported By', key: 'posted_by' }, { label: 'Category', key: 'category' }, { label: 'Status', key: 'status' }, { label: 'Address', key: 'address' }, { label: 'Latitude', key: 'latitude' }, { label: 'Longitude', key: 'longitude' }, { label: 'Created At', key: 'created_at' }]} disabled={loading} />
      </div>

      {state.error && <div role="alert" style={{ marginTop: '16px', padding: '12px 16px', borderRadius: '8px', background: '#fef2f2', color: '#b91c1c' }}>{state.error}</div>}
      {state.notice && <div role="status" style={{ marginTop: '16px', color: '#15803d' }}>{state.notice}</div>}
      {state.retryDecision && <div style={{ marginTop: '12px' }}>
        <p>Pending {state.retryStatus.toLowerCase()} decision for {state.retryTitle}.</p>
        <button className="btn-secondary" disabled={pending}
          onClick={() => void controllerRef.current.retryModeration()}>Retry decision</button>
      </div>}

      <div className="stations-split-layout" style={{ marginTop: '24px', alignItems: 'flex-start' }}>

        {/* Left Column: Queue & Map */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: '32px', flex: 1 }}>

          {/* Reports Queue Section */}
          <div>
            <h2 style={{ fontSize: '18px', fontWeight: '600', marginBottom: '16px', color: 'var(--text-main)' }}>
              Reports Queue ({filteredReports.length})
            </h2>

            {/* Search Bar */}
            <form aria-label="Search reports" style={{ marginBottom: '24px', display: 'flex', gap: '8px' }}
              onSubmit={(event) => { event.preventDefault(); if (!pending) void controllerRef.current.applyFilter(searchQuery); }}>
              <input
                type="text"
                placeholder="Search for a report by title, category, or user..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                aria-label="Report search"
                disabled={pending || !state.initialized}
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
              <button className="btn-secondary" type="submit" disabled={pending || !state.initialized}>Search</button>
              <button className="btn-secondary" type="button" disabled={pending || !state.initialized}
                onClick={async () => { if (await controllerRef.current.applyFilter('')) setSearchQuery(''); }}>Clear</button>
            </form>

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
                          {state.query ? 'No reports match this search.' : state.initialized ? 'No community reports submitted yet.' : 'Refresh to load reports.'}
                        </td>
                      </tr>
                    ) : (
                      filteredReports.map((report) => (
                        <tr
                          key={report.id}
                          onClick={() => selectReport(report)}
                          tabIndex={pending ? -1 : 0}
                          aria-selected={selectedReport?.id === report.id}
                          onKeyDown={(event) => {
                            if (event.target === event.currentTarget && ['Enter', ' '].includes(event.key)) {
                              event.preventDefault(); selectReport(report);
                            }
                          }}
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
                              <button
                                type="button" disabled={pending}
                                onClick={(event) => openMedia(event, report)}
                                style={{ display: 'inline-flex', alignItems: 'center', gap: '4px', padding: '4px 10px', border: 0, cursor: 'pointer', borderRadius: '6px', backgroundColor: '#f0fdf4', color: '#16a34a', fontWeight: '700', fontSize: '12px' }}
                              >
                                View Media
                              </button>
                            ) : (
                              <span style={{ color: '#94a3b8', fontSize: '12px' }}>No media</span>
                            )}
                          </td>
                          <td style={{ padding: '16px 24px', fontSize: '14px', fontWeight: '600' }}>
                            {renderStatusPill(report.status)}
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
                  onClick={() => void controllerRef.current.refresh()}
                  disabled={pending}
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
                    <ReportMarker
                      key={rpt.id}
                      report={rpt} selected={isSelected} selectionOperation={state.selectionOperation}
                      onInspect={() => selectReport(rpt)}
                    >
                      <Popup>
                        <div style={{ padding: '4px', maxWidth: '200px' }}>
                          <div style={{ marginBottom: '6px' }}>
                            {renderStatusPill(rpt.status)}
                          </div>
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
                    </ReportMarker>
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
                {renderStatusPill(selectedReport.status)}
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
                    <button type="button" disabled={pending} aria-label="Open report attachment"
                      onClick={(event) => openMedia(event, selectedReport)} style={{ padding: 0, border: 0, display: 'block', width: '100%', cursor: 'pointer' }}>
                      <img
                        src={selectedReport.media_url}
                        alt="Report Attachment"
                        style={{ width: '100%', maxHeight: '220px', objectFit: 'cover', display: 'block' }}
                      />
                    </button>
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
                  <span>{selectedReport.created_at ? new Date(selectedReport.created_at).toLocaleString() : 'Time unavailable'}</span>
                </div>
              </div>

              {/* Admin Moderation Actions */}
              <div style={{ display: 'flex', flexDirection: 'column', gap: '10px', paddingTop: '10px', borderTop: '1px solid #e2e8f0' }}>
                <span style={{ fontSize: '12px', fontWeight: '700', color: '#64748b', textTransform: 'uppercase' }}>
                  Admin Actions
                </span>

                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '10px' }}>
                  <button
                    onClick={() => void controllerRef.current.moderate('Approved')}
                    disabled={pending || state.retryDecision}
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
                    <Check size={16} /> Approve
                  </button>

                  <button
                    onClick={() => void controllerRef.current.moderate('Resolved')}
                    disabled={pending || state.retryDecision}
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
                    <ShieldCheck size={16} /> Resolve
                  </button>
                </div>

                <button
                  onClick={() => void controllerRef.current.moderate('Rejected')}
                  disabled={pending || state.retryDecision}
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
                  <X size={16} /> Reject Report
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
