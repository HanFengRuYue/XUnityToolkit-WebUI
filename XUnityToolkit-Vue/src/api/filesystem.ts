import { api } from '@/api/client'
import type { DriveEntry, ListDirectoryResponse, QuickAccessEntry, ReadTextResponse } from '@/api/types'

export const filesystemApi = {
  getDrives: () => api.get<DriveEntry[]>('/api/filesystem/drives'),
  getQuickAccess: () => api.get<QuickAccessEntry[]>('/api/filesystem/quick-access'),
  listDirectory: (path: string) =>
    api.post<ListDirectoryResponse>('/api/filesystem/list', { path }),
  readText: (path: string) =>
    api.post<ReadTextResponse>('/api/filesystem/read-text', { path }),
}
