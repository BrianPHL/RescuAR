import React, { useState } from 'react';
import { 
  LayoutDashboard, 
  Activity, 
  ChevronDown, 
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
  // Determine initial active group from activeView
  const getInitialGroup = () => {
    if (activeView.startsWith('monitoring-')) return 'monitoring';
    if (activeView.startsWith('community-')) return 'community';
    if (activeView.startsWith('content-')) return 'content';
    if (activeView.startsWith('system-')) return 'system';
    return 'monitoring';
  };

  const [expandedGroups, setExpandedGroups] = useState({
    monitoring: getInitialGroup() === 'monitoring',
    community: getInitialGroup() === 'community',
    content: getInitialGroup() === 'content',
    system: getInitialGroup() === 'system'
  });

  // Auto-expand group when activeView changes externally
  React.useEffect(() => {
    const activeGroup = getInitialGroup();
    setExpandedGroups({
      monitoring: activeGroup === 'monitoring',
      community: activeGroup === 'community',
      content: activeGroup === 'content',
      system: activeGroup === 'system'
    });
  }, [activeView]);

  const toggleGroup = (group) => {
    setExpandedGroups(prev => {
      const nextState = {
        monitoring: false,
        community: false,
        content: false,
        system: false
      };
      nextState[group] = !prev[group];
      return nextState;
    });
  };

  return (
    <aside className="app-sidebar" aria-label="Primary navigation">
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
          <button
            type="button"
            className={`menu-group-header ${activeView.startsWith('monitoring-') ? 'active' : ''}`}
            onClick={() => toggleGroup('monitoring')}
            aria-expanded={expandedGroups.monitoring}
            aria-controls="sidebar-monitoring-links"
          >
            <div className="menu-group-title">
              <Activity size={18} />
              <span>Monitoring</span>
            </div>
            <ChevronDown size={14} className={`sidebar-chevron ${expandedGroups.monitoring ? 'expanded' : ''}`} />
          </button>
          
          <div
            id="sidebar-monitoring-links"
            className={`sidebar-accordion ${expandedGroups.monitoring ? 'expanded' : ''}`}
            aria-hidden={!expandedGroups.monitoring}
            inert={!expandedGroups.monitoring}
          >
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
          </div>
        </div>

        {/* Community Management Section */}
        <div>
          <button
            type="button"
            className={`menu-group-header ${activeView.startsWith('community-') ? 'active' : ''}`}
            onClick={() => toggleGroup('community')}
            aria-expanded={expandedGroups.community}
            aria-controls="sidebar-community-links"
          >
            <div className="menu-group-title">
              <Users size={18} />
              <span>Community Management</span>
            </div>
            <ChevronDown size={14} className={`sidebar-chevron ${expandedGroups.community ? 'expanded' : ''}`} />
          </button>
          
          <div
            id="sidebar-community-links"
            className={`sidebar-accordion ${expandedGroups.community ? 'expanded' : ''}`}
            aria-hidden={!expandedGroups.community}
            inert={!expandedGroups.community}
          >
            <div className="menu-group-sublist">
              <div 
                className={`sidebar-link ${activeView === 'community-reports-moderation' ? 'active' : ''}`}
                onClick={() => onViewChange('community-reports-moderation')}
              >
                <span>Reports Moderation</span>
              </div>
              <div 
                className={`sidebar-link ${activeView === 'community-user-management' ? 'active' : ''}`}
                onClick={() => onViewChange('community-user-management')}
              >
                <span>User Management</span>
              </div>
            </div>
          </div>
        </div>

        {/* Content Management Section */}
        <div>
          <button
            type="button"
            className={`menu-group-header ${activeView.startsWith('content-') ? 'active' : ''}`}
            onClick={() => toggleGroup('content')}
            aria-expanded={expandedGroups.content}
            aria-controls="sidebar-content-links"
          >
            <div className="menu-group-title">
              <Rss size={18} />
              <span>Content Management</span>
            </div>
            <ChevronDown size={14} className={`sidebar-chevron ${expandedGroups.content ? 'expanded' : ''}`} />
          </button>
          
          <div
            id="sidebar-content-links"
            className={`sidebar-accordion ${expandedGroups.content ? 'expanded' : ''}`}
            aria-hidden={!expandedGroups.content}
            inert={!expandedGroups.content}
          >
            <div className="menu-group-sublist">
              <div 
                className={`sidebar-link ${activeView === 'content-advisories' ? 'active' : ''}`}
                onClick={() => onViewChange('content-advisories')}
              >
                <span>Advisories</span>
              </div>
              <div 
                className={`sidebar-link ${activeView === 'content-evacuation' || activeView === 'content-news' ? 'active' : ''}`}
                onClick={() => onViewChange('content-evacuation')}
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
          </div>
        </div>

        {/* System Section */}
        <div>
          <button
            type="button"
            className={`menu-group-header ${activeView.startsWith('system-') ? 'active' : ''}`}
            onClick={() => toggleGroup('system')}
            aria-expanded={expandedGroups.system}
            aria-controls="sidebar-system-links"
          >
            <div className="menu-group-title">
              <Settings size={18} />
              <span>System Settings</span>
            </div>
            <ChevronDown size={14} className={`sidebar-chevron ${expandedGroups.system ? 'expanded' : ''}`} />
          </button>
          
          <div
            id="sidebar-system-links"
            className={`sidebar-accordion ${expandedGroups.system ? 'expanded' : ''}`}
            aria-hidden={!expandedGroups.system}
            inert={!expandedGroups.system}
          >
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
          </div>
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
