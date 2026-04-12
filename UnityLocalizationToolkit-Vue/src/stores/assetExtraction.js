import { defineStore } from 'pinia';
import { ref } from 'vue';
import * as signalR from '@microsoft/signalr';
import { assetApi } from '@/api/games';
export const useAssetExtractionStore = defineStore('assetExtraction', () => {
    const extractionResult = ref(null);
    const preTranslationStatus = ref(null);
    const extracting = ref(false);
    const extractError = ref(null);
    const termExtractionComplete = ref(false);
    let connection = null;
    let activeGameId = null;
    let checkpointStatusRefreshPromise = null;
    function shouldRefreshCheckpointStatus(status) {
        return Boolean(status.checkpointUpdatedAt)
            && !status.canResume
            && status.state !== 'Running'
            && status.state !== 'AwaitingTermReview';
    }
    async function syncPreTranslationStatus(gameId) {
        preTranslationStatus.value = await assetApi.getPreTranslationStatus(gameId);
    }
    async function refreshCheckpointStatus(gameId) {
        if (checkpointStatusRefreshPromise)
            return checkpointStatusRefreshPromise;
        checkpointStatusRefreshPromise = syncPreTranslationStatus(gameId)
            .finally(() => {
            checkpointStatusRefreshPromise = null;
        });
        return checkpointStatusRefreshPromise;
    }
    async function connect(gameId) {
        if (connection && connection.state !== signalR.HubConnectionState.Disconnected && activeGameId === gameId)
            return;
        await disconnect();
        activeGameId = gameId;
        connection = new signalR.HubConnectionBuilder()
            .withUrl('/hubs/install')
            .withAutomaticReconnect()
            .build();
        connection.on('preTranslationUpdate', (update) => {
            preTranslationStatus.value = update;
            if (activeGameId === update.gameId && shouldRefreshCheckpointStatus(update))
                void refreshCheckpointStatus(update.gameId);
        });
        // These events send partial objects (not full PreTranslationStatus).
        // Do NOT overwrite preTranslationStatus — only extract relevant info.
        connection.on('roundProgress', (_update) => {
            // Status is already tracked via preTranslationUpdate broadcasts
        });
        connection.on('patternAnalysisProgress', (_update) => {
            // Status is already tracked via preTranslationUpdate broadcasts
        });
        connection.on('termExtractionComplete', (_update) => {
            termExtractionComplete.value = true;
        });
        connection.onreconnected(async () => {
            try {
                await connection?.invoke('JoinPreTranslationGroup', activeGameId);
                // Re-fetch current status to recover any missed updates
                if (activeGameId)
                    await syncPreTranslationStatus(activeGameId);
            }
            catch { /* ignore */ }
        });
        await connection.start();
        await connection.invoke('JoinPreTranslationGroup', gameId);
    }
    async function disconnect() {
        if (connection) {
            if (activeGameId) {
                try {
                    await connection.invoke('LeavePreTranslationGroup', activeGameId);
                }
                catch { /* ignore */ }
            }
            await connection.stop();
            connection = null;
        }
        activeGameId = null;
        checkpointStatusRefreshPromise = null;
    }
    async function loadCachedResult(gameId) {
        extractionResult.value = await assetApi.getExtractedTexts(gameId);
    }
    async function extractAssets(gameId) {
        extracting.value = true;
        extractError.value = null;
        try {
            extractionResult.value = await assetApi.extractAssets(gameId);
        }
        catch (e) {
            extractError.value = e instanceof Error ? e.message : String(e);
            throw e;
        }
        finally {
            extracting.value = false;
        }
    }
    async function startPreTranslation(gameId, fromLang, toLang, restart = false) {
        await connect(gameId);
        try {
            preTranslationStatus.value = await assetApi.startPreTranslation(gameId, fromLang, toLang, restart);
        }
        catch (e) {
            try {
                await refreshCheckpointStatus(gameId);
            }
            catch {
                // Ignore status refresh failures and preserve the original error.
            }
            throw e;
        }
    }
    async function resumePreTranslation(gameId) {
        await connect(gameId);
        preTranslationStatus.value = await assetApi.resumePreTranslation(gameId);
    }
    async function cancelPreTranslation(gameId) {
        await assetApi.cancelPreTranslation(gameId);
    }
    async function fetchPreTranslationStatus(gameId) {
        await syncPreTranslationStatus(gameId);
    }
    async function clearCache(gameId) {
        await assetApi.deleteExtractedTexts(gameId);
        extractionResult.value = null;
        await syncPreTranslationStatus(gameId);
    }
    function resetTermExtractionComplete() {
        termExtractionComplete.value = false;
    }
    return {
        extractionResult,
        preTranslationStatus,
        extracting,
        extractError,
        termExtractionComplete,
        connect,
        disconnect,
        loadCachedResult,
        extractAssets,
        startPreTranslation,
        resumePreTranslation,
        cancelPreTranslation,
        fetchPreTranslationStatus,
        clearCache,
        resetTermExtractionComplete,
    };
});
