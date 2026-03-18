import { defineStore } from 'pinia'
import { ref } from 'vue'
import * as signalR from '@microsoft/signalr'
import { assetApi } from '@/api/games'
import type { AssetExtractionResult, PreTranslationStatus } from '@/api/types'

export const useAssetExtractionStore = defineStore('assetExtraction', () => {
  const extractionResult = ref<AssetExtractionResult | null>(null)
  const preTranslationStatus = ref<PreTranslationStatus | null>(null)
  const extracting = ref(false)
  const extractError = ref<string | null>(null)
  const termExtractionComplete = ref(false)

  let connection: signalR.HubConnection | null = null
  let activeGameId: string | null = null

  async function connect(gameId: string) {
    if (connection && connection.state !== signalR.HubConnectionState.Disconnected && activeGameId === gameId)
      return

    await disconnect()
    activeGameId = gameId

    connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/install')
      .withAutomaticReconnect()
      .build()

    connection.on('preTranslationUpdate', (update: PreTranslationStatus) => {
      preTranslationStatus.value = update
    })

    connection.on('roundProgress', (update: PreTranslationStatus) => {
      preTranslationStatus.value = update
    })

    connection.on('patternAnalysisProgress', (update: PreTranslationStatus) => {
      preTranslationStatus.value = update
    })

    connection.on('termExtractionComplete', (update: PreTranslationStatus) => {
      preTranslationStatus.value = update
      termExtractionComplete.value = true
    })

    connection.onreconnected(async () => {
      try {
        await connection?.invoke('JoinPreTranslationGroup', activeGameId)
      } catch { /* ignore */ }
    })

    await connection.start()
    await connection.invoke('JoinPreTranslationGroup', gameId)
  }

  async function disconnect() {
    if (connection) {
      if (activeGameId) {
        try {
          await connection.invoke('LeavePreTranslationGroup', activeGameId)
        } catch { /* ignore */ }
      }
      await connection.stop()
      connection = null
    }
    activeGameId = null
  }

  async function loadCachedResult(gameId: string) {
    extractionResult.value = await assetApi.getExtractedTexts(gameId)
  }

  async function extractAssets(gameId: string) {
    extracting.value = true
    extractError.value = null
    try {
      extractionResult.value = await assetApi.extractAssets(gameId)
    } catch (e: unknown) {
      extractError.value = e instanceof Error ? e.message : String(e)
      throw e
    } finally {
      extracting.value = false
    }
  }

  async function startPreTranslation(gameId: string, fromLang?: string, toLang?: string) {
    await connect(gameId)
    preTranslationStatus.value = await assetApi.startPreTranslation(gameId, fromLang, toLang)
  }

  async function cancelPreTranslation(gameId: string) {
    await assetApi.cancelPreTranslation(gameId)
  }

  async function fetchPreTranslationStatus(gameId: string) {
    preTranslationStatus.value = await assetApi.getPreTranslationStatus(gameId)
  }

  async function clearCache(gameId: string) {
    await assetApi.deleteExtractedTexts(gameId)
    extractionResult.value = null
  }

  return {
    extractionResult,
    preTranslationStatus,
    extracting,
    extractError,
    termExtractionComplete,
    connect,
    disconnect,
    loadCachedResult,
    extractAssets,
    startPreTranslation,
    cancelPreTranslation,
    fetchPreTranslationStatus,
    clearCache,
  }
})
