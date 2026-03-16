import { api } from './client'
import type { UpdateCheckResult, UpdateStatusInfo } from './types'

export const updateApi = {
  check: () => api.get<UpdateCheckResult>('/api/update/check'),
  download: () => api.post<void>('/api/update/download'),
  cancel: () => api.post<void>('/api/update/cancel'),
  apply: () => api.post<string>('/api/update/apply'),
  dismiss: () => api.post<void>('/api/update/dismiss'),
}
