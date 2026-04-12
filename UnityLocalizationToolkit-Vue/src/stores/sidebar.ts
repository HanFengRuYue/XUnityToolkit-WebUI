import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

export const useSidebarStore = defineStore('sidebar', () => {
  const COLLAPSED_WIDTH = 64
  const DEFAULT_WIDTH = 230
  const MIN_WIDTH = 180
  const MAX_WIDTH = 400

  const collapsed = ref(localStorage.getItem('sidebarCollapsed') === 'true')
  const customWidth = ref(loadCustomWidth())

  function loadCustomWidth(): number {
    const saved = localStorage.getItem('sidebarWidth')
    if (saved) {
      const num = parseInt(saved, 10)
      if (num >= MIN_WIDTH && num <= MAX_WIDTH) return num
    }
    return DEFAULT_WIDTH
  }

  const effectiveWidth = computed(() =>
    collapsed.value ? COLLAPSED_WIDTH : customWidth.value
  )

  function toggleCollapse() {
    collapsed.value = !collapsed.value
    localStorage.setItem('sidebarCollapsed', String(collapsed.value))
  }

  function setWidth(width: number) {
    const clamped = Math.min(MAX_WIDTH, Math.max(MIN_WIDTH, width))
    customWidth.value = clamped
    localStorage.setItem('sidebarWidth', String(clamped))
  }

  function resetWidth() {
    setWidth(DEFAULT_WIDTH)
  }

  return {
    collapsed, customWidth, effectiveWidth,
    COLLAPSED_WIDTH, DEFAULT_WIDTH, MIN_WIDTH, MAX_WIDTH,
    toggleCollapse, setWidth, resetWidth,
  }
})
