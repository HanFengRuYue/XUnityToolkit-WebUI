import { defineStore } from 'pinia'
import { ref } from 'vue'
import * as signalR from '@microsoft/signalr'
import { logsApi } from '@/api/games'
import type { LogEntry } from '@/api/types'

const MAX_ENTRIES = 500

export const useLogStore = defineStore('log', () => {
  const entries = ref<LogEntry[]>([])
  const levelCounts = ref<Record<string, number>>({
    DBG: 0,
    INF: 0,
    WRN: 0,
    ERR: 0,
    CRI: 0,
  })

  let connection: signalR.HubConnection | null = null

  function resetLevelCounts() {
    levelCounts.value = {
      DBG: 0,
      INF: 0,
      WRN: 0,
      ERR: 0,
      CRI: 0,
    }
  }

  function incrementLevel(level: string, delta: number) {
    if (!(level in levelCounts.value)) return
    levelCounts.value[level] = Math.max(0, (levelCounts.value[level] ?? 0) + delta)
  }

  function rebuildEntries(nextEntries: LogEntry[]) {
    entries.value = nextEntries
    resetLevelCounts()
    for (const entry of nextEntries) {
      incrementLevel(entry.level, 1)
    }
  }

  function appendEntry(entry: LogEntry) {
    entries.value.push(entry)
    incrementLevel(entry.level, 1)

    const overflow = entries.value.length - MAX_ENTRIES
    if (overflow > 0) {
      const removed = entries.value.splice(0, overflow)
      for (const item of removed) {
        incrementLevel(item.level, -1)
      }
    }
  }

  async function connect() {
    if (connection && connection.state !== signalR.HubConnectionState.Disconnected) return

    connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/install')
      .withAutomaticReconnect()
      .build()

    connection.on('logEntry', (entry: LogEntry) => {
      appendEntry(entry)
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
    rebuildEntries(await logsApi.getRecent(200))
  }

  async function fetchHistory() {
    rebuildEntries(await logsApi.getHistory(500))
  }

  function clearLocal() {
    entries.value = []
    resetLevelCounts()
  }

  return { entries, levelCounts, connect, disconnect, fetchRecent, fetchHistory, clearLocal }
})
