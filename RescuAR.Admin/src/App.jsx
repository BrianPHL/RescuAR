import React, { useState } from 'react';

// Core Layout & Dashboards
import Header from './components/Header';
import Sidebar from './components/Sidebar';
import Dashboard from './components/Dashboard';

// Monitoring Components
import MonitoringStations from './components/MonitoringStations';
import RiverLevel from './components/RiverLevel';
import InundationPrediction from './components/InundationPrediction';

// Community & Reports Components
import ReportsModeration from './components/ReportsModeration';
import UserManagement from './components/UserManagement';

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
  NotifyModal,
  PredictModal
} from './components/Modals';

export default function App({ session }) {
  const [activeView, setActiveView] = useState('monitoring-stations');

  // Modal states
  const [isAdvisoryOpen, setIsAdvisoryOpen] = useState(false);
  const [isNotifyOpen, setIsNotifyOpen] = useState(false);
  const [isPredictOpen, setIsPredictOpen] = useState(false);
  const [activeStationData, setActiveStationData] = useState(null);

  const handleActionClick = (actionType, data = null) => {
    if (actionType === 'advisory') {
      if (data) setActiveStationData(data);
      setIsAdvisoryOpen(true);
    } else if (actionType === 'notify') {
      setIsNotifyOpen(true);
    } else if (actionType === 'predict') {
      setIsPredictOpen(true);
    }
  };

  // View router combining both configurations
  const renderView = () => {
    switch (activeView) {
      case 'dashboard':
        return <Dashboard onViewChange={setActiveView} onActionClick={handleActionClick} />;
      case 'monitoring-stations':
        return <MonitoringStations />;
      case 'monitoring-river-level':
        return <RiverLevel onActionClick={handleActionClick} />;
      case 'monitoring-inundation':
        return <InundationPrediction />;
      case 'community-reports-moderation':
        return <ReportsModeration />;
      case 'community-residents':
        return <div style={{ padding: '20px' }}>Residents Directory View (Placeholder)</div>;
      case 'community-sms-parser':
        return <div style={{ padding: '20px' }}>SMS Parser Console View (Placeholder)</div>;
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
      <Header session={session} />

      {/* Main Layout Content */}
      <div className="app-content">
        {/* Sidebar Nav */}
        <Sidebar activeView={activeView} onViewChange={setActiveView} />

        {/* Dynamic Main Workspace Panel */}
        {renderView()}
      </div>

      {/* Action Simulation Modals */}
      <AdvisoryModal
        isOpen={isAdvisoryOpen}
        onClose={() => setIsAdvisoryOpen(false)}
        stationData={activeStationData}
        onViewAdvisories={() => setActiveView('content-advisories')}
      />
      <NotifyModal
        isOpen={isNotifyOpen}
        onClose={() => setIsNotifyOpen(false)}
      />
      <PredictModal
        isOpen={isPredictOpen}
        onClose={() => setIsPredictOpen(false)}
      />
    </div>
  );
}
