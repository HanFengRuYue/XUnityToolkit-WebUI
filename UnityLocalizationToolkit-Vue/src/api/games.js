import { api } from './client';
export const gamesApi = {
    list: () => api.get('/api/games'),
    get: (id) => api.get(`/api/games/${id}`),
    add: (gamePath, name) => api.post('/api/games', { gamePath, name }),
    addWithDetection: (folderPath, exePath) => api.post('/api/games/add-with-detection', { folderPath, exePath }),
    batchAdd: (parentFolderPath) => api.post('/api/games/batch-add', { parentFolderPath }),
    update: (id, data) => api.put(`/api/games/${id}`, data),
    remove: (id) => api.del(`/api/games/${id}`),
    detect: (id) => api.post(`/api/games/${id}/detect`),
    uninstallFramework: (id, framework) => api.del(`/api/games/${id}/framework/${framework}`),
    getConfig: (id) => api.get(`/api/games/${id}/config`),
    saveConfig: (id, config) => api.put(`/api/games/${id}/config`, config),
    getRawConfig: (id) => api.get(`/api/games/${id}/config/raw`),
    saveRawConfig: (id, content) => api.put(`/api/games/${id}/config/raw`, { content }),
    install: (id, config, options) => api.post(`/api/games/${id}/install`, { config, options }),
    uninstall: (id) => api.del(`/api/games/${id}/install`),
    getStatus: (id) => api.get(`/api/games/${id}/status`),
    cancel: (id) => api.post(`/api/games/${id}/cancel`),
    openFolder: (id) => api.post(`/api/games/${id}/open-folder`),
    launch: (id) => api.post(`/api/games/${id}/launch`),
    getAiEndpointStatus: (id) => api.get(`/api/games/${id}/ai-endpoint`),
    installAiEndpoint: (id) => api.post(`/api/games/${id}/ai-endpoint`, {}),
    uninstallAiEndpoint: (id) => api.del(`/api/games/${id}/ai-endpoint`),
    getTmpFontStatus: (id) => api.get(`/api/games/${id}/tmp-font`),
    installTmpFont: (id) => api.post(`/api/games/${id}/tmp-font`, {}),
    uninstallTmpFont: (id) => api.del(`/api/games/${id}/tmp-font`),
    // Unified term management
    getTerms: (id) => api.get(`/api/games/${id}/terms`),
    saveTerms: (id, entries) => api.put(`/api/games/${id}/terms`, entries),
    importTermsFromGame: (id, sourceGameId) => api.post(`/api/games/${id}/terms/import-from-game`, { sourceGameId }),
    getDescription: (id) => api.get(`/api/games/${id}/description`),
    saveDescription: (id, description) => api.put(`/api/games/${id}/description`, { description }),
    // Icon
    getIconUrl: (id) => `/api/games/${id}/icon`,
    uploadIcon: async (id, file) => {
        const formData = new FormData();
        formData.append('icon', file);
        const resp = await fetch(`/api/games/${id}/icon/upload`, {
            method: 'POST',
            body: formData,
        });
        if (!resp.ok) {
            const text = await resp.text();
            let message = `HTTP ${resp.status}`;
            try {
                const json = JSON.parse(text);
                if (json.error)
                    message = json.error;
            }
            catch { /* ignore */ }
            throw new Error(message);
        }
    },
    uploadIconFromPath: (id, filePath) => api.post(`/api/games/${id}/icon/upload-from-path`, { filePath }),
    deleteCustomIcon: (id) => api.del(`/api/games/${id}/icon/custom`),
    searchIconGames: (id, query) => api.post(`/api/games/${id}/icon/search`, { query }),
    getIconGrids: (id, steamGridDbGameId) => api.post(`/api/games/${id}/icon/grids`, { steamGridDbGameId }),
    selectSteamGridDbIcon: (id, imageUrl, steamGridDbGameId) => api.post(`/api/games/${id}/icon/select`, { imageUrl, steamGridDbGameId }),
    // Cover image
    getCoverUrl: (id) => `/api/games/${id}/cover`,
    deleteCover: (id) => api.del(`/api/games/${id}/cover`),
    uploadCover: async (id, file) => {
        const formData = new FormData();
        formData.append('cover', file);
        const resp = await fetch(`/api/games/${id}/cover/upload`, {
            method: 'POST',
            body: formData,
        });
        if (!resp.ok) {
            const text = await resp.text();
            let message = `HTTP ${resp.status}`;
            try {
                const json = JSON.parse(text);
                if (json.error)
                    message = json.error;
            }
            catch { /* ignore */ }
            throw new Error(message);
        }
        const result = await resp.json();
        return result.data;
    },
    searchCovers: (id, query) => api.post(`/api/games/${id}/cover/search`, { query }),
    getCoverGrids: (id, steamGridDbGameId) => api.post(`/api/games/${id}/cover/grids`, { steamGridDbGameId }),
    selectCover: (id, imageUrl, steamGridDbGameId) => api.post(`/api/games/${id}/cover/select`, { imageUrl, steamGridDbGameId }),
    searchSteamGames: (id, query) => api.post(`/api/games/${id}/cover/steam-search`, { query }),
    selectSteamCover: (id, steamAppId) => api.post(`/api/games/${id}/cover/steam-select`, { steamAppId }),
    // Web image search
    searchWebImages: (id, query, engine, sizeFilter) => api.post(`/api/games/${id}/cover/web-search`, { query, engine, sizeFilter }),
    uploadCoverFromPath: (id, filePath) => api.post(`/api/games/${id}/cover/upload-from-path`, { filePath }),
    selectWebCover: (id, imageUrl) => api.post(`/api/games/${id}/cover/web-select`, { imageUrl }),
    searchWebIconImages: (id, query, engine, sizeFilter) => api.post(`/api/games/${id}/icon/web-search`, { query, engine, sizeFilter }),
    selectWebIcon: (id, imageUrl) => api.post(`/api/games/${id}/icon/web-select`, { imageUrl }),
    // Background image
    getBackgroundUrl: (id) => `/api/games/${id}/background`,
    searchBackgroundGames: (id, query) => api.post(`/api/games/${id}/background/search`, { query }),
    getBackgroundHeroes: (id, steamGridDbGameId) => api.post(`/api/games/${id}/background/heroes`, { steamGridDbGameId }),
    selectBackground: (id, imageUrl, steamGridDbGameId) => api.post(`/api/games/${id}/background/select`, { imageUrl, steamGridDbGameId }),
    searchSteamBackgrounds: (id, query) => api.post(`/api/games/${id}/background/steam-search`, { query }),
    selectSteamBackground: (id, steamAppId) => api.post(`/api/games/${id}/background/steam-select`, { steamAppId }),
    uploadBackground: async (id, file) => {
        const formData = new FormData();
        formData.append('background', file);
        const resp = await fetch(`/api/games/${id}/background/upload`, {
            method: 'POST',
            body: formData,
        });
        if (!resp.ok) {
            const text = await resp.text();
            let message = `HTTP ${resp.status}`;
            try {
                const json = JSON.parse(text);
                if (json.error)
                    message = json.error;
            }
            catch { /* ignore */ }
            throw new Error(message);
        }
    },
    uploadBackgroundFromPath: (id, filePath) => api.post(`/api/games/${id}/background/upload-from-path`, { filePath }),
    deleteBackground: (id) => api.del(`/api/games/${id}/background`),
    searchWebBackgroundImages: (id, query, engine, sizeFilter) => api.post(`/api/games/${id}/background/web-search`, { query, engine, sizeFilter }),
    selectWebBackground: (id, imageUrl) => api.post(`/api/games/${id}/background/web-select`, { imageUrl }),
};
export const assetApi = {
    extractAssets: (id) => api.post(`/api/games/${id}/extract-assets`, {}),
    getExtractedTexts: (id) => api.get(`/api/games/${id}/extracted-texts`),
    deleteExtractedTexts: (id) => api.del(`/api/games/${id}/extracted-texts`),
    startPreTranslation: (id, fromLang, toLang, restart = false) => api.post(`/api/games/${id}/pre-translate`, { fromLang, toLang, restart }),
    resumePreTranslation: (id) => api.post(`/api/games/${id}/pre-translate/resume`, {}),
    getPreTranslationStatus: (id) => api.get(`/api/games/${id}/pre-translate/status`),
    cancelPreTranslation: (id) => api.post(`/api/games/${id}/pre-translate/cancel`, {}),
};
export const manualTranslationApi = {
    scan: (id) => api.post(`/api/games/${id}/manual-translation/scan`, {}),
    getStatus: (id) => api.get(`/api/games/${id}/manual-translation/status`),
    getAssets: (id, query) => {
        const params = new URLSearchParams();
        if (query?.scope)
            params.set('scope', query.scope);
        if (query?.search)
            params.set('search', query.search);
        if (query?.editableOnly)
            params.set('editableOnly', 'true');
        if (query?.overriddenOnly)
            params.set('overriddenOnly', 'true');
        const suffix = params.toString() ? `?${params.toString()}` : '';
        return api.get(`/api/games/${id}/manual-translation/assets${suffix}`);
    },
    getAssetDetail: (id, assetId) => api.get(`/api/games/${id}/manual-translation/asset-detail?assetId=${encodeURIComponent(assetId)}`),
    getAssetContent: (id, assetId) => api.get(`/api/games/${id}/manual-translation/asset-content?assetId=${encodeURIComponent(assetId)}`),
    saveOverride: (id, assetId, value, source) => api.put(`/api/games/${id}/manual-translation/save-override`, { assetId, value, source }),
    deleteOverride: (id, assetId) => api.del(`/api/games/${id}/manual-translation/delete-override?assetId=${encodeURIComponent(assetId)}`),
    apply: (id) => api.post(`/api/games/${id}/manual-translation/apply`, {}),
    restore: (id) => api.post(`/api/games/${id}/manual-translation/restore`, {}),
    buildPackageUrl: (id) => `/api/games/${id}/manual-translation/build-package`,
    importPackage: (id, zipPath) => api.post(`/api/games/${id}/manual-translation/import-package`, { zipPath }),
    exportAssetUrl: (id, assetId) => `/api/games/${id}/manual-translation/export-asset?assetId=${encodeURIComponent(assetId)}`,
};
export const settingsApi = {
    get: () => api.get('/api/settings'),
    save: (settings) => api.put('/api/settings', settings),
    getVersion: () => api.get('/api/settings/version'),
    reset: () => api.post('/api/settings/reset'),
    getDataPath: () => api.get('/api/settings/data-path'),
    openDataFolder: () => api.post('/api/settings/open-data-folder'),
    async exportData() {
        const res = await fetch('/api/settings/export', { method: 'POST' });
        if (!res.ok)
            throw new Error('导出失败');
        const blob = await res.blob();
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        const disposition = res.headers.get('content-disposition');
        a.download = disposition?.match(/filename="?([^";\s]+)"?/)?.[1]
            ?? `UnityLocalizationToolkit_data_${new Date().toISOString().slice(0, 10)}.zip`;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    },
    importFromPath: (filePath) => api.post('/api/settings/import-from-path', { filePath }),
};
export const translateApi = {
    getStats: () => api.get('/api/translate/stats'),
    toggle: (enabled) => api.post('/api/ai/toggle', { enabled }),
    fetchModels: (provider, apiBaseUrl, apiKey) => api.get(`/api/ai/models?provider=${provider}&apiBaseUrl=${encodeURIComponent(apiBaseUrl)}&apiKey=${encodeURIComponent(apiKey)}`),
    testTranslate: (endpoints, systemPrompt, temperature) => api.post('/api/translate/test', { endpoints, systemPrompt, temperature }),
    getExtractionStats: () => api.get('/api/ai/extraction/stats'),
};
export const translationEditorApi = {
    getEntries: (id, options) => api.get(`/api/games/${id}/translation-editor${buildTranslationEditorQuery(options)}`),
    saveEntries: (id, entries, options) => api.put(`/api/games/${id}/translation-editor${buildTranslationEditorQuery(options)}`, { entries }),
    parseImport: (id, content) => api.post(`/api/games/${id}/translation-editor/import`, { content }),
    getExportUrl: (id, options) => `/api/games/${id}/translation-editor/export${buildTranslationEditorQuery(options)}`,
    getRegex: (id, lang) => api.get(`/api/games/${id}/translation-editor/regex${buildTranslationEditorQuery({ lang })}`),
    saveRegex: (id, rules, lang) => api.put(`/api/games/${id}/translation-editor/regex${buildTranslationEditorQuery({ lang })}`, { rules }),
    importRegex: (id, content, lang) => api.post(`/api/games/${id}/translation-editor/regex/import${buildTranslationEditorQuery({ lang })}`, { content }),
    getRegexExportUrl: (id, lang) => `/api/games/${id}/translation-editor/regex/export${buildTranslationEditorQuery({ lang })}`,
};
function buildTranslationEditorQuery(options) {
    const params = new URLSearchParams();
    if (options?.source)
        params.set('source', options.source);
    if (options?.lang)
        params.set('lang', options.lang);
    const query = params.toString();
    return query ? `?${query}` : '';
}
export const pluginPackageApi = {
    getExportUrl: (id) => `/api/games/${id}/plugin-package/export`,
    importPackage: (id, zipPath) => api.post(`/api/games/${id}/plugin-package/import`, { zipPath }),
};
export const localLlmApi = {
    test: () => api.post('/api/local-llm/test'),
    getStatus: () => api.get('/api/local-llm/status'),
    getGpus: () => api.get('/api/local-llm/gpus'),
    refreshGpus: () => api.post('/api/local-llm/gpus/refresh'),
    getSettings: () => api.get('/api/local-llm/settings'),
    saveSettings: (req) => api.put('/api/local-llm/settings', req),
    getCatalog: () => api.get('/api/local-llm/catalog'),
    getActiveDownloads: () => api.get('/api/local-llm/downloads'),
    getLlamaStatus: () => api.get('/api/local-llm/llama-status'),
    downloadLlama: () => api.post('/api/local-llm/llama-download'),
    cancelLlamaDownload: () => api.post('/api/local-llm/llama-download/cancel'),
    start: (modelPath, gpuLayers, contextLength) => api.post('/api/local-llm/start', { modelPath, gpuLayers, contextLength }),
    stop: () => api.post('/api/local-llm/stop'),
    downloadModel: (catalogId) => api.post('/api/local-llm/download', { catalogId }),
    pauseDownload: (catalogId) => api.post('/api/local-llm/download/pause', { catalogId }),
    cancelDownload: (catalogId) => api.post('/api/local-llm/download/cancel', { catalogId }),
    getModels: () => api.get('/api/local-llm/models'),
    addModel: (filePath, name) => api.post('/api/local-llm/models/add', { filePath, name }),
    removeModel: (id) => api.del(`/api/local-llm/models/${id}`),
};
export const logsApi = {
    getRecent: (count = 200) => api.get(`/api/logs?count=${count}`),
    getHistory: (lines = 500) => api.get(`/api/logs/history?lines=${lines}`),
    getDownloadUrl: () => '/api/logs/download',
};
export const scriptTagApi = {
    getPresets: () => api.get('/api/script-tag-presets'),
    get: (gameId) => api.get(`/api/games/${gameId}/script-tags`),
    save: (gameId, config) => api.put(`/api/games/${gameId}/script-tags`, config),
};
export const bepinexLogApi = {
    get: (id) => api.get(`/api/games/${id}/bepinex-log`),
    analyze: (id) => api.post(`/api/games/${id}/bepinex-log/analyze`, {}),
    getDownloadUrl: (id) => `/api/games/${id}/bepinex-log/download`,
};
// Plugin Health Check
export const pluginHealthApi = {
    check: (id) => api.get(`/api/games/${id}/health-check`),
    verify: (id) => api.post(`/api/games/${id}/health-check/verify`, {}),
};
// BepInEx Plugin Management
export const bepinexPluginApi = {
    list: (gameId) => api.get(`/api/games/${gameId}/plugins`),
    install: (gameId, filePath) => api.post(`/api/games/${gameId}/plugins/install`, { filePath }),
    upload: async (gameId, file) => {
        const formData = new FormData();
        formData.append('file', file);
        const resp = await fetch(`/api/games/${gameId}/plugins/upload`, {
            method: 'POST',
            body: formData,
        });
        if (!resp.ok) {
            const result = await resp.json().catch(() => null);
            throw new Error(result?.error || '上传失败');
        }
    },
    uninstall: (gameId, relativePath) => api.del(`/api/games/${gameId}/plugins?relativePath=${encodeURIComponent(relativePath)}`),
    toggle: (gameId, relativePath) => api.post(`/api/games/${gameId}/plugins/toggle`, { relativePath }),
    getConfig: (gameId, configFile) => api.get(`/api/games/${gameId}/plugins/config?configFile=${encodeURIComponent(configFile)}`),
};
// Term Candidates
export const termCandidatesApi = {
    get: (gameId) => api.get(`/api/games/${gameId}/term-candidates`),
    apply: (gameId, originals) => api.post(`/api/games/${gameId}/term-candidates/apply`, { originals }),
    clear: (gameId) => api.del(`/api/games/${gameId}/term-candidates`),
};
