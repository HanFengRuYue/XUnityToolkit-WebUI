import { ref, computed } from 'vue'
import { defineStore } from 'pinia'
import * as signalR from '@microsoft/signalr'
import { updateApi } from '@/api/update'
import type { UpdateState, UpdateCheckResult, UpdateStatusInfo, UpdateAvailableInfo } from '@/api/types'

export const useUpdateStore = defineStore('update', () => {
  const state = ref<UpdateState>('none')
  const progress = ref(0)
  const downloadedBytes = ref(0)
  const totalBytes = ref(0)
  const currentPackage = ref<string>()
  const message = ref<string>()
  const error = ref<string>()

  const checkResult = ref<UpdateCheckResult>()
  const availableInfo = ref<UpdateAvailableInfo>()

  let connection: signalR.HubConnection | null = null

  const isUpdateAvailable = computed(() => state.value === 'available')
  const isDownloading = computed(() => state.value === 'downloading')
  const isReady = computed(() => state.value === 'ready')
  const hasError = computed(() => state.value === 'error')

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
    })

    connection.on('UpdateAvailable', (info: UpdateAvailableInfo) => {
      availableInfo.value = info
      state.value = 'available'
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

  async function checkForUpdate() {
    try {
      checkResult.value = await updateApi.check()
      if (checkResult.value.updateAvailable) {
        availableInfo.value = {
          version: checkResult.value.newVersion!,
          changelog: checkResult.value.changelog,
          downloadSize: checkResult.value.downloadSize,
          changedPackages: checkResult.value.changedPackages,
        }
      }
    } catch (err) {
      console.error('Check for update failed:', err)
    }
  }

  async function downloadUpdate() {
    try {
      await updateApi.download()
    } catch (err) {
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
    try {
      await updateApi.apply()
    } catch (err) {
      console.error('Apply update failed:', err)
    }
  }

  async function dismissUpdate() {
    try {
      await updateApi.dismiss()
      state.value = 'none'
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
    hasError,
    formatBytes,
    connectHub,
    disconnectHub,
    checkForUpdate,
    downloadUpdate,
    cancelDownload,
    applyUpdate,
    dismissUpdate,
  }
})
