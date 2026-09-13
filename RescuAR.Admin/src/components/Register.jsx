import React, { useState } from 'react';
import { Mail, Lock, AlertCircle, MapPin, Waves, Loader2 } from 'lucide-react';
import { supabase } from '../supabaseClient';

export default function Register({ onSwitchToLogin }) {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState(null);

  const handleRegister = async (e) => {
    e.preventDefault();
    setIsLoading(true);
    setError(null);
    
    const { error } = await supabase.auth.signUp({
      email,
      password,
    });
    
    if (error) {
      setError(error.message);
      setIsLoading(false);
    }
    // If successful, and email confirmation is disabled on Supabase side, 
    // the user will be automatically logged in and the session will be detected by App.jsx.
    // If email confirmation is required, you might want to show a success message instead.
  };

  return (
    <div className="auth-wrapper">
      <div className="auth-card">
        <div className="auth-header">
          <div className="auth-logo">
            <div className="auth-logo-icon-container">
              <div style={{ display: 'flex', alignItems: 'center', color: '#0284c7' }}>
                <MapPin size={28} style={{ marginRight: '-12px', zIndex: 2 }} />
                <Waves size={18} style={{ marginTop: '12px', color: '#10b981', zIndex: 1 }} />
              </div>
            </div>
          </div>
          <h1 className="auth-title">Create Account</h1>
          <p className="auth-subtitle">Sign up to access RescuAR system</p>
        </div>

        {error && (
          <div className="auth-error-message">
            <AlertCircle size={16} />
            <span>{error}</span>
          </div>
        )}

        <form className="auth-form" onSubmit={handleRegister}>
          <div className="auth-input-group">
            <label className="auth-label" htmlFor="email">Email address</label>
            <div className="auth-input-wrapper">
              <Mail className="auth-input-icon" size={18} />
              <input
                id="email"
                type="email"
                className="auth-input"
                placeholder="Enter your email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
              />
            </div>
          </div>

          <div className="auth-input-group">
            <label className="auth-label" htmlFor="password">Password</label>
            <div className="auth-input-wrapper">
              <Lock className="auth-input-icon" size={18} />
              <input
                id="password"
                type="password"
                className="auth-input"
                placeholder="Create a strong password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
              />
            </div>
          </div>

          <button 
            type="submit" 
            className="auth-btn" 
            disabled={isLoading}
          >
            {isLoading ? <Loader2 size={18} className="spin-icon" /> : 'Sign Up'}
          </button>
        </form>

        <div className="auth-footer">
          Already have an account?{' '}
          <span className="auth-link" onClick={onSwitchToLogin}>
            Log in
          </span>
        </div>
      </div>
    </div>
  );
}
