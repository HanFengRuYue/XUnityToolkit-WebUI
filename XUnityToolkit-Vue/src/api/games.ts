import { api } from './client'
import type { Game, UnityGameInfo, XUnityConfig, InstallationStatus, InstallOptions, AppSettings, VersionInfo, DataPathInfo, AddGameResponse, BatchAddResult, ModFrameworkType, TranslationStats, AiEndpointStatus, TmpFontStatus, TermEntry, LlmProvider, ApiEndpointConfig, EndpointTestResult, SteamGridDbSearchResult, SteamGridDbImage, CoverInfo, SteamStoreSearchResult, WebImageResult, GlossaryExtractionStats, LogEntry, AssetExtractionResult, PreTranslationStatus, TranslationEditorData, TranslationEntry, LocalLlmStatus, LocalLlmSettings, GpuInfo, BuiltInModelInfo, LocalModelEntry, LlamaStatus, LocalLlmTestResult, BepInExLogResponse, BepInExLogAnalysis, ScriptTagConfig, ScriptTagPreset, DynamicPatternStore, TermCandidateStore, PluginHealthReport, BepInExPlugin } from './types'

export const gamesApi = {
  list: () => api.get<Game[]>('/api/games'),

  get: (id: string) => api.get<Game>(`/api/games/${id}`),

  add: (gamePath: string, name?: string) =>
    api.post<Game>('/api/games', { gamePath, name }),

  addWithDetection: (folderPath: string, exePath?: string) =>
    api.post<AddGameResponse>('/api/games/add-with-detection', { folderPath, exePath }),

  batchAdd: (parentFolderPath: string) =>
    api.post<BatchAddResult>('/api/games/batch-add', { parentFolderPath }),

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

  install: (id: string, config?: XUnityConfig, options?: InstallOptions) =>
    api.post<InstallationStatus>(`/api/games/${id}/install`, { config, options }),

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

  // Unified term management
  getTerms: (id: string) => api.get<TermEntry[]>(`/api/games/${id}/terms`),
  saveTerms: (id: string, entries: TermEntry[]) => api.put(`/api/games/${id}/terms`, entries),
  importTermsFromGame: (id: string, sourceGameId: string) =>
    api.post<{ added: number; skipped: number }>(`/api/games/${id}/terms/import-from-game`, { sourceGameId }),

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
  uploadIconFromPath: (id: string, filePath: string) =>
    api.post<void>(`/api/games/${id}/icon/upload-from-path`, { filePath }),
  deleteCustomIcon: (id: string) => api.del<void>(`/api/games/${id}/icon/custom`),
  searchIconGames: (id: string, query: string) =>
    api.post<SteamGridDbSearchResult[]>(`/api/games/${id}/icon/search`, { query }),
  getIconGrids: (id: string, steamGridDbGameId: number) =>
    api.post<SteamGridDbImage[]>(`/api/games/${id}/icon/grids`, { steamGridDbGameId }),
  selectSteamGridDbIcon: (id: string, imageUrl: string, steamGridDbGameId: number) =>
    api.post<void>(`/api/games/${id}/icon/select`, { imageUrl, steamGridDbGameId }),

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

  // Web image search
  searchWebImages: (id: string, query: string, engine: string, sizeFilter?: string) =>
    api.post<WebImageResult[]>(`/api/games/${id}/cover/web-search`, { query, engine, sizeFilter }),
  uploadCoverFromPath: (id: string, filePath: string) =>
    api.post<CoverInfo>(`/api/games/${id}/cover/upload-from-path`, { filePath }),
  selectWebCover: (id: string, imageUrl: string) =>
    api.post<CoverInfo>(`/api/games/${id}/cover/web-select`, { imageUrl }),
  searchWebIconImages: (id: string, query: string, engine: string, sizeFilter?: string) =>
    api.post<WebImageResult[]>(`/api/games/${id}/icon/web-search`, { query, engine, sizeFilter }),
  selectWebIcon: (id: string, imageUrl: string) =>
    api.post<void>(`/api/games/${id}/icon/web-select`, { imageUrl }),

  // Background image
  getBackgroundUrl: (id: string) => `/api/games/${id}/background`,
  searchBackgroundGames: (id: string, query: string) =>
    api.post<SteamGridDbSearchResult[]>(`/api/games/${id}/background/search`, { query }),
  getBackgroundHeroes: (id: string, steamGridDbGameId: number) =>
    api.post<SteamGridDbImage[]>(`/api/games/${id}/background/heroes`, { steamGridDbGameId }),
  selectBackground: (id: string, imageUrl: string, steamGridDbGameId: number) =>
    api.post<void>(`/api/games/${id}/background/select`, { imageUrl, steamGridDbGameId }),
  searchSteamBackgrounds: (id: string, query: string) =>
    api.post<SteamStoreSearchResult[]>(`/api/games/${id}/background/steam-search`, { query }),
  selectSteamBackground: (id: string, steamAppId: number) =>
    api.post<void>(`/api/games/${id}/background/steam-select`, { steamAppId }),
  uploadBackground: async (id: string, file: File) => {
    const formData = new FormData()
    formData.append('background', file)
    const resp = await fetch(`/api/games/${id}/background/upload`, {
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
  uploadBackgroundFromPath: (id: string, filePath: string) =>
    api.post<void>(`/api/games/${id}/background/upload-from-path`, { filePath }),
  deleteBackground: (id: string) => api.del<void>(`/api/games/${id}/background`),
  searchWebBackgroundImages: (id: string, query: string, engine: string, sizeFilter?: string) =>
    api.post<WebImageResult[]>(`/api/games/${id}/background/web-search`, { query, engine, sizeFilter }),
  selectWebBackground: (id: string, imageUrl: string) =>
    api.post<void>(`/api/games/${id}/background/web-select`, { imageUrl }),
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

export const settingsApi = {
  get: () => api.get<AppSettings>('/api/settings'),
  save: (settings: AppSettings) => api.put<AppSettings>('/api/settings', settings),
  getVersion: () => api.get<VersionInfo>('/api/settings/version'),
  reset: () => api.post<{ partial: boolean; errors?: string[] }>('/api/settings/reset'),
  getDataPath: () => api.get<DataPathInfo>('/api/settings/data-path'),
  openDataFolder: () => api.post('/api/settings/open-data-folder'),
  async exportData(): Promise<void> {
    const res = await fetch('/api/settings/export', { method: 'POST' })
    if (!res.ok) throw new Error('导出失败')
    const blob = await res.blob()
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    const disposition = res.headers.get('content-disposition')
    a.download = disposition?.match(/filename="?([^";\s]+)"?/)?.[1]
        ?? `XUnityToolkit_data_${new Date().toISOString().slice(0, 10)}.zip`
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
    setTimeout(() => URL.revokeObjectURL(url), 1000)
  },
  importFromPath: (filePath: string) =>
    api.post<void>('/api/settings/import-from-path', { filePath }),
}

export const translateApi = {
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
  saveSettings: (req: { gpuLayers: number; contextLength: number; kvCacheType?: string }) =>
    api.put<void>('/api/local-llm/settings', req),
  getCatalog: () => api.get<BuiltInModelInfo[]>('/api/local-llm/catalog'),
  getLlamaStatus: () => api.get<LlamaStatus>('/api/local-llm/llama-status'),
  downloadLlama: () => api.post<void>('/api/local-llm/llama-download'),
  cancelLlamaDownload: () => api.post<void>('/api/local-llm/llama-download/cancel'),
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

export const scriptTagApi = {
  getPresets: () => api.get<ScriptTagPreset>('/api/script-tag-presets'),
  get: (gameId: string) => api.get<ScriptTagConfig>(`/api/games/${gameId}/script-tags`),
  save: (gameId: string, config: ScriptTagConfig) =>
    api.put<ScriptTagConfig>(`/api/games/${gameId}/script-tags`, config),
}

export const bepinexLogApi = {
  get: (id: string) => api.get<BepInExLogResponse>(`/api/games/${id}/bepinex-log`),
  analyze: (id: string) => api.post<BepInExLogAnalysis>(`/api/games/${id}/bepinex-log/analyze`, {}),
  getDownloadUrl: (id: string) => `/api/games/${id}/bepinex-log/download`,
}

// Plugin Health Check
export const pluginHealthApi = {
  check: (id: string) => api.get<PluginHealthReport>(`/api/games/${id}/health-check`),
  verify: (id: string) => api.post<PluginHealthReport>(`/api/games/${id}/health-check/verify`, {}),
}

// BepInEx Plugin Management
export const bepinexPluginApi = {
  list: (gameId: string) =>
    api.get<BepInExPlugin[]>(`/api/games/${gameId}/plugins`),
  install: (gameId: string, filePath: string) =>
    api.post(`/api/games/${gameId}/plugins/install`, { filePath }),
  upload: async (gameId: string, file: File) => {
    const formData = new FormData()
    formData.append('file', file)
    const resp = await fetch(`/api/games/${gameId}/plugins/upload`, {
      method: 'POST',
      body: formData,
    })
    if (!resp.ok) {
      const result = await resp.json().catch(() => null)
      throw new Error(result?.error || '上传失败')
    }
  },
  uninstall: (gameId: string, relativePath: string) =>
    api.del(`/api/games/${gameId}/plugins?relativePath=${encodeURIComponent(relativePath)}`),
  toggle: (gameId: string, relativePath: string) =>
    api.post<BepInExPlugin>(`/api/games/${gameId}/plugins/toggle`, { relativePath }),
  getConfig: (gameId: string, configFile: string) =>
    api.get<string>(`/api/games/${gameId}/plugins/config?configFile=${encodeURIComponent(configFile)}`),
}

// Term Candidates
export const termCandidatesApi = {
  get: (gameId: string) =>
    api.get<TermCandidateStore>(`/api/games/${gameId}/term-candidates`),
  apply: (gameId: string, originals: string[] | null) =>
    api.post(`/api/games/${gameId}/term-candidates/apply`, { originals }),
  clear: (gameId: string) =>
    api.del(`/api/games/${gameId}/term-candidates`),
}
