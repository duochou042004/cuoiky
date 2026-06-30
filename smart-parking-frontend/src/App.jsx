import React, { useState } from 'react';
import { Routes, Route, Navigate } from 'react-router-dom';
import Sidebar from './components/Sidebar';
import Dashboard from './pages/Dashboard';
import CheckIn from './pages/CheckIn';
import CheckOut from './pages/CheckOut';
import CameraMonitoring from './pages/CameraMonitoring';
import MomoPaymentReturn from './pages/MomoPaymentReturn';
import Reports from './pages/Reports';
import MonthlyRegistration from './pages/MonthlyRegistration';
import AccessControl from './pages/AccessControl';
import Settings from './pages/Settings';
import Login from './pages/Login';
import ForgotPassword from './pages/ForgotPassword';
import ResetPassword from './pages/ResetPassword';
import ProtectedRoute from './components/ProtectedRoute';
import ErrorBoundary from './components/ErrorBoundary';
import { AuthProvider, useAuth } from './contexts/AuthContext';
import './App.css';

// Main App component wrapped with AuthProvider
function AppWithAuth() {
  return (
    <AuthProvider>
      <AppContent />
    </AuthProvider>
  );
}

// App content with authentication state
function AppContent() {
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false);
  const { isAuthenticated, user, loading, login } = useAuth();

  const handleSidebarToggle = (collapsed) => {
    setSidebarCollapsed(collapsed);
  };

  // Handle login
  const handleLogin = (authData) => {
    // This will be passed to the Login component
    login(authData);
  };

  // If still loading auth state, don't render anything yet
  if (loading) {
    return <div className="loading-spinner">Loading...</div>;
  }

  return (
    <div className="app">
      {/* Only show sidebar when authenticated */}
      {isAuthenticated && <Sidebar onToggle={handleSidebarToggle} userRole={user?.role} />}

      <div className={`content ${isAuthenticated ? (sidebarCollapsed ? 'sidebar-collapsed' : '') : 'no-sidebar'}`}>
        <ErrorBoundary>
        <Routes>
          {/* Public routes */}
          <Route path="/login" element={!isAuthenticated ? <Login onLogin={handleLogin} /> : <Navigate to="/" replace />} />
          <Route path="/forgot-password" element={!isAuthenticated ? <ForgotPassword /> : <Navigate to="/" replace />} />
          <Route path="/reset-password" element={!isAuthenticated ? <ResetPassword /> : <Navigate to="/" replace />} />

          {/* Special route that doesn't require authentication */}
          <Route path="/payment/momo/return" element={<MomoPaymentReturn />} />

          {/* Protected routes for all authenticated users */}
          <Route element={<ProtectedRoute />}>
            <Route path="/" element={<Dashboard />} />
            <Route path="/access-control" element={<AccessControl />} />
            <Route path="/check-in" element={<CheckIn />} />
            <Route path="/check-out" element={<CheckOut />} />
            <Route path="/cameras" element={<CameraMonitoring />} />
            <Route path="/reports" element={<Reports />} />
            <Route path="/monthly-registration" element={<MonthlyRegistration />} />
          </Route>

          {/* Admin-only routes */}
          <Route element={<ProtectedRoute requiredRole="ADMIN" />}>
            <Route path="/settings" element={<Settings />} />
          </Route>

          {/* Redirect to login if not authenticated and trying to access protected route */}
          <Route path="*" element={isAuthenticated ? <Navigate to="/" /> : <Navigate to="/login" />} />
        </Routes>
        </ErrorBoundary>
      </div>
    </div>
  );
}

export default AppWithAuth;
