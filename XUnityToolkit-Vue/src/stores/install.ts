import { defineStore } from 'pinia'
import { ref } from 'vue'
import * as signalR from '@microsoft/signalr'
import { gamesApi } from '@/api/games'
import type { InstallationStatus, XUnityConfig } from '@/api/types'

export type OperationType = 'install' | 'uninstall'

/** Check if an InstallationStatus indicates an active operation */
function statusIsInProgress(s: InstallationStatus | null): boolean {
  if (!s) return false
  const step = s.step
  return step !== 'Idle' && step !== 'Complete' && step !== 'Failed'
}

export const useInstallStore = defineStore('install', () => {
  const status = ref<InstallationStatus | null>(null)
  const isDrawerOpen = ref(false)
  const activeGameId = ref<string | null>(null)
  const operationType = ref<OperationType>('install')

  let connection: signalR.HubConnection | null = null

  async function connectHub(gameId: string) {
    if (connection) {
      await connection.stop()
    }

    connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/install')
      .withAutomaticReconnect()
      .build()

    connection.on('progressUpdate', (update: InstallationStatus) => {
      status.value = update
    })

    await connection.start()
    await connection.invoke('JoinGameGroup', gameId)
  }

  async function disconnectHub() {
    if (connection) {
      if (activeGameId.value) {
        try {
          await connection.invoke('LeaveGameGroup', activeGameId.value)
        } catch {
          // Connection might already be closed
        }
      }
      await connection.stop()
      connection = null
    }
  }

  /**
   * Resume watching an already-running install.
   * Fetches current status from backend, connects SignalR, and opens drawer.
   */
  async function resumeInstall(gameId: string, backendStatus: InstallationStatus) {
    activeGameId.value = gameId
    operationType.value = 'install'
    status.value = backendStatus
    isDrawerOpen.value = true
    await connectHub(gameId)
  }

  async function startInstall(gameId: string, config?: XUnityConfig) {
    // Fast path: frontend state says this game is already installing → reopen drawer
    if (activeGameId.value === gameId && statusIsInProgress(status.value)) {
      isDrawerOpen.value = true
      return
    }

    // Fallback: query backend for current status (handles page reload, state loss, etc.)
    try {
      const backendStatus = await gamesApi.getStatus(gameId)
      if (statusIsInProgress(backendStatus)) {
        await resumeInstall(gameId, backendStatus)
        return
      }
    } catch {
      // Status endpoint failed — proceed with normal install attempt
    }

    activeGameId.value = gameId
    operationType.value = 'install'
    isDrawerOpen.value = true

    await connectHub(gameId)
    try {
      status.value = await gamesApi.install(gameId, config)
    } catch (e) {
      await disconnectHub()
      isDrawerOpen.value = false
      activeGameId.value = null
      throw e
    }
  }

  async function startUninstall(gameId: string) {
    // Fast path: frontend state says this game is already operating → reopen drawer
    if (activeGameId.value === gameId && statusIsInProgress(status.value)) {
      isDrawerOpen.value = true
      return
    }

    // Fallback: query backend for current status
    try {
      const backendStatus = await gamesApi.getStatus(gameId)
      if (statusIsInProgress(backendStatus)) {
        await resumeInstall(gameId, backendStatus)
        return
      }
    } catch {
      // Proceed with normal uninstall
    }

    activeGameId.value = gameId
    operationType.value = 'uninstall'
    isDrawerOpen.value = true

    await connectHub(gameId)
    try {
      status.value = await gamesApi.uninstall(gameId)
    } catch (e) {
      await disconnectHub()
      isDrawerOpen.value = false
      activeGameId.value = null
      throw e
    }
  }

  async function cancel() {
    if (activeGameId.value) {
      await gamesApi.cancel(activeGameId.value)
    }
  }

  async function closeDrawer() {
    isDrawerOpen.value = false
    // Only fully reset state when the operation is complete or failed
    if (!statusIsInProgress(status.value)) {
      await disconnectHub()
      activeGameId.value = null
      status.value = null
    }
  }

  return {
    status,
    isDrawerOpen,
    activeGameId,
    operationType,
    startInstall,
    startUninstall,
    cancel,
    closeDrawer,
  }
})
