import React, { useState } from 'react';
import { MapPin, Waves, Lock, Mail, AlertCircle } from 'lucide-react';
import { supabase } from '../supabaseClient';

export default function Auth({ onLogin }) {
  const [isHovered, setIsHovered] = useState(false);
  const [focusedInput, setFocusedInput] = useState(null);
  
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [errorMsg, setErrorMsg] = useState('');
  const [failedAttempts, setFailedAttempts] = useState(0);
  const [isLocked, setIsLocked] = useState(false);

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
      
      // Verify if the user is an admin
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
        // Not an admin, sign them out immediately
        await supabase.auth.signOut();
        throw new Error('Unauthorized. You do not have administrator access.');
      }

      setFailedAttempts(0);
      
      // On success, we don't necessarily need to call onLogin if AuthWrapper is listening to state changes,
      // but we can call it to instantly trigger a local state flip if AuthWrapper expects it.
      if (onLogin) onLogin();
    } catch (error) {
      const newAttempts = failedAttempts + 1;
      setFailedAttempts(newAttempts);
      
      if (newAttempts >= 5) {
        setIsLocked(true);
        setErrorMsg('Too many failed attempts. Try again in 3 minutes.');
        // Unlock after 3 minutes (180000 ms)
        setTimeout(() => {
          setIsLocked(false);
          setFailedAttempts(0);
          setErrorMsg('');
        }, 180000);
      } else {
        setErrorMsg(`Login failed. You have ${5 - newAttempts} attempts remaining.`);
      }
    } finally {
      setLoading(false);
    }
  };

  const styles = {
    wrapper: {
      display: 'flex',
      justifyContent: 'center',
      alignItems: 'center',
      minHeight: '100vh',
      backgroundColor: 'var(--bg-app)',
      padding: '20px'
    },
    card: {
      width: '100%',
      maxWidth: '420px',
      padding: '40px 32px',
      backgroundColor: 'var(--bg-card)',
      borderRadius: 'var(--radius-lg)',
      boxShadow: 'var(--shadow-lg)',
      border: '1px solid var(--color-border)',
      display: 'flex',
      flexDirection: 'column',
      alignItems: 'center'
    },
    logoContainer: {
      display: 'flex',
      flexDirection: 'column',
      alignItems: 'center',
      marginBottom: '32px'
    },
    logoText: {
      fontSize: '36px',
      fontWeight: 800,
      color: 'var(--text-main)',
      display: 'flex',
      alignItems: 'center',
      gap: '8px',
      marginBottom: '8px'
    },
    logoSpan: {
      color: '#10b981'
    },
    subtitle: {
      fontSize: '14px',
      color: 'var(--text-muted)',
      textAlign: 'center',
      lineHeight: 1.5
    },
    form: {
      width: '100%',
      display: 'flex',
      flexDirection: 'column',
      gap: '20px'
    },
    inputGroup: {
      display: 'flex',
      flexDirection: 'column',
      gap: '8px'
    },
    label: {
      fontSize: '13px',
      fontWeight: 600,
      color: 'var(--text-main)'
    },
    inputWrapper: {
      position: 'relative',
      display: 'flex',
      alignItems: 'center'
    },
    inputIcon: (isFocused) => ({
      position: 'absolute',
      left: '14px',
      color: isFocused ? 'var(--color-brand)' : 'var(--text-light)',
      transition: 'color 0.2s ease'
    }),
    input: (isFocused) => ({
      width: '100%',
      padding: '12px 16px',
      paddingLeft: '44px',
      border: isFocused ? '1px solid var(--color-brand)' : '1px solid var(--color-border)',
      borderRadius: 'var(--radius-md)',
      fontSize: '14px',
      outline: 'none',
      backgroundColor: isFocused ? '#ffffff' : '#fafafa',
      boxShadow: isFocused ? '0 0 0 3px rgba(2, 132, 199, 0.1)' : 'none',
      transition: 'all 0.2s ease'
    }),
    btn: {
      width: '100%',
      padding: '14px',
      backgroundColor: isHovered && !loading && !isLocked ? '#0369a1' : (isLocked ? '#9ca3af' : 'var(--color-brand)'),
      color: 'white',
      border: 'none',
      borderRadius: 'var(--radius-md)',
      fontSize: '15px',
      fontWeight: 600,
      cursor: (loading || isLocked) ? 'not-allowed' : 'pointer',
      marginTop: '12px',
      transition: 'all 0.2s ease',
      transform: isHovered && !loading && !isLocked ? 'translateY(-1px)' : 'translateY(0)',
      boxShadow: isHovered && !loading && !isLocked ? '0 4px 6px -1px rgba(2, 132, 199, 0.2)' : 'none',
      opacity: (loading || isLocked) ? 0.7 : 1
    },
    errorBox: {
      display: 'flex',
      alignItems: 'center',
      gap: '8px',
      padding: '12px',
      backgroundColor: 'var(--color-offline-bg)',
      color: 'var(--color-offline)',
      borderRadius: 'var(--radius-md)',
      fontSize: '13px',
      fontWeight: 500,
      width: '100%'
    }
  };

  return (
    <div style={styles.wrapper}>
      <div style={styles.card}>
        
        {/* Centered Logo & Branding */}
        <div style={styles.logoContainer}>
          <div style={styles.logoText}>
            <div style={{ display: 'flex', alignItems: 'center', color: 'var(--color-brand)' }}>
              <MapPin size={36} style={{ marginRight: '-18px', zIndex: 2 }} />
              <Waves size={24} style={{ marginTop: '18px', color: '#10b981', zIndex: 1 }} />
            </div>
            Rescu<span style={styles.logoSpan}>AR</span>
          </div>
          <p style={styles.subtitle}>
            Enter your administrative credentials to access the telemetry dashboard.
          </p>
        </div>

        {/* Authentication Form */}
        <form style={styles.form} onSubmit={handleSubmit}>
          
          {errorMsg && (
            <div style={styles.errorBox}>
              <AlertCircle size={16} />
              {errorMsg}
            </div>
          )}

          <div style={styles.inputGroup}>
            <label style={styles.label}>Email Address</label>
            <div style={styles.inputWrapper}>
              <Mail size={18} style={styles.inputIcon(focusedInput === 'email')} />
              <input 
                type="email" 
                style={styles.input(focusedInput === 'email')} 
                placeholder="operator.mcdrrmo@gmail.com" 
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                onFocus={() => setFocusedInput('email')}
                onBlur={() => setFocusedInput(null)}
                required 
                disabled={loading || isLocked}
              />
            </div>
          </div>

          <div style={styles.inputGroup}>
            <label style={styles.label}>Password</label>
            <div style={styles.inputWrapper}>
              <Lock size={18} style={styles.inputIcon(focusedInput === 'password')} />
              <input 
                type="password" 
                style={styles.input(focusedInput === 'password')} 
                placeholder="••••••••" 
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                onFocus={() => setFocusedInput('password')}
                onBlur={() => setFocusedInput(null)}
                required 
                disabled={loading || isLocked}
              />
            </div>
          </div>

          <button 
            type="submit" 
            style={styles.btn}
            onMouseEnter={() => setIsHovered(true)}
            onMouseLeave={() => setIsHovered(false)}
            disabled={loading || isLocked}
          >
            {loading ? 'Authenticating...' : isLocked ? 'Locked' : 'Sign In'}
          </button>
        </form>

      </div>
    </div>
  );
}
