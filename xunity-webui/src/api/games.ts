import { api } from './client'
import type { Game, UnityGameInfo, XUnityConfig, InstallationStatus } from './types'

export const gamesApi = {
  list: () => api.get<Game[]>('/api/games'),

  get: (id: string) => api.get<Game>(`/api/games/${id}`),

  add: (gamePath: string, name?: string) =>
    api.post<Game>('/api/games', { gamePath, name }),

  update: (id: string, data: { name?: string }) =>
    api.put<Game>(`/api/games/${id}`, data),

  remove: (id: string) => api.del<void>(`/api/games/${id}`),

  detect: (id: string) => api.post<UnityGameInfo>(`/api/games/${id}/detect`),

  getConfig: (id: string) => api.get<XUnityConfig>(`/api/games/${id}/config`),

  saveConfig: (id: string, config: XUnityConfig) =>
    api.put<XUnityConfig>(`/api/games/${id}/config`, config),

  install: (id: string, config?: XUnityConfig) =>
    api.post<InstallationStatus>(`/api/games/${id}/install`, { config }),

  uninstall: (id: string) => api.del<InstallationStatus>(`/api/games/${id}/install`),

  getStatus: (id: string) => api.get<InstallationStatus>(`/api/games/${id}/status`),

  cancel: (id: string) => api.post<void>(`/api/games/${id}/cancel`),
}

export const dialogApi = {
  selectFolder: () => api.post<string>('/api/dialog/select-folder'),
  selectFile: (filter?: string) => api.post<string>('/api/dialog/select-file', { filter }),
}

export const releasesApi = {
  getBepInEx: () => api.get<unknown[]>('/api/releases/bepinex'),
  getXUnity: () => api.get<unknown[]>('/api/releases/xunity'),
}
