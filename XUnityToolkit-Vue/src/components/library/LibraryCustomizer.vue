<script setup lang="ts">
import { ref, computed, onBeforeUnmount } from 'vue'
import { NIcon, NSwitch, NPopover, NColorPicker } from 'naive-ui'
import { TuneRound } from '@vicons/material'
import { useGamesStore } from '@/stores/games'
import { useThemeStore, accentPresets } from '@/stores/theme'

const gamesStore = useGamesStore()
const themeStore = useThemeStore()

const showColorPicker = ref(false)
const isCustomAccent = computed(() =>
  !accentPresets.some(p => p.hex === themeStore.accentColor)
)

let saveTimer: ReturnType<typeof setTimeout> | null = null

function setAccentFromPicker(hex: string) {
  themeStore.setAccentColor(hex)
  if (saveTimer) clearTimeout(saveTimer)
  saveTimer = setTimeout(() => {
    import('@/api/games').then(({ settingsApi }) => {
      settingsApi.get().then(s => {
        s.accentColor = hex
        settingsApi.save(s)
      })
    })
  }, 500)
}

onBeforeUnmount(() => {
  if (saveTimer) clearTimeout(saveTimer)
})

const cardSizeOptions = [
  { value: 'small' as const, label: 'S' },
  { value: 'medium' as const, label: 'M' },
  { value: 'large' as const, label: 'L' },
  { value: 'xlarge' as const, label: 'XL' },
]

const gapOptions = [
  { value: 'compact' as const, label: '紧凑' },
  { value: 'normal' as const, label: '标准' },
  { value: 'spacious' as const, label: '宽松' },
]

function setCardSize(size: 'small' | 'medium' | 'large' | 'xlarge') {
  gamesStore.setCardSize(size)
  gamesStore.savePreferences()
}

function setGap(gap: 'compact' | 'normal' | 'spacious') {
  gamesStore.setGap(gap)
  gamesStore.savePreferences()
}

function toggleLabels(val: boolean) {
  gamesStore.setShowLabels(val)
  gamesStore.savePreferences()
}

function setAccent(hex: string) {
  themeStore.setAccentColor(hex)
  // Also persist to backend
  import('@/api/games').then(({ settingsApi }) => {
    settingsApi.get().then(settings => {
      settings.accentColor = hex
      settingsApi.save(settings)
    })
  })
}
</script>

<template>
  <NPopover trigger="click" placement="bottom-end" :width="280">
    <template #trigger>
      <button class="customizer-trigger" title="显示设置">
        <NIcon :size="18"><TuneRound /></NIcon>
      </button>
    </template>

    <div class="customizer-panel">
      <div class="panel-title">显示设置</div>

      <!-- Card Size -->
      <div class="option-group">
        <label class="option-label">卡片大小</label>
        <div class="segmented-control">
          <button
            v-for="opt in cardSizeOptions"
            :key="opt.value"
            class="seg-btn"
            :class="{ active: gamesStore.cardSize === opt.value }"
            @click="setCardSize(opt.value)"
          >
            {{ opt.label }}
          </button>
        </div>
      </div>

      <!-- Gap -->
      <div class="option-group">
        <label class="option-label">卡片间距</label>
        <div class="segmented-control">
          <button
            v-for="opt in gapOptions"
            :key="opt.value"
            class="seg-btn"
            :class="{ active: gamesStore.gap === opt.value }"
            @click="setGap(opt.value)"
          >
            {{ opt.label }}
          </button>
        </div>
      </div>

      <!-- Show Labels -->
      <div class="option-group option-row">
        <label class="option-label">显示游戏名称</label>
        <NSwitch :value="gamesStore.showLabels" @update:value="toggleLabels" size="small" />
      </div>

      <!-- Accent Color -->
      <div class="option-group">
        <label class="option-label">主题色</label>
        <div class="color-grid">
          <button
            v-for="preset in accentPresets"
            :key="preset.hex"
            class="color-swatch"
            :class="{ active: themeStore.accentColor === preset.hex }"
            :style="{ '--swatch-color': preset.hex }"
            :title="preset.name"
            @click="setAccent(preset.hex)"
          >
            <svg v-if="themeStore.accentColor === preset.hex" class="check-icon" viewBox="0 0 16 16" fill="none">
              <path d="M4 8.5L7 11.5L12 5" stroke="white" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
            </svg>
          </button>
          <NColorPicker
            :show="showColorPicker"
            :value="themeStore.accentColor"
            :modes="['hex']"
            :show-alpha="false"
            :swatches="accentPresets.map(p => p.hex)"
            :actions="[]"
            placement="bottom-end"
            @update:show="showColorPicker = $event"
            @update:value="setAccentFromPicker($event)"
          >
            <template #trigger>
              <button
                class="color-swatch custom-swatch"
                :class="{ active: isCustomAccent }"
                :style="isCustomAccent ? { '--swatch-color': themeStore.accentColor } : {}"
                title="自定义颜色"
                @click="showColorPicker = !showColorPicker"
              >
                <svg v-if="isCustomAccent" class="check-icon" viewBox="0 0 16 16" fill="none">
                  <path d="M4 8.5L7 11.5L12 5" stroke="white" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
                </svg>
              </button>
            </template>
          </NColorPicker>
        </div>
      </div>
    </div>
  </NPopover>
</template>

<style scoped>
.customizer-trigger {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 34px;
  height: 28px;
  border-radius: var(--radius-sm);
  border: 1px solid var(--border);
  background: var(--bg-card);
  color: var(--text-2);
  cursor: pointer;
  transition: all 0.2s ease;
}

.customizer-trigger:hover {
  color: var(--accent);
  border-color: var(--accent-border);
  background: var(--accent-soft);
}

.customizer-panel {
  padding: 4px 0;
}

.panel-title {
  font-family: var(--font-display);
  font-size: 14px;
  font-weight: 600;
  color: var(--text-1);
  margin-bottom: 16px;
  letter-spacing: -0.01em;
}

.option-group {
  margin-bottom: 16px;
}

.option-group:last-child {
  margin-bottom: 0;
}

.option-label {
  display: block;
  font-size: 12px;
  font-weight: 500;
  color: var(--text-2);
  margin-bottom: 8px;
  letter-spacing: 0.02em;
}

.option-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.option-row .option-label {
  margin-bottom: 0;
}

/* Segmented Control */
.segmented-control {
  display: flex;
  gap: 0;
  background: var(--bg-muted);
  border-radius: var(--radius-sm);
  padding: 3px;
  border: 1px solid var(--border);
}

.seg-btn {
  flex: 1;
  padding: 5px 0;
  font-size: 12px;
  font-weight: 500;
  font-family: var(--font-body);
  color: var(--text-3);
  background: transparent;
  border: none;
  border-radius: 5px;
  cursor: pointer;
  transition: all 0.2s ease;
  white-space: nowrap;
}

.seg-btn:hover {
  color: var(--text-1);
}

.seg-btn.active {
  background: var(--accent);
  color: #fff;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.2);
}

/* Color Swatches */
.color-grid {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.color-swatch {
  width: 28px;
  height: 28px;
  border-radius: 50%;
  border: 2px solid transparent;
  background: var(--swatch-color);
  cursor: pointer;
  transition: all 0.2s var(--ease-out);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0;
  position: relative;
  appearance: none;
}

.color-swatch:hover {
  transform: scale(1.15);
  box-shadow: 0 2px 12px color-mix(in srgb, var(--swatch-color) 50%, transparent);
}

.color-swatch.active {
  border-color: var(--text-1);
  transform: scale(1.1);
  box-shadow: 0 2px 12px color-mix(in srgb, var(--swatch-color) 40%, transparent);
}

.check-icon {
  width: 12px;
  height: 12px;
}

.custom-swatch:not(.active) {
  background: conic-gradient(
    #f43f5e, #f97316, #f59e0b, #10b981, #06b6d4, #3b82f6, #8b5cf6, #f43f5e
  ) border-box;
}
</style>
