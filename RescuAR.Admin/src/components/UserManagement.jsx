import React, { useState, useEffect } from 'react';
import {
  ShieldOff,
  ShieldCheck,
  RefreshCw,
  ChevronLeft,
  ChevronRight,
  UserCog,
  Shield,
  Mail,
  Calendar,
  Clock,
  Hash,
  Phone,
  MapPin,
  Users,
  Edit2,
  Ban,
  Trash2,
  MessageSquare,
  AlertTriangle,
  X,
  AlertCircle
} from 'lucide-react';
import { supabase } from '../supabaseClient';

const ITEMS_PER_PAGE = 8;

export default function UserManagement() {
  const [searchTerm, setSearchTerm] = useState('');
  const [filterRole, setFilterRole] = useState('');
  const [selectedUser, setSelectedUser] = useState(null);
  const [loading, setLoading] = useState(true);
  const [currentPage, setCurrentPage] = useState(1);
  const [lastUpdated, setLastUpdated] = useState(new Date().toLocaleString());

  const [users, setUsers] = useState([]);
  
  // Action Modals State
  const [showEditModal, setShowEditModal] = useState(false);
  const [showDeleteModal, setShowDeleteModal] = useState(false);
  const [showNotifyModal, setShowNotifyModal] = useState(false);
  const [editFormData, setEditFormData] = useState({});
  const [notifyMsg, setNotifyMsg] = useState('');
  const [actionLoading, setActionLoading] = useState(false);

  // ─── Fetch all users + cross-reference admin_roles ───────────────
  const fetchUsers = async () => {
    setLoading(true);
    try {
      // Call the RPC function that bypasses RLS to return all users with their roles
      const { data, error } = await supabase.rpc('get_all_users_with_roles');

      if (error) {
        console.warn('Supabase RPC error:', error.message);
        setLoading(false);
        return;
      }

      if (data) {
        const mapped = data.map(item => ({
          id: item.id,
          first_name: item.first_name || '',
          middle_name: item.middle_name || '',
          last_name: item.last_name || '',
          full_name: [item.first_name, item.middle_name, item.last_name].filter(Boolean).join(' ') || item.username || item.email || 'Unnamed User',
          username: item.username || '—',
          email: item.email || '—',
          phone_number: item.phone_number || '—',
          address: item.address || '—',
          avatar_url: item.avatar_url || null,
          role: item.role || 'user',
          status: item.status || 'active',
          created_at: item.created_at || null,
          blood_type: item.blood_type || '—',
          allergies: item.allergies || '—',
          emergency_contact1_name: item.emergency_contact1_name || '—',
          emergency_contact1_phone: item.emergency_contact1_phone || '—'
        }));
        setUsers(mapped);
        if (mapped.length > 0 && !selectedUser) {
          setSelectedUser(mapped[0]);
        }
      }
      setLastUpdated(new Date().toLocaleString());
    } catch (err) {
      console.warn('Supabase client error:', err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchUsers();

    // Subscribe to real-time changes on the users table
    const usersChannel = supabase
      .channel('users-db-changes')
      .on('postgres_changes', { event: '*', schema: 'public', table: 'users' }, () => {
        fetchUsers();
      })
      .subscribe();

    // Subscribe to admin_roles changes too (role may change)
    const adminChannel = supabase
      .channel('admin-roles-changes')
      .on('postgres_changes', { event: '*', schema: 'public', table: 'admin_roles' }, () => {
        fetchUsers();
      })
      .subscribe();

    return () => {
      supabase.removeChannel(usersChannel);
      supabase.removeChannel(adminChannel);
    };
  }, []);

  // ─── Action Handlers ────────────────────────────────────────────
  const handleSuspendToggle = async () => {
    if (!selectedUser) return;
    const isSuspended = selectedUser.status === 'suspended';
    const newStatus = isSuspended ? 'active' : 'suspended';
    setActionLoading(true);
    const { error } = await supabase.rpc('admin_toggle_suspend_user', {
      target_user_id: selectedUser.id,
      new_status: newStatus
    });
    setActionLoading(false);
    if (error) {
      alert('Error suspending user: ' + error.message);
    } else {
      setSelectedUser({ ...selectedUser, status: newStatus });
      fetchUsers();
    }
  };

  const handleDelete = async () => {
    if (!selectedUser) return;
    setActionLoading(true);
    const { error } = await supabase.rpc('admin_delete_user', {
      target_user_id: selectedUser.id
    });
    setActionLoading(false);
    if (error) {
      alert('Error deleting user: ' + error.message);
    } else {
      setShowDeleteModal(false);
      setSelectedUser(null);
      fetchUsers();
    }
  };

  const handleEditSubmit = async (e) => {
    e.preventDefault();
    setActionLoading(true);
    const { error } = await supabase.rpc('admin_update_user', {
      target_user_id: selectedUser.id,
      update_data: editFormData
    });
    setActionLoading(false);
    if (error) {
      alert('Error updating user: ' + error.message);
    } else {
      setShowEditModal(false);
      setSelectedUser({ ...selectedUser, ...editFormData });
      fetchUsers();
    }
  };

  const handleNotifySubmit = async (e) => {
    e.preventDefault();
    alert(`Notification sent to ${selectedUser.full_name}:\\n\\n${notifyMsg}`);
    setShowNotifyModal(false);
    setNotifyMsg('');
  };

  // ─── Filtering & Pagination ──────────────────────────────────────
  const filteredUsers = users.filter(u => {
    const matchesSearch =
      u.full_name.toLowerCase().includes(searchTerm.toLowerCase()) ||
      u.email.toLowerCase().includes(searchTerm.toLowerCase()) ||
      u.username.toLowerCase().includes(searchTerm.toLowerCase());
    const matchesRole = filterRole ? u.role === filterRole : true;
    return matchesSearch && matchesRole;
  });

  const totalPages = Math.max(1, Math.ceil(filteredUsers.length / ITEMS_PER_PAGE));
  const paginatedUsers = filteredUsers.slice(
    (currentPage - 1) * ITEMS_PER_PAGE,
    currentPage * ITEMS_PER_PAGE
  );

  // Reset to page 1 when filters change
  useEffect(() => {
    setCurrentPage(1);
  }, [searchTerm, filterRole]);

  // ─── Helper formatters ───────────────────────────────────────────
  const formatDate = (dateStr) => {
    if (!dateStr) return '—';
    return new Date(dateStr).toLocaleDateString('en-US', {
      year: 'numeric', month: 'short', day: 'numeric'
    });
  };

  const getRoleBadge = (role) => {
    const isAdmin = role === 'admin';
    return (
      <span style={{
        backgroundColor: isAdmin ? '#dbeafe' : '#f1f5f9',
        color: isAdmin ? '#1e40af' : '#475569',
        padding: '3px 10px',
        borderRadius: '12px',
        fontSize: '11px',
        fontWeight: '700',
        display: 'inline-flex',
        alignItems: 'center',
        gap: '4px',
        whiteSpace: 'nowrap'
      }}>
        {isAdmin ? <Shield size={11} /> : <Users size={11} />}
        {isAdmin ? 'Admin' : 'User'}
      </span>
    );
  };

  const isProfileIncomplete = (user) => {
    if (user.role === 'admin') return false;
    return !user.first_name || !user.last_name || user.phone_number === '—' || user.address === '—';
  };

  const getInitials = (user) => {
    if (user.first_name && user.last_name) {
      return (user.first_name[0] + user.last_name[0]).toUpperCase();
    }
    return user.full_name.substring(0, 2).toUpperCase();
  };

  // ─── Styles (matching EmergencyHotlines pattern) ─────────────────
  const styles = {
    panelContainer: { display: 'flex', gap: '20px', alignItems: 'flex-start' },
    leftPanel: { flex: '1', backgroundColor: '#fff', borderRadius: '8px', boxShadow: '0 1px 3px rgba(0,0,0,0.1)', overflow: 'hidden', minHeight: '600px', display: 'flex', flexDirection: 'column' },
    rightPanel: { width: '380px', backgroundColor: 'transparent', flexShrink: 0 },
    headerFlex: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '20px 20px 15px' },
    controlsFlex: { display: 'flex', gap: '10px', padding: '0 20px 15px' },
    searchInput: { flex: 1, padding: '8px 12px', borderRadius: '6px', border: '1px solid #e2e8f0', fontSize: '13px' },
    selectInput: { width: '130px', padding: '8px 12px', borderRadius: '6px', border: '1px solid #e2e8f0', fontSize: '13px', backgroundColor: '#fff' },
    tableHeader: { backgroundColor: '#f8fafc', padding: '12px 20px', borderBottom: '1px solid #e2e8f0', textAlign: 'left', fontSize: '12px', fontWeight: '600', color: '#64748b' },
    tableRow: { borderBottom: '1px solid #f1f5f9', cursor: 'pointer', transition: 'background 0.2s' },
    tableCell: { padding: '14px 20px', fontSize: '13px', color: '#334155' },
    pagination: { marginTop: 'auto', padding: '15px 20px', borderTop: '1px solid #e2e8f0', display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '12px', color: '#94a3b8' },
    pageControls: { display: 'flex', gap: '5px' },
    pageBtn: { padding: '4px 8px', border: '1px solid #e2e8f0', borderRadius: '4px', backgroundColor: '#fff', cursor: 'pointer', color: '#64748b', display: 'flex', alignItems: 'center' },

    // Details panel styles
    detailsCard: { backgroundColor: '#fff', borderRadius: '8px', boxShadow: '0 1px 3px rgba(0,0,0,0.1)', padding: '20px' },
    sectionLabel: { fontSize: '12px', fontWeight: '500', color: '#94a3b8', marginBottom: '12px', marginTop: '16px', textTransform: 'uppercase', letterSpacing: '0.5px' },
    rowPair: { display: 'flex', justifyContent: 'space-between', marginBottom: '10px', fontSize: '13px' },
    label: { color: '#64748b', display: 'flex', alignItems: 'center', gap: '6px' },
    val: { color: '#0f172a', fontWeight: '500', textAlign: 'right', maxWidth: '60%', wordBreak: 'break-word', lineHeight: '1.4' },
    emptyState: { display: 'flex', justifyContent: 'center', alignItems: 'center', height: '200px', fontSize: '14px', color: '#94a3b8', fontWeight: '500', textAlign: 'center' }
  };

  return (
    <div className="main-view" style={{ backgroundColor: '#f1f5f9', minHeight: '100vh', padding: '30px' }}>

      {/* HEADER SECTION */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '24px' }}>
        <div>
          <h1 style={{ fontSize: '24px', fontWeight: '700', color: '#0f172a', margin: '0 0 4px 0' }}>User Management</h1>
          <span style={{ fontSize: '13px', color: '#64748b' }}>View and manage RescuAR app users and administrators • Last updated: {lastUpdated}</span>
        </div>
        <button
          style={{ ...styles.pageBtn, padding: '8px 16px', fontWeight: '600', color: '#334155', gap: '8px' }}
          onClick={fetchUsers}
        >
          <RefreshCw size={14} /> Refresh
        </button>
      </div>

      {/* SUMMARY CARDS */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '16px', marginBottom: '24px' }}>
        {[
          { label: 'Total Users', value: users.length, icon: <UserCog size={18} />, color: '#0284c7', bg: '#e0f2fe' },
          { label: 'Admins', value: users.filter(u => u.role === 'admin').length, icon: <ShieldCheck size={18} />, color: '#7c3aed', bg: '#ede9fe' },
          { label: 'Regular Users', value: users.filter(u => u.role === 'user').length, icon: <Users size={18} />, color: '#16a34a', bg: '#dcfce7' }
        ].map((card, i) => (
          <div key={i} style={{
            backgroundColor: '#fff', borderRadius: '8px', padding: '18px 20px',
            boxShadow: '0 1px 3px rgba(0,0,0,0.1)', display: 'flex', alignItems: 'center', gap: '14px'
          }}>
            <div style={{
              width: '40px', height: '40px', borderRadius: '10px',
              backgroundColor: card.bg, color: card.color,
              display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0
            }}>
              {card.icon}
            </div>
            <div>
              <div style={{ fontSize: '22px', fontWeight: '700', color: '#0f172a' }}>{card.value}</div>
              <div style={{ fontSize: '12px', color: '#94a3b8', fontWeight: '500' }}>{card.label}</div>
            </div>
          </div>
        ))}
      </div>

      {/* CONTENT LAYOUT */}
      <div style={styles.panelContainer}>

        {/* LEFT PANEL: USER TABLE */}
        <div style={styles.leftPanel}>
          <div style={styles.headerFlex}>
            <h2 style={{ fontSize: '16px', fontWeight: '600', color: '#0f172a', margin: 0 }}>Registered Users</h2>
          </div>

          <div style={styles.controlsFlex}>
            <input
              type="text"
              style={styles.searchInput}
              placeholder="Search by name, username, or email..."
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
            />
            <select style={styles.selectInput} value={filterRole} onChange={(e) => setFilterRole(e.target.value)}>
              <option value="">All Roles</option>
              <option value="admin">Admin</option>
              <option value="user">User</option>
            </select>
          </div>

          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr>
                <th style={styles.tableHeader}>Name</th>
                <th style={styles.tableHeader}>Email</th>
                <th style={styles.tableHeader}>Phone</th>
                <th style={styles.tableHeader}>Role</th>
                <th style={{ ...styles.tableHeader, textAlign: 'right' }}>Registered</th>
              </tr>
            </thead>
            <tbody>
              {loading ? (
                <tr>
                  <td colSpan="5" style={{ textAlign: 'center', padding: '40px', color: '#94a3b8', fontSize: '13px' }}>
                    Loading users...
                  </td>
                </tr>
              ) : paginatedUsers.length === 0 ? (
                <tr>
                  <td colSpan="5" style={{ textAlign: 'center', padding: '40px', color: '#94a3b8', fontSize: '13px' }}>
                    No users found.
                  </td>
                </tr>
              ) : (
                paginatedUsers.map((u) => (
                  <tr
                    key={u.id}
                    style={{
                      ...styles.tableRow,
                      backgroundColor: selectedUser?.id === u.id ? '#f8fafc' : '#fff'
                    }}
                    onClick={() => setSelectedUser(u)}
                  >
                    <td style={{ ...styles.tableCell, fontWeight: '600' }}>
                      <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                        <div style={{
                          width: '32px', height: '32px', borderRadius: '8px',
                          backgroundColor: u.role === 'admin' ? '#ede9fe' : '#e0f2fe',
                          color: u.role === 'admin' ? '#7c3aed' : '#0284c7',
                          display: 'flex', alignItems: 'center', justifyContent: 'center',
                          fontSize: '12px', fontWeight: '700', flexShrink: 0
                        }}>
                          {getInitials(u)}
                        </div>
                        {u.full_name}
                        {isProfileIncomplete(u) && (
                          <AlertCircle size={14} color="#f59e0b" title="Incomplete Profile" style={{ flexShrink: 0 }} />
                        )}
                      </div>
                    </td>
                    <td style={{ ...styles.tableCell, color: '#64748b' }}>{u.email}</td>
                    <td style={{ ...styles.tableCell, color: '#64748b', fontFamily: 'monospace', fontSize: '12px' }}>{u.phone_number}</td>
                    <td style={styles.tableCell}>{getRoleBadge(u.role)}</td>
                    <td style={{ ...styles.tableCell, textAlign: 'right', color: '#94a3b8', fontSize: '12px' }}>
                      {formatDate(u.created_at)}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>

          <div style={styles.pagination}>
            <span>
              {filteredUsers.length === 0
                ? '0 records'
                : `${(currentPage - 1) * ITEMS_PER_PAGE + 1}–${Math.min(currentPage * ITEMS_PER_PAGE, filteredUsers.length)} of ${filteredUsers.length} records`
              }
            </span>
            <div style={styles.pageControls}>
              <button
                style={{ ...styles.pageBtn, opacity: currentPage <= 1 ? 0.4 : 1 }}
                disabled={currentPage <= 1}
                onClick={() => setCurrentPage(p => Math.max(1, p - 1))}
              >
                <ChevronLeft size={14} />
              </button>
              {Array.from({ length: totalPages }, (_, i) => i + 1).map(page => (
                <button
                  key={page}
                  style={{
                    ...styles.pageBtn,
                    backgroundColor: page === currentPage ? '#f1f5f9' : '#fff',
                    fontWeight: page === currentPage ? '600' : '400'
                  }}
                  onClick={() => setCurrentPage(page)}
                >
                  {page}
                </button>
              ))}
              <button
                style={{ ...styles.pageBtn, opacity: currentPage >= totalPages ? 0.4 : 1 }}
                disabled={currentPage >= totalPages}
                onClick={() => setCurrentPage(p => Math.min(totalPages, p + 1))}
              >
                <ChevronRight size={14} />
              </button>
            </div>
          </div>
        </div>

        {/* RIGHT PANEL: USER DETAILS */}
        <div style={styles.rightPanel}>
          <h2 style={{ fontSize: '15px', fontWeight: '600', color: '#0f172a', margin: '0 0 16px 0' }}>
            User Details
          </h2>

          {selectedUser ? (
            <div style={styles.detailsCard}>
              {/* Avatar & Name Header */}
              <div style={{ display: 'flex', alignItems: 'center', gap: '14px', marginBottom: '8px' }}>
                <div style={{
                  width: '48px', height: '48px', borderRadius: '12px',
                  backgroundColor: selectedUser.role === 'admin' ? '#ede9fe' : '#e0f2fe',
                  color: selectedUser.role === 'admin' ? '#7c3aed' : '#0284c7',
                  display: 'flex', alignItems: 'center', justifyContent: 'center',
                  fontSize: '18px', fontWeight: '700', flexShrink: 0
                }}>
                  {getInitials(selectedUser)}
                </div>
                <div>
                  <div style={{ fontSize: '15px', fontWeight: '700', color: '#0f172a' }}>{selectedUser.full_name}</div>
                  <div style={{ fontSize: '12px', color: '#94a3b8', display: 'flex', alignItems: 'center', gap: '6px' }}>
                    {getRoleBadge(selectedUser.role)}
                    {selectedUser.status === 'suspended' && (
                      <span style={{ backgroundColor: '#fee2e2', color: '#b91c1c', padding: '2px 6px', borderRadius: '4px', fontSize: '10px', fontWeight: '700', textTransform: 'uppercase' }}>Suspended</span>
                    )}
                  </div>
                </div>
              </div>

              {/* INCOMPLETE PROFILE WARNING */}
              {isProfileIncomplete(selectedUser) && (
                <div style={{ backgroundColor: '#fef3c7', color: '#b45309', padding: '10px 14px', borderRadius: '6px', fontSize: '12px', display: 'flex', alignItems: 'flex-start', gap: '10px', marginTop: '16px' }}>
                  <AlertCircle size={16} style={{ flexShrink: 0, marginTop: '2px' }} />
                  <div>
                    <strong style={{ display: 'block', marginBottom: '2px' }}>Incomplete Profile</strong>
                    This user has missing account information (e.g., missing name, phone, or address).
                  </div>
                </div>
              )}

              {/* ACTION BUTTONS */}
              {selectedUser.role !== 'admin' && (
                <div style={{ display: 'flex', gap: '8px', marginTop: '16px', marginBottom: '8px' }}>
                  <button
                    style={{ flex: 1, padding: '8px', border: '1px solid #e2e8f0', backgroundColor: '#fff', borderRadius: '6px', color: '#475569', fontSize: '12px', fontWeight: '500', cursor: 'pointer', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '6px' }}
                    onClick={() => { setEditFormData(selectedUser); setShowEditModal(true); }}
                  >
                    <Edit2 size={14} /> Edit
                  </button>
                  <button
                    style={{ flex: 1, padding: '8px', border: '1px solid #e2e8f0', backgroundColor: '#fff', borderRadius: '6px', color: selectedUser.status === 'suspended' ? '#16a34a' : '#ea580c', fontSize: '12px', fontWeight: '500', cursor: 'pointer', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '6px' }}
                    onClick={handleSuspendToggle}
                    disabled={actionLoading}
                  >
                    <Ban size={14} /> {selectedUser.status === 'suspended' ? 'Unsuspend' : 'Suspend'}
                  </button>
                  <button
                    style={{ flex: 1, padding: '8px', border: '1px solid #e2e8f0', backgroundColor: '#fff', borderRadius: '6px', color: '#0284c7', fontSize: '12px', fontWeight: '500', cursor: 'pointer', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '6px' }}
                    onClick={() => setShowNotifyModal(true)}
                  >
                    <MessageSquare size={14} /> Notify
                  </button>
                  <button
                    style={{ padding: '8px 12px', border: '1px solid #fecaca', backgroundColor: '#fef2f2', borderRadius: '6px', color: '#dc2626', cursor: 'pointer', display: 'flex', alignItems: 'center', justifyContent: 'center' }}
                    onClick={() => setShowDeleteModal(true)}
                  >
                    <Trash2 size={16} />
                  </button>
                </div>
              )}

              {/* Account Information */}
              <div style={{ ...styles.sectionLabel, marginTop: '20px' }}>Account Information</div>
              <div style={styles.rowPair}>
                <span style={styles.label}><Hash size={13} /> User ID</span>
                <span style={{ ...styles.val, fontSize: '11px', fontFamily: 'monospace' }}>
                  {selectedUser.id ? selectedUser.id.substring(0, 12) + '...' : '—'}
                </span>
              </div>
              <div style={styles.rowPair}>
                <span style={styles.label}><Mail size={13} /> Email</span>
                <span style={styles.val}>{selectedUser.email}</span>
              </div>
              <div style={styles.rowPair}>
                <span style={styles.label}><Phone size={13} /> Phone</span>
                <span style={{ ...styles.val, fontFamily: 'monospace' }}>{selectedUser.phone_number}</span>
              </div>
              <div style={styles.rowPair}>
                <span style={styles.label}><MapPin size={13} /> Address</span>
                <span style={styles.val}>{selectedUser.address}</span>
              </div>

              {/* Emergency Contact */}
              <div style={styles.sectionLabel}>Emergency Contact</div>
              <div style={styles.rowPair}>
                <span style={styles.label}>Contact Name</span>
                <span style={styles.val}>{selectedUser.emergency_contact1_name}</span>
              </div>
              <div style={styles.rowPair}>
                <span style={styles.label}>Contact Phone</span>
                <span style={{ ...styles.val, fontFamily: 'monospace' }}>{selectedUser.emergency_contact1_phone}</span>
              </div>

              {/* Medical Information */}
              <div style={styles.sectionLabel}>Medical Information</div>
              <div style={styles.rowPair}>
                <span style={styles.label}>Blood Type</span>
                <span style={styles.val}>{selectedUser.blood_type}</span>
              </div>
              <div style={styles.rowPair}>
                <span style={styles.label}>Allergies</span>
                <span style={styles.val}>{selectedUser.allergies}</span>
              </div>

              <div style={{ margin: '16px 0', borderBottom: '1px solid #f1f5f9' }} />

              {/* Registration Info */}
              <div style={styles.rowPair}>
                <span style={styles.label}><Calendar size={13} /> Registered</span>
                <span style={styles.val}>{formatDate(selectedUser.created_at)}</span>
              </div>
            </div>
          ) : (
            <div style={styles.emptyState}>
              Select a user from the list to<br />view their details.
            </div>
          )}
        </div>
      </div>

      {/* --- MODALS --- */}
      {showEditModal && (
        <div style={{ position: 'fixed', top: 0, left: 0, right: 0, bottom: 0, backgroundColor: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000 }}>
          <div style={{ backgroundColor: '#fff', padding: '24px', borderRadius: '12px', width: '400px', maxWidth: '90%' }}>
            <h3 style={{ margin: '0 0 16px 0', fontSize: '18px', display: 'flex', justifyContent: 'space-between' }}>
              Edit User Profile
              <X size={20} style={{ cursor: 'pointer', color: '#94a3b8' }} onClick={() => setShowEditModal(false)} />
            </h3>
            <form onSubmit={handleEditSubmit}>
              <div style={{ marginBottom: '12px' }}>
                <label style={{ display: 'block', fontSize: '12px', color: '#64748b', marginBottom: '4px' }}>First Name</label>
                <input style={{ width: '100%', boxSizing: 'border-box', padding: '8px 12px', borderRadius: '6px', border: '1px solid #e2e8f0', fontSize: '13px' }} value={editFormData.first_name || ''} onChange={e => setEditFormData({...editFormData, first_name: e.target.value})} />
              </div>
              <div style={{ marginBottom: '12px' }}>
                <label style={{ display: 'block', fontSize: '12px', color: '#64748b', marginBottom: '4px' }}>Last Name</label>
                <input style={{ width: '100%', boxSizing: 'border-box', padding: '8px 12px', borderRadius: '6px', border: '1px solid #e2e8f0', fontSize: '13px' }} value={editFormData.last_name || ''} onChange={e => setEditFormData({...editFormData, last_name: e.target.value})} />
              </div>
              <div style={{ marginBottom: '12px' }}>
                <label style={{ display: 'block', fontSize: '12px', color: '#64748b', marginBottom: '4px' }}>Phone Number</label>
                <input style={{ width: '100%', boxSizing: 'border-box', padding: '8px 12px', borderRadius: '6px', border: '1px solid #e2e8f0', fontSize: '13px' }} value={editFormData.phone_number || ''} onChange={e => setEditFormData({...editFormData, phone_number: e.target.value})} />
              </div>
              <div style={{ marginBottom: '20px' }}>
                <label style={{ display: 'block', fontSize: '12px', color: '#64748b', marginBottom: '4px' }}>Address</label>
                <input style={{ width: '100%', boxSizing: 'border-box', padding: '8px 12px', borderRadius: '6px', border: '1px solid #e2e8f0', fontSize: '13px' }} value={editFormData.address || ''} onChange={e => setEditFormData({...editFormData, address: e.target.value})} />
              </div>
              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px' }}>
                <button type="button" onClick={() => setShowEditModal(false)} style={{ padding: '8px 16px', borderRadius: '6px', border: '1px solid #e2e8f0', backgroundColor: '#fff', cursor: 'pointer' }}>Cancel</button>
                <button type="submit" disabled={actionLoading} style={{ padding: '8px 16px', borderRadius: '6px', border: 'none', backgroundColor: '#0ea5e9', color: '#fff', cursor: 'pointer', fontWeight: '600' }}>{actionLoading ? 'Saving...' : 'Save Changes'}</button>
              </div>
            </form>
          </div>
        </div>
      )}

      {showDeleteModal && (
        <div style={{ position: 'fixed', top: 0, left: 0, right: 0, bottom: 0, backgroundColor: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000 }}>
          <div style={{ backgroundColor: '#fff', padding: '24px', borderRadius: '12px', width: '380px', maxWidth: '90%', textAlign: 'center' }}>
            <AlertTriangle size={48} color="#ef4444" style={{ margin: '0 auto 16px' }} />
            <h3 style={{ margin: '0 0 10px 0', fontSize: '18px' }}>Delete User?</h3>
            <p style={{ margin: '0 0 20px 0', fontSize: '14px', color: '#64748b', lineHeight: '1.5' }}>
              Are you sure you want to permanently delete <strong>{selectedUser?.full_name}</strong>? This action cannot be undone and will remove all their data.
            </p>
            <div style={{ display: 'flex', gap: '10px' }}>
              <button onClick={() => setShowDeleteModal(false)} style={{ flex: 1, padding: '10px', borderRadius: '6px', border: '1px solid #e2e8f0', backgroundColor: '#fff', cursor: 'pointer', fontWeight: '600' }}>Cancel</button>
              <button onClick={handleDelete} disabled={actionLoading} style={{ flex: 1, padding: '10px', borderRadius: '6px', border: 'none', backgroundColor: '#ef4444', color: '#fff', cursor: 'pointer', fontWeight: '600' }}>{actionLoading ? 'Deleting...' : 'Delete'}</button>
            </div>
          </div>
        </div>
      )}

      {showNotifyModal && (
        <div style={{ position: 'fixed', top: 0, left: 0, right: 0, bottom: 0, backgroundColor: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000 }}>
          <div style={{ backgroundColor: '#fff', padding: '24px', borderRadius: '12px', width: '400px', maxWidth: '90%' }}>
            <h3 style={{ margin: '0 0 16px 0', fontSize: '18px', display: 'flex', justifyContent: 'space-between' }}>
              Send Notification
              <X size={20} style={{ cursor: 'pointer', color: '#94a3b8' }} onClick={() => setShowNotifyModal(false)} />
            </h3>
            <p style={{ fontSize: '13px', color: '#64748b', marginBottom: '16px' }}>Sending message to <strong>{selectedUser?.email}</strong></p>
            <form onSubmit={handleNotifySubmit}>
              <textarea 
                style={{ width: '100%', boxSizing: 'border-box', padding: '12px', border: '1px solid #e2e8f0', borderRadius: '6px', minHeight: '100px', marginBottom: '20px', resize: 'vertical' }} 
                placeholder="Type your message here..."
                value={notifyMsg}
                onChange={(e) => setNotifyMsg(e.target.value)}
                required
              />
              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px' }}>
                <button type="button" onClick={() => setShowNotifyModal(false)} style={{ padding: '8px 16px', borderRadius: '6px', border: '1px solid #e2e8f0', backgroundColor: '#fff', cursor: 'pointer' }}>Cancel</button>
                <button type="submit" style={{ padding: '8px 16px', borderRadius: '6px', border: 'none', backgroundColor: '#0ea5e9', color: '#fff', cursor: 'pointer', fontWeight: '600' }}>Send</button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
