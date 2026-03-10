import { defineStore } from 'pinia'
import { ref } from 'vue'
import * as signalR from '@microsoft/signalr'
import { translateApi } from '@/api/games'
import type { TranslationStats } from '@/api/types'

export const useAiTranslationStore = defineStore('aiTranslation', () => {
  const stats = ref<TranslationStats | null>(null)

  let connection: signalR.HubConnection | null = null

  async function connect() {
    if (connection?.state === signalR.HubConnectionState.Connected) return

    connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/install')
      .withAutomaticReconnect()
      .build()

    connection.on('statsUpdate', (update: TranslationStats) => {
      stats.value = update
    })

    await connection.start()
    await connection.invoke('JoinAiTranslationGroup')
  }

  async function disconnect() {
    if (connection) {
      try {
        await connection.invoke('LeaveAiTranslationGroup')
      } catch {
        // Connection might already be closed
      }
      await connection.stop()
      connection = null
    }
  }

  async function fetchStats() {
    stats.value = await translateApi.getStats()
  }

  return { stats, connect, disconnect, fetchStats }
})
