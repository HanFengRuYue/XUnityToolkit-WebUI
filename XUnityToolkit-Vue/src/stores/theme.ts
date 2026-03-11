import { defineStore } from 'pinia'
import { ref } from 'vue'

export type ThemeMode = 'dark' | 'light'

export interface AccentColorPreset {
  hex: string
  name: string
}

export const accentPresets: AccentColorPreset[] = [
  { hex: '#3b82f6', name: '钴蓝' },
  { hex: '#06b6d4', name: '天蓝' },
  { hex: '#14b8a6', name: '青绿' },
  { hex: '#10b981', name: '翠绿' },
  { hex: '#f59e0b', name: '琥珀' },
  { hex: '#f97316', name: '橙色' },
  { hex: '#f43f5e', name: '玫红' },
  { hex: '#8b5cf6', name: '紫色' },
]

function hexToRgb(hex: string): { r: number; g: number; b: number } {
  const result = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex)
  if (!result) return { r: 59, g: 130, b: 246 }
  return { r: parseInt(result[1]!, 16), g: parseInt(result[2]!, 16), b: parseInt(result[3]!, 16) }
}

function clamp(v: number): number {
  return Math.min(255, Math.max(0, Math.round(v)))
}

function rgbToHex(r: number, g: number, b: number): string {
  return '#' + [r, g, b].map(v => clamp(v).toString(16).padStart(2, '0')).join('')
}

export function lightenColor(hex: string, amount: number): string {
  const { r, g, b } = hexToRgb(hex)
  return rgbToHex(r + (255 - r) * amount, g + (255 - g) * amount, b + (255 - b) * amount)
}

export function darkenColor(hex: string, amount: number): string {
  const { r, g, b } = hexToRgb(hex)
  return rgbToHex(r * (1 - amount), g * (1 - amount), b * (1 - amount))
}

/** Generate Naive UI primary color variants from a single accent hex */
export function getAccentVariants(hex: string, mode: ThemeMode) {
  if (mode === 'dark') {
    return {
      primary: hex,
      hover: lightenColor(hex, 0.25),
      pressed: darkenColor(hex, 0.2),
    }
  } else {
    const base = darkenColor(hex, 0.15)
    return {
      primary: base,
      hover: darkenColor(hex, 0.25),
      pressed: darkenColor(hex, 0.35),
    }
  }
}

export const useThemeStore = defineStore('theme', () => {
  const mode = ref<ThemeMode>(loadInitialTheme())
  const accentColor = ref<string>(loadInitialAccent())

  function loadInitialTheme(): ThemeMode {
    const saved = localStorage.getItem('theme')
    if (saved === 'light' || saved === 'dark') return saved
    // Auto-detect OS theme, default to light if unavailable
    if (window.matchMedia?.('(prefers-color-scheme: dark)').matches) return 'dark'
    return 'light'
  }

  function loadInitialAccent(): string {
    return localStorage.getItem('accentColor') || '#3b82f6'
  }

  function setTheme(theme: ThemeMode) {
    mode.value = theme
    localStorage.setItem('theme', theme)
    applyTheme(theme)
    applyAccentColor(accentColor.value, theme)
  }

  function toggle() {
    setTheme(mode.value === 'dark' ? 'light' : 'dark')
  }

  function setAccentColor(hex: string) {
    accentColor.value = hex
    localStorage.setItem('accentColor', hex)
    applyAccentColor(hex, mode.value)
  }

  function applyTheme(theme: ThemeMode) {
    document.documentElement.setAttribute('data-theme', theme)
  }

  function applyAccentColor(hex: string, theme: ThemeMode) {
    const { r, g, b } = hexToRgb(hex)
    const el = document.documentElement.style
    const effectiveHex = theme === 'light' ? darkenColor(hex, 0.15) : hex
    const { r: er, g: eg, b: eb } = hexToRgb(effectiveHex)

    el.setProperty('--accent', effectiveHex)
    el.setProperty('--accent-soft', `rgba(${er}, ${eg}, ${eb}, ${theme === 'dark' ? '0.10' : '0.08'})`)
    el.setProperty('--accent-glow', `rgba(${er}, ${eg}, ${eb}, ${theme === 'dark' ? '0.25' : '0.15'})`)
    el.setProperty('--accent-border', `rgba(${er}, ${eg}, ${eb}, ${theme === 'dark' ? '0.20' : '0.18'})`)
    el.setProperty('--ambient-1', `rgba(${r}, ${g}, ${b}, ${theme === 'dark' ? '0.025' : '0.015'})`)
    el.setProperty('--selection-bg', `rgba(${er}, ${eg}, ${eb}, ${theme === 'dark' ? '0.25' : '0.20'})`)
    el.setProperty('--logo-glow', `drop-shadow(0 0 8px rgba(${er}, ${eg}, ${eb}, 0.3))`)
    el.setProperty('--shadow-glow', `0 0 20px rgba(${er}, ${eg}, ${eb}, 0.12)`)
    el.setProperty(
      '--shadow-card-hover',
      theme === 'dark'
        ? `0 12px 40px rgba(0, 0, 0, 0.4), 0 0 0 1px rgba(${er}, ${eg}, ${eb}, 0.08)`
        : `0 8px 32px rgba(0, 0, 0, 0.08), 0 0 0 1px rgba(${er}, ${eg}, ${eb}, 0.12)`
    )
  }

  // Apply on init
  applyTheme(mode.value)
  applyAccentColor(accentColor.value, mode.value)

  return { mode, accentColor, setTheme, toggle, setAccentColor }
})
