import { ref, computed } from 'vue'
import { defineStore } from 'pinia'
import * as signalR from '@microsoft/signalr'
import { updateApi } from '@/api/update'
import type { UpdateState, UpdateCheckResult, UpdateStatusInfo, UpdateAvailableInfo } from '@/api/types'

export const useUpdateStore = defineStore('update', () => {
  const state = ref<UpdateState>('None')
  const progress = ref(0)
  const downloadedBytes = ref(0)
  const totalBytes = ref(0)
  const currentPackage = ref<string>()
  const message = ref<string>()
  const error = ref<string>()

  const checkResult = ref<UpdateCheckResult>()
  const availableInfo = ref<UpdateAvailableInfo>()

  let connection: signalR.HubConnection | null = null
  let restartPollTimer: ReturnType<typeof setInterval> | null = null

  const isUpdateAvailable = computed(() => state.value === 'Available')
  const isDownloading = computed(() => state.value === 'Downloading')
  const isReady = computed(() => state.value === 'Ready')
  const isApplying = computed(() => state.value === 'Applying')
  const hasError = computed(() => state.value === 'Error')

  function formatBytes(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
  }

  async function connectHub() {
    if (connection) return

    connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/install')
      .withAutomaticReconnect()
      .build()

    connection.on('UpdateStatus', (status: UpdateStatusInfo) => {
      state.value = status.state
      progress.value = status.progress
      downloadedBytes.value = status.downloadedBytes
      totalBytes.value = status.totalBytes
      currentPackage.value = status.currentPackage ?? undefined
      message.value = status.message ?? undefined
      error.value = status.error ?? undefined
      if (status.availableUpdate) {
        availableInfo.value = status.availableUpdate
      }
    })

    connection.on('UpdateAvailable', (info: UpdateAvailableInfo) => {
      availableInfo.value = info
      state.value = 'Available'
    })

    try {
      await connection.start()
      await connection.invoke('JoinUpdateGroup')
    } catch (err) {
      console.error('Failed to connect update hub:', err)
    }
  }

  async function disconnectHub() {
    if (!connection) return
    try {
      await connection.invoke('LeaveUpdateGroup')
      await connection.stop()
    } catch { /* best effort */ }
    connection = null
  }

  /** Initialize update system: connect hub and fetch current status from backend */
  async function init() {
    // Run in parallel — fetchStatus via HTTP doesn't depend on SignalR connection
    await Promise.all([connectHub(), fetchStatus()])
  }

  /** Fetch current update status from backend (recovers state after page refresh) */
  async function fetchStatus() {
    try {
      const status = await updateApi.status()
      state.value = status.state
      progress.value = status.progress
      downloadedBytes.value = status.downloadedBytes
      totalBytes.value = status.totalBytes
      currentPackage.value = status.currentPackage ?? undefined
      message.value = status.message ?? undefined
      error.value = status.error ?? undefined
      if (status.availableUpdate) {
        availableInfo.value = status.availableUpdate
      }
    } catch {
      // Best effort — hub will receive updates
    }
  }

  async function checkForUpdate() {
    state.value = 'Checking'
    error.value = undefined
    try {
      checkResult.value = await updateApi.check()
      if (checkResult.value.updateAvailable) {
        availableInfo.value = {
          version: checkResult.value.newVersion!,
          changelog: checkResult.value.changelog,
          downloadSize: checkResult.value.downloadSize,
          changedPackages: checkResult.value.changedPackages,
        }
        state.value = 'Available'
      } else {
        state.value = 'None'
      }
    } catch (err) {
      state.value = 'Error'
      error.value = err instanceof Error ? err.message : '检查更新失败'
      console.error('Check for update failed:', err)
    }
  }

  async function downloadUpdate() {
    try {
      await updateApi.download()
    } catch (err) {
      // Download cancelled or failed — SignalR already updated state,
      // but if not, set error state
      if (state.value !== 'Available') {
        state.value = 'Error'
        error.value = err instanceof Error ? err.message : '下载更新失败'
      }
      console.error('Download update failed:', err)
    }
  }

  async function cancelDownload() {
    try {
      await updateApi.cancel()
    } catch (err) {
      console.error('Cancel download failed:', err)
    }
  }

  async function applyUpdate() {
    const prevState = state.value
    try {
      state.value = 'Applying'
      message.value = '正在应用更新，应用即将重启...'
      await updateApi.apply()
      // Server will shut down and restart — poll for reconnection
      pollForRestart()
    } catch (err) {
      state.value = prevState
      error.value = err instanceof Error ? err.message : '应用更新失败'
      if (error.value) {
        state.value = 'Error'
      }
      console.error('Apply update failed:', err)
    }
  }

  function pollForRestart() {
    if (restartPollTimer) return
    const expectedVersion = availableInfo.value?.version
    let elapsed = 0
    restartPollTimer = setInterval(async () => {
      elapsed += 2000
      if (elapsed > 120000) {
        // 2 minutes timeout
        if (restartPollTimer) clearInterval(restartPollTimer)
        restartPollTimer = null
        state.value = 'Error'
        error.value = '等待重启超时，请手动重启应用'
        return
      }
      try {
        const resp = await fetch('/api/settings/version')
        if (resp.ok) {
          // Verify the new version is actually running (not a rollback to old version)
          const result = await resp.json()
          const runningVersion = (result?.data?.version as string)?.split('+')[0]
          if (expectedVersion && runningVersion && runningVersion !== expectedVersion) {
            // Server restarted but with old version — update failed (rollback)
            if (restartPollTimer) clearInterval(restartPollTimer)
            restartPollTimer = null
            state.value = 'Error'
            error.value = '更新失败，已回滚到旧版本'
            return
          }
          if (restartPollTimer) clearInterval(restartPollTimer)
          restartPollTimer = null
          window.location.reload()
        }
      } catch {
        // Server still down, keep polling
      }
    }, 2000)
  }

  async function dismissUpdate() {
    try {
      await updateApi.dismiss()
      state.value = 'None'
      checkResult.value = undefined
      availableInfo.value = undefined
    } catch (err) {
      console.error('Dismiss update failed:', err)
    }
  }

  return {
    state,
    progress,
    downloadedBytes,
    totalBytes,
    currentPackage,
    message,
    error,
    checkResult,
    availableInfo,
    isUpdateAvailable,
    isDownloading,
    isReady,
    isApplying,
    hasError,
    formatBytes,
    init,
    connectHub,
    disconnectHub,
    fetchStatus,
    checkForUpdate,
    downloadUpdate,
    cancelDownload,
    applyUpdate,
    dismissUpdate,
  }
})
