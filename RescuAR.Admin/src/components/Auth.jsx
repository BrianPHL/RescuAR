import React, { useState } from 'react';
import {
  MapPin,
  Waves,
  Lock,
  Mail,
  AlertCircle,
  Eye,
  EyeOff,
  ArrowRight,
} from 'lucide-react';
import { supabase } from '../supabaseClient';
import loginFloodHero from '../assets/login-flood-hero.png';
import '../auth-login-layout.css';

export default function Auth({ onLogin }) {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [errorMsg, setErrorMsg] = useState('');
  const [failedAttempts, setFailedAttempts] = useState(0);
  const [isLocked, setIsLocked] = useState(false);
  const [showPassword, setShowPassword] = useState(false);

  const handleSubmit = async (e) => {
    e.preventDefault();
    if (isLocked) return;

    setLoading(true);
    setErrorMsg('');

    try {
      const { data, error } = await supabase.auth.signInWithPassword({
        email,
        password,
      });

      if (error) throw error;

      const { data: adminData, error: adminError } = await supabase
        .from('admin_roles')
        .select('*')
        .eq('user_id', data.user.id)
        .maybeSingle();

      if (adminError) {
        await supabase.auth.signOut();
        throw new Error('Error verifying admin privileges. Please try again.');
      }

      if (!adminData) {
        await supabase.auth.signOut();
        throw new Error('Unauthorized. You do not have administrator access.');
      }

      setFailedAttempts(0);
      if (onLogin) onLogin();
    } catch (error) {
      const newAttempts = failedAttempts + 1;
      setFailedAttempts(newAttempts);

      if (newAttempts >= 5) {
        setIsLocked(true);
        setErrorMsg('Too many failed attempts. Try again in 3 minutes.');
        setTimeout(() => {
          setIsLocked(false);
          setFailedAttempts(0);
          setErrorMsg('');
        }, 180000);
      } else {
        setErrorMsg(error?.message || `Login failed. You have ${5 - newAttempts} attempts remaining.`);
      }
    } finally {
      setLoading(false);
    }
  };

  const handleResetPassword = async () => {
    const normalizedEmail = email.trim();
    if (!normalizedEmail) {
      setErrorMsg('Enter your email first, then click Reset password.');
      return;
    }

    setLoading(true);
    setErrorMsg('');
    try {
      const { error } = await supabase.auth.resetPasswordForEmail(normalizedEmail);
      if (error) throw error;
      setErrorMsg('Password reset email sent. Please check your inbox.');
    } catch (error) {
      setErrorMsg(error?.message || 'Unable to send a reset email right now.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="auth-page">
      <div className="auth-shell">
        <section className="auth-panel" aria-label="Administrator login form">
          <div className="auth-panel-inner">
            <div className="auth-content-anchor">
              <div className="auth-brand-block">
              <div className="auth-brand-mark">
                <div className="auth-brand-icon" aria-hidden="true">
                  <MapPin size={28} style={{ marginRight: '-12px', zIndex: 2 }} />
                  <Waves size={18} style={{ marginTop: '12px', color: '#10b981', zIndex: 1 }} />
                </div>
                <div>
                  <div className="auth-brand-title">Rescu<span>AR</span></div>
                  <div className="auth-brand-subtitle">AR-Based Evacuation Guidance System</div>
                </div>
              </div>
            </div>

            <form className="auth-form-card" onSubmit={handleSubmit}>
              {errorMsg && (
                <div className={`auth-notice ${errorMsg.toLowerCase().includes('sent') ? 'success' : 'error'}`}>
                  <AlertCircle size={16} />
                  <span>{errorMsg}</span>
                </div>
              )}

              <div className="auth-field-group">
                <label className="auth-field-label" htmlFor="admin-email">Email Address</label>
                <div className="auth-input-shell">
                  <Mail className="auth-input-leading-icon" size={18} />
                  <input
                    id="admin-email"
                    type="email"
                    className="auth-input-field"
                    placeholder="operator.mcdrrmo@gmail.com"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    required
                    disabled={loading || isLocked}
                    autoComplete="email"
                  />
                </div>
              </div>

              <div className="auth-field-group">
                <label className="auth-field-label" htmlFor="admin-password">Password</label>
                <div className="auth-input-shell auth-password-shell">
                  <Lock className="auth-input-leading-icon" size={18} />
                  <input
                    id="admin-password"
                    type={showPassword ? 'text' : 'password'}
                    className="auth-input-field auth-password-field"
                    placeholder="••••••••"
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    required
                    disabled={loading || isLocked}
                    autoComplete="current-password"
                  />
                  <button
                    type="button"
                    className="auth-password-toggle"
                    aria-label={showPassword ? 'Hide password' : 'Show password'}
                    onClick={() => setShowPassword((value) => !value)}
                    disabled={loading || isLocked}
                  >
                    {showPassword ? <EyeOff size={18} /> : <Eye size={18} />}
                  </button>
                </div>
              </div>

              <div className="auth-meta-row">
                <span className="auth-attempts-copy">
                  {isLocked
                    ? 'Account temporarily locked.'
                    : failedAttempts > 0
                      ? `${5 - failedAttempts} attempt${5 - failedAttempts === 1 ? '' : 's'} remaining`
                      : ' '}
                </span>
                <button
                  type="button"
                  className="auth-inline-link"
                  onClick={handleResetPassword}
                  disabled={loading || isLocked}
                >
                  Reset password
                </button>
              </div>

              <button type="submit" className="auth-submit-button" disabled={loading || isLocked}>
                <span>{loading ? 'Authenticating...' : isLocked ? 'Locked' : 'Log In'}</span>
                {!loading && !isLocked && <ArrowRight size={16} />}
              </button>

              <p className="auth-bottom-copy">
                Don&apos;t have an account yet? <span>Sign up</span>
              </p>
            </form>
            </div>
          </div>
        </section>

        <aside className="auth-hero" aria-hidden="true">
          <img src={loginFloodHero} alt="" className="auth-hero-image" />
        </aside>
      </div>
    </div>
  );
}
