import React, { useState, useEffect } from 'react';
import { X, Copy, Check, Send } from 'lucide-react';
import { buildSafeAdvisoryDraft } from '../utils/advisorySafety.js';

/* --- MODAL WRAPPER COMPONENT --- */
function BaseModal({ isOpen, onClose, title, children, className = '' }) {
  if (!isOpen) return null;

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className={`modal-container${className ? ` ${className}` : ''}`} role="dialog" aria-modal="true" aria-label={title} onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">{title}</span>
          <button className="btn-close-modal" onClick={onClose}>
            <X size={18} />
          </button>
        </div>
        {children}
      </div>
    </div>
  );
}

import { supabase } from '../supabaseClient';

/* --- GENERATE ADVISORY MODAL --- */
export function AdvisoryModal({ isOpen, onClose, stationData, onViewAdvisories }) {
  const [copied, setCopied] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [category, setCategory] = useState('Flood Risk');
  const [severity, setSeverity] = useState('High');
  const [affectedAreas, setAffectedAreas] = useState('');
  const [subject, setSubject] = useState('');
  const [message, setMessage] = useState('');
  const [escalationActions, setEscalationActions] = useState('');

  // Build a neutral, review-only draft. Never invent a gauge reading, affected
  // barangay, risk classification, or official evacuation instruction.
  useEffect(() => {
    if (!isOpen) return;
    const draft = buildSafeAdvisoryDraft(stationData);
    setCategory('Flood Risk');
    setSeverity(draft.severity);
    setAffectedAreas(draft.affectedAreas);
    setSubject(draft.subject);
    setMessage(draft.message);
    setEscalationActions(draft.escalationActions);
  }, [isOpen, stationData]);

  const handleCopy = () => {
    navigator.clipboard.writeText(`Subject: ${subject}\n\n${message}`);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const handleSend = async () => {
    if (!subject.trim() || !message.trim()) {
      alert('Please provide a subject and body text for the advisory.');
      return;
    }

    setIsSubmitting(true);
    try {
      const { data, error } = await supabase
        .from('advisories')
        .insert([
          {
            title: subject,
            category: category,
            severity: severity,
            status: 'Draft',
            description: message,
            recommended_action: 'Monitor river levels, keep emergency kits ready, and obey local LGU instructions.',
            escalation_actions: escalationActions,
            affected_areas: affectedAreas,
            duration_start: null,
            published_at: null
          }
        ])
        .select();

      if (error) {
        console.error('Error inserting advisory to Supabase:', error);
        alert(`Could not publish advisory to database: ${error.message}`);
      } else {
        alert('Advisory draft saved for manual review. No public notification was sent.');
        if (onViewAdvisories) {
          onViewAdvisories();
        }
        onClose();
      }
    } catch (err) {
      console.error('Unexpected error publishing advisory:', err);
      alert('An unexpected error occurred while publishing the advisory.');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <BaseModal isOpen={isOpen} onClose={onClose} title="Prepare Advisory Draft" className="advisory-modal">
      <div className="modal-body">
        <div role="status" style={{ padding: '12px', marginBottom: '14px', background: '#fffbeb', border: '1px solid #fbbf24', borderRadius: '8px', fontSize: '13px' }}>
          <strong>Internal draft only.</strong> The water level and geographical impact must be verified before publication. Saving this draft does not issue an official alert, evacuation order, or resident notification.
        </div>
        <div className="form-group advisory-form-row" style={{ marginBottom: '12px' }}>
          <div>
            <label className="form-label">Category</label>
            <select className="form-input" value={category} onChange={(e) => setCategory(e.target.value)}>
              <option value="Flood Risk">Flood</option>
              <option value="Weather">Monitoring</option>
              <option value="Evacuation">Weather</option>
            </select>
          </div>
          <div>
            <label className="form-label">Severity Level</label>
            <select className="form-input" value={severity} onChange={(e) => setSeverity(e.target.value)}>
              <option value="Critical">Critical</option>
              <option value="High">High</option>
              <option value="Medium">Medium</option>
              <option value="Low">Low</option>
            </select>
          </div>
        </div>

        <div className="form-group" style={{ marginBottom: '12px' }}>
          <label className="form-label">Affected Barangays / Sectors</label>
          <input 
            type="text" 
            className="form-input" 
            value={affectedAreas} 
            onChange={(e) => setAffectedAreas(e.target.value)} 
          />
        </div>

        <div className="form-group" style={{ marginBottom: '12px' }}>
          <label className="form-label">Advisory Subject Title</label>
          <input 
            type="text" 
            className="form-input" 
            value={subject} 
            onChange={(e) => setSubject(e.target.value)} 
          />
        </div>

        <div className="form-group" style={{ marginBottom: '12px' }}>
          <label className="form-label">Advisory Message Content</label>
          <textarea
            className="form-textarea"
            rows="4"
            value={message}
            onChange={(e) => setMessage(e.target.value)}
            style={{ padding: '10px 12px', border: '1px solid #cbd5e1', borderRadius: '6px', fontSize: '13px', outline: 'none', resize: 'vertical', fontFamily: 'inherit', width: '100%' }}
          />
        </div>

        <div className="form-group" style={{ marginBottom: '12px' }}>
          <label className="form-label" style={{ color: '#dc2626', fontWeight: '600' }}>
            Escalation Actions (If conditions worsen)
          </label>
          <textarea
            className="form-textarea"
            rows="3"
            value={escalationActions}
            onChange={(e) => setEscalationActions(e.target.value)}
            placeholder="Actions citizens should take if conditions worsen (e.g. Evacuate immediately; Turn off LPG; Contact hotlines)..."
            style={{ padding: '10px 12px', border: '1px solid #fca5a5', borderRadius: '6px', fontSize: '13px', outline: 'none', resize: 'vertical', fontFamily: 'inherit', width: '100%', backgroundColor: '#fef2f2' }}
          />
        </div>


      </div>
      <div className="modal-footer">
        <button className="btn-secondary" onClick={handleCopy} style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          {copied ? <Check size={14} style={{ color: 'var(--color-online)' }} /> : <Copy size={14} />}
          <span>{copied ? 'Copied!' : 'Copy Text'}</span>
        </button>
        <button 
          className="btn-primary" 
          onClick={handleSend} 
          disabled={isSubmitting}
          style={{ display: 'flex', alignItems: 'center', gap: '8px' }}
        >
          <Send size={14} />
          <span>{isSubmitting ? 'Saving draft...' : 'Save Draft for Review'}</span>
        </button>
      </div>
    </BaseModal>
  );
}

/* --- NOTIFY RESIDENTS MODAL --- */
// Intentionally no dispatch call: existing UI had a simulated success animation.
// Real delivery must use the separately authorized SMS queue and delivery receipts.
export function NotifyModal({ isOpen, onClose, onOpenSmsConsole }) {
  return (
    <BaseModal isOpen={isOpen} onClose={onClose} title="Notify Residents — Not Yet Connected">
      <div className="modal-body">
        <div role="alert" style={{ padding: '16px', background: '#fffbeb', border: '1px solid #f59e0b', borderRadius: '8px' }}>
          <strong>Broadcasting from this panel is disabled.</strong>
          <p style={{ marginTop: '8px', marginBottom: 0 }}>
            This page is not connected to a verified recipient list or delivery gateway.
            No SMS, push notification, or siren command has been sent. Do not use
            this panel to confirm that residents were notified.
          </p>
        </div>
        <p style={{ marginTop: '14px', fontSize: '13px', color: 'var(--text-muted)' }}>
          Use only the separately authorized SMS Composer for controlled tests.
          A queued message is not proof that a resident received it.
        </p>
      </div>
      <div className="modal-footer">
        <button className="btn-secondary" type="button" onClick={onClose}>Close</button>
        {onOpenSmsConsole && <button className="btn-primary" type="button" onClick={onOpenSmsConsole}>Open SMS Composer</button>}
      </div>
    </BaseModal>
  );
}

/* --- LEGACY INUNDATION SIMULATION MODAL --- */
// The prior animation fabricated measurements and projected flood impacts.
// Never present demonstration values as a real flood prediction.
export function PredictModal({ isOpen, onClose }) {
  return (
    <BaseModal isOpen={isOpen} onClose={onClose} title="Inundation Simulation Unavailable">
      <div className="modal-body">
        <div role="alert" style={{ padding: '16px', border: '1px solid #f59e0b', borderRadius: '8px', background: '#fffbeb' }}>
          No verified live inundation simulation is connected to this dialog.
          No prediction has been calculated. Use the Flood Hazard Exposure module
          for its separately labeled reference scenarios and limitations.
        </div>
      </div>
      <div className="modal-footer"><button className="btn-secondary" type="button" onClick={onClose}>Close</button></div>
    </BaseModal>
  );
}
