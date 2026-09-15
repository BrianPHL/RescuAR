import React, { useState, useEffect } from 'react';
import { 
  Shield, 
  Users, 
  MapPin, 
  Phone, 
  Plus, 
  Search, 
  CheckCircle2, 
  AlertTriangle,
  RefreshCw,
  Edit,
  Archive,
  X,
  ChevronLeft,
  ChevronRight
} from 'lucide-react';
import { supabase } from '../supabaseClient';

export default function EvacuationCenters() {
  const [searchTerm, setSearchTerm] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [selectedCenter, setSelectedCenter] = useState(null);
  const [isDrawerOpen, setIsDrawerOpen] = useState(false);
  const [isEditing, setIsEditing] = useState(false);
  const [loading, setLoading] = useState(true);
  const [isRefreshSpinning, setIsRefreshSpinning] = useState(false);

  // Initial mock data fallback if database is empty
  const defaultCenters = [
    {
      id: '1',
      name: 'Malanday Elementary School',
      barangay: 'Malanday',
      capacity: 1200,
      currentEvacuees: 450,
      status: 'Open',
      headOfficer: 'Captain Roberto Santos',
      contact: '0917-555-0192',
      facilities: ['Medical Station', 'Clean Water', 'Generator', 'Modular Tents']
    },
    {
      id: '2',
      name: 'Tumana Evacuation Center',
      barangay: 'Tumana',
      capacity: 1500,
      currentEvacuees: 980,
      status: 'Open',
      headOfficer: 'Elena Cruz (LGU Coordinator)',
      contact: '0918-444-9120',
      facilities: ['Medical Station', 'Kitchen Area', 'Clean Water', 'Child-Friendly Space']
    },
    {
      id: '3',
      name: 'Nangka Elementary School',
      barangay: 'Nangka',
      capacity: 1000,
      currentEvacuees: 310,
      status: 'Open',
      headOfficer: 'Kagawad Manuel Reyes',
      contact: '0920-333-8101',
      facilities: ['Clean Water', 'Generator', 'Restrooms']
    },
    {
      id: '4',
      name: 'Provident Multipurpose Hall',
      barangay: 'Provident',
      capacity: 600,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Maria Gonzales',
      contact: '0915-222-7711',
      facilities: ['Generator', 'Restrooms', 'Parking']
    },
    {
      id: '5',
      name: 'Marikina Sports Center',
      barangay: 'Sto. Niño',
      capacity: 3500,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'MDRRMO Relief Team Alpha',
      contact: '0917-809-5141',
      facilities: ['Major Relief Hub', 'Medical Station', 'Helipad', 'Full Kitchen']
    }
  ];

  const [centers, setCenters] = useState(defaultCenters);

  const [formData, setFormData] = useState({
    id: null,
    name: '',
    barangay: '',
    capacity: '',
    currentEvacuees: 0,
    status: 'Standby',
    headOfficer: '',
    contact: '',
    facilities: ''
  });

  const fetchCenters = async () => {
    setLoading(true);
    try {
      const { data, error } = await supabase
        .from('evacuation_centers')
        .select('*')
        .order('name', { ascending: true });

      if (error) {
        console.warn('Supabase fetch notice (evacuation_centers):', error.message);
      } else if (data && data.length > 0) {
        const mapped = data.map(item => ({
          id: item.id,
          name: item.name,
          barangay: item.barangay || 'Marikina',
          capacity: item.capacity || 500,
          currentEvacuees: item.current_evacuees || 0,
          status: item.status || 'Standby',
          headOfficer: item.head_officer || 'Unassigned',
          contact: item.contact || 'N/A',
          facilities: Array.isArray(item.facilities) 
            ? item.facilities 
            : typeof item.facilities === 'string'
              ? item.facilities.split(',').map(s => s.trim()).filter(Boolean)
              : ['Clean Water', 'Restrooms']
        }));
        setCenters(mapped);
        if (!selectedCenter) {
          setSelectedCenter(mapped[0]);
        }
      } else {
        if (!selectedCenter && defaultCenters.length > 0) {
          setSelectedCenter(defaultCenters[0]);
        }
      }
    } catch (err) {
      console.warn('Supabase client error:', err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchCenters();

    const channel = supabase
      .channel('evac-centers-db-changes')
      .on('postgres_changes', { event: '*', schema: 'public', table: 'evacuation_centers' }, () => {
        fetchCenters();
      })
      .subscribe();

    return () => {
      supabase.removeChannel(channel);
    };
  }, []);

  const handleRefresh = async () => {
    setIsRefreshSpinning(true);
    await fetchCenters();
    setTimeout(() => {
      setIsRefreshSpinning(false);
    }, 500);
  };

  const filteredCenters = centers.filter(c => {
    const matchesSearch = c.name.toLowerCase().includes(searchTerm.toLowerCase()) ||
      c.barangay.toLowerCase().includes(searchTerm.toLowerCase()) ||
      c.headOfficer.toLowerCase().includes(searchTerm.toLowerCase());
    const matchesStatus = statusFilter ? c.status === statusFilter : true;
    return matchesSearch && matchesStatus;
  });

  const openAddModal = () => {
    setIsEditing(false);
    setFormData({
      id: null,
      name: '',
      barangay: '',
      capacity: '',
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: '',
      contact: '',
      facilities: 'Clean Water, Restrooms, Generator'
    });
    setIsDrawerOpen(true);
  };

  const openEditModal = (center) => {
    setIsEditing(true);
    setFormData({
      id: center.id,
      name: center.name,
      barangay: center.barangay,
      capacity: center.capacity,
      currentEvacuees: center.currentEvacuees,
      status: center.status,
      headOfficer: center.headOfficer,
      contact: center.contact,
      facilities: Array.isArray(center.facilities) ? center.facilities.join(', ') : center.facilities || ''
    });
    setIsDrawerOpen(true);
  };

  const handleSave = async () => {
    if (!formData.name || !formData.barangay) {
      alert('Please provide a shelter name and barangay location.');
      return;
    }

    const facilitiesArray = formData.facilities
      ? formData.facilities.split(',').map(s => s.trim()).filter(Boolean)
      : ['Clean Water', 'Restrooms'];

    const payload = {
      name: formData.name,
      barangay: formData.barangay,
      capacity: parseInt(formData.capacity) || 500,
      current_evacuees: parseInt(formData.currentEvacuees) || 0,
      status: formData.status,
      head_officer: formData.headOfficer || 'Unassigned',
      contact: formData.contact || 'N/A',
      facilities: facilitiesArray
    };

    if (isEditing && formData.id) {
      const { error } = await supabase.from('evacuation_centers').update(payload).eq('id', formData.id);
      if (error) {
        console.warn('Updating local state due to Supabase notice:', error.message);
        setCenters(prev => prev.map(item => item.id === formData.id ? { ...item, ...payload, facilities: facilitiesArray, currentEvacuees: payload.current_evacuees, headOfficer: payload.head_officer } : item));
      } else {
        fetchCenters();
      }
    } else {
      const { error } = await supabase.from('evacuation_centers').insert([payload]);
      if (error) {
        console.warn('Adding to local state due to Supabase notice:', error.message);
        const newLocalCenter = {
          id: String(Date.now()),
          ...payload,
          facilities: facilitiesArray,
          currentEvacuees: payload.current_evacuees,
          headOfficer: payload.head_officer
        };
        setCenters(prev => [...prev, newLocalCenter]);
        setSelectedCenter(newLocalCenter);
      } else {
        fetchCenters();
      }
    }

    setIsDrawerOpen(false);
  };

  const getStatusBadge = (status) => {
    switch (status) {
      case 'Open':
        return { bg: '#ecfdf5', color: '#059669', label: 'Open' };
      case 'Standby':
        return { bg: '#eff6ff', color: '#3b82f6', label: 'Standby' };
      case 'Full':
        return { bg: '#fef2f2', color: '#dc2626', label: 'Full' };
      default:
        return { bg: '#f1f5f9', color: '#64748b', label: status || 'Standby' };
    }
  };

  // Reusable inline style objects matching EmergencyHotlines UI
  const styles = {
    panelContainer: { display: 'flex', gap: '20px', alignItems: 'flex-start' },
    leftPanel: { flex: '1', backgroundColor: '#fff', borderRadius: '8px', boxShadow: '0 1px 3px rgba(0,0,0,0.1)', overflow: 'hidden', minHeight: '600px', display: 'flex', flexDirection: 'column' },
    rightPanel: { width: '380px', backgroundColor: 'transparent', flexShrink: 0 },
    headerFlex: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '20px 20px 15px' },
    controlsFlex: { display: 'flex', gap: '10px', padding: '0 20px 15px' },
    searchInput: { flex: 1, padding: '8px 12px', borderRadius: '6px', border: '1px solid #e2e8f0', fontSize: '13px' },
    selectInput: { width: '130px', padding: '8px 12px', borderRadius: '6px', border: '1px solid #e2e8f0', fontSize: '13px', backgroundColor: '#fff' },
    addButton: { backgroundColor: '#0d9488', color: '#fff', border: 'none', padding: '8px 16px', borderRadius: '6px', fontSize: '13px', fontWeight: '600', display: 'flex', alignItems: 'center', gap: '6px', cursor: 'pointer' },
    tableHeader: { backgroundColor: '#f8fafc', padding: '12px 20px', borderBottom: '1px solid #e2e8f0', textAlign: 'left', fontSize: '12px', fontWeight: '600', color: '#64748b' },
    tableRow: { borderBottom: '1px solid #f1f5f9', cursor: 'pointer', transition: 'background 0.2s' },
    tableCell: { padding: '14px 20px', fontSize: '13px', color: '#334155' },
    pagination: { marginTop: 'auto', padding: '15px 20px', borderTop: '1px solid #e2e8f0', display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '12px', color: '#94a3b8' },
    pageControls: { display: 'flex', gap: '5px' },
    pageBtn: { padding: '4px 8px', border: '1px solid #e2e8f0', borderRadius: '4px', backgroundColor: '#fff', cursor: 'pointer', color: '#64748b', display: 'flex', alignItems: 'center' },

    // Details card styles
    detailsCard: { backgroundColor: '#fff', borderRadius: '8px', boxShadow: '0 1px 3px rgba(0,0,0,0.1)', padding: '20px' },
    sectionLabel: { fontSize: '12px', fontWeight: '500', color: '#94a3b8', marginBottom: '12px', marginTop: '16px', textTransform: 'uppercase', letterSpacing: '0.5px' },
    rowPair: { display: 'flex', justifyContent: 'space-between', marginBottom: '10px', fontSize: '13px' },
    label: { color: '#64748b' },
    val: { color: '#0f172a', fontWeight: '500', textAlign: 'right', maxWidth: '60%', wordBreak: 'break-word', lineHeight: '1.4' },
    outlineBtn: { width: '100%', padding: '10px', border: '1px solid #cbd5e1', borderRadius: '6px', backgroundColor: 'transparent', display: 'flex', justifyContent: 'center', alignItems: 'center', gap: '8px', cursor: 'pointer', fontSize: '13px', fontWeight: '600', color: '#334155', marginTop: '12px' },
    emptyState: { display: 'flex', justifyContent: 'center', alignItems: 'center', height: '600px', fontSize: '14px', color: '#94a3b8', fontWeight: '500', textAlign: 'center' },

    // Drawer styles
    overlay: { position: 'fixed', top: 0, left: 0, right: 0, bottom: 0, backgroundColor: 'rgba(0,0,0,0.4)', zIndex: 999, display: 'flex', justifyContent: 'flex-end' },
    drawer: { width: '400px', backgroundColor: '#fff', height: '100%', display: 'flex', flexDirection: 'column', boxShadow: '-4px 0 15px rgba(0,0,0,0.1)' },
    drawerHeader: { padding: '20px', borderBottom: '1px solid #e2e8f0', display: 'flex', justifyContent: 'space-between', alignItems: 'center' },
    drawerBody: { padding: '20px', overflowY: 'auto', flex: 1, display: 'flex', flexDirection: 'column', gap: '16px' },
    drawerFooter: { padding: '20px', borderTop: '1px solid #e2e8f0', display: 'flex', justifyContent: 'flex-end', gap: '12px' },
    formGroup: { display: 'flex', flexDirection: 'column', gap: '6px' },
    input: { padding: '10px 12px', border: '1px solid #cbd5e1', borderRadius: '6px', fontSize: '13px', outline: 'none' }
  };

  const activeCenter = selectedCenter || filteredCenters[0] || centers[0];

  return (
    <div className="main-view" style={{ backgroundColor: '#f1f5f9', minHeight: '100vh', padding: '30px' }}>
      
      {/* PAGE HEADER */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px' }}>
        <div>
          <h1 style={{ fontSize: '24px', fontWeight: '700', color: '#0f172a', margin: '0 0 4px 0' }}>Evacuation Centers</h1>
          <span style={{ fontSize: '12px', color: '#94a3b8' }}>
            Monitor shelter capacity, occupancy rates, and assigned LGU relief officers across Marikina
          </span>
        </div>
        <button 
          className="btn-refresh" 
          onClick={handleRefresh}
          style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: '8px 14px', borderRadius: '6px', border: '1px solid #cbd5e1', backgroundColor: '#fff', cursor: 'pointer', fontSize: '13px', color: '#334155' }}
        >
          <RefreshCw size={13} className={isRefreshSpinning ? 'spin-icon' : ''} />
          <span>Refresh</span>
        </button>
      </div>

      {/* SPLIT PANEL LAYOUT */}
      <div style={styles.panelContainer}>
        
        {/* LEFT PANEL: LIST OF EVACUATION CENTERS */}
        <div style={styles.leftPanel}>
          <div style={styles.headerFlex}>
            <h2 style={{ fontSize: '16px', fontWeight: '600', color: '#0f172a', margin: 0 }}>List of Evacuation Centers</h2>
            <button style={styles.addButton} onClick={openAddModal}>
              <Plus size={16} />
              <span>Add New Center</span>
            </button>
          </div>

          <div style={styles.controlsFlex}>
            <input
              type="text"
              placeholder="Search for an evacuation center..."
              style={styles.searchInput}
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
            />
            <select
              style={styles.selectInput}
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value)}
            >
              <option value="">Status...</option>
              <option value="Open">Open</option>
              <option value="Standby">Standby</option>
              <option value="Full">Full</option>
            </select>
          </div>

          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr>
                <th style={styles.tableHeader}>Shelter Name</th>
                <th style={styles.tableHeader}>Barangay</th>
                <th style={styles.tableHeader}>Capacity</th>
                <th style={styles.tableHeader}>Occupancy</th>
                <th style={styles.tableHeader}>Status</th>
              </tr>
            </thead>
            <tbody>
              {filteredCenters.map((c) => {
                const badge = getStatusBadge(c.status);
                const isSelected = activeCenter && activeCenter.id === c.id;
                const occupancyPercent = Math.min(100, Math.round((c.currentEvacuees / c.capacity) * 100));

                return (
                  <tr
                    key={c.id}
                    style={{
                      ...styles.tableRow,
                      backgroundColor: isSelected ? '#f8fafc' : 'transparent'
                    }}
                    onClick={() => setSelectedCenter(c)}
                  >
                    <td style={{ ...styles.tableCell, fontWeight: '600', color: '#0f172a' }}>{c.name}</td>
                    <td style={styles.tableCell}>{c.barangay}</td>
                    <td style={styles.tableCell}>{c.capacity.toLocaleString()} evacuees</td>
                    <td style={styles.tableCell}>
                      <span style={{ fontWeight: '500' }}>{c.currentEvacuees} ({occupancyPercent}%)</span>
                    </td>
                    <td style={styles.tableCell}>
                      <span style={{
                        backgroundColor: badge.bg,
                        color: badge.color,
                        padding: '3px 8px',
                        borderRadius: '12px',
                        fontSize: '11px',
                        fontWeight: '700'
                      }}>
                        {badge.label}
                      </span>
                    </td>
                  </tr>
                );
              })}
              {filteredCenters.length === 0 && (
                <tr>
                  <td colSpan="5" style={{ padding: '30px', textAlign: 'center', color: '#94a3b8', fontSize: '13px' }}>
                    No evacuation centers found.
                  </td>
                </tr>
              )}
            </tbody>
          </table>

          <div style={styles.pagination}>
            <span>1 of {filteredCenters.length || 1} record</span>
            <div style={styles.pageControls}>
              <button style={styles.pageBtn}><ChevronLeft size={14} /></button>
              <button style={{ ...styles.pageBtn, backgroundColor: '#0d9488', color: '#fff', borderColor: '#0d9488' }}>1</button>
              <button style={styles.pageBtn}><ChevronRight size={14} /></button>
            </div>
          </div>
        </div>

        {/* RIGHT PANEL: EVACUATION CENTER DETAILS */}
        <div style={styles.rightPanel}>
          {activeCenter ? (
            <div style={styles.detailsCard}>
              <h2 style={{ fontSize: '16px', fontWeight: '600', color: '#0f172a', margin: '0 0 16px 0' }}>Evacuation Center Details</h2>

              <div style={styles.sectionLabel}>SHELTER INFORMATION</div>
              <div style={styles.rowPair}>
                <span style={styles.label}>Shelter Name</span>
                <span style={styles.val}>{activeCenter.name}</span>
              </div>
              <div style={styles.rowPair}>
                <span style={styles.label}>Barangay Location</span>
                <span style={styles.val}>{activeCenter.barangay}</span>
              </div>
              <div style={styles.rowPair}>
                <span style={styles.label}>Operating Status</span>
                <span style={{
                  ...styles.val,
                  color: getStatusBadge(activeCenter.status).color,
                  fontWeight: '700'
                }}>
                  {getStatusBadge(activeCenter.status).label}
                </span>
              </div>

              <div style={styles.sectionLabel}>OCCUPANCY & CAPACITY</div>
              <div style={styles.rowPair}>
                <span style={styles.label}>Maximum Capacity</span>
                <span style={styles.val}>{activeCenter.capacity.toLocaleString()} evacuees</span>
              </div>
              <div style={styles.rowPair}>
                <span style={styles.label}>Current Evacuees</span>
                <span style={styles.val}>{activeCenter.currentEvacuees.toLocaleString()}</span>
              </div>
              
              {/* Occupancy Progress Bar */}
              <div style={{ marginTop: '8px', marginBottom: '12px' }}>
                <div style={{ width: '100%', backgroundColor: '#f1f5f9', height: '8px', borderRadius: '4px', overflow: 'hidden' }}>
                  <div style={{ 
                    width: `${Math.min(100, Math.round((activeCenter.currentEvacuees / activeCenter.capacity) * 100))}%`, 
                    backgroundColor: (activeCenter.currentEvacuees / activeCenter.capacity) > 0.8 ? '#ef4444' : '#0d9488', 
                    height: '100%', 
                    borderRadius: '4px',
                    transition: 'width 0.3s ease'
                  }} />
                </div>
              </div>

              <div style={styles.sectionLabel}>OFFICER IN CHARGE</div>
              <div style={styles.rowPair}>
                <span style={styles.label}>Head Officer</span>
                <span style={styles.val}>{activeCenter.headOfficer}</span>
              </div>
              <div style={styles.rowPair}>
                <span style={styles.label}>Contact Number</span>
                <span style={styles.val}>{activeCenter.contact}</span>
              </div>

              <div style={styles.sectionLabel}>FACILITIES & AMENITIES</div>
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: '6px', marginBottom: '16px' }}>
                {Array.isArray(activeCenter.facilities) ? activeCenter.facilities.map((fac, idx) => (
                  <span key={idx} style={{ backgroundColor: '#f1f5f9', color: '#334155', padding: '4px 10px', borderRadius: '16px', fontSize: '11px', fontWeight: '600', border: '1px solid #e2e8f0' }}>
                    {fac}
                  </span>
                )) : (
                  <span style={{ fontSize: '12px', color: '#94a3b8' }}>Standard Relief Facilities</span>
                )}
              </div>

              <button style={styles.outlineBtn} onClick={() => openEditModal(activeCenter)}>
                <Edit size={14} />
                <span>Edit Center Details</span>
              </button>
            </div>
          ) : (
            <div style={styles.detailsCard}>
              <div style={styles.emptyState}>Select an evacuation center to view details</div>
            </div>
          )}
        </div>
      </div>

      {/* DRAWER MODAL FOR ADDING / EDITING CENTER */}
      {isDrawerOpen && (
        <div style={styles.overlay}>
          <div style={styles.drawer}>
            <div style={styles.drawerHeader}>
              <h3 style={{ margin: 0, fontSize: '16px', fontWeight: '600', color: '#0f172a' }}>
                {isEditing ? 'Edit Evacuation Center' : 'Add New Evacuation Center'}
              </h3>
              <button 
                onClick={() => setIsDrawerOpen(false)}
                style={{ background: 'none', border: 'none', cursor: 'pointer', color: '#64748b' }}
              >
                <X size={18} />
              </button>
            </div>

            <div style={styles.drawerBody}>
              <div style={styles.formGroup}>
                <label style={{ fontSize: '12px', fontWeight: '500', color: '#64748b' }}>Shelter Name</label>
                <input 
                  type="text" 
                  placeholder="e.g. Malanday Elementary School" 
                  style={styles.input} 
                  value={formData.name}
                  onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                />
              </div>

              <div style={styles.formGroup}>
                <label style={{ fontSize: '12px', fontWeight: '500', color: '#64748b' }}>Barangay Location</label>
                <input 
                  type="text" 
                  placeholder="e.g. Malanday" 
                  style={styles.input} 
                  value={formData.barangay}
                  onChange={(e) => setFormData({ ...formData, barangay: e.target.value })}
                />
              </div>

              <div style={styles.formGroup}>
                <label style={{ fontSize: '12px', fontWeight: '500', color: '#64748b' }}>Maximum Capacity (Evacuees)</label>
                <input 
                  type="number" 
                  placeholder="e.g. 1200" 
                  style={styles.input} 
                  value={formData.capacity}
                  onChange={(e) => setFormData({ ...formData, capacity: e.target.value })}
                />
              </div>

              <div style={styles.formGroup}>
                <label style={{ fontSize: '12px', fontWeight: '500', color: '#64748b' }}>Current Evacuees Count</label>
                <input 
                  type="number" 
                  placeholder="0" 
                  style={styles.input} 
                  value={formData.currentEvacuees}
                  onChange={(e) => setFormData({ ...formData, currentEvacuees: e.target.value })}
                />
              </div>

              <div style={styles.formGroup}>
                <label style={{ fontSize: '12px', fontWeight: '500', color: '#64748b' }}>Operating Status</label>
                <select 
                  style={styles.input} 
                  value={formData.status}
                  onChange={(e) => setFormData({ ...formData, status: e.target.value })}
                >
                  <option value="Open">Open</option>
                  <option value="Standby">Standby</option>
                  <option value="Full">Full</option>
                </select>
              </div>

              <div style={styles.formGroup}>
                <label style={{ fontSize: '12px', fontWeight: '500', color: '#64748b' }}>Officer In Charge</label>
                <input 
                  type="text" 
                  placeholder="e.g. Captain Roberto Santos" 
                  style={styles.input} 
                  value={formData.headOfficer}
                  onChange={(e) => setFormData({ ...formData, headOfficer: e.target.value })}
                />
              </div>

              <div style={styles.formGroup}>
                <label style={{ fontSize: '12px', fontWeight: '500', color: '#64748b' }}>Contact Hotline Number</label>
                <input 
                  type="text" 
                  placeholder="e.g. 0917-555-0192" 
                  style={styles.input} 
                  value={formData.contact}
                  onChange={(e) => setFormData({ ...formData, contact: e.target.value })}
                />
              </div>

              <div style={styles.formGroup}>
                <label style={{ fontSize: '12px', fontWeight: '500', color: '#64748b' }}>Facilities & Amenities (Comma separated)</label>
                <textarea 
                  rows="3"
                  placeholder="e.g. Medical Station, Generator, Clean Water, Modular Tents" 
                  style={{ ...styles.input, resize: 'vertical' }} 
                  value={formData.facilities}
                  onChange={(e) => setFormData({ ...formData, facilities: e.target.value })}
                />
              </div>
            </div>

            <div style={styles.drawerFooter}>
              <button 
                onClick={() => setIsDrawerOpen(false)}
                style={{ padding: '8px 16px', borderRadius: '6px', border: '1px solid #cbd5e1', backgroundColor: '#fff', cursor: 'pointer', fontSize: '13px', color: '#334155' }}
              >
                Cancel
              </button>
              <button 
                onClick={handleSave}
                style={{ padding: '8px 16px', borderRadius: '6px', border: 'none', backgroundColor: '#0d9488', color: '#fff', cursor: 'pointer', fontSize: '13px', fontWeight: '600' }}
              >
                {isEditing ? 'Save Changes' : 'Save Evacuation Center'}
              </button>
            </div>
          </div>
        </div>
      )}

    </div>
  );
}
