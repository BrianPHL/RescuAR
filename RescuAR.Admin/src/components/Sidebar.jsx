import React, { useState } from 'react';
import {canView} from '../rbac/access';
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

export default function Sidebar({ activeView, onViewChange, isMobileViewport = false, isMobileOpen = false, access }) {
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
    <aside
      id="admin-sidebar"
      className={`app-sidebar ${isMobileOpen ? 'mobile-open' : ''}`}
      aria-label="Primary navigation"
      aria-hidden={isMobileViewport && !isMobileOpen}
      inert={isMobileViewport && !isMobileOpen}
    >
      <div className="sidebar-menu-list">
        {/* Dashboard Link */}
        {canView(access,'dashboard') && (<button type="button"
          className={`sidebar-link ${activeView === 'dashboard' ? 'active' : ''}`}
          onClick={() => onViewChange('dashboard')}
        >
          <LayoutDashboard size={18} />
          <span>Dashboard</span>
        </button>)}

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
              {canView(access,'monitoring-stations') && (<button type="button"
                className={`sidebar-link ${activeView === 'monitoring-stations' ? 'active' : ''}`}
                onClick={() => onViewChange('monitoring-stations')}
              >
                <Radio size={16} />
                <span>Monitoring Stations</span>
              </button>)}
              {canView(access,'monitoring-river-level') && (<button type="button"
                className={`sidebar-link ${activeView === 'monitoring-river-level' ? 'active' : ''}`}
                onClick={() => onViewChange('monitoring-river-level')}
              >
                <Waves size={16} />
                <span>Marikina River Level</span>
              </button>)}
              {canView(access,'monitoring-hazard-exposure') && (<button type="button"
                className={`sidebar-link ${activeView === 'monitoring-hazard-exposure' ? 'active' : ''}`}
                onClick={() => onViewChange('monitoring-hazard-exposure')}
              >
                <AlertTriangle size={16} />
                <span>Flood Hazard Exposure</span>
              </button>)}
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
              {canView(access,'community-reports-moderation') && (<button type="button"
                className={`sidebar-link ${activeView === 'community-reports-moderation' ? 'active' : ''}`}
                onClick={() => onViewChange('community-reports-moderation')}
              >
                <span>Reports Moderation</span>
              </button>)}
              {canView(access,'community-user-management') && (<button type="button"
                className={`sidebar-link ${activeView === 'community-user-management' ? 'active' : ''}`}
                onClick={() => onViewChange('community-user-management')}
              >
                <span>User Management</span>
              </button>)}
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
              {canView(access,'content-advisories') && (<button type="button"
                className={`sidebar-link ${activeView === 'content-advisories' ? 'active' : ''}`}
                onClick={() => onViewChange('content-advisories')}
              >
                <span>Advisories</span>
              </button>)}
              {canView(access,'content-evacuation') && (<button type="button"
                className={`sidebar-link ${activeView === 'content-evacuation' || activeView === 'content-news' ? 'active' : ''}`}
                onClick={() => onViewChange('content-evacuation')}
              >
                <span>Evacuation Centers</span>
              </button>)}
              {canView(access,'content-hotlines') && (<button type="button"
                className={`sidebar-link ${activeView === 'content-hotlines' ? 'active' : ''}`}
                onClick={() => onViewChange('content-hotlines')}
              >
                <span>Emergency Hotlines</span>
              </button>)}
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
              {canView(access,'system-settings') && (<button type="button"
                className={`sidebar-link ${activeView === 'system-settings' ? 'active' : ''}`}
                onClick={() => onViewChange('system-settings')}
              >
                <span>Settings</span>
              </button>)}
              {canView(access,'system-logs') && (<button type="button"
                className={`sidebar-link ${activeView === 'system-logs' ? 'active' : ''}`}
                onClick={() => onViewChange('system-logs')}
              >
                <span>System Logs</span>
              </button>)}
            </div>
          </div>
        </div>
      </div>

      {canView(access,'rbac-research') && <button className="sidebar-link" type="button" onClick={()=>onViewChange('rbac-research')}>PREP Research</button>}
      {canView(access,'rbac-admins') && <button className="sidebar-link" type="button" onClick={()=>onViewChange('rbac-admins')}>Administrator Management</button>}
      {canView(access,'rbac-tests') && <button className="sidebar-link" type="button" onClick={()=>onViewChange('rbac-tests')}>Testing & Simulation</button>}
      <div className="sidebar-footer">
        {canView(access,'documentation') && (<button className="footer-btn" onClick={() => onViewChange('documentation')} title="Documentation">
          <BookOpen size={18} />
        </button>)}
      </div>
    </aside>
  );
}
