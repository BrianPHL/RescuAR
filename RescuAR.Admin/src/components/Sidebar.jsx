import React, { useState } from 'react';
import {canView} from '../rbac/access';
import './SidebarRbac.css';
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
  Gavel,
  FlaskConical,
  UserRoundCog
} from 'lucide-react';

export default function Sidebar({ activeView, onViewChange, isMobileViewport = false, isMobileOpen = false, access }) {
  // Determine initial active group from activeView
  const getInitialGroup = () => {
    if (activeView.startsWith('monitoring-') || activeView === 'rbac-tests') return 'monitoring';
    if (activeView.startsWith('community-') || activeView === 'rbac-admins') return 'community';
    if (activeView.startsWith('content-')) return 'content';
    if (activeView.startsWith('system-')) return 'system';
    return 'monitoring';
  };

  const [expandedNested, setExpandedNested] = useState({
    hazard: activeView === 'rbac-tests' || activeView === 'monitoring-hazard-exposure',
    users: activeView === 'rbac-admins' || activeView === 'community-user-management'
  });

  const [expandedGroups, setExpandedGroups] = useState({
    monitoring: getInitialGroup() === 'monitoring',
    community: getInitialGroup() === 'community',
    content: getInitialGroup() === 'content',
    system: getInitialGroup() === 'system'
  });

  // Auto-expand group when activeView changes externally
  React.useEffect(() => {
    const activeGroup = getInitialGroup();
    if (activeView === 'rbac-tests' || activeView === 'monitoring-hazard-exposure') setExpandedNested((previous) => ({ ...previous, hazard: true }));
    if (activeView === 'rbac-admins' || activeView === 'community-user-management') setExpandedNested((previous) => ({ ...previous, users: true }));
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

  const toggleNested = (item) => {
    setExpandedNested((previous) => ({ ...previous, [item]: !previous[item] }));
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
        {['monitoring-stations', 'monitoring-river-level', 'monitoring-hazard-exposure', 'rbac-tests'].some((view) => canView(access, view)) && (<div>
          <button
            type="button"
            className={`menu-group-header ${activeView.startsWith('monitoring-') || activeView === 'rbac-tests' ? 'active' : ''}`}
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
              {(canView(access, 'monitoring-hazard-exposure') || canView(access, 'rbac-tests')) && (
                <div className="sidebar-nested-group">
                  <div className="sidebar-parent-row">
                    {canView(access, 'monitoring-hazard-exposure') && (
                      <button type="button" className={`sidebar-link sidebar-parent-nav ${activeView === 'monitoring-hazard-exposure' ? 'active' : ''}`} onClick={() => onViewChange('monitoring-hazard-exposure')}>
                        <AlertTriangle size={16} /><span>Flood Hazard Exposure</span>
                      </button>
                    )}
                    {canView(access, 'rbac-tests') && (
                      <button type="button" className="sidebar-nested-toggle" aria-label="Toggle Flood Hazard Exposure subpages" aria-expanded={expandedNested.hazard} aria-controls="sidebar-hazard-subpages" onClick={() => toggleNested('hazard')}>
                        <ChevronDown size={13} className={`sidebar-chevron ${expandedNested.hazard ? 'expanded' : ''}`} />
                      </button>
                    )}
                  </div>
                  {canView(access, 'rbac-tests') && (
                    <div className={`sidebar-accordion sidebar-nested-accordion ${expandedNested.hazard ? 'expanded' : ''}`} id="sidebar-hazard-subpages" aria-hidden={!expandedNested.hazard} inert={!expandedNested.hazard}>
                      <div className="menu-group-sublist sidebar-nested-sublist">
                        <button type="button" className={`sidebar-link sidebar-nested-link ${activeView === 'rbac-tests' ? 'active' : ''}`} onClick={() => onViewChange('rbac-tests')}><FlaskConical size={15} /><span>Testing &amp; Simulation</span></button>
                      </div>
                    </div>
                  )}
                </div>
              )}
            </div>
          </div>
        </div>)}

        {/* Community Management Section */}
        {['community-reports-moderation', 'community-user-management', 'rbac-admins'].some((view) => canView(access, view)) && (<div>
          <button
            type="button"
            className={`menu-group-header ${activeView.startsWith('community-') || activeView === 'rbac-admins' ? 'active' : ''}`}
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
              {(canView(access, 'community-user-management') || canView(access, 'rbac-admins')) && (
                <div className="sidebar-nested-group">
                  <div className="sidebar-parent-row">
                    {canView(access, 'community-user-management') && (
                      <button type="button" className={`sidebar-link sidebar-parent-nav ${activeView === 'community-user-management' ? 'active' : ''}`} onClick={() => onViewChange('community-user-management')}><span>User Management</span></button>
                    )}
                    {canView(access, 'rbac-admins') && (
                      <button type="button" className="sidebar-nested-toggle" aria-label="Toggle User Management subpages" aria-expanded={expandedNested.users} aria-controls="sidebar-user-subpages" onClick={() => toggleNested('users')}>
                        <ChevronDown size={13} className={`sidebar-chevron ${expandedNested.users ? 'expanded' : ''}`} />
                      </button>
                    )}
                  </div>
                  {canView(access, 'rbac-admins') && (
                    <div className={`sidebar-accordion sidebar-nested-accordion ${expandedNested.users ? 'expanded' : ''}`} id="sidebar-user-subpages" aria-hidden={!expandedNested.users} inert={!expandedNested.users}>
                      <div className="menu-group-sublist sidebar-nested-sublist">
                        <button type="button" className={`sidebar-link sidebar-nested-link ${activeView === 'rbac-admins' ? 'active' : ''}`} onClick={() => onViewChange('rbac-admins')}><UserRoundCog size={15} /><span>Administrator Management</span></button>
                      </div>
                    </div>
                  )}
                </div>
              )}
            </div>
          </div>
        </div>)}

        {/* Content Management Section */}
        {['content-advisories', 'content-evacuation', 'content-hotlines'].some((view) => canView(access, view)) && (<div>
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
        </div>)}

        {/* System Section */}
        {['system-settings', 'system-logs'].some((view) => canView(access, view)) && (<div>
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
        </div>)}
      </div>

      <div className="sidebar-footer">
        {canView(access,'documentation') && (<button className="footer-btn" onClick={() => onViewChange('documentation')} title="Documentation">
          <BookOpen size={18} />
        </button>)}
      </div>
    </aside>
  );
}
