import React from 'react';
import { LogOut, Menu, X } from 'lucide-react';
import BrandLogo from './BrandLogo';
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
        <BrandLogo variant="header" />
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
