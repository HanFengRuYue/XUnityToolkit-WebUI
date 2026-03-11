import { defineStore } from 'pinia'
import { ref } from 'vue'
import * as signalR from '@microsoft/signalr'
import { logsApi } from '@/api/games'
import type { LogEntry } from '@/api/types'

const MAX_ENTRIES = 500

export const useLogStore = defineStore('log', () => {
  const entries = ref<LogEntry[]>([])

  let connection: signalR.HubConnection | null = null

  async function connect() {
    if (connection && connection.state !== signalR.HubConnectionState.Disconnected) return

    connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/install')
      .withAutomaticReconnect()
      .build()

    connection.on('logEntry', (entry: LogEntry) => {
      entries.value = [...entries.value, entry].slice(-MAX_ENTRIES)
    })

    connection.onreconnected(async () => {
      try { await connection?.invoke('JoinLogGroup') } catch { /* ignore */ }
    })

    await connection.start()
    await connection.invoke('JoinLogGroup')
  }

  async function disconnect() {
    if (connection) {
      try {
        await connection.invoke('LeaveLogGroup')
      } catch {
        // Connection might already be closed
      }
      await connection.stop()
      connection = null
    }
  }

  async function fetchRecent() {
    entries.value = await logsApi.getRecent(200)
  }

  async function fetchHistory() {
    entries.value = await logsApi.getHistory(500)
  }

  function clearLocal() {
    entries.value = []
  }

  return { entries, connect, disconnect, fetchRecent, fetchHistory, clearLocal }
})
