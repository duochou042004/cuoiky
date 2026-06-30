import React, { createContext, useState, useEffect, useContext, useRef } from 'react';
import axios from 'axios';
import { toast } from 'react-toastify';

// Create the context
const AuthContext = createContext();

// Custom hook to use the auth context
export const useAuth = () => {
  return useContext(AuthContext);
};

// Decode the `exp` claim (seconds since epoch) from a JWT without a library.
// Returns the expiry time in milliseconds, or null if it cannot be parsed.
const getTokenExpiryMs = (token) => {
  try {
    const payload = token.split('.')[1];
    if (!payload) return null;
    const normalized = payload.replace(/-/g, '+').replace(/_/g, '/');
    const decoded = JSON.parse(atob(normalized));
    return decoded.exp ? decoded.exp * 1000 : null;
  } catch {
    return null;
  }
};

// Warn the operator this long before the token expires.
const EXPIRY_WARNING_LEAD_MS = 5 * 60 * 1000; // 5 minutes

// Provider component
export const AuthProvider = ({ children }) => {
  const [authState, setAuthState] = useState({
    isAuthenticated: false,
    user: null,
    loading: true
  });

  // Timers for the pre-expiry warning and the auto-logout at expiry.
  const warningTimerRef = useRef(null);
  const logoutTimerRef = useRef(null);

  // Initialize auth state from localStorage on component mount
  useEffect(() => {
    const initializeAuth = () => {
      try {
        const token = localStorage.getItem('token');
        const userStr = localStorage.getItem('user');

        if (token && userStr) {
          // Try to parse the user JSON
          let user;
          try {
            user = JSON.parse(userStr);
          } catch (e) {
            console.error('Failed to parse user data from localStorage:', e);
            throw new Error('Invalid user data');
          }

          // Validate user object
          if (!user || typeof user !== 'object') {
            throw new Error('Invalid user data format');
          }

          // Set the authorization header for all future requests
          axios.defaults.headers.common['Authorization'] = `Bearer ${token}`;

          setAuthState({
            isAuthenticated: true,
            user: user,
            loading: false
          });
        } else {
          throw new Error('Missing authentication data');
        }
      } catch (error) {
        // Clear any potentially invalid auth data
        console.log('Auth initialization failed:', error.message);
        localStorage.removeItem('token');
        localStorage.removeItem('user');
        delete axios.defaults.headers.common['Authorization'];

        setAuthState({
          isAuthenticated: false,
          user: null,
          loading: false
        });
      }
    };

    initializeAuth();
  }, []);

  // Login function
  const login = (authData) => {
    setAuthState({
      isAuthenticated: true,
      user: authData.user,
      loading: false
    });
  };

  // Logout function
  const logout = () => {
    // Clear auth data from localStorage
    localStorage.removeItem('token');
    localStorage.removeItem('user');

    // Remove authorization header
    delete axios.defaults.headers.common['Authorization'];

    // Update auth state
    setAuthState({
      isAuthenticated: false,
      user: null,
      loading: false
    });
  };

  // Schedule a warning toast before the JWT expires and an auto-logout at expiry.
  // Re-runs whenever the authentication state flips so a fresh login re-arms the timers.
  useEffect(() => {
    const clearTimers = () => {
      if (warningTimerRef.current) clearTimeout(warningTimerRef.current);
      if (logoutTimerRef.current) clearTimeout(logoutTimerRef.current);
      warningTimerRef.current = null;
      logoutTimerRef.current = null;
    };

    if (!authState.isAuthenticated) {
      clearTimers();
      return;
    }

    const token = localStorage.getItem('token');
    const expiryMs = token ? getTokenExpiryMs(token) : null;
    if (!expiryMs) return undefined;

    const msUntilExpiry = expiryMs - Date.now();

    // Already expired — log out immediately.
    if (msUntilExpiry <= 0) {
      logout();
      return undefined;
    }

    const msUntilWarning = msUntilExpiry - EXPIRY_WARNING_LEAD_MS;
    if (msUntilWarning > 0) {
      warningTimerRef.current = setTimeout(() => {
        const minutesLeft = Math.max(1, Math.round((expiryMs - Date.now()) / 60000));
        toast.warning(
          `Phiên đăng nhập sẽ hết hạn sau khoảng ${minutesLeft} phút. Vui lòng lưu công việc và đăng nhập lại.`,
          { autoClose: false }
        );
      }, msUntilWarning);
    }

    logoutTimerRef.current = setTimeout(() => {
      toast.error('Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.');
      logout();
    }, msUntilExpiry);

    return clearTimers;
  }, [authState.isAuthenticated]);

  // Update user function
  const updateUser = (userData) => {
    const updatedUser = { ...authState.user, ...userData };
    localStorage.setItem('user', JSON.stringify(updatedUser));

    setAuthState({
      ...authState,
      user: updatedUser
    });
  };

  // Context value
  const value = {
    ...authState,
    login,
    logout,
    updateUser
  };

  return (
    <AuthContext.Provider value={value}>
      {children}
    </AuthContext.Provider>
  );
};

export default AuthContext;
