import React, { useState, useEffect } from 'react';
import {
  Search,
  Plus,
  Trash2,
  Edit3,
  Archive,
  Copy,
  X,
  Check,
  AlertTriangle,
  RefreshCw,
  Eye,
  Calendar,
  Clock,
  MapPin,
  ChevronLeft,
  ChevronRight
} from 'lucide-react';
import { supabase } from '../supabaseClient';
import { MARIKINA_DISTRICTS } from '../constants/marikinaData.js';
import { ADVISORY_TEMPLATES } from '../constants/advisoryTemplates.js';


export default function Advisories() {
  const [advisories, setAdvisories] = useState([]);
  const [loading, setLoading] = useState(true);
  const [selectedId, setSelectedId] = useState(null);
  const [searchQuery, setSearchQuery] = useState('');
  const [categoryFilter, setCategoryFilter] = useState('');
  const [severityFilter, setSeverityFilter] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [isRefreshSpinning, setIsRefreshSpinning] = useState(false);
  const [currentPage, setCurrentPage] = useState(1);
  const ITEMS_PER_PAGE = 10;

  // Modal State
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [modalMode, setModalMode] = useState('create'); // 'create' or 'edit'

  // Cascading Template Selector State
  const [templateCategory, setTemplateCategory] = useState('');
  const [templateSubCategory, setTemplateSubCategory] = useState('');
  const [selectedTemplateId, setSelectedTemplateId] = useState('');

  const [formState, setFormState] = useState({
    id: null,
    title: '',
    category: 'Weather',
    severity: 'Low',
    status: 'Active',
    affectedAreas: '',
    description: '',
    recommendedAction: '',
    escalationActions: '',
    startDate: '',
    startTime: '',
    endDate: '',
    endTime: ''
  });

  const fetchAdvisories = async () => {
    setLoading(true);
    try {
      const { data, error } = await supabase
        .from('advisories')
        .select('*')
        .order('created_at', { ascending: false });

      if (error) {
        console.warn('Supabase fetch notice:', error.message);
      } else if (data) {
        const mapped = data.map(item => ({
          id: item.id,
          title: item.title,
          category: item.category,
          severity: item.severity,
          status: item.status,
          published: new Date(item.published_at || item.created_at).toLocaleDateString('en-US', { month: 'short', day: 'numeric' }),
          description: item.description || '',
          recommendedAction: item.recommended_action || '',
          escalationActions: item.escalation_actions || '',
          durationStart: item.duration_start || '',
          durationEnd: item.duration_end || '',
          affectedAreas: item.affected_areas || ''
        }));
        setAdvisories(mapped);
        if (mapped.length > 0 && !selectedId) {
          setSelectedId(mapped[0].id);
        }
      }
    } catch (err) {
      console.warn('Supabase client error:', err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchAdvisories();

    // Subscribe to real-time changes
    const channel = supabase
      .channel('advisories-db-changes')
      .on('postgres_changes', { event: '*', schema: 'public', table: 'advisories' }, () => {
        fetchAdvisories();
      })
      .subscribe();

    return () => {
      supabase.removeChannel(channel);
    };
  }, []);

  const selectedAdvisory = advisories.find(a => a.id === selectedId) || advisories[0];

  const handleRefresh = async () => {
    setIsRefreshSpinning(true);
    await fetchAdvisories();
    setTimeout(() => {
      setIsRefreshSpinning(false);
    }, 500);
  };

  // Affected Areas helpers
  const handleAddArea = (val) => {
    if (!val) return;

    if (val === 'ALL_CITY') {
      setFormState(prev => ({ ...prev, affectedAreas: 'All Marikina City' }));
      return;
    }
    if (val === 'ALL_D1') {
      const d1Areas = MARIKINA_DISTRICTS[0].barangays.join(', ');
      setFormState(prev => ({ ...prev, affectedAreas: d1Areas }));
      return;
    }
    if (val === 'ALL_D2') {
      const d2Areas = MARIKINA_DISTRICTS[1].barangays.join(', ');
      setFormState(prev => ({ ...prev, affectedAreas: d2Areas }));
      return;
    }

    // Individual Barangay
    const currentList = formState.affectedAreas
      ? formState.affectedAreas.split(',').map(s => s.trim()).filter(Boolean)
      : [];

    if (!currentList.includes(val)) {
      const newList = [...currentList, val].join(', ');
      setFormState(prev => ({ ...prev, affectedAreas: newList }));
    }
  };

  const handleRemoveArea = (areaToRemove) => {
    const currentList = formState.affectedAreas
      ? formState.affectedAreas.split(',').map(s => s.trim()).filter(Boolean)
      : [];
    const newList = currentList.filter(a => a !== areaToRemove).join(', ');
    setFormState(prev => ({ ...prev, affectedAreas: newList }));
  };

  // Filter logic
  const filteredAdvisories = advisories.filter(adv => {
    const matchesSearch = adv.title.toLowerCase().includes(searchQuery.toLowerCase()) ||
      adv.description.toLowerCase().includes(searchQuery.toLowerCase());
    const matchesCategory = categoryFilter ? adv.category === categoryFilter : true;
    const matchesSeverity = severityFilter ? adv.severity === severityFilter : true;
    const matchesStatus = statusFilter ? adv.status === statusFilter : true;
    return matchesSearch && matchesCategory && matchesSeverity && matchesStatus;
  });

  // Reset to page 1 when filters change
  useEffect(() => {
    setCurrentPage(1);
  }, [searchQuery, categoryFilter, severityFilter, statusFilter]);

  // Pagination logic
  const totalPages = Math.max(1, Math.ceil(filteredAdvisories.length / ITEMS_PER_PAGE));
  const paginatedAdvisories = filteredAdvisories.slice(
    (currentPage - 1) * ITEMS_PER_PAGE,
    currentPage * ITEMS_PER_PAGE
  );

  const getSeverityColor = (sev) => {
    switch (sev) {
      case 'Critical': return { color: '#ec1515ff', bg: '#fef2f2' };
      case 'High': return { color: '#e75050ff', bg: '#fef2f2' };
      case 'Moderate': return { color: '#b4a872ff', bg: '#fff7ed' };
      case 'Low': return { color: '#60bb3cff', bg: '#f0f9ff' };
      default: return { color: '#4b5563', bg: '#f3f4f6' };
    }
  };

  const getStatusColor = (status) => {
    switch (status) {
      case 'Active': return { color: '#16a34a', bg: '#f0fdf4' };
      case 'Archived': return { color: '#6b7280', bg: '#f3f4f6' };
      case 'Draft': return { color: '#ca8a04', bg: '#fefce8' };
      default: return { color: '#4b5563', bg: '#f3f4f6' };
    }
  };

  const openCreateModal = () => {
    setModalMode('create');
    setTemplateCategory('');
    setTemplateSubCategory('');
    setSelectedTemplateId('');
    const today = new Date().toISOString().split('T')[0];
    setFormState({
      id: null,
      title: '',
      category: 'Weather',
      severity: 'Low',
      status: 'Active',
      affectedAreas: '',
      description: '',
      recommendedAction: '',
      escalationActions: '',
      startDate: today,
      startTime: '08:00',
      endDate: today,
      endTime: '18:00'
    });
    setIsModalOpen(true);
  };

  const openEditModal = (adv) => {
    setModalMode('edit');
    setTemplateCategory('');
    setTemplateSubCategory('');
    setSelectedTemplateId('');
    let startDate = '', startTime = '';
    let endDate = '', endTime = '';

    if (adv.durationStart) {
      const parts = adv.durationStart.split('T');
      startDate = parts[0] || '';
      startTime = parts[1] ? parts[1].substring(0, 5) : '';
    }
    if (adv.durationEnd) {
      const parts = adv.durationEnd.split('T');
      endDate = parts[0] || '';
      endTime = parts[1] ? parts[1].substring(0, 5) : '';
    }

    setFormState({
      ...adv,
      startDate,
      startTime,
      endDate,
      endTime
    });
    setIsModalOpen(true);
  };

  const handleDelete = async (id) => {
    if (confirm('Are you sure you want to delete this advisory?')) {
      const { error } = await supabase.from('advisories').delete().eq('id', id);
      if (error) {
        alert('Could not delete: ' + error.message);
      } else {
        fetchAdvisories();
      }
    }
  };

  const handleArchive = async (id, currentStatus) => {
    const newStatus = currentStatus === 'Archived' ? 'Active' : 'Archived';
    const { error } = await supabase
      .from('advisories')
      .update({ status: newStatus })
      .eq('id', id);

    if (error) {
      alert('Could not update status: ' + error.message);
    } else {
      fetchAdvisories();
    }
  };

  const handleDuplicate = async (adv) => {
    const payload = {
      title: `${adv.title} (Copy)`,
      category: adv.category,
      severity: adv.severity,
      status: 'Active',
      description: adv.description,
      recommended_action: adv.recommendedAction,
      escalation_actions: adv.escalationActions || '',
      affected_areas: adv.affectedAreas
    };

    const { error } = await supabase.from('advisories').insert([payload]);
    if (error) {
      alert('Could not duplicate advisory: ' + error.message);
    } else {
      fetchAdvisories();
    }
  };

  const handleApplyTemplate = (templateId) => {
    setSelectedTemplateId(templateId);
    const tpl = ADVISORY_TEMPLATES.find(t => t.id === templateId);
    if (!tpl) return;

    setFormState(prev => ({
      ...prev,
      title: tpl.title,
      category: tpl.category,
      severity: tpl.severity,
      description: tpl.description,
      recommendedAction: tpl.recommendedAction,
      escalationActions: tpl.escalationActions || '',
      affectedAreas: tpl.affectedAreas
    }));
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    const durationStart = (formState.startDate && formState.startTime)
      ? `${formState.startDate}T${formState.startTime}:00`
      : formState.startDate
        ? `${formState.startDate}T00:00:00`
        : null;

    const durationEnd = (formState.endDate && formState.endTime)
      ? `${formState.endDate}T${formState.endTime}:00`
      : formState.endDate
        ? `${formState.endDate}T23:59:59`
        : null;

    const payload = {
      title: formState.title,
      category: formState.category,
      severity: formState.severity,
      status: formState.status,
      description: formState.description,
      recommended_action: formState.recommendedAction,
      escalation_actions: formState.escalationActions,
      affected_areas: formState.affectedAreas,
      duration_start: durationStart,
      duration_end: durationEnd
    };

    if (modalMode === 'create') {
      const { error } = await supabase.from('advisories').insert([payload]);
      if (error) {
        alert('Error publishing to Supabase: ' + error.message);
      } else {
        await fetchAdvisories();
      }
    } else {
      const { error } = await supabase.from('advisories').update(payload).eq('id', formState.id);
      if (error) {
        alert('Error updating in Supabase: ' + error.message);
      } else {
        await fetchAdvisories();
      }
    }
    setIsModalOpen(false);
  };

  return (
    <div className="main-view">
      {/* View Header */}
      <div className="view-header">
        <div className="view-title-container">
          <h1>Advisories</h1>
          <span className="view-subtitle">Live Public Emergency Broadcasting</span>
        </div>
        <button className="btn-refresh" onClick={handleRefresh}>
          <RefreshCw size={13} className={isRefreshSpinning ? 'spin-icon' : ''} />
          <span>Refresh</span>
        </button>
      </div>

      <div className="stations-split-layout">

        {/* Left Side: Advisories List */}
        <div className="stations-card">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <h2 className="stations-card-title">List of Advisories</h2>
            <button className="btn-primary" onClick={openCreateModal} style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '6px 14px', fontSize: '13px' }}>
              <Plus size={16} />
              <span>Publish an Advisory</span>
            </button>
          </div>

          {/* Search and Filters */}
          <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap' }}>
            <div className="search-input-wrapper" style={{ flex: 1, minWidth: '200px' }}>
              <input
                type="text"
                placeholder="Search for an advisory.."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
              />
            </div>

            <select
              value={categoryFilter}
              onChange={(e) => setCategoryFilter(e.target.value)}
              className="time-range-select"
              style={{ padding: '10px 14px', borderRadius: '10px', width: '130px', fontSize: '13px' }}
            >
              <option value="">Category...</option>
              <option value="Weather">Weather</option>
              <option value="Monitoring">Monitoring</option>
              <option value="Flood">Flood</option>
              <option value="Earthquake">Earthquake</option>
            </select>

            <select
              value={severityFilter}
              onChange={(e) => setSeverityFilter(e.target.value)}
              className="time-range-select"
              style={{ padding: '10px 14px', borderRadius: '10px', width: '130px', fontSize: '13px' }}
            >
              <option value="">Severity...</option>
              <option value="Critical">Critical</option>
              <option value="High">High</option>
              <option value="Moderate">Moderate</option>
              <option value="Low">Low</option>
            </select>

            <select
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value)}
              className="time-range-select"
              style={{ padding: '10px 14px', borderRadius: '10px', width: '130px', fontSize: '13px' }}
            >
              <option value="">Status...</option>
              <option value="Active">Active</option>
              <option value="Archived">Archived</option>
              <option value="Draft">Draft</option>
            </select>
          </div>

          {/* Advisories Table */}
          <div className="table-container">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Title</th>
                  <th>Category</th>
                  <th>Severity</th>
                  <th>Status</th>
                  <th>Published</th>
                </tr>
              </thead>
              <tbody>
                {loading ? (
                  <tr>
                    <td colSpan="5" style={{ textAlign: 'center', padding: '24px', color: 'var(--text-light)' }}>
                      Loading Advisories...
                    </td>
                  </tr>
                ) : filteredAdvisories.length === 0 ? (
                  <tr>
                    <td colSpan="5" style={{ textAlign: 'center', padding: '24px', color: 'var(--text-light)' }}>
                      No advisories in database yet. Click <strong>"+ Publish an Advisory"</strong> to create one!
                    </td>
                  </tr>
                ) : (
                  paginatedAdvisories.map((adv) => {
                    const sevStyle = getSeverityColor(adv.severity);
                    const statusStyle = getStatusColor(adv.status);
                    return (
                      <tr
                        key={adv.id}
                        className={selectedAdvisory?.id === adv.id ? 'selected' : ''}
                        onClick={() => setSelectedId(adv.id)}
                      >
                        <td style={{ fontWeight: '600', color: 'var(--text-main)' }}>{adv.title}</td>
                        <td>{adv.category}</td>
                        <td>
                          <span style={{
                            color: sevStyle.color,
                            backgroundColor: sevStyle.bg,
                            padding: '3px 8px',
                            borderRadius: '12px',
                            fontSize: '11px',
                            fontWeight: '700'
                          }}>
                            {adv.severity}
                          </span>
                        </td>
                        <td>
                          <span style={{
                            color: statusStyle.color,
                            backgroundColor: statusStyle.bg,
                            padding: '3px 8px',
                            borderRadius: '12px',
                            fontSize: '11px',
                            fontWeight: '700'
                          }}>
                            {adv.status}
                          </span>
                        </td>
                        <td className="text-muted">{adv.published}</td>
                      </tr>
                    );
                  })
                )}
              </tbody>
            </table>
          </div>

          {/* Pagination */}
          <div className="table-footer">
            <span>{paginatedAdvisories.length} of {filteredAdvisories.length} records</span>
            <div className="pagination-controls">
              <button
                className="pagination-btn"
                disabled={currentPage === 1}
                onClick={() => setCurrentPage(p => Math.max(1, p - 1))}
              >
                <ChevronLeft size={14} />
              </button>
              {Array.from({ length: totalPages }, (_, i) => i + 1).map(page => (
                <button
                  key={page}
                  className={`pagination-btn ${currentPage === page ? 'active' : ''}`}
                  onClick={() => setCurrentPage(page)}
                >
                  {page}
                </button>
              ))}
              <button
                className="pagination-btn"
                disabled={currentPage === totalPages}
                onClick={() => setCurrentPage(p => Math.min(totalPages, p + 1))}
              >
                <ChevronRight size={14} />
              </button>
            </div>
          </div>
        </div>

        {/* Right Side: Advisory Details */}
        <div className="details-panel">
          {!selectedAdvisory ? (
            <div className="details-empty-state">
              <Eye size={32} style={{ color: 'var(--text-light)', marginBottom: '8px' }} />
              <p>Select an advisory or publish a new one to view details.</p>
            </div>
          ) : (
            <div className="details-content">
              <div className="details-header">
                <span className="details-station-name">{selectedAdvisory.title}</span>
                <div style={{ display: 'flex', gap: '8px', marginTop: '8px' }}>
                  <span style={{
                    color: getSeverityColor(selectedAdvisory.severity).color,
                    backgroundColor: getSeverityColor(selectedAdvisory.severity).bg,
                    padding: '3px 8px',
                    borderRadius: '12px',
                    fontSize: '11px',
                    fontWeight: '700'
                  }}>
                    {selectedAdvisory.severity} Severity
                  </span>
                  <span style={{
                    color: getStatusColor(selectedAdvisory.status).color,
                    backgroundColor: getStatusColor(selectedAdvisory.status).bg,
                    padding: '3px 8px',
                    borderRadius: '12px',
                    fontSize: '11px',
                    fontWeight: '700'
                  }}>
                    {selectedAdvisory.status}
                  </span>
                </div>
              </div>

              <div className="details-grid">
                <div className="detail-item" style={{ gridColumn: 'span 2' }}>
                  <span className="detail-label">Advisory Description</span>
                  <p className="detail-value" style={{ fontWeight: 'normal', fontSize: '13px', lineHeight: '1.5', marginTop: '4px' }}>
                    {selectedAdvisory.description}
                  </p>
                </div>

                <div className="detail-item" style={{ gridColumn: 'span 2' }}>
                  <span className="detail-label">Recommended Action Plan</span>
                  <p className="detail-value" style={{ fontWeight: 'normal', fontSize: '13px', lineHeight: '1.5', marginTop: '4px', color: '#ea580c' }}>
                    {selectedAdvisory.recommendedAction}
                  </p>
                </div>

                {selectedAdvisory.escalationActions && (
                  <div className="detail-item" style={{ gridColumn: 'span 2', backgroundColor: '#fef2f2', padding: '10px 12px', borderRadius: '8px', border: '1px solid #fecaca' }}>
                    <span className="detail-label" style={{ color: '#dc2626', fontWeight: '700' }}>
                      Escalated Actions (If conditions worsen)
                    </span>
                    <p className="detail-value" style={{ fontWeight: 'normal', fontSize: '13px', lineHeight: '1.5', marginTop: '4px', color: '#b91c1c' }}>
                      {selectedAdvisory.escalationActions}
                    </p>
                  </div>
                )}

                <div className="detail-item">
                  <span className="detail-label">Category Group</span>
                  <span className="detail-value">{selectedAdvisory.category}</span>
                </div>

                <div className="detail-item">
                  <span className="detail-label">Affected Area Sectors</span>
                  <div style={{ display: 'flex', alignItems: 'center', gap: '4px', fontSize: '13px', fontWeight: '600', color: 'var(--text-main)', marginTop: '2px' }}>
                    <MapPin size={12} style={{ color: 'var(--color-brand)' }} />
                    <span>{selectedAdvisory.affectedAreas || 'N/A'}</span>
                  </div>
                </div>

                <div className="detail-item" style={{ gridColumn: 'span 2' }}>
                  <span className="detail-label">Valid Duration Period</span>
                  <div style={{ display: 'flex', alignItems: 'center', gap: '6px', fontSize: '12px', color: 'var(--text-muted)', marginTop: '2px' }}>
                    <Calendar size={12} />
                    <span>
                      {selectedAdvisory.durationStart ? new Date(selectedAdvisory.durationStart).toLocaleString() : 'N/A'} to{' '}
                      {selectedAdvisory.durationEnd ? new Date(selectedAdvisory.durationEnd).toLocaleString() : 'N/A'}
                    </span>
                  </div>
                </div>
              </div>

              {/* Action Buttons Panel */}
              <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', marginTop: 'auto', paddingTop: '16px', borderTop: '1px solid var(--color-border)' }}>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '8px' }}>
                  <button
                    className="action-row-btn"
                    onClick={() => openEditModal(selectedAdvisory)}
                    style={{ justifyContent: 'center', gap: '8px' }}
                  >
                    <Edit3 size={14} />
                    <span>Edit</span>
                  </button>
                  <button
                    className="action-row-btn"
                    onClick={() => handleArchive(selectedAdvisory.id, selectedAdvisory.status)}
                    style={{ justifyContent: 'center', gap: '8px' }}
                  >
                    <Archive size={14} />
                    <span>{selectedAdvisory.status === 'Archived' ? 'Activate' : 'Archive'}</span>
                  </button>
                </div>

                <button
                  className="action-row-btn"
                  onClick={() => handleDuplicate(selectedAdvisory)}
                  style={{
                    justifyContent: 'center',
                    gap: '8px',
                    backgroundColor: '#e0f2fe',
                    borderColor: '#bae6fd',
                    color: '#0369a1'
                  }}
                >
                  <Copy size={14} />
                  <span>Duplicate</span>
                </button>

                <button
                  className="action-row-btn"
                  onClick={() => handleDelete(selectedAdvisory.id)}
                  style={{
                    justifyContent: 'center',
                    gap: '8px',
                    backgroundColor: '#fef2f2',
                    borderColor: '#fca5a5',
                    color: '#dc2626'
                  }}
                >
                  <Trash2 size={14} />
                  <span>Delete</span>
                </button>
              </div>
            </div>
          )}
        </div>
      </div>

      {/* Right-side Drawer for Create/Edit */}
      {isModalOpen && (
        <div
          style={{ position: 'fixed', top: 0, left: 0, right: 0, bottom: 0, backgroundColor: 'rgba(0,0,0,0.4)', zIndex: 999, display: 'flex', justifyContent: 'flex-end' }}
          onClick={() => setIsModalOpen(false)}
        >
          <div
            style={{ width: '420px', backgroundColor: '#fff', height: '100%', display: 'flex', flexDirection: 'column', boxShadow: '-4px 0 15px rgba(0,0,0,0.1)' }}
            onClick={(e) => e.stopPropagation()}
          >
            {/* Drawer Header */}
            <div style={{ padding: '20px', borderBottom: '1px solid #e2e8f0', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <h2 style={{ fontSize: '16px', fontWeight: '700', margin: 0, color: '#0f172a' }}>
                {modalMode === 'create' ? 'Create Advisory' : 'Edit Advisory'}
              </h2>
              <button
                onClick={() => setIsModalOpen(false)}
                style={{ background: 'none', border: '1px solid #e2e8f0', borderRadius: '4px', cursor: 'pointer', padding: '4px', display: 'flex', alignItems: 'center' }}
              >
                <X size={16} />
              </button>
            </div>

            <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', flex: 1, minHeight: 0 }}>
              <div style={{ flex: 1, overflowY: 'auto', display: 'flex', flexDirection: 'column', gap: '16px', padding: '24px' }}>

                {/* Cascading 2-Step / 3-Step Pre-made Template Selector */}
                <div className="form-group" style={{ backgroundColor: '#f8fafc', padding: '14px', borderRadius: '10px', border: '1px solid #cbd5e1', display: 'flex', flexDirection: 'column', gap: '10px' }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <label className="form-label" style={{ fontWeight: '700', color: '#0f172a', margin: 0 }}>Load Pre-made Scenario Template</label>
                    {(templateCategory || selectedTemplateId) && (
                      <button
                        type="button"
                        onClick={() => {
                          setTemplateCategory('');
                          setTemplateSubCategory('');
                          setSelectedTemplateId('');
                        }}
                        style={{ border: 'none', background: 'none', color: '#64748b', fontSize: '11px', cursor: 'pointer', textDecoration: 'underline' }}
                      >
                        Reset Filter
                      </button>
                    )}
                  </div>

                  {/* Step 1: Select Type / Group */}
                  <select
                    className="form-input"
                    value={templateCategory}
                    onChange={(e) => {
                      setTemplateCategory(e.target.value);
                      setTemplateSubCategory('');
                      setSelectedTemplateId('');
                    }}
                  >
                    <option value="">1. Choose Category / Event Type...</option>
                    <option value="Earthquake">Earthquake Advisories (7)</option>
                    <option value="Weather">Weather Advisories (10)</option>
                    <option value="River">River Station Alarms (18)</option>
                    <option value="CityDistrict">City & District Flood Advisories (12)</option>
                    <option value="BarangayD1">District 1 Barangay Floods (36)</option>
                    <option value="BarangayD2">District 2 Barangay Floods (28)</option>
                  </select>

                  {/* Step 2: Select Station or Barangay (if applicable) */}
                  {templateCategory === 'River' && (
                    <select
                      className="form-input"
                      value={templateSubCategory}
                      onChange={(e) => {
                        setTemplateSubCategory(e.target.value);
                        setSelectedTemplateId('');
                      }}
                    >
                      <option value="">2. Select River Station...</option>
                      {['Batasan', 'Nangka', 'Rodriguez', 'San Jose', 'Sto. Niño', 'Tumana'].map(st => (
                        <option key={st} value={st}>{st} Station</option>
                      ))}
                    </select>
                  )}

                  {templateCategory === 'BarangayD1' && (
                    <select
                      className="form-input"
                      value={templateSubCategory}
                      onChange={(e) => {
                        setTemplateSubCategory(e.target.value);
                        setSelectedTemplateId('');
                      }}
                    >
                      <option value="">2. Select District 1 Barangay...</option>
                      {MARIKINA_DISTRICTS[0].barangays.map(b => (
                        <option key={b} value={b}>{b}</option>
                      ))}
                    </select>
                  )}

                  {templateCategory === 'BarangayD2' && (
                    <select
                      className="form-input"
                      value={templateSubCategory}
                      onChange={(e) => {
                        setTemplateSubCategory(e.target.value);
                        setSelectedTemplateId('');
                      }}
                    >
                      <option value="">2. Select District 2 Barangay...</option>
                      {MARIKINA_DISTRICTS[1].barangays.map(b => (
                        <option key={b} value={b}>{b}</option>
                      ))}
                    </select>
                  )}

                  {/* Step 3 (or Step 2): Select Specific Template */}
                  {templateCategory && (templateCategory === 'Earthquake' || templateCategory === 'Weather' || templateCategory === 'CityDistrict' || templateSubCategory) && (
                    <select
                      className="form-input"
                      value={selectedTemplateId}
                      onChange={(e) => handleApplyTemplate(e.target.value)}
                      style={{ fontWeight: '600', borderColor: '#3b82f6', backgroundColor: '#eff6ff' }}
                    >
                      <option value="" disabled>
                        {templateCategory === 'Earthquake' || templateCategory === 'Weather' || templateCategory === 'CityDistrict'
                          ? '2. Select Specific Scenario...'
                          : '3. Select Specific Scenario...'}
                      </option>

                      {ADVISORY_TEMPLATES.filter(t => {
                        if (templateCategory === 'Earthquake') return t.group === 'Earthquake Advisories';
                        if (templateCategory === 'Weather') return t.group === 'Weather Advisories';
                        if (templateCategory === 'CityDistrict') return t.group === 'City & District Flood Advisories';
                        if (templateCategory === 'River') return t.group === `River Monitoring: ${templateSubCategory}`;
                        if (templateCategory === 'BarangayD1' || templateCategory === 'BarangayD2') return t.affectedAreas === templateSubCategory;
                        return false;
                      }).map(tpl => (
                        <option key={tpl.id} value={tpl.id}>
                          [{tpl.severity}] {tpl.title}
                        </option>
                      ))}
                    </select>
                  )}
                </div>

                <div className="form-group">
                  <label className="form-label">Title</label>
                  <input
                    type="text"
                    className="form-input"
                    placeholder="Advisory Title.."
                    value={formState.title}
                    onChange={(e) => setFormState({ ...formState, title: e.target.value })}
                    required
                  />
                  <span style={{ fontSize: '11px', color: '#94a3b8', marginTop: '2px' }}>Example: Heavy Rainfall Warning</span>
                </div>

                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
                  <div className="form-group">
                    <label className="form-label">Category</label>
                    <select
                      className="form-input"
                      value={formState.category}
                      onChange={(e) => setFormState({ ...formState, category: e.target.value })}
                    >
                      <option value="Weather">Weather</option>
                      <option value="Monitoring">Monitoring</option>
                      <option value="Flood">Flood</option>
                      <option value="Earthquake">Earthquake</option>
                    </select>
                  </div>

                  <div className="form-group">
                    <label className="form-label">Severity</label>
                    <select
                      className="form-input"
                      value={formState.severity}
                      onChange={(e) => setFormState({ ...formState, severity: e.target.value })}
                    >
                      <option value="Low">Low Severity</option>
                      <option value="Moderate">Moderate Severity</option>
                      <option value="High">High Severity</option>
                      <option value="Critical">Critical Severity</option>
                    </select>
                  </div>
                </div>

                {/* District Grouped Barangays Selector */}
                <div className="form-group">
                  <label className="form-label">Affected Areas (Marikina Barangays by District)</label>

                  {/* Selected Chips */}
                  {formState.affectedAreas && (
                    <div style={{ display: 'flex', flexWrap: 'wrap', gap: '6px', marginBottom: '8px' }}>
                      {formState.affectedAreas.split(',').map(s => s.trim()).filter(Boolean).map((area, idx) => (
                        <span
                          key={idx}
                          style={{
                            display: 'inline-flex',
                            alignItems: 'center',
                            gap: '6px',
                            backgroundColor: '#e0f2fe',
                            color: '#0369a1',
                            fontSize: '12px',
                            fontWeight: '600',
                            padding: '4px 10px',
                            borderRadius: '16px',
                            border: '1px solid #bae6fd'
                          }}
                        >
                          {area}
                          <button
                            type="button"
                            onClick={() => handleRemoveArea(area)}
                            style={{ border: 'none', background: 'none', cursor: 'pointer', padding: 0, display: 'flex', color: '#0369a1' }}
                          >
                            <X size={13} />
                          </button>
                        </span>
                      ))}
                      <button
                        type="button"
                        onClick={() => setFormState({ ...formState, affectedAreas: '' })}
                        style={{ border: 'none', background: 'none', color: '#dc2626', fontSize: '11px', cursor: 'pointer', marginLeft: '4px', textDecoration: 'underline' }}
                      >
                        Clear all
                      </button>
                    </div>
                  )}

                  <select
                    className="form-input"
                    value=""
                    onChange={(e) => handleAddArea(e.target.value)}
                  >
                    <option value="" disabled>+ Add Barangay or District...</option>
                    <option value="ALL_CITY">All Marikina City</option>
                    <option value="ALL_D1">All District 1 Barangays</option>
                    <option value="ALL_D2">All District 2 Barangays</option>

                    <optgroup label="District 1">
                      {MARIKINA_DISTRICTS[0].barangays.map(b => (
                        <option key={b} value={b}>{b}</option>
                      ))}
                    </optgroup>

                    <optgroup label="District 2">
                      {MARIKINA_DISTRICTS[1].barangays.map(b => (
                        <option key={b} value={b}>{b}</option>
                      ))}
                    </optgroup>
                  </select>
                </div>

                <div className="form-group">
                  <label className="form-label">Description</label>
                  <textarea
                    className="form-textarea"
                    rows="3"
                    placeholder="Advisory description..."
                    value={formState.description}
                    onChange={(e) => setFormState({ ...formState, description: e.target.value })}
                    required
                    style={{ padding: '10px 12px', border: '1px solid #cbd5e1', borderRadius: '6px', fontSize: '13px', outline: 'none', resize: 'vertical', fontFamily: 'inherit' }}
                  />
                </div>

                <div className="form-group">
                  <label className="form-label">Recommended Action</label>
                  <textarea
                    className="form-textarea"
                    rows="3"
                    placeholder="Advisory action recommendations..."
                    value={formState.recommendedAction}
                    onChange={(e) => setFormState({ ...formState, recommendedAction: e.target.value })}
                    style={{ padding: '10px 12px', border: '1px solid #cbd5e1', borderRadius: '6px', fontSize: '13px', outline: 'none', resize: 'vertical', fontFamily: 'inherit' }}
                  />
                </div>

                <div className="form-group">
                  <label className="form-label" style={{ color: '#dc2626', fontWeight: '600' }}>
                    Escalation Actions (If conditions worsen)
                  </label>
                  <textarea
                    className="form-textarea"
                    rows="3"
                    placeholder="Actions citizens should take if conditions worsen (e.g. Evacuate immediately; Turn off LPG; Contact hotlines)..."
                    value={formState.escalationActions}
                    onChange={(e) => setFormState({ ...formState, escalationActions: e.target.value })}
                    style={{ padding: '10px 12px', border: '1px solid #fca5a5', borderRadius: '6px', fontSize: '13px', outline: 'none', resize: 'vertical', fontFamily: 'inherit', backgroundColor: '#fef2f2' }}
                  />
                </div>

                {/* Separate Duration Start Date and Time */}
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
                  <div className="form-group">
                    <label className="form-label">Duration Start Date</label>
                    <input
                      type="date"
                      className="form-input"
                      value={formState.startDate}
                      onChange={(e) => setFormState({ ...formState, startDate: e.target.value })}
                    />
                  </div>

                  <div className="form-group">
                    <label className="form-label">Start Time</label>
                    <input
                      type="time"
                      className="form-input"
                      value={formState.startTime}
                      onChange={(e) => setFormState({ ...formState, startTime: e.target.value })}
                    />
                  </div>
                </div>

                {/* Separate Duration End Date and Time */}
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
                  <div className="form-group">
                    <label className="form-label">Duration End Date</label>
                    <input
                      type="date"
                      className="form-input"
                      value={formState.endDate}
                      onChange={(e) => setFormState({ ...formState, endDate: e.target.value })}
                    />
                  </div>

                  <div className="form-group">
                    <label className="form-label">End Time</label>
                    <input
                      type="time"
                      className="form-input"
                      value={formState.endTime}
                      onChange={(e) => setFormState({ ...formState, endTime: e.target.value })}
                    />
                  </div>
                </div>

                <div className="form-group">
                  <label className="form-label">Status</label>
                  <select
                    className="form-input"
                    value={formState.status}
                    onChange={(e) => setFormState({ ...formState, status: e.target.value })}
                  >
                    <option value="Active">Active</option>
                    <option value="Draft">Draft</option>
                    <option value="Archived">Archived</option>
                  </select>
                </div>

              </div>

              {/* Drawer Footer */}
              <div style={{ padding: '20px', borderTop: '1px solid #e2e8f0', display: 'flex', justifyContent: 'flex-end', gap: '12px', marginTop: 'auto' }}>
                <button
                  type="button"
                  onClick={() => setIsModalOpen(false)}
                  style={{ padding: '8px 16px', border: '1px solid #e2e8f0', borderRadius: '6px', backgroundColor: '#fff', cursor: 'pointer', fontWeight: '600', color: '#334155', fontSize: '13px' }}
                >
                  Close
                </button>
                <button
                  type="submit"
                  style={{ backgroundColor: '#0d9488', color: '#fff', border: 'none', padding: '8px 16px', borderRadius: '6px', fontSize: '13px', fontWeight: '600', display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer' }}
                >
                  <Check size={16} />
                  <span>Publish Advisory</span>
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

    </div>
  );
}