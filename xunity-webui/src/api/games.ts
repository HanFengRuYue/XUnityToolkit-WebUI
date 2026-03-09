import { api } from './client'
import type { Game, UnityGameInfo, XUnityConfig, InstallationStatus, CacheInfo, AppSettings, VersionInfo, AddGameResponse, ModFrameworkType } from './types'

export const gamesApi = {
  list: () => api.get<Game[]>('/api/games'),

  get: (id: string) => api.get<Game>(`/api/games/${id}`),

  add: (gamePath: string, name?: string) =>
    api.post<Game>('/api/games', { gamePath, name }),

  addWithDetection: (folderPath: string, exePath?: string) =>
    api.post<AddGameResponse>('/api/games/add-with-detection', { folderPath, exePath }),

  update: (id: string, data: { name?: string; executableName?: string }) =>
    api.put<Game>(`/api/games/${id}`, data),

  remove: (id: string) => api.del<void>(`/api/games/${id}`),

  detect: (id: string) => api.post<UnityGameInfo>(`/api/games/${id}/detect`),

  uninstallFramework: (id: string, framework: ModFrameworkType) =>
    api.del<Game>(`/api/games/${id}/framework/${framework}`),

  getConfig: (id: string) => api.get<XUnityConfig>(`/api/games/${id}/config`),

  saveConfig: (id: string, config: XUnityConfig) =>
    api.put<XUnityConfig>(`/api/games/${id}/config`, config),

  install: (id: string, config?: XUnityConfig) =>
    api.post<InstallationStatus>(`/api/games/${id}/install`, { config }),

  uninstall: (id: string) => api.del<InstallationStatus>(`/api/games/${id}/install`),

  getStatus: (id: string) => api.get<InstallationStatus>(`/api/games/${id}/status`),

  cancel: (id: string) => api.post<void>(`/api/games/${id}/cancel`),

  openFolder: (id: string) => api.post<void>(`/api/games/${id}/open-folder`),

  launch: (id: string) => api.post<void>(`/api/games/${id}/launch`),
}

export const dialogApi = {
  selectFolder: () => api.post<string>('/api/dialog/select-folder'),
  selectFile: (filter?: string) => api.post<string>('/api/dialog/select-file', { filter }),
}

export const releasesApi = {
  getBepInEx: () => api.get<unknown[]>('/api/releases/bepinex'),
  getXUnity: () => api.get<unknown[]>('/api/releases/xunity'),
}

export const cacheApi = {
  getInfo: () => api.get<CacheInfo>('/api/cache/downloads'),
  clear: () => api.del<CacheInfo>('/api/cache/downloads'),
}

export const settingsApi = {
  get: () => api.get<AppSettings>('/api/settings'),
  save: (settings: AppSettings) => api.put<AppSettings>('/api/settings', settings),
  getVersion: () => api.get<VersionInfo>('/api/settings/version'),
}
