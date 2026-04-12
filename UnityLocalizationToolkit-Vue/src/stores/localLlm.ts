import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import * as signalR from '@microsoft/signalr'
import { localLlmApi } from '@/api/games'
import type { LocalLlmStatus, LocalLlmSettings, GpuInfo, BuiltInModelInfo, LocalLlmDownloadProgress, LlamaStatus, LlamaDownloadProgress } from '@/api/types'

export const useLocalLlmStore = defineStore('localLlm', () => {
  const status = ref<LocalLlmStatus | null>(null)
  const settings = ref<LocalLlmSettings | null>(null)
  const gpus = ref<GpuInfo[]>([])
  const catalog = ref<BuiltInModelInfo[]>([])
  const downloads = ref<Map<string, LocalLlmDownloadProgress>>(new Map())
  const llamaStatus = ref<LlamaStatus | null>(null)
  const llamaDownload = ref<LlamaDownloadProgress | null>(null)

  const isRunning = computed(() => status.value?.state === 'Running')
  const isStarting = computed(() => status.value?.state === 'Starting')
  const isBusy = computed(() =>
    status.value?.state === 'Starting' || status.value?.state === 'Stopping')

  let connection: signalR.HubConnection | null = null
  let downloadSyncTimer: ReturnType<typeof setInterval> | null = null
  let isSyncingDownloads = false

  function replaceDownloads(items: LocalLlmDownloadProgress[]) {
    downloads.value = new Map(items.map(item => [item.catalogId, item]))
  }

  function stopDownloadSync() {
    if (downloadSyncTimer !== null) {
      clearInterval(downloadSyncTimer)
      downloadSyncTimer = null
    }
  }

  function ensureDownloadSync() {
    if (downloadSyncTimer !== null) return

    downloadSyncTimer = setInterval(() => {
      void syncDownloadState()
    }, 3000)
  }

  async function syncDownloadState() {
    if (isSyncingDownloads) return
    isSyncingDownloads = true

    try {
      const [currentSettings, activeDownloads] = await Promise.all([
        localLlmApi.getSettings(),
        localLlmApi.getActiveDownloads(),
      ])

      settings.value = currentSettings
      replaceDownloads(activeDownloads)

      if (activeDownloads.length > 0) {
        ensureDownloadSync()
      } else {
        stopDownloadSync()
      }
    } catch {
      // Keep the last known UI state if reconciliation fails temporarily.
    } finally {
      isSyncingDownloads = false
    }
  }

  async function connect() {
    if (connection && connection.state !== signalR.HubConnectionState.Disconnected) return

    connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/install')
      .withAutomaticReconnect()
      .build()

    connection.on('localLlmStatusUpdate', (update: LocalLlmStatus) => {
      status.value = update
    })

    connection.on('localLlmDownloadProgress', (progress: LocalLlmDownloadProgress) => {
      if (progress.done || progress.error) {
        downloads.value.delete(progress.catalogId)
        downloads.value = new Map(downloads.value)
        void syncDownloadState()
      } else if (progress.paused) {
        downloads.value.delete(progress.catalogId)
        downloads.value = new Map(downloads.value)
        void syncDownloadState()
      } else {
        downloads.value.set(progress.catalogId, progress)
        downloads.value = new Map(downloads.value)
        ensureDownloadSync()
      }
    })

    connection.on('llamaDownloadProgress', (progress: LlamaDownloadProgress) => {
      if (progress.done && !progress.error) {
        llamaDownload.value = null
        fetchLlamaStatus()
      } else if (progress.error) {
        llamaDownload.value = progress
      } else {
        llamaDownload.value = progress
      }
    })

    connection.onreconnected(async () => {
      try { await connection?.invoke('JoinLocalLlmGroup') } catch { /* ignore */ }
      void syncDownloadState()
    })

    await connection.start()
    await connection.invoke('JoinLocalLlmGroup')
    await syncDownloadState()
  }

  async function disconnect() {
    if (connection) {
      try { await connection.invoke('LeaveLocalLlmGroup') } catch { /* ignore */ }
      await connection.stop()
      connection = null
    }
    stopDownloadSync()
  }

  async function fetchStatus() {
    status.value = await localLlmApi.getStatus()
  }

  async function fetchSettings() {
    settings.value = await localLlmApi.getSettings()
  }

  async function fetchGpus() {
    gpus.value = await localLlmApi.getGpus()
  }

  async function refreshGpus() {
    gpus.value = await localLlmApi.refreshGpus()
  }

  async function fetchCatalog() {
    catalog.value = await localLlmApi.getCatalog()
  }

  async function fetchLlamaStatus() {
    llamaStatus.value = await localLlmApi.getLlamaStatus()
  }

  async function fetchModels() {
    const models = await localLlmApi.getModels()
    if (settings.value) {
      settings.value = { ...settings.value, models }
    }
  }

  async function startServer(modelPath: string, gpuLayers?: number, contextLength?: number) {
    status.value = await localLlmApi.start(modelPath, gpuLayers, contextLength)
  }

  async function stopServer() {
    status.value = await localLlmApi.stop()
  }

  async function downloadModel(catalogId: string) {
    await localLlmApi.downloadModel(catalogId)
    downloads.value.set(catalogId, {
      catalogId,
      bytesDownloaded: 0,
      totalBytes: 0,
      speedBytesPerSec: 0,
      done: false,
    })
    downloads.value = new Map(downloads.value)
    ensureDownloadSync()
  }

  async function pauseDownload(catalogId: string) {
    await localLlmApi.pauseDownload(catalogId)
  }

  async function cancelDownload(catalogId: string) {
    await localLlmApi.cancelDownload(catalogId)
    downloads.value.delete(catalogId)
    downloads.value = new Map(downloads.value)
    await syncDownloadState()
  }

  async function downloadLlama() {
    await localLlmApi.downloadLlama()
  }

  async function cancelLlamaDownload() {
    await localLlmApi.cancelLlamaDownload()
    llamaDownload.value = null
  }

  async function retryLlamaDownload() {
    llamaDownload.value = null
    await localLlmApi.downloadLlama()
  }

  return {
    status, settings, gpus, catalog, downloads, llamaStatus, llamaDownload,
    isRunning, isStarting, isBusy,
    connect, disconnect,
    fetchStatus, fetchSettings, fetchGpus, refreshGpus, fetchCatalog, fetchLlamaStatus, fetchModels, syncDownloadState,
    startServer, stopServer, downloadModel, pauseDownload, cancelDownload,
    downloadLlama, cancelLlamaDownload, retryLlamaDownload,
  }
})
