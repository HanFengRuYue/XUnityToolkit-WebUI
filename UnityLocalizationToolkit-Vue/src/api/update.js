import { api } from './client';
export const updateApi = {
    check: () => api.get('/api/update/check'),
    status: () => api.get('/api/update/status'),
    download: () => api.post('/api/update/download'),
    cancel: () => api.post('/api/update/cancel'),
    apply: () => api.post('/api/update/apply'),
    dismiss: () => api.post('/api/update/dismiss'),
};
