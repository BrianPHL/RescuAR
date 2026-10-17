import React from 'react';
import { MapPin, Waves, LogOut, Menu, X } from 'lucide-react';
import { supabase } from '../supabaseClient';

export default function Header({ session, onMenuToggle, isMenuOpen, menuButtonRef }) {
  const handleLogout = async () => {
    await supabase.auth.signOut();
  };

  const userEmail = session?.user?.email || 'operator.mcdrrmo@gmail.com';
  // Attempt to generate initials from email if no name is provided
  const initials = userEmail.substring(0, 2).toUpperCase();

  return (
    <header className="app-header">
      <div className="logo-section">
        <button
          type="button"
          className="mobile-menu-toggle"
          onClick={onMenuToggle}
          ref={menuButtonRef}
          aria-controls="admin-sidebar"
          aria-expanded={isMenuOpen}
          aria-label={isMenuOpen ? 'Close navigation menu' : 'Open navigation menu'}
        >
          {isMenuOpen ? <X size={22} /> : <Menu size={22} />}
        </button>
        <div className="logo-text">
          {/* Map Pin + River Waves Icon combination */}
          <div style={{ display: 'flex', alignItems: 'center', marginRight: '8px', color: '#0284c7' }}>
            <MapPin size={24} style={{ marginRight: '-12px', zIndex: 2 }} />
            <Waves size={16} style={{ marginTop: '12px', color: '#10b981', zIndex: 1 }} />
          </div>
          Rescu<span>AR</span>
        </div>
      </div>
      
      <button type="button" className="user-profile-badge" onClick={handleLogout} title="Sign Out" aria-label={`Sign out ${userEmail}`}>
        <div className="avatar-circle">{initials}</div>
        <div className="user-info">
          <span className="user-name">System Admin</span>
          <span className="user-email">{userEmail}</span>
        </div>
        <LogOut size={16} className="dropdown-arrow" style={{ marginLeft: '8px', color: '#ef4444' }} />
      </button>
    </header>
  );
}
