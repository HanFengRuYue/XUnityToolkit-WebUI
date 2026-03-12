import { api } from './client'
import type { Game, UnityGameInfo, XUnityConfig, InstallationStatus, CacheInfo, AppSettings, VersionInfo, AddGameResponse, ModFrameworkType, TranslationStats, AiEndpointStatus, TmpFontStatus, GlossaryEntry, LlmProvider, ApiEndpointConfig, EndpointTestResult, SteamGridDbSearchResult, SteamGridDbImage, CoverInfo, SteamStoreSearchResult, GlossaryExtractionStats, LogEntry, AssetExtractionResult, PreTranslationStatus, TranslationEditorData, TranslationEntry, LocalLlmStatus, LocalLlmSettings, GpuInfo, BuiltInModelInfo, LocalModelEntry, LlamaStatus, LocalLlmTestResult } from './types'

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

  getTmpFontStatus: (id: string) => api.get<TmpFontStatus>(`/api/games/${id}/tmp-font`),
  installTmpFont: (id: string) => api.post<TmpFontStatus>(`/api/games/${id}/tmp-font`, {}),
  uninstallTmpFont: (id: string) => api.del<TmpFontStatus>(`/api/games/${id}/tmp-font`),

  getGlossary: (id: string) => api.get<GlossaryEntry[]>(`/api/games/${id}/glossary`),
  saveGlossary: (id: string, entries: GlossaryEntry[]) =>
    api.put<GlossaryEntry[]>(`/api/games/${id}/glossary`, entries),

  getDescription: (id: string) => api.get<string | null>(`/api/games/${id}/description`),
  saveDescription: (id: string, description: string | null) =>
    api.put<void>(`/api/games/${id}/description`, { description }),

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

export const assetApi = {
  extractAssets: (id: string) =>
    api.post<AssetExtractionResult>(`/api/games/${id}/extract-assets`, {}),
  getExtractedTexts: (id: string) =>
    api.get<AssetExtractionResult | null>(`/api/games/${id}/extracted-texts`),
  deleteExtractedTexts: (id: string) =>
    api.del<void>(`/api/games/${id}/extracted-texts`),
  startPreTranslation: (id: string, fromLang?: string, toLang?: string) =>
    api.post<PreTranslationStatus>(`/api/games/${id}/pre-translate`, { fromLang, toLang }),
  getPreTranslationStatus: (id: string) =>
    api.get<PreTranslationStatus>(`/api/games/${id}/pre-translate/status`),
  cancelPreTranslation: (id: string) =>
    api.post<void>(`/api/games/${id}/pre-translate/cancel`, {}),
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

export const translationEditorApi = {
  getEntries: (id: string) =>
    api.get<TranslationEditorData>(`/api/games/${id}/translation-editor`),
  saveEntries: (id: string, entries: TranslationEntry[]) =>
    api.put<void>(`/api/games/${id}/translation-editor`, { entries }),
  parseImport: (id: string, content: string) =>
    api.post<TranslationEntry[]>(`/api/games/${id}/translation-editor/import`, { content }),
  getExportUrl: (id: string) =>
    `/api/games/${id}/translation-editor/export`,
}

export const pluginPackageApi = {
  getExportUrl: (id: string) => `/api/games/${id}/plugin-package/export`,
  importPackage: (id: string, zipPath: string) =>
    api.post<void>(`/api/games/${id}/plugin-package/import`, { zipPath }),
}

export const localLlmApi = {
  test: () => api.post<LocalLlmTestResult>('/api/local-llm/test'),
  getStatus: () => api.get<LocalLlmStatus>('/api/local-llm/status'),
  getGpus: () => api.get<GpuInfo[]>('/api/local-llm/gpus'),
  refreshGpus: () => api.post<GpuInfo[]>('/api/local-llm/gpus/refresh'),
  getSettings: () => api.get<LocalLlmSettings>('/api/local-llm/settings'),
  saveSettings: (req: { gpuLayers: number; contextLength: number }) =>
    api.put<void>('/api/local-llm/settings', req),
  getCatalog: () => api.get<BuiltInModelInfo[]>('/api/local-llm/catalog'),
  getLlamaStatus: () => api.get<LlamaStatus>('/api/local-llm/llama-status'),
  downloadLlama: () => api.post<void>('/api/local-llm/download-llama'),
  pauseLlamaDownload: (downloadId: string) => api.post<void>('/api/local-llm/download-llama/pause', { downloadId }),
  cancelLlamaDownload: (downloadId: string, backend: string) =>
    api.post<void>('/api/local-llm/download-llama/cancel', { downloadId, backend }),
  start: (modelPath: string, gpuLayers?: number, contextLength?: number) =>
    api.post<LocalLlmStatus>('/api/local-llm/start', { modelPath, gpuLayers, contextLength }),
  stop: () => api.post<LocalLlmStatus>('/api/local-llm/stop'),
  downloadModel: (catalogId: string) => api.post<void>('/api/local-llm/download', { catalogId }),
  pauseDownload: (catalogId: string) => api.post<void>('/api/local-llm/download/pause', { catalogId }),
  cancelDownload: (catalogId: string) => api.post<void>('/api/local-llm/download/cancel', { catalogId }),
  getModels: () => api.get<LocalModelEntry[]>('/api/local-llm/models'),
  addModel: (filePath: string, name: string) => api.post<LocalModelEntry>('/api/local-llm/models/add', { filePath, name }),
  removeModel: (id: string) => api.del<void>(`/api/local-llm/models/${id}`),
}

export const logsApi = {
  getRecent: (count = 200) => api.get<LogEntry[]>(`/api/logs?count=${count}`),
  getHistory: (lines = 500) => api.get<LogEntry[]>(`/api/logs/history?lines=${lines}`),
  getDownloadUrl: () => '/api/logs/download',
}
