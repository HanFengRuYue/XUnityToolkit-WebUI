import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { gamesApi, settingsApi } from '@/api/games'
import type { Game } from '@/api/types'
import { useThemeStore } from '@/stores/theme'

export const useGamesStore = defineStore('games', () => {
  const games = ref<Game[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)
  const viewMode = ref<'grid' | 'list'>('grid')
  const sortBy = ref<'name' | 'recent' | 'added'>('name')
  const cardSize = ref<'small' | 'medium' | 'large' | 'xlarge'>('medium')
  const gap = ref<'compact' | 'normal' | 'spacious'>('normal')
  const showLabels = ref(true)

  const gameCount = computed(() => games.value.length)
  const installedCount = computed(
    () => games.value.filter((g) => g.installState === 'FullyInstalled').length,
  )

  const sortedGames = computed(() => {
    const sorted = [...games.value]
    switch (sortBy.value) {
      case 'recent':
        return sorted.sort((a, b) => {
          const aTime = a.lastPlayedAt ? new Date(a.lastPlayedAt).getTime() : 0
          const bTime = b.lastPlayedAt ? new Date(b.lastPlayedAt).getTime() : 0
          return bTime - aTime
        })
      case 'added':
        return sorted.sort((a, b) =>
          new Date(b.addedAt).getTime() - new Date(a.addedAt).getTime()
        )
      default:
        return sorted.sort((a, b) => a.name.localeCompare(b.name))
    }
  })

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

  async function renameGame(id: string, name: string) {
    try {
      const game = await gamesApi.update(id, { name })
      const index = games.value.findIndex((g) => g.id === id)
      if (index >= 0) games.value[index] = game
      return game
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to rename game'
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

  async function launchGame(id: string) {
    try {
      await gamesApi.launch(id)
      // Update lastPlayedAt locally
      const game = games.value.find((g) => g.id === id)
      if (game) game.lastPlayedAt = new Date().toISOString()
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to launch game'
    }
  }

  async function loadPreferences() {
    try {
      const settings = await settingsApi.get()
      if (settings.libraryViewMode === 'grid' || settings.libraryViewMode === 'list')
        viewMode.value = settings.libraryViewMode
      if (settings.librarySortBy === 'name' || settings.librarySortBy === 'recent' || settings.librarySortBy === 'added')
        sortBy.value = settings.librarySortBy
      if (settings.libraryCardSize === 'small' || settings.libraryCardSize === 'medium' || settings.libraryCardSize === 'large' || settings.libraryCardSize === 'xlarge')
        cardSize.value = settings.libraryCardSize
      if (settings.libraryGap === 'compact' || settings.libraryGap === 'normal' || settings.libraryGap === 'spacious')
        gap.value = settings.libraryGap
      if (typeof settings.libraryShowLabels === 'boolean')
        showLabels.value = settings.libraryShowLabels
      // Sync accent color from backend (overrides localStorage if different)
      if (settings.accentColor) {
        const themeStore = useThemeStore()
        themeStore.setAccentColor(settings.accentColor)
      }
    } catch { /* ignore */ }
  }

  async function savePreferences() {
    try {
      const settings = await settingsApi.get()
      settings.libraryViewMode = viewMode.value
      settings.librarySortBy = sortBy.value
      settings.libraryCardSize = cardSize.value
      settings.libraryGap = gap.value
      settings.libraryShowLabels = showLabels.value
      await settingsApi.save(settings)
    } catch { /* ignore */ }
  }

  function getGame(id: string) {
    return computed(() => games.value.find((g) => g.id === id))
  }

  return {
    games,
    loading,
    error,
    viewMode,
    sortBy,
    cardSize,
    gap,
    showLabels,
    sortedGames,
    gameCount,
    installedCount,
    fetchGames,
    addGame,
    renameGame,
    removeGame,
    refreshGame,
    detectGame,
    launchGame,
    loadPreferences,
    savePreferences,
    getGame,
  }
})
