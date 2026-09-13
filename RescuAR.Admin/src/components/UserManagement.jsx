import React from 'react';
import { Check } from 'lucide-react';

export default function BroadcastLogs() {
    const logs = [
        { date: 'June 18, 2026 - 08:30 AM', target: 'Tumana, Nangka', message: 'Alert Level 2 warning. Please begin packing essential belongings.', status: 'Delivered (1,240 SMS)', type: 'warning' },
        { date: 'June 15, 2026 - 02:15 PM', target: 'All Sectors', message: 'Routine test: early alert system is operational.', status: 'Delivered (1,420 SMS)', type: 'info' },
        { date: 'June 02, 2026 - 09:00 AM', target: 'Provident', message: 'Water level warning: River level reached 14.8m.', status: 'Delivered (350 SMS)', type: 'warning' }
    ];

    return (
        <div className="main-view">
            <div className="view-header">
                <div className="view-title-container">
                    <h1>User Management</h1>
                    <span className="view-subtitle">History of emergency text/alert broadcasts sent to the community</span>
                </div>
            </div>

            <div className="stations-card">
                <h2 className="stations-card-title">Recent Broadcast History</h2>
                <div className="table-container">
                    <table className="data-table">
                        <thead>
                            <tr>
                                <th>Date & Time</th>
                                <th>Recipients</th>
                                <th>Broadcast Message</th>
                                <th>Delivery Status</th>
                            </tr>
                        </thead>
                        <tbody>
                            {logs.map((log, i) => (
                                <tr key={i}>
                                    <td style={{ fontWeight: '600', whiteSpace: 'nowrap' }}>{log.date}</td>
                                    <td>{log.target}</td>
                                    <td>{log.message}</td>
                                    <td>
                                        <span style={{
                                            color: 'var(--color-online)',
                                            backgroundColor: 'var(--color-online-bg)',
                                            padding: '3px 8px',
                                            borderRadius: '12px',
                                            fontSize: '11px',
                                            fontWeight: '700',
                                            display: 'inline-flex',
                                            alignItems: 'center',
                                            gap: '4px'
                                        }}>
                                            <Check size={12} />
                                            {log.status}
                                        </span>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            </div>
        </div>
    );
}
