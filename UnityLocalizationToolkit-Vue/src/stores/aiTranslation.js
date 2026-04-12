import { defineStore } from 'pinia';
import { ref } from 'vue';
import * as signalR from '@microsoft/signalr';
import { translateApi } from '@/api/games';
export const useAiTranslationStore = defineStore('aiTranslation', () => {
    const stats = ref(null);
    const extractionStats = ref(null);
    const cacheStats = ref(null);
    let connection = null;
    async function connect() {
        if (connection && connection.state !== signalR.HubConnectionState.Disconnected)
            return;
        connection = new signalR.HubConnectionBuilder()
            .withUrl('/hubs/install')
            .withAutomaticReconnect()
            .build();
        connection.on('statsUpdate', (update) => {
            stats.value = update;
        });
        connection.on('extractionStatsUpdate', (update) => {
            extractionStats.value = update;
        });
        connection.on('preCacheStatsUpdate', (data) => {
            cacheStats.value = data;
        });
        connection.onreconnected(async () => {
            try {
                await connection?.invoke('JoinAiTranslationGroup');
            }
            catch { /* ignore */ }
        });
        await connection.start();
        await connection.invoke('JoinAiTranslationGroup');
    }
    async function disconnect() {
        if (connection) {
            try {
                await connection.invoke('LeaveAiTranslationGroup');
            }
            catch {
                // Connection might already be closed
            }
            await connection.stop();
            connection = null;
        }
    }
    async function fetchStats() {
        stats.value = await translateApi.getStats();
        try {
            extractionStats.value = await translateApi.getExtractionStats();
        }
        catch {
            // Extraction stats endpoint may not exist on older backends
        }
    }
    async function fetchCacheStats() {
        try {
            const res = await fetch('/api/translate/cache-stats');
            const json = await res.json();
            if (json.success)
                cacheStats.value = json.data;
        }
        catch { /* ignore */ }
    }
    async function toggleEnabled(enabled) {
        await translateApi.toggle(enabled);
        if (stats.value) {
            stats.value = { ...stats.value, enabled };
        }
    }
    return { stats, extractionStats, cacheStats, connect, disconnect, fetchStats, fetchCacheStats, toggleEnabled };
});
