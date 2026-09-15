import React, { useState } from 'react';
import { 
  LayoutDashboard, 
  Activity, 
  ChevronDown, 
  ChevronUp, 
  Radio, 
  Waves, 
  AlertTriangle, 
  Users, 
  Rss, 
  Settings, 
  BookOpen,
  HelpCircle,
  Gavel
} from 'lucide-react';

export default function Sidebar({ activeView, onViewChange }) {
  // Manage expand/collapse state for collapsible groups
  const [expandedGroups, setExpandedGroups] = useState({
    monitoring: true,
    community: false,
    content: false,
    system: false
  });

  const toggleGroup = (group) => {
    setExpandedGroups(prev => ({
      ...prev,
      [group]: !prev[group]
    }));
  };

  return (
    <aside className="app-sidebar">
      <div className="sidebar-menu-list">
        {/* Dashboard Link */}
        <div 
          className={`sidebar-link ${activeView === 'dashboard' ? 'active' : ''}`}
          onClick={() => onViewChange('dashboard')}
        >
          <LayoutDashboard size={18} />
          <span>Dashboard</span>
        </div>

        {/* Monitoring Section */}
        <div>
          <div 
            className={`menu-group-header ${activeView.startsWith('monitoring-') ? 'active' : ''}`}
            onClick={() => toggleGroup('monitoring')}
          >
            <div className="menu-group-title">
              <Activity size={18} />
              <span>Monitoring</span>
            </div>
            {expandedGroups.monitoring ? <ChevronUp size={14} /> : <ChevronDown size={14} />}
          </div>
          
          {expandedGroups.monitoring && (
            <div className="menu-group-sublist">
              <div 
                className={`sidebar-link ${activeView === 'monitoring-stations' ? 'active' : ''}`}
                onClick={() => onViewChange('monitoring-stations')}
              >
                <Radio size={16} />
                <span>Monitoring Stations</span>
              </div>
              <div 
                className={`sidebar-link ${activeView === 'monitoring-river-level' ? 'active' : ''}`}
                onClick={() => onViewChange('monitoring-river-level')}
              >
                <Waves size={16} />
                <span>Marikina River Level</span>
              </div>
              <div 
                className={`sidebar-link ${activeView === 'monitoring-inundation' ? 'active' : ''}`}
                onClick={() => onViewChange('monitoring-inundation')}
              >
                <AlertTriangle size={16} />
                <span>Inundation Prediction</span>
              </div>
            </div>
          )}
        </div>

        {/* Community Management Section */}
        <div>
          <div 
            className={`menu-group-header ${activeView.startsWith('community-') ? 'active' : ''}`}
            onClick={() => toggleGroup('community')}
          >
            <div className="menu-group-title">
              <Users size={18} />
              <span>Community Management</span>
            </div>
            {expandedGroups.community ? <ChevronUp size={14} /> : <ChevronDown size={14} />}
          </div>
          
          {expandedGroups.community && (
            <div className="menu-group-sublist">
              <div 
                className={`sidebar-link ${activeView === 'community-reports-moderation' ? 'active' : ''}`}
                onClick={() => onViewChange('community-reports-moderation')}
              >
                <span>Reports Moderation</span>
              </div>
              <div 
                className={`sidebar-link ${activeView === 'community-alerts' ? 'active' : ''}`}
                onClick={() => onViewChange('community-alerts')}
              >
                <span>User Management</span>
              </div>
            </div>
          )}
        </div>

        {/* Content Management Section */}
        <div>
          <div 
            className={`menu-group-header ${activeView.startsWith('content-') ? 'active' : ''}`}
            onClick={() => toggleGroup('content')}
          >
            <div className="menu-group-title">
              <Rss size={18} />
              <span>Content Management</span>
            </div>
            {expandedGroups.content ? <ChevronUp size={14} /> : <ChevronDown size={14} />}
          </div>
          
          {expandedGroups.content && (
            <div className="menu-group-sublist">
              <div 
                className={`sidebar-link ${activeView === 'content-advisories' ? 'active' : ''}`}
                onClick={() => onViewChange('content-advisories')}
              >
                <span>Advisories</span>
              </div>
              <div 
                className={`sidebar-link ${activeView === 'content-news' ? 'active' : ''}`}
                onClick={() => onViewChange('content-news')}
              >
                <span>Evacuation Centers</span>
              </div>
              <div 
                className={`sidebar-link ${activeView === 'content-hotlines' ? 'active' : ''}`}
                onClick={() => onViewChange('content-hotlines')}
              >
                <span>Emergency Hotlines</span>
              </div>
            </div>
          )}
        </div>

        {/* System Section */}
        <div>
          <div 
            className={`menu-group-header ${activeView.startsWith('system-') ? 'active' : ''}`}
            onClick={() => toggleGroup('system')}
          >
            <div className="menu-group-title">
              <Settings size={18} />
              <span>System Settings</span>
            </div>
            {expandedGroups.system ? <ChevronUp size={14} /> : <ChevronDown size={14} />}
          </div>
          
          {expandedGroups.system && (
            <div className="menu-group-sublist">
              <div 
                className={`sidebar-link ${activeView === 'system-settings' ? 'active' : ''}`}
                onClick={() => onViewChange('system-settings')}
              >
                <span>Settings</span>
              </div>
              <div 
                className={`sidebar-link ${activeView === 'system-logs' ? 'active' : ''}`}
                onClick={() => onViewChange('system-logs')}
              >
                <span>System Logs</span>
              </div>
            </div>
          )}
        </div>
      </div>

      <div className="sidebar-footer">
        <button className="footer-btn" onClick={() => onViewChange('documentation')} title="Documentation">
          <BookOpen size={18} />
        </button>
      </div>
    </aside>
  );
}
