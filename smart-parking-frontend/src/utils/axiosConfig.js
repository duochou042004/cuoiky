import axios from 'axios';
import { toast } from 'react-toastify';

// Single source of truth for axios configuration.
//
// We configure the GLOBAL axios default instance (rather than a separate
// `axios.create()` instance) so that every component which does
// `import axios from 'axios'` automatically gets the auth token and the
// 401-handling below — no per-file rewiring required. Import this module
// once for its side effects (see main.jsx).

// Same-origin: requests go through the Vite dev proxy / the deployed host.
axios.defaults.baseURL = '';
axios.defaults.timeout = 30000; // 30 seconds

// Request interceptor — attach the bearer token if present.
axios.interceptors.request.use(
  (config) => {
    const token = localStorage.getItem('token');
    if (token) {
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  },
  (error) => Promise.reject(error)
);

// Response interceptor — on 401, clear the session and bounce to login.
axios.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response && error.response.status === 401) {
      localStorage.removeItem('token');
      localStorage.removeItem('user');
      delete axios.defaults.headers.common['Authorization'];

      toast.error('Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.');

      // Avoid redirect loops if we are already on the login page.
      if (window.location.pathname !== '/login') {
        window.location.href = '/login';
      }
    }
    return Promise.reject(error);
  }
);

export default axios;
