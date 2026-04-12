import { api } from '@/api/client';
export const filesystemApi = {
    getDrives: () => api.get('/api/filesystem/drives'),
    getQuickAccess: () => api.get('/api/filesystem/quick-access'),
    listDirectory: (path) => api.post('/api/filesystem/list', { path }),
    readText: (path) => api.post('/api/filesystem/read-text', { path }),
};
