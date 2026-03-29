import { defineStore } from 'pinia'
import { ref } from 'vue'
import * as signalR from '@microsoft/signalr'
import { gamesApi, settingsApi } from '@/api/games'
import type { InstallationStatus, XUnityConfig, InstallOptions, PluginHealthReport } from '@/api/types'

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
  const healthReport = ref<PluginHealthReport | null>(null)
  const skippedSteps = ref<Set<string>>(new Set())

  let connection: signalR.HubConnection | null = null

  async function connectHub(gameId: string) {
    if (connection) {
      try { await connection.stop() } catch { /* best effort */ }
      connection = null
    }

    connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/install')
      .withAutomaticReconnect()
      .build()

    connection.on('progressUpdate', (update: InstallationStatus) => {
      status.value = update
    })

    connection.on('healthReportReady', (report: PluginHealthReport) => {
      healthReport.value = report
    })

    connection.onreconnected(async () => {
      if (activeGameId.value) {
        try { await connection?.invoke('JoinGameGroup', activeGameId.value) } catch { /* ignore */ }
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

  /**
   * Resume watching an already-running install.
   * Fetches current status from backend, connects SignalR, and opens drawer.
   */
  async function resumeInstall(gameId: string, backendStatus: InstallationStatus) {
    activeGameId.value = gameId
    // Infer operation type from step — removal steps indicate uninstall
    const step = backendStatus.step
    operationType.value = (step === 'RemovingXUnity' || step === 'RemovingBepInEx')
      ? 'uninstall'
      : 'install'
    status.value = backendStatus
    // Restore skipped steps from persisted settings
    try {
      const appSettings = await settingsApi.get()
      skippedSteps.value = computeSkippedSteps(appSettings.installOptions)
    } catch { /* best effort */ }
    isDrawerOpen.value = true
    await connectHub(gameId)
  }

  function computeSkippedSteps(options?: InstallOptions): Set<string> {
    if (!options) return new Set()
    const steps = new Set<string>()
    if (!options.autoInstallTmpFont) steps.add('InstallingTmpFont')
    if (!options.autoDeployAiEndpoint) steps.add('InstallingAiTranslation')
    if (!options.autoGenerateConfig) steps.add('GeneratingConfig')
    if (!options.autoApplyOptimalConfig) steps.add('ApplyingConfig')
    if (!options.autoExtractAssets) steps.add('ExtractingAssets')
    if (!options.autoVerifyHealth) steps.add('VerifyingHealth')
    return steps
  }

  async function startInstall(gameId: string, config?: XUnityConfig, options?: InstallOptions) {
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
    healthReport.value = null
    skippedSteps.value = computeSkippedSteps(options)
    isDrawerOpen.value = true

    await connectHub(gameId)
    try {
      status.value = await gamesApi.install(gameId, config, options)
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
      healthReport.value = null
      skippedSteps.value = new Set()
    }
  }

  return {
    status,
    isDrawerOpen,
    activeGameId,
    operationType,
    healthReport,
    skippedSteps,
    startInstall,
    startUninstall,
    cancel,
    closeDrawer,
  }
})
