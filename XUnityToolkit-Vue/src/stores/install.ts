import { defineStore } from 'pinia'
import { ref } from 'vue'
import * as signalR from '@microsoft/signalr'
import { gamesApi } from '@/api/games'
import type { InstallationStatus, XUnityConfig } from '@/api/types'

export type OperationType = 'install' | 'uninstall'

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
      if (update.step === 'Complete' || update.step === 'Failed') {
        // Keep drawer open so user can see result
      }
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

  async function startInstall(gameId: string, config?: XUnityConfig) {
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
    await disconnectHub()
    activeGameId.value = null
    status.value = null
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
