export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || '';

const streamingBaseUrl = import.meta.env.VITE_STREAMING_API_BASE_URL || 'http://localhost:4051';
export const STREAMING_API_BASE_URL = streamingBaseUrl.replace(/\/$/, '');
