import React, { useState } from 'react';
import { 
  Shield, 
  Users, 
  MapPin, 
  Phone, 
  Plus, 
  Search, 
  Filter, 
  CheckCircle2, 
  AlertTriangle,
  RefreshCw,
  Edit,
  X
} from 'lucide-react';

export default function EvacuationCenters() {
  const [searchTerm, setSearchTerm] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [isDrawerOpen, setIsDrawerOpen] = useState(false);

  const [centers, setCenters] = useState([
    {
      id: 1,
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
      id: 2,
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
      id: 3,
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
      id: 4,
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
      id: 5,
      name: 'Marikina Sports Center',
      barangay: 'Sto. Niño',
      capacity: 3500,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'MDRRMO Relief Team Alpha',
      contact: '0917-809-5141',
      facilities: ['Major Relief Hub', 'Medical Station', 'Helipad', 'Full Kitchen']
    }
  ]);

  const [formData, setFormData] = useState({
    name: '',
    barangay: '',
    capacity: '',
    headOfficer: '',
    contact: ''
  });

  const filteredCenters = centers.filter(c => {
    const matchesSearch = c.name.toLowerCase().includes(searchTerm.toLowerCase()) ||
                          c.barangay.toLowerCase().includes(searchTerm.toLowerCase());
    const matchesStatus = statusFilter ? c.status === statusFilter : true;
    return matchesSearch && matchesStatus;
  });

  const handleAddCenter = () => {
    if (!formData.name || !formData.barangay) return;
    const newCenter = {
      id: Date.now(),
      name: formData.name,
      barangay: formData.barangay,
      capacity: parseInt(formData.capacity) || 500,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: formData.headOfficer || 'Unassigned',
      contact: formData.contact || 'N/A',
      facilities: ['Clean Water', 'Restrooms']
    };
    setCenters([...centers, newCenter]);
    setIsDrawerOpen(false);
    setFormData({ name: '', barangay: '', capacity: '', headOfficer: '', contact: '' });
  };

  const getStatusBadge = (status) => {
    switch(status) {
      case 'Open':
        return { bg: '#ecfdf5', color: '#059669', label: 'Active Open' };
      case 'Standby':
        return { bg: '#eff6ff', color: '#3b82f6', label: 'Standby Ready' };
      case 'Full':
        return { bg: '#fef2f2', color: '#dc2626', label: 'At Max Capacity' };
      default:
        return { bg: '#f1f5f9', color: '#64748b', label: status };
    }
  };

  return (
    <div className="main-view" style={{ backgroundColor: '#f8fafc', minHeight: '100vh', padding: '24px' }}>
      
      {/* HEADER SECTION */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '24px' }}>
        <div>
          <h1 style={{ fontSize: '24px', fontWeight: '800', color: '#0f172a', margin: '0 0 4px 0', letterSpacing: '-0.02em' }}>
            Evacuation Centers
          </h1>
          <span style={{ fontSize: '13px', color: '#64748b', fontWeight: '500' }}>
            Monitor shelter capacity, occupancy rates, and assigned LGU relief officers across Marikina
          </span>
        </div>

        <button 
          onClick={() => setIsDrawerOpen(true)}
          style={{ 
            display: 'flex', 
            alignItems: 'center', 
            gap: '8px', 
            padding: '10px 18px',
            backgroundColor: '#0d9488',
            color: '#fff',
            border: 'none',
            borderRadius: '8px',
            fontWeight: '600',
            fontSize: '13px',
            cursor: 'pointer',
            boxShadow: '0 2px 4px rgba(13, 148, 136, 0.2)'
          }}
        >
          <Plus size={16} /> Add Evacuation Center
        </button>
      </div>

      {/* FILTER & SEARCH */}
      <div style={{ 
        backgroundColor: '#ffffff', 
        borderRadius: '12px', 
        padding: '16px 20px', 
        marginBottom: '24px',
        border: '1px solid #e2e8f0',
        display: 'flex',
        justify: 'space-between',
        alignItems: 'center',
        gap: '16px'
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flex: 1, backgroundColor: '#f1f5f9', padding: '8px 14px', borderRadius: '8px' }}>
          <Search size={16} color="#64748b" />
          <input 
            type="text" 
            placeholder="Search by shelter name or barangay..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            style={{ border: 'none', background: 'transparent', outline: 'none', fontSize: '13px', width: '100%', color: '#0f172a' }}
          />
        </div>

        <select 
          value={statusFilter} 
          onChange={(e) => setStatusFilter(e.target.value)}
          style={{ 
            padding: '8px 14px', 
            borderRadius: '8px', 
            border: '1px solid #cbd5e1', 
            fontSize: '13px', 
            backgroundColor: '#fff',
            color: '#334155',
            outline: 'none',
            cursor: 'pointer'
          }}
        >
          <option value="">All Statuses</option>
          <option value="Open">Open</option>
          <option value="Standby">Standby</option>
          <option value="Full">Full</option>
        </select>
      </div>

      {/* CARDS GRID */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(320px, 1fr))', gap: '20px' }}>
        {filteredCenters.map((center) => {
          const badge = getStatusBadge(center.status);
          const occupancyPercent = Math.min(100, Math.round((center.currentEvacuees / center.capacity) * 100));

          return (
            <div 
              key={center.id}
              style={{
                backgroundColor: '#ffffff',
                borderRadius: '12px',
                border: '1px solid #e2e8f0',
                boxShadow: '0 1px 3px rgba(0,0,0,0.05)',
                padding: '20px',
                display: 'flex',
                flexDirection: 'column',
                justify: 'space-between'
              }}
            >
              <div>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px' }}>
                  <span style={{ 
                    backgroundColor: badge.bg, 
                    color: badge.color, 
                    padding: '4px 10px', 
                    borderRadius: '20px', 
                    fontSize: '11px', 
                    fontWeight: '700' 
                  }}>
                    {badge.label}
                  </span>

                  <span style={{ fontSize: '12px', color: '#64748b', fontWeight: '600', display: 'flex', alignItems: 'center', gap: '4px' }}>
                    <MapPin size={14} color="#0d9488" /> {center.barangay}
                  </span>
                </div>

                <h3 style={{ fontSize: '16px', fontWeight: '700', color: '#0f172a', marginBottom: '16px' }}>
                  {center.name}
                </h3>

                {/* Occupancy Progress Bar */}
                <div style={{ marginBottom: '16px' }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '12px', fontWeight: '600', marginBottom: '6px' }}>
                    <span style={{ color: '#64748b' }}>Occupancy</span>
                    <span style={{ color: '#0f172a' }}>{center.currentEvacuees} / {center.capacity} evacuees ({occupancyPercent}%)</span>
                  </div>
                  <div style={{ width: '100%', backgroundColor: '#f1f5f9', height: '8px', borderRadius: '4px', overflow: 'hidden' }}>
                    <div style={{ 
                      width: `${occupancyPercent}%`, 
                      backgroundColor: occupancyPercent > 80 ? '#ef4444' : '#0d9488', 
                      height: '100%', 
                      borderRadius: '4px',
                      transition: 'width 0.3s ease'
                    }} />
                  </div>
                </div>

                <div style={{ fontSize: '12px', color: '#475569', marginBottom: '12px' }}>
                  <div style={{ marginBottom: '4px' }}><strong>Officer in Charge:</strong> {center.headOfficer}</div>
                  <div><strong>Hotline Contact:</strong> {center.contact}</div>
                </div>

                {/* Facilities Tags */}
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: '6px', marginTop: '12px' }}>
                  {center.facilities.map((fac, idx) => (
                    <span key={idx} style={{ backgroundColor: '#f1f5f9', color: '#475569', padding: '2px 8px', borderRadius: '4px', fontSize: '11px', fontWeight: '500' }}>
                      {fac}
                    </span>
                  ))}
                </div>
              </div>

              <div style={{ marginTop: '20px', paddingTop: '14px', borderTop: '1px solid #f1f5f9', display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
                <button style={{ padding: '6px 12px', border: '1px solid #cbd5e1', borderRadius: '6px', backgroundColor: '#fff', fontSize: '12px', fontWeight: '600', color: '#334155', cursor: 'pointer' }}>
                  Edit Details
                </button>
              </div>
            </div>
          );
        })}
      </div>

      {/* DRAWER MODAL FOR ADDING CENTER */}
      {isDrawerOpen && (
        <div style={{ position: 'fixed', top: 0, left: 0, right: 0, bottom: 0, backgroundColor: 'rgba(0,0,0,0.4)', zIndex: 999, display: 'flex', justifyContent: 'flex-end' }}>
          <div style={{ width: '400px', backgroundColor: '#fff', height: '100%', padding: '24px', display: 'flex', flexDirection: 'column', gap: '16px', boxShadow: '-4px 0 15px rgba(0,0,0,0.1)' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', borderBottom: '1px solid #e2e8f0', paddingBottom: '16px' }}>
              <h2 style={{ fontSize: '16px', fontWeight: '700', margin: 0 }}>Add Evacuation Center</h2>
              <button onClick={() => setIsDrawerOpen(false)} style={{ background: 'none', border: 'none', cursor: 'pointer' }}><X size={18} /></button>
            </div>

            <div style={{ display: 'flex', flexDirection: 'column', gap: '12px', flex: 1 }}>
              <div>
                <label style={{ fontSize: '12px', fontWeight: '600', color: '#334155' }}>Center Name</label>
                <input type="text" placeholder="e.g. Malanday Elementary School" value={formData.name} onChange={e => setFormData({...formData, name: e.target.value})} style={{ width: '100%', padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: '6px', marginTop: '4px', fontSize: '13px' }} />
              </div>
              <div>
                <label style={{ fontSize: '12px', fontWeight: '600', color: '#334155' }}>Barangay Location</label>
                <input type="text" placeholder="e.g. Tumana" value={formData.barangay} onChange={e => setFormData({...formData, barangay: e.target.value})} style={{ width: '100%', padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: '6px', marginTop: '4px', fontSize: '13px' }} />
              </div>
              <div>
                <label style={{ fontSize: '12px', fontWeight: '600', color: '#334155' }}>Maximum Capacity (Evacuees)</label>
                <input type="number" placeholder="1000" value={formData.capacity} onChange={e => setFormData({...formData, capacity: e.target.value})} style={{ width: '100%', padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: '6px', marginTop: '4px', fontSize: '13px' }} />
              </div>
              <div>
                <label style={{ fontSize: '12px', fontWeight: '600', color: '#334155' }}>Officer In Charge</label>
                <input type="text" placeholder="e.g. Capt. Juan Dela Cruz" value={formData.headOfficer} onChange={e => setFormData({...formData, headOfficer: e.target.value})} style={{ width: '100%', padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: '6px', marginTop: '4px', fontSize: '13px' }} />
              </div>
              <div>
                <label style={{ fontSize: '12px', fontWeight: '600', color: '#334155' }}>Contact Number</label>
                <input type="text" placeholder="0917-xxx-xxxx" value={formData.contact} onChange={e => setFormData({...formData, contact: e.target.value})} style={{ width: '100%', padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: '6px', marginTop: '4px', fontSize: '13px' }} />
              </div>
            </div>

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '12px', paddingTop: '16px', borderTop: '1px solid #e2e8f0' }}>
              <button onClick={() => setIsDrawerOpen(false)} style={{ padding: '8px 16px', border: '1px solid #cbd5e1', borderRadius: '6px', backgroundColor: '#fff', cursor: 'pointer', fontSize: '13px', fontWeight: '600' }}>Cancel</button>
              <button onClick={handleAddCenter} style={{ padding: '8px 16px', border: 'none', borderRadius: '6px', backgroundColor: '#0d9488', color: '#fff', cursor: 'pointer', fontSize: '13px', fontWeight: '600' }}>Save Evacuation Center</button>
            </div>
          </div>
        </div>
      )}

    </div>
  );
}
