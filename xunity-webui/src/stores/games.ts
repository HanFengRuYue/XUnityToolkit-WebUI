import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { gamesApi, dialogApi } from '@/api/games'
import type { Game } from '@/api/types'

export const useGamesStore = defineStore('games', () => {
  const games = ref<Game[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  const gameCount = computed(() => games.value.length)
  const installedCount = computed(
    () => games.value.filter((g) => g.installState === 'FullyInstalled').length,
  )

  async function fetchGames() {
    loading.value = true
    error.value = null
    try {
      games.value = await gamesApi.list()
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to load games'
    } finally {
      loading.value = false
    }
  }

  async function addGameViaDialog() {
    try {
      const path = await dialogApi.selectFolder()
      if (!path) return null
      const game = await gamesApi.add(path)
      games.value.push(game)
      return game
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to add game'
      return null
    }
  }

  async function addGame(gamePath: string, name?: string) {
    try {
      const game = await gamesApi.add(gamePath, name)
      games.value.push(game)
      return game
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to add game'
      return null
    }
  }

  async function removeGame(id: string) {
    try {
      await gamesApi.remove(id)
      games.value = games.value.filter((g) => g.id !== id)
      return true
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to remove game'
      return false
    }
  }

  async function refreshGame(id: string) {
    try {
      const game = await gamesApi.get(id)
      const index = games.value.findIndex((g) => g.id === id)
      if (index >= 0) games.value[index] = game
      return game
    } catch {
      return null
    }
  }

  async function detectGame(id: string) {
    try {
      const info = await gamesApi.detect(id)
      await refreshGame(id)
      return info
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Detection failed'
      return null
    }
  }

  function getGame(id: string) {
    return computed(() => games.value.find((g) => g.id === id))
  }

  return {
    games,
    loading,
    error,
    gameCount,
    installedCount,
    fetchGames,
    addGameViaDialog,
    addGame,
    removeGame,
    refreshGame,
    detectGame,
    getGame,
  }
})
