import { api } from '@/api/client'
import type { DriveEntry, ListDirectoryResponse } from '@/api/types'

export const filesystemApi = {
  getDrives: () => api.get<DriveEntry[]>('/api/filesystem/drives'),
  listDirectory: (path: string) =>
    api.post<ListDirectoryResponse>('/api/filesystem/list', { path }),
}
