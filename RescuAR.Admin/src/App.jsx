import React, { useEffect, useRef, useState } from 'react';

// Core Layout & Dashboards
import Header from './components/Header';
import Sidebar from './components/Sidebar';
import Dashboard from './components/Dashboard';

// Monitoring Components
import MonitoringStations from './components/MonitoringStations';
import RiverLevel from './components/RiverLevel';
import HazardExposure from './components/HazardExposure';

// Community & Reports Components
import ReportsModeration from './components/ReportsModeration';
import UserManagement from './components/UserManagement';
import SmsOutboundConsole from './components/SmsOutboundConsole';

// Content & Management Components
import Advisories from './components/Advisories';
import EvacuationCenters from './components/EvacuationCenters';
import EmergencyHotlines from './components/EmergencyHotlines';

// System Components
import SystemSettings from './components/SystemSettings';
import SystemLogs from './components/SystemLogs';
import Documentation from './components/Documentation';

// Modals
import {
  AdvisoryModal,
  NotifyModal
} from './components/Modals';

export default function App({ session }) {
  const [activeView, setActiveView] = useState('monitoring-stations');
  const [mobileNavigationOpen, setMobileNavigationOpen] = useState(false);
  const [mobileViewport, setMobileViewport] = useState(
    () => typeof window !== 'undefined' && window.matchMedia('(max-width: 991.98px)').matches
  );
  const menuButtonRef = useRef(null);

  // Keep the mobile drawer and keyboard interaction in sync with Bootstrap's lg breakpoint.
  useEffect(() => {
    const query = window.matchMedia('(max-width: 991.98px)');
    const updateViewport = () => {
      setMobileViewport(query.matches);
      if (!query.matches) setMobileNavigationOpen(false);
    };
    updateViewport();
    query.addEventListener('change', updateViewport);
    return () => query.removeEventListener('change', updateViewport);
  }, []);

  useEffect(() => {
    if (!mobileNavigationOpen || !mobileViewport) return undefined;
    const onKeyDown = (event) => {
      if (event.key === 'Escape') {
        setMobileNavigationOpen(false);
        menuButtonRef.current?.focus();
      }
    };
    document.addEventListener('keydown', onKeyDown);
    document.getElementById('admin-sidebar')?.querySelector('button')?.focus();
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [mobileNavigationOpen, mobileViewport]);

  const navigateTo = (view) => {
    setActiveView(view);
    setMobileNavigationOpen(false);
  };
  const workspaceRef = useRef(null);

  // Each page begins at the top of its own scrollable workspace.
  useEffect(() => {
    if (workspaceRef.current) workspaceRef.current.scrollTop = 0;
  }, [activeView]);

  // Modal states
  const [isAdvisoryOpen, setIsAdvisoryOpen] = useState(false);
  const [isNotifyOpen, setIsNotifyOpen] = useState(false);
  const [activeStationData, setActiveStationData] = useState(null);

  const handleActionClick = (actionType, data = null) => {
    if (actionType === 'advisory') {
      if (data) setActiveStationData(data);
      setIsAdvisoryOpen(true);
    } else if (actionType === 'notify') {
      setIsNotifyOpen(true);
    } else if (actionType === 'predict') {
      navigateTo('monitoring-hazard-exposure');
    }
  };

  // View router combining both configurations
  const renderView = () => {
    switch (activeView) {
      case 'dashboard':
        return <Dashboard onViewChange={navigateTo} onActionClick={handleActionClick} />;
      case 'monitoring-stations':
        return <MonitoringStations />;
      case 'monitoring-river-level':
        return <RiverLevel onActionClick={handleActionClick} />;
      case 'monitoring-hazard-exposure':
        return <HazardExposure />;
      case 'community-reports-moderation':
        return <ReportsModeration />;
      case 'community-residents':
        return <div style={{ padding: '20px' }}>Residents Directory View (Placeholder)</div>;
      case 'community-sms-parser':
        return <SmsOutboundConsole />;
      case 'community-user-management':
        return <UserManagement />;
      case 'content-advisories':
        return <Advisories onOpenAdvisoryModal={() => setIsAdvisoryOpen(true)} />;
      case 'content-evacuation':
      case 'content-news':
        return <EvacuationCenters />;
      case 'content-hotlines':
        return <EmergencyHotlines />;
      case 'system-settings':
        return <SystemSettings />;
      case 'system-logs':
        return <SystemLogs />;
      case 'documentation':
        return <Documentation />;
      default:
        return <MonitoringStations />;
    }
  };

  return (
    <div className="app-container">
      {/* Top Header */}
      <Header
        session={session}
        onMenuToggle={() => setMobileNavigationOpen((open) => !open)}
        isMenuOpen={mobileNavigationOpen}
        menuButtonRef={menuButtonRef}
      />

      {/* Main Layout Content */}
      <div className="app-content">
        {/* Sidebar Nav */}
        {mobileViewport && mobileNavigationOpen && (
          <button
            type="button"
            className="mobile-sidebar-backdrop"
            aria-label="Close navigation"
            onClick={() => setMobileNavigationOpen(false)}
          />
        )}
        <Sidebar
          activeView={activeView}
          onViewChange={navigateTo}
          isMobileViewport={mobileViewport}
          isMobileOpen={mobileNavigationOpen}
        />

        {/* Only the workspace scrolls; the header and sidebar stay stationary. */}
        <main className="app-workspace" ref={workspaceRef} id="admin-workspace">
          {renderView()}
        </main>
      </div>

      {/* Action Simulation Modals */}
      <AdvisoryModal
        isOpen={isAdvisoryOpen}
        onClose={() => setIsAdvisoryOpen(false)}
        stationData={activeStationData}
        onViewAdvisories={() => navigateTo('content-advisories')}
      />
      <NotifyModal
        isOpen={isNotifyOpen}
        onClose={() => setIsNotifyOpen(false)}
      />
    </div>
  );
}
