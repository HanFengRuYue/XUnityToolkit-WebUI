import { api } from './client'
import type { Game, UnityGameInfo, XUnityConfig, InstallationStatus, CacheInfo, AppSettings, VersionInfo, AddGameResponse, ModFrameworkType, TranslationStats, AiEndpointStatus, GlossaryEntry, LlmProvider, ApiEndpointConfig, EndpointTestResult, SteamGridDbSearchResult, SteamGridDbImage, CoverInfo, SteamStoreSearchResult, GlossaryExtractionStats, LogEntry } from './types'

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

  getRawConfig: (id: string) =>
    api.get<string>(`/api/games/${id}/config/raw`),

  saveRawConfig: (id: string, content: string) =>
    api.put<string>(`/api/games/${id}/config/raw`, { content }),

  install: (id: string, config?: XUnityConfig) =>
    api.post<InstallationStatus>(`/api/games/${id}/install`, { config }),

  uninstall: (id: string) => api.del<InstallationStatus>(`/api/games/${id}/install`),

  getStatus: (id: string) => api.get<InstallationStatus>(`/api/games/${id}/status`),

  cancel: (id: string) => api.post<void>(`/api/games/${id}/cancel`),

  openFolder: (id: string) => api.post<void>(`/api/games/${id}/open-folder`),

  launch: (id: string) => api.post<void>(`/api/games/${id}/launch`),

  getAiEndpointStatus: (id: string) => api.get<AiEndpointStatus>(`/api/games/${id}/ai-endpoint`),
  installAiEndpoint: (id: string) => api.post<AiEndpointStatus>(`/api/games/${id}/ai-endpoint`, {}),
  uninstallAiEndpoint: (id: string) => api.del<AiEndpointStatus>(`/api/games/${id}/ai-endpoint`),

  getGlossary: (id: string) => api.get<GlossaryEntry[]>(`/api/games/${id}/glossary`),
  saveGlossary: (id: string, entries: GlossaryEntry[]) =>
    api.put<GlossaryEntry[]>(`/api/games/${id}/glossary`, entries),

  // Icon
  getIconUrl: (id: string) => `/api/games/${id}/icon`,
  uploadIcon: async (id: string, file: File) => {
    const formData = new FormData()
    formData.append('icon', file)
    const resp = await fetch(`/api/games/${id}/icon/upload`, {
      method: 'POST',
      body: formData,
    })
    if (!resp.ok) {
      const text = await resp.text()
      let message = `HTTP ${resp.status}`
      try { const json = JSON.parse(text); if (json.error) message = json.error } catch { /* ignore */ }
      throw new Error(message)
    }
  },
  deleteCustomIcon: (id: string) => api.del<void>(`/api/games/${id}/icon/custom`),

  // Cover image
  getCoverUrl: (id: string) => `/api/games/${id}/cover`,
  deleteCover: (id: string) => api.del<void>(`/api/games/${id}/cover`),
  uploadCover: async (id: string, file: File) => {
    const formData = new FormData()
    formData.append('cover', file)
    const resp = await fetch(`/api/games/${id}/cover/upload`, {
      method: 'POST',
      body: formData,
    })
    if (!resp.ok) {
      const text = await resp.text()
      let message = `HTTP ${resp.status}`
      try { const json = JSON.parse(text); if (json.error) message = json.error } catch { /* ignore */ }
      throw new Error(message)
    }
    const result = await resp.json()
    return result.data as CoverInfo
  },
  searchCovers: (id: string, query: string) =>
    api.post<SteamGridDbSearchResult[]>(`/api/games/${id}/cover/search`, { query }),
  getCoverGrids: (id: string, steamGridDbGameId: number) =>
    api.post<SteamGridDbImage[]>(`/api/games/${id}/cover/grids`, { steamGridDbGameId }),
  selectCover: (id: string, imageUrl: string, steamGridDbGameId: number) =>
    api.post<CoverInfo>(`/api/games/${id}/cover/select`, { imageUrl, steamGridDbGameId }),
  searchSteamGames: (id: string, query: string) =>
    api.post<SteamStoreSearchResult[]>(`/api/games/${id}/cover/steam-search`, { query }),
  selectSteamCover: (id: string, steamAppId: number) =>
    api.post<CoverInfo>(`/api/games/${id}/cover/steam-select`, { steamAppId }),
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
  reset: () => api.post<{ partial: boolean; errors?: string[] }>('/api/settings/reset'),
}

export const translateApi = {
  /** POST /api/translate returns raw TranslateResponse (not ApiResult) since it's also called by the in-game DLL */
  translate: async (texts: string[], from: string, to: string) => {
    const resp = await fetch('/api/translate', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ texts, from, to }),
    })
    if (!resp.ok) {
      const text = await resp.text()
      let message = `HTTP ${resp.status}`
      try { const json = JSON.parse(text); if (json.error) message = json.error } catch { /* ignore */ }
      throw new Error(message)
    }
    return (await resp.json()) as { translations: string[] }
  },
  getStats: () => api.get<TranslationStats>('/api/translate/stats'),
  toggle: (enabled: boolean) => api.post<boolean>('/api/ai/toggle', { enabled }),
  fetchModels: (provider: LlmProvider, apiBaseUrl: string, apiKey: string) =>
    api.get<string[]>(`/api/ai/models?provider=${provider}&apiBaseUrl=${encodeURIComponent(apiBaseUrl)}&apiKey=${encodeURIComponent(apiKey)}`),
  testTranslate: (endpoints: ApiEndpointConfig[], systemPrompt: string, temperature: number) =>
    api.post<EndpointTestResult[]>('/api/translate/test', { endpoints, systemPrompt, temperature }),
  getExtractionStats: () => api.get<GlossaryExtractionStats>('/api/ai/extraction/stats'),
}

export const logsApi = {
  getRecent: (count = 200) => api.get<LogEntry[]>(`/api/logs?count=${count}`),
  getHistory: (lines = 500) => api.get<LogEntry[]>(`/api/logs/history?lines=${lines}`),
  getDownloadUrl: () => '/api/logs/download',
}
