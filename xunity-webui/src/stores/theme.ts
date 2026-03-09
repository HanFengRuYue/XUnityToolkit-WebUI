import { defineStore } from 'pinia'
import { ref, watch } from 'vue'

export type ThemeMode = 'dark' | 'light'

export const useThemeStore = defineStore('theme', () => {
  const mode = ref<ThemeMode>(loadInitialTheme())

  function loadInitialTheme(): ThemeMode {
    const saved = localStorage.getItem('theme')
    if (saved === 'light' || saved === 'dark') return saved
    return 'dark'
  }

  function setTheme(theme: ThemeMode) {
    mode.value = theme
    localStorage.setItem('theme', theme)
    applyTheme(theme)
  }

  function toggle() {
    setTheme(mode.value === 'dark' ? 'light' : 'dark')
  }

  function applyTheme(theme: ThemeMode) {
    document.documentElement.setAttribute('data-theme', theme)
  }

  // Apply on init
  applyTheme(mode.value)

  return { mode, setTheme, toggle }
})
