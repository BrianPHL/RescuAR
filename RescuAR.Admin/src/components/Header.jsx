import React, { useState } from 'react';
import { LogOut, Menu, X } from 'lucide-react';
import BrandLogo from './BrandLogo';
import { supabase } from '../supabaseClient';

export default function Header({ session, access, onMenuToggle, isMenuOpen, menuButtonRef }) {
  const [signingOut, setSigningOut] = useState(false);
  const [signOutError, setSignOutError] = useState('');
  const handleLogout = async () => {
    if (signingOut) return;
    setSigningOut(true);
    setSignOutError('');
    try {
      // Revokes only the current session, not every active admin device.
      const { error } = await supabase.auth.signOut({ scope: 'local' });
      if (error) throw error;
    } catch (error) {
      console.error('Administrator sign out failed:', error);
      setSignOutError('Unable to sign out. Please try again.');
    } finally {
      setSigningOut(false);
    }
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

      {signOutError && <span role="alert" style={{ fontSize: 12, color: "#b42318", marginRight: 8 }}>{signOutError}</span>}
      <button type="button" className="user-profile-badge" onClick={handleLogout} disabled={signingOut} title="Sign Out" aria-label={`Sign out ${userEmail}`}>
        <div className="avatar-circle">{initials}</div>
        <div className="user-info">
          <span className="user-name">{access?.roles?.join(', ') || 'Admin'}</span>
          <span className="user-email">{userEmail}</span>
        </div>
        <LogOut size={16} className="dropdown-arrow" style={{ marginLeft: '8px', color: '#ef4444' }} />
      </button>
    </header>
  );
}
