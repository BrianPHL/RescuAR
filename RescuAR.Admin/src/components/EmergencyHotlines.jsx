import React, { useState, useEffect } from 'react';
import {
  Phone,
  Plus,
  X,
  Edit,
  Archive,
  RefreshCw,
  ChevronLeft,
  ChevronRight
} from 'lucide-react';
import { supabase } from '../supabaseClient';

export default function EmergencyHotlines() {
  const [searchTerm, setSearchTerm] = useState('');
  const [filterCategory, setFilterCategory] = useState('');
  const [selectedHotline, setSelectedHotline] = useState(null);
  const [isDrawerOpen, setIsDrawerOpen] = useState(false);
  const [loading, setLoading] = useState(true);

  const [hotlines, setHotlines] = useState([]);

  const [formData, setFormData] = useState({
    agency: '',
    category: '',
    primaryNumber: '',
    alternativeNumber: '',
    email: '',
    availability: '',
    coverage: ''
  });

  const fetchHotlines = async () => {
    setLoading(true);
    try {
      const { data, error } = await supabase
        .from('emergency_hotlines')
        .select('*')
        .order('created_at', { ascending: true });

      if (error) {
        console.warn('Supabase fetch error:', error.message);
      } else if (data) {
        const mapped = data.map(item => ({
          id: item.id,
          agency: item.agency,
          number: item.primary_number,
          primaryNumber: item.primary_number,
          category: item.category,
          availability: item.availability || '24/7',
          alternativeNumber: item.alternative_number || 'N/A',
          email: item.email || 'N/A',
          contactPerson: item.contact_person || 'N/A',
          contactNumber: item.contact_number || 'N/A',
          coverage: item.coverage || 'Citywide'
        }));
        setHotlines(mapped);
        if (mapped.length > 0 && !selectedHotline) {
          setSelectedHotline(mapped[0]);
        }
      }
    } catch (err) {
      console.warn('Supabase client error:', err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchHotlines();

    const channel = supabase
      .channel('hotlines-db-changes')
      .on('postgres_changes', { event: '*', schema: 'public', table: 'emergency_hotlines' }, () => {
        fetchHotlines();
      })
      .subscribe();

    return () => {
      supabase.removeChannel(channel);
    };
  }, []);

  const filteredHotlines = hotlines.filter(h => {
    const matchesSearch = h.agency.toLowerCase().includes(searchTerm.toLowerCase()) ||
      h.number.includes(searchTerm);
    const matchesCategory = filterCategory ? h.category === filterCategory : true;
    return matchesSearch && matchesCategory;
  });

  const handleSaveNew = async () => {
    const payload = {
      agency: formData.agency,
      primary_number: formData.primaryNumber,
      alternative_number: formData.alternativeNumber || null,
      category: formData.category || 'Uncategorized',
      availability: formData.availability || '24/7',
      email: formData.email || null,
      coverage: formData.coverage || 'Citywide'
    };

    const { error } = await supabase.from('emergency_hotlines').insert([payload]);
    if (error) {
      alert('Error adding hotline: ' + error.message);
    } else {
      fetchHotlines();
      setIsDrawerOpen(false);
      setFormData({ agency: '', category: '', primaryNumber: '', alternativeNumber: '', email: '', availability: '', coverage: '' });
    }
  };

  // Reusable inline style objects
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

    // Details styles
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

  return (
    <div className="main-view" style={{ backgroundColor: '#f1f5f9', minHeight: '100vh', padding: '30px' }}>

      {/* HEADER SECTION */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '24px' }}>
        <div>
          <h1 style={{ fontSize: '24px', fontWeight: '700', color: '#0f172a', margin: '0 0 4px 0' }}>Emergency Hotlines</h1>
          <span style={{ fontSize: '13px', color: '#64748b' }}>Last updated: June 18, 2026 • 08:42 AM</span>
        </div>
        <button style={{ ...styles.pageBtn, padding: '8px 16px', fontWeight: '600', color: '#334155', gap: '8px' }}>
          <RefreshCw size={14} /> Refresh
        </button>
      </div>

      {/* CONTENT LAYOUT */}
      <div style={styles.panelContainer}>

        {/* LEFT PANEL: LIST OF HOTLINES */}
        <div style={styles.leftPanel}>
          <div style={styles.headerFlex}>
            <h2 style={{ fontSize: '16px', fontWeight: '600', color: '#0f172a', margin: 0 }}>List of Emergency Hotlines</h2>
            <button style={styles.addButton} onClick={() => setIsDrawerOpen(true)}>
              <Plus size={16} /> Add New Hotline
            </button>
          </div>

          <div style={styles.controlsFlex}>
            <input
              type="text"
              style={styles.searchInput}
              placeholder="Search for an emergency hotline..."
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
            />
            <select style={styles.selectInput} value={filterCategory} onChange={(e) => setFilterCategory(e.target.value)}>
              <option value="">Category...</option>
              <option value="Marikina DRRMO">Marikina DRRMO</option>
              <option value="PNP">PNP</option>
              <option value="BFP">BFP</option>
            </select>
          </div>

          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr>
                <th style={styles.tableHeader}>Agency</th>
                <th style={styles.tableHeader}>Number</th>
                <th style={styles.tableHeader}>Category</th>
                <th style={styles.tableHeader}>Coverage</th>
                <th style={{ ...styles.tableHeader, textAlign: 'right' }}>Availability</th>
              </tr>
            </thead>
            <tbody>
              {filteredHotlines.map((h, i) => (
                <tr
                  key={i}
                  style={{ ...styles.tableRow, backgroundColor: selectedHotline?.id === h.id ? '#f8fafc' : '#fff' }}
                  onClick={() => setSelectedHotline(h)}
                >
                  <td style={styles.tableCell}>{h.agency}</td>
                  <td style={styles.tableCell}>{h.number}</td>
                  <td style={styles.tableCell}>{h.category}</td>
                  <td style={styles.tableCell}>{h.coverage || 'N/A'}</td>
                  <td style={{ ...styles.tableCell, textAlign: 'right' }}>{h.availability}</td>
                </tr>
              ))}
              {filteredHotlines.length === 0 && (
                <tr>
                  <td colSpan="5" style={{ textAlign: 'center', padding: '30px', color: '#94a3b8', fontSize: '13px' }}>
                    No hotlines found.
                  </td>
                </tr>
              )}
            </tbody>
          </table>

          <div style={styles.pagination}>
            <span>1 of {filteredHotlines.length || 1} record</span>
            <div style={styles.pageControls}>
              <button style={styles.pageBtn}><ChevronLeft size={14} /></button>
              <button style={{ ...styles.pageBtn, backgroundColor: '#f1f5f9' }}>1</button>
              <button style={styles.pageBtn}><ChevronRight size={14} /></button>
            </div>
          </div>
        </div>

        {/* RIGHT PANEL: DETAILS */}
        <div style={styles.rightPanel}>
          <h2 style={{ fontSize: '15px', fontWeight: '600', color: '#0f172a', margin: '0 0 16px 0' }}>
            Emergency Hotline Details
          </h2>

          {selectedHotline ? (
            <div style={styles.detailsCard}>
              <div style={{ ...styles.sectionLabel, marginTop: 0 }}>Agency Information</div>
              <div style={styles.rowPair}>
                <span style={styles.label}>Agency</span>
                <span style={styles.val}>{selectedHotline.agency}</span>
              </div>
              <div style={styles.rowPair}>
                <span style={styles.label}>Category</span>
                <span style={styles.val}>{selectedHotline.category}</span>
              </div>

              <div style={styles.sectionLabel}>Contact Information</div>
              <div style={styles.rowPair}>
                <span style={styles.label}>Primary Number</span>
                <span style={styles.val}>{selectedHotline.primaryNumber}</span>
              </div>
              <div style={styles.rowPair}>
                <span style={styles.label}>Alternative Number</span>
                <span style={styles.val}>{selectedHotline.alternativeNumber}</span>
              </div>
              <div style={styles.rowPair}>
                <span style={styles.label}>Email Address</span>
                <span style={styles.val}>{selectedHotline.email}</span>
              </div>

              <div style={styles.sectionLabel}>Contact Person Details</div>
              <div style={styles.rowPair}>
                <span style={styles.label}>Contact Person</span>
                <span style={styles.val}>{selectedHotline.contactPerson}</span>
              </div>
              <div style={styles.rowPair}>
                <span style={styles.label}>Contact Number</span>
                <span style={styles.val}>{selectedHotline.contactNumber}</span>
              </div>

              <div style={{ margin: '16px 0', borderBottom: '1px solid #f1f5f9' }}></div>

              <div style={styles.rowPair}>
                <span style={styles.label}>Availability</span>
                <span style={styles.val}>{selectedHotline.availability}</span>
              </div>
              <div style={styles.rowPair}>
                <span style={styles.label}>Coverage</span>
                <span style={styles.val}>{selectedHotline.coverage}</span>
              </div>

              <div style={{ marginTop: '24px' }}>
                <button style={styles.outlineBtn}><Edit size={14} /> Edit</button>
                <button style={styles.outlineBtn}><Archive size={14} /> Archive</button>
              </div>
            </div>
          ) : (
            <div style={styles.emptyState}>
              Select an emergency hotline first to<br />view details.
            </div>
          )}
        </div>
      </div>

      {/* ADD NEW HOTLINE DRAWER */}
      {isDrawerOpen && (
        <div style={styles.overlay}>
          <div style={styles.drawer}>
            <div style={styles.drawerHeader}>
              <h2 style={{ fontSize: '16px', fontWeight: '700', margin: 0 }}>Add New Hotline</h2>
              <button
                onClick={() => setIsDrawerOpen(false)}
                style={{ background: 'none', border: '1px solid #e2e8f0', borderRadius: '4px', cursor: 'pointer', padding: '4px' }}
              >
                <X size={16} />
              </button>
            </div>

            <div style={styles.drawerBody}>
              <div style={styles.formGroup}>
                <label style={{ fontSize: '12px', fontWeight: '500', color: '#334155' }}>Agency</label>
                <input style={styles.input} placeholder="Agency Name..." value={formData.agency} onChange={e => setFormData({ ...formData, agency: e.target.value })} />
              </div>
              <div style={styles.formGroup}>
                <label style={{ fontSize: '12px', fontWeight: '500', color: '#334155' }}>Category</label>
                <select style={styles.input} value={formData.category} onChange={e => setFormData({ ...formData, category: e.target.value })}>
                  <option value="">Category...</option>
                  <option value="Marikina DRRMO">Marikina DRRMO</option>
                  <option value="PNP">PNP</option>
                  <option value="BFP">BFP</option>
                  <option value="Hospital">Hospital</option>
                </select>
              </div>
              <div style={styles.formGroup}>
                <label style={{ fontSize: '12px', fontWeight: '500', color: '#334155' }}>Primary Number</label>
                <input style={styles.input} placeholder="Hotline Primary Number..." value={formData.primaryNumber} onChange={e => setFormData({ ...formData, primaryNumber: e.target.value })} />
              </div>
              <div style={styles.formGroup}>
                <label style={{ fontSize: '12px', fontWeight: '500', color: '#334155' }}>Alternative Number</label>
                <input style={styles.input} placeholder="Hotline Alternative Number..." value={formData.alternativeNumber} onChange={e => setFormData({ ...formData, alternativeNumber: e.target.value })} />
              </div>
              <div style={styles.formGroup}>
                <label style={{ fontSize: '12px', fontWeight: '500', color: '#334155' }}>Email Address</label>
                <input style={styles.input} placeholder="Agency Email Address..." value={formData.email} onChange={e => setFormData({ ...formData, email: e.target.value })} />
              </div>
              <div style={styles.formGroup}>
                <label style={{ fontSize: '12px', fontWeight: '500', color: '#334155' }}>Availability</label>
                <select style={styles.input} value={formData.availability} onChange={e => setFormData({ ...formData, availability: e.target.value })}>
                  <option value="">Availability...</option>
                  <option value="24/7">24/7</option>
                  <option value="Office Hours">Office Hours</option>
                </select>
              </div>
              <div style={styles.formGroup}>
                <label style={{ fontSize: '12px', fontWeight: '500', color: '#334155' }}>Coverage</label>
                <select style={styles.input} value={formData.coverage} onChange={e => setFormData({ ...formData, coverage: e.target.value })}>
                  <option value="">Coverage...</option>
                  <option value="Citywide">Citywide</option>
                  <option value="Specific Barangays">Specific Barangays</option>
                </select>
              </div>
            </div>

            <div style={styles.drawerFooter}>
              <button
                onClick={() => setIsDrawerOpen(false)}
                style={{ ...styles.pageBtn, padding: '8px 16px', fontWeight: '600' }}
              >
                Close
              </button>
              <button onClick={handleSaveNew} style={styles.addButton}>
                <Plus size={16} /> Add New Hotline
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
