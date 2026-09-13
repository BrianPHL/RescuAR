import React, { useState, useEffect } from 'react';
import { X, Copy, Check, Send, Play, AlertTriangle } from 'lucide-react';

/* --- MODAL WRAPPER COMPONENT --- */
function BaseModal({ isOpen, onClose, title, children }) {
  if (!isOpen) return null;

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal-container" onClick={(e) => e.stopPropagation()}>
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
  const [affectedAreas, setAffectedAreas] = useState('Tumana, Nangka, Malanday');
  const [subject, setSubject] = useState('');
  const [message, setMessage] = useState('');

  // Prefill default template dynamically when modal opens or stationData updates
  useEffect(() => {
    if (isOpen) {
      const rawStationName = stationData?.stationName || 'Sto. Niño';
      const cleanStationName = rawStationName.replace(/ Station/gi, '').trim();
      const displayStationTitle = `${cleanStationName} Station`;
      const level = stationData?.level !== undefined ? Number(stationData.level).toFixed(1) : '16.2';
      const alertStatus = stationData?.alertStatus || '2nd Alarm';
      const alertLabel = stationData?.alertLabel || '2nd Alarm (Preparation)';

      // Automate Affected Areas mapping based on station location & alert severity
      let computedAreas = [];
      let computedSeverity = 'Low';

      const is3rd = alertStatus.includes('3rd') || Number(level) >= 18;
      const is2nd = alertStatus.includes('2nd') || (Number(level) >= 16 && Number(level) < 18);
      const is1st = alertStatus.includes('1st') || (Number(level) >= 15 && Number(level) < 16);

      if (is3rd) {
        computedSeverity = 'Critical';
      } else if (is2nd) {
        computedSeverity = 'High';
      } else if (is1st) {
        computedSeverity = 'Medium';
      } else {
        computedSeverity = 'Low';
      }

      const nameLower = cleanStationName.toLowerCase();

      if (nameLower.includes('nangka')) {
        if (is3rd) {
          computedAreas = ['Nangka', 'Tumana', 'Concepcion I', 'Banaba Boundary'];
        } else if (is2nd) {
          computedAreas = ['Nangka', 'Tumana Riverside'];
        } else {
          computedAreas = ['Nangka Low-Lying Zones'];
        }
      } else if (nameLower.includes('tumana')) {
        if (is3rd) {
          computedAreas = ['Tumana', 'Malanday', 'Nangka', 'Concepcion I'];
        } else if (is2nd) {
          computedAreas = ['Tumana', 'Malanday Riverside'];
        } else {
          computedAreas = ['Tumana Low-Lying Zones'];
        }
      } else if (nameLower.includes('sto') || nameLower.includes('nino')) {
        if (is3rd) {
          computedAreas = ['Sto. Niño', 'Malanday', 'Tumana', 'Jesus dela Peña', 'Kalumpang', 'San Roque'];
        } else if (is2nd) {
          computedAreas = ['Sto. Niño', 'Malanday', 'Tumana', 'Jesus dela Peña'];
        } else if (is1st) {
          computedAreas = ['Sto. Niño', 'Malanday Riverside'];
        } else {
          computedAreas = ['Sto. Niño Low-Lying Zones'];
        }
      } else if (nameLower.includes('rodriguez')) {
        if (is3rd || is2nd) {
          computedAreas = ['Upper Marikina Basin', 'Nangka Floodway Entrance', 'Tumana Floodplain'];
        } else {
          computedAreas = ['Upper Marikina Basin Corridor'];
        }
      } else if (nameLower.includes('san jose')) {
        if (is3rd || is2nd) {
          computedAreas = ['San Jose Flood Control Zone', 'Nangka Area', 'Tumana Riverbanks'];
        } else {
          computedAreas = ['San Jose Stream Gauge Perimeter'];
        }
      } else if (nameLower.includes('batasan')) {
        if (is3rd || is2nd) {
          computedAreas = ['Batasan Hills Boundary', 'Tumana', 'Malanday Spillway Zone'];
        } else {
          computedAreas = ['Batasan Stream Corridor'];
        }
      } else {
        if (is3rd) {
          computedAreas = ['Barangka', 'Jesus dela Peña', 'Malanday', 'Nangka', 'Sto. Niño', 'Tumana'];
        } else if (is2nd) {
          computedAreas = ['Tumana', 'Nangka', 'Malanday', 'Sto. Niño'];
        } else {
          computedAreas = ['Riverbank Low-Lying Barangays'];
        }
      }

      const affectedString = computedAreas.join(', ');
      setAffectedAreas(affectedString);
      setSeverity(computedSeverity);

      const generatedSubject = `FLOOD WARNING: ${displayStationTitle} reached ${alertStatus.toUpperCase()} (${level}m)`;
      
      let actionInstructions = '';
      if (is3rd) {
        actionInstructions = `CRITICAL ACTION REQUIRED:\n1. Immediate mandatory evacuation is in effect for identified danger zones in ${affectedString}.\n2. Proceed to designated evacuation centers immediately.\n3. Turn off main electric switches before evacuating.`;
      } else if (is2nd) {
        actionInstructions = `PREPARATION REQUIRED:\n1. Residents in ${affectedString} must secure emergency go-bags and vital documents.\n2. Move electrical appliances and vehicles to higher ground.\n3. Stand by for potential mandatory evacuation orders (Alert Level 3).`;
      } else if (is1st) {
        actionInstructions = `MONITORING ADVISORY:\n1. Communities in ${affectedString} should monitor river level updates closely.\n2. Keep emergency communications active and charge electronic devices.`;
      } else {
        actionInstructions = `ROUTINE NOTICE:\n1. Water level at ${displayStationTitle} is within normal bounds (${level}m).\n2. No immediate threat of overflow in ${affectedString}.`;
      }

      const generatedMessage = `AUTOMATIC PUBLIC WATER LEVEL ADVISORY:\n\nPlease be advised that the ${displayStationTitle} has registered a water gauge reading of ${level} meters (${alertLabel}).\n\nDirectly Affected Barangays / Sectors:\n${affectedString}\n\n${actionInstructions}\n\n- Marikina Disaster Risk Reduction & Management Office (MCDRRMO)`;

      setSubject(generatedSubject);
      setMessage(generatedMessage);
    }
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
            status: 'Active',
            description: message,
            recommended_action: 'Monitor river levels, keep emergency kits ready, and obey local LGU instructions.',
            affected_areas: affectedAreas,
            duration_start: new Date().toISOString(),
            published_at: new Date().toISOString()
          }
        ])
        .select();

      if (error) {
        console.error('Error inserting advisory to Supabase:', error);
        alert(`Could not publish advisory to database: ${error.message}`);
      } else {
        alert('✅ Advisory created and published directly to the Advisories page!');
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
    <BaseModal isOpen={isOpen} onClose={onClose} title="Generate & Publish Advisory">
      <div className="modal-body">
        <div className="form-group" style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px', marginBottom: '12px' }}>
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
          <span>{isSubmitting ? 'Publishing...' : 'Publish to Advisories Page'}</span>
        </button>
      </div>
    </BaseModal>
  );
}

/* --- NOTIFY RESIDENTS MODAL --- */
export function NotifyModal({ isOpen, onClose }) {
  const [sectors, setSectors] = useState({
    Tumana: true,
    Nangka: true,
    Malanday: false,
    Provident: false
  });
  const [channel, setChannel] = useState('SMS');
  const [sendingState, setSendingState] = useState('idle'); // idle, sending, done
  const [progress, setProgress] = useState(0);

  const handleSectorChange = (sector) => {
    setSectors(prev => ({ ...prev, [sector]: !prev[sector] }));
  };

  const startBroadcast = () => {
    setSendingState('sending');
    setProgress(0);
  };

  useEffect(() => {
    let interval;
    if (sendingState === 'sending') {
      interval = setInterval(() => {
        setProgress(prev => {
          if (prev >= 100) {
            clearInterval(interval);
            setSendingState('done');
            return 100;
          }
          return prev + 5;
        });
      }, 100);
    }
    return () => clearInterval(interval);
  }, [sendingState]);

  const targetCount = Object.values(sectors).filter(Boolean).length;

  return (
    <BaseModal isOpen={isOpen} onClose={onClose} title="Notify Residents Broadcast">
      <div className="modal-body">
        {sendingState === 'idle' && (
          <>
            <div className="form-group">
              <label className="form-label">Target Barangay Sectors</label>
              <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', marginTop: '4px' }}>
                {Object.keys(sectors).map((sector) => (
                  <label key={sector} style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '13px', cursor: 'pointer' }}>
                    <input 
                      type="checkbox" 
                      checked={sectors[sector]} 
                      onChange={() => handleSectorChange(sector)} 
                      style={{ width: '16px', height: '16px' }}
                    />
                    <span>Barangay {sector}</span>
                  </label>
                ))}
              </div>
            </div>

            <div className="form-group">
              <label className="form-label">Broadcast Delivery Channel</label>
              <select 
                className="form-input" 
                value={channel} 
                onChange={(e) => setChannel(e.target.value)}
                style={{ backgroundColor: '#ffffff', cursor: 'pointer' }}
              >
                <option value="SMS">Emergency SMS Broadcast</option>
                <option value="Push">Mobile App Push Notification</option>
                <option value="Siren">LGU Voice Siren Alert Trigger</option>
              </select>
            </div>
            
            <div style={{ fontSize: '12px', color: 'var(--text-light)', borderTop: '1px solid var(--color-border)', paddingTop: '12px' }}>
              You are preparing to broadcast warning triggers to residents in <strong>{targetCount}</strong> sector(s) using the <strong>{channel}</strong> gateway.
            </div>
          </>
        )}

        {sendingState === 'sending' && (
          <div className="simulation-box">
            <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '13px', fontWeight: '600' }}>
              <span>Transmitting alert packets...</span>
              <span>{progress}%</span>
            </div>
            <div className="progress-bar-container">
              <div className="progress-bar-fill" style={{ width: `${progress}%` }}></div>
            </div>
            <div style={{ fontSize: '12px', color: 'var(--text-muted)' }}>
              Queue batch processing: Sending SMS to {Math.round(progress * 12.4)} / 1240 subscribers.
            </div>
          </div>
        )}

        {sendingState === 'done' && (
          <div style={{ textAlign: 'center', padding: '16px 0', display: 'flex', flexDirection: 'column', align: 'center', gap: '12px' }}>
            <div style={{ display: 'flex', justifyContent: 'center' }}>
              <div style={{ width: '48px', height: '48px', borderRadius: '50%', backgroundColor: 'var(--color-online-bg)', color: 'var(--color-online)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                <Check size={28} />
              </div>
            </div>
            <h3 style={{ fontSize: '16px', fontWeight: '700' }}>Broadcast Completed!</h3>
            <p style={{ fontSize: '13px', color: 'var(--text-muted)' }}>
              SMS transmission logs have been saved. A total of 1,240 coordinators and residents have been alerted.
            </p>
          </div>
        )}
      </div>
      
      <div className="modal-footer">
        {sendingState === 'idle' && (
          <>
            <button className="btn-secondary" onClick={onClose}>Cancel</button>
            <button 
              className="btn-primary" 
              onClick={startBroadcast} 
              disabled={targetCount === 0}
              style={{ display: 'flex', alignItems: 'center', gap: '8px' }}
            >
              <Send size={14} />
              <span>Send Warning Alerts</span>
            </button>
          </>
        )}
        {sendingState === 'sending' && (
          <button className="btn-secondary" disabled>Sending...</button>
        )}
        {sendingState === 'done' && (
          <button className="btn-primary" onClick={onClose}>Done</button>
        )}
      </div>
    </BaseModal>
  );
}

/* --- RUN INUNDATION MODAL --- */
export function PredictModal({ isOpen, onClose }) {
  const [progress, setProgress] = useState(0);
  const [running, setRunning] = useState('idle'); // idle, running, done
  const [log, setLog] = useState([]);

  const startSimulation = () => {
    setRunning('running');
    setProgress(0);
    setLog(['Initializing simulation engine...', 'Syncing water levels at 16.2m...']);
  };

  useEffect(() => {
    let interval;
    if (running === 'running') {
      interval = setInterval(() => {
        setProgress(prev => {
          if (prev >= 100) {
            clearInterval(interval);
            setRunning('done');
            setLog(logs => [...logs, 'Calculation completed.', 'Identified critical threats: Barangays Tumana, Provident.']);
            return 100;
          }
          
          if (prev === 20) {
            setLog(logs => [...logs, 'Loading topographical height meshes...']);
          } else if (prev === 50) {
            setLog(logs => [...logs, 'Simulating flood front velocity paths...']);
          } else if (prev === 80) {
            setLog(logs => [...logs, 'Evaluating risk factor ratios for residential grids...']);
          }
          return prev + 10;
        });
      }, 200);
    }
    return () => clearInterval(interval);
  }, [running]);

  return (
    <BaseModal isOpen={isOpen} onClose={onClose} title="Hydrologic Inundation Simulation">
      <div className="modal-body">
        {running === 'idle' && (
          <>
            <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-start' }}>
              <AlertTriangle size={24} style={{ color: 'var(--color-alert-2)', flexShrink: 0 }} />
              <div style={{ fontSize: '13px', lineHeight: '1.4', color: 'var(--text-muted)' }}>
                You are about to execute the <strong>Inundation Prediction Simulation</strong>. This uses real-time telemetry elevations to compute flooding boundaries along the river basin.
              </div>
            </div>
            <div className="form-group">
              <label className="form-label">Simulation Time Horizon</label>
              <select className="form-input" style={{ backgroundColor: 'white' }}>
                <option>Next 6 Hours (Standard)</option>
                <option>Next 12 Hours (Extended)</option>
                <option>Next 24 Hours (Full forecast)</option>
              </select>
            </div>
          </>
        )}

        {(running === 'running' || running === 'done') && (
          <div className="simulation-box">
            <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '13px', fontWeight: '600' }}>
              <span>Hydro-Modeling Engine</span>
              <span>{progress}%</span>
            </div>
            <div className="progress-bar-container">
              <div className="progress-bar-fill" style={{ width: `${progress}%` }}></div>
            </div>
            <div className="simulation-log">
              {log.map((entry, idx) => (
                <div key={idx} style={{ marginBottom: '2px' }}>&gt; {entry}</div>
              ))}
            </div>
          </div>
        )}
      </div>
      
      <div className="modal-footer">
        {running === 'idle' && (
          <>
            <button className="btn-secondary" onClick={onClose}>Cancel</button>
            <button className="btn-primary" onClick={startSimulation} style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
              <Play size={14} fill="white" />
              <span>Start Simulation</span>
            </button>
          </>
        )}
        {running === 'running' && (
          <button className="btn-secondary" disabled>Simulating...</button>
        )}
        {running === 'done' && (
          <button className="btn-primary" onClick={onClose}>Close Simulation</button>
        )}
      </div>
    </BaseModal>
  );
}
