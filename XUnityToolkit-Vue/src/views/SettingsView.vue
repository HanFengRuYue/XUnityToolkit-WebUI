<script setup lang="ts">
import { ref, computed, onMounted, watch, nextTick } from 'vue'
import {
  NButton,
  NInput,
  NSelect,
  NIcon,
  useMessage,
  useDialog,
} from 'naive-ui'
import {
  CloudDownloadOutlined,
  InfoOutlined,
  PaletteOutlined,
  TuneOutlined,
  LocalOfferOutlined,
  LayersOutlined,
  DisplaySettingsOutlined,
  PersonOutlined,
  OpenInNewOutlined,
  WarningAmberOutlined,
  ImageOutlined,
  ColorLensOutlined,
} from '@vicons/material'
import { LogoGithub } from '@vicons/ionicons5'
import { settingsApi } from '@/api/games'
import type { AppSettings } from '@/api/types'
import { useThemeStore, accentPresets } from '@/stores/theme'
import type { ThemeMode } from '@/stores/theme'
import { useAutoSave } from '@/composables/useAutoSave'

defineOptions({ name: 'SettingsView' })

const message = useMessage()
const dialog = useDialog()
const themeStore = useThemeStore()

// Settings
const settings = ref<AppSettings>({
  hfMirrorUrl: 'https://hf-mirror.com',
  theme: themeStore.mode,
  aiTranslation: {
    enabled: true,
    activeMode: 'cloud',
    maxConcurrency: 4,
    port: 51821,
    systemPrompt: '',
    temperature: 0.3,
    contextSize: 10,
    localContextSize: 0,
    endpoints: [],
    glossaryExtractionEnabled: false,
    glossaryExtractionEndpointId: undefined,
  },
  steamGridDbApiKey: undefined,
  libraryViewMode: 'grid',
  librarySortBy: 'name',
  accentColor: themeStore.accentColor,
  libraryCardSize: 'medium',
  libraryGap: 'normal',
  libraryShowLabels: true,
})

const themeOptions = [
  { label: '深色主题', value: 'dark' },
  { label: '浅色主题', value: 'light' },
]

const { enable: enableAutoSave } = useAutoSave(
  () => settings.value,
  async () => {
    try {
      await settingsApi.save(settings.value)
    } catch {
      message.error('保存设置失败')
    }
  },
  { debounceMs: 1000, deep: true },
)

// Sync theme changes immediately when user selects
watch(() => settings.value.theme, (newTheme) => {
  themeStore.setTheme(newTheme as ThemeMode)
})

// Sync accent color changes immediately
watch(() => settings.value.accentColor, (newColor) => {
  if (newColor) themeStore.setAccentColor(newColor)
})

async function loadSettings() {
  try {
    const loaded = await settingsApi.get()
    // Use frontend theme store as source of truth (localStorage + OS detection),
    // don't let backend defaults override the correctly detected theme
    loaded.theme = themeStore.mode
    loaded.accentColor = loaded.accentColor || themeStore.accentColor
    settings.value = loaded
  } catch {
    // Use defaults
  }
  await nextTick()
  enableAutoSave()
}

// Version
const version = ref('...')

const shortVersion = computed(() => {
  // Keep only first 3 segments: "1.0.0+abc..." → "1.0.0"
  const match = version.value.match(/^(\d+\.\d+\.\d+)/)
  return match ? match[1] : version.value
})

async function loadVersion() {
  try {
    const info = await settingsApi.getVersion()
    version.value = info.version
  } catch {
    version.value = '1.0.0'
  }
}

// Reset
const resetLoading = ref(false)

function handleReset() {
  dialog.warning({
    title: '重置所有配置',
    content: '此操作将删除所有设置、游戏库、下载缓存和备份数据，且无法恢复。确定要继续吗？',
    positiveText: '确认重置',
    negativeText: '取消',
    onPositiveClick: async () => {
      resetLoading.value = true
      try {
        const result = await settingsApi.reset()
        if (result.partial) {
          message.warning('部分数据清除失败，请关闭占用的程序后重试')
        } else {
          message.success('已重置所有配置')
        }
        setTimeout(() => window.location.reload(), 500)
      } catch {
        message.error('重置失败')
      } finally {
        resetLoading.value = false
      }
    },
  })
}

onMounted(() => {
  loadSettings()
  loadVersion()
})
</script>

<template>
  <div class="settings-page">
    <h1 class="page-title" style="animation-delay: 0s">
      <span class="page-title-icon">
        <NIcon :size="24"><TuneOutlined /></NIcon>
      </span>
      设置
    </h1>

    <!-- Settings card -->
    <div class="section-card" style="animation-delay: 0.05s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon download">
            <NIcon :size="16"><DisplaySettingsOutlined /></NIcon>
          </span>
          外观与下载
        </h2>
      </div>
      <div class="settings-form">
        <div class="form-row">
          <label class="form-label">
            <NIcon :size="14" color="var(--text-3)"><PaletteOutlined /></NIcon>
            界面主题
          </label>
          <NSelect
            v-model:value="settings.theme"
            :options="themeOptions"
          />
        </div>
        <div class="form-row">
          <label class="form-label">
            <NIcon :size="14" color="var(--text-3)"><ColorLensOutlined /></NIcon>
            主题色
          </label>
          <div class="accent-color-row">
            <button
              v-for="preset in accentPresets"
              :key="preset.hex"
              class="accent-swatch"
              :class="{ active: settings.accentColor === preset.hex }"
              :style="{ '--swatch-color': preset.hex }"
              :title="preset.name"
              @click="settings.accentColor = preset.hex"
            >
              <svg v-if="settings.accentColor === preset.hex" class="swatch-check" viewBox="0 0 16 16" fill="none">
                <path d="M4 8.5L7 11.5L12 5" stroke="white" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
              </svg>
            </button>
          </div>
          <span class="form-hint">选择应用的主题色，将影响所有页面的高亮和按钮颜色</span>
        </div>
        <div class="form-row">
          <label class="form-label">
            <NIcon :size="14" color="var(--text-3)"><CloudDownloadOutlined /></NIcon>
            HuggingFace 镜像地址
          </label>
          <NInput
            v-model:value="settings.hfMirrorUrl"
            placeholder="https://hf-mirror.com"
            clearable
          />
          <span class="form-hint">加速 AI 模型下载（留空使用官方地址）</span>
        </div>
      </div>
    </div>

    <!-- Game Covers -->
    <div class="section-card" style="animation-delay: 0.15s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon covers">
            <NIcon :size="16"><ImageOutlined /></NIcon>
          </span>
          游戏封面
        </h2>
      </div>
      <div class="settings-form">
        <div class="form-row">
          <label class="form-label">SteamGridDB API Key</label>
          <NInput
            :value="settings.steamGridDbApiKey ?? ''"
            @update:value="settings.steamGridDbApiKey = $event || undefined"
            type="password"
            show-password-on="click"
            placeholder="输入 SteamGridDB API Key"
          />
          <span class="form-hint">
            免费注册获取：<a href="https://www.steamgriddb.com/profile/preferences/api" target="_blank" rel="noopener noreferrer" class="about-link">steamgriddb.com</a>
            — 用于在游戏库中搜索高清封面图
          </span>
        </div>
      </div>
    </div>

    <!-- Danger Zone (full width) -->
    <div class="section-card danger-card" style="animation-delay: 0.2s">
      <div class="danger-bar"></div>
      <div class="danger-body">
        <div class="section-header">
          <h2 class="section-title">
            <span class="section-icon danger">
              <NIcon :size="16"><WarningAmberOutlined /></NIcon>
            </span>
            危险区域
          </h2>
        </div>
        <div class="danger-action">
          <div class="danger-text">
            <div class="danger-description">重置将删除所有应用数据，包括游戏库、设置、下载缓存和备份。此操作不可撤销。</div>
            <span class="danger-hint">重置后页面将自动刷新</span>
          </div>
          <NButton type="error" :loading="resetLoading" ghost @click="handleReset">
            重置所有配置
          </NButton>
        </div>
      </div>
    </div>

    <!-- About (full width) -->
    <div class="section-card" style="animation-delay: 0.25s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon about">
            <NIcon :size="16"><InfoOutlined /></NIcon>
          </span>
          关于
        </h2>
      </div>
      <div class="about-grid">
        <div class="info-card">
          <div class="info-card-icon version">
            <NIcon :size="18"><LocalOfferOutlined /></NIcon>
          </div>
          <div class="info-card-content">
            <span class="info-label">版本</span>
            <span class="info-value mono">v{{ shortVersion }}</span>
          </div>
        </div>
        <div class="info-card">
          <div class="info-card-icon tech">
            <NIcon :size="18"><LayersOutlined /></NIcon>
          </div>
          <div class="info-card-content">
            <span class="info-label">技术栈</span>
            <span class="info-value">.NET 10 + Vue 3</span>
          </div>
        </div>
        <div class="info-card">
          <div class="info-card-icon author">
            <NIcon :size="18"><PersonOutlined /></NIcon>
          </div>
          <div class="info-card-content">
            <span class="info-label">作者</span>
            <span class="info-value">
              <a
                href="https://github.com/HanFengRuYue"
                target="_blank"
                rel="noopener noreferrer"
                class="about-link"
              >
                寒枫如玥
                <NIcon :size="12" style="margin-left: 4px; vertical-align: middle;"><OpenInNewOutlined /></NIcon>
              </a>
            </span>
          </div>
        </div>
        <div class="info-card">
          <div class="info-card-icon github">
            <NIcon :size="18"><LogoGithub /></NIcon>
          </div>
          <div class="info-card-content">
            <span class="info-label">源代码</span>
            <span class="info-value">
              <a
                href="https://github.com/HanFengRuYue/XUnityToolkit-WebUI"
                target="_blank"
                rel="noopener noreferrer"
                class="about-link"
              >
                GitHub 仓库
                <NIcon :size="12" style="margin-left: 4px; vertical-align: middle;"><OpenInNewOutlined /></NIcon>
              </a>
            </span>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.settings-page {
  /* Full width - no max-width restriction */
  display: flex;
  flex-direction: column;
  gap: 16px;
  animation: fadeIn 0.3s ease;
}

.page-title {
  font-family: var(--font-display);
  font-size: 26px;
  font-weight: 600;
  color: var(--text-1);
  margin-bottom: 0;
  letter-spacing: -0.03em;
  animation: slideUp 0.4s var(--ease-out) backwards;
  display: flex;
  align-items: center;
  gap: 12px;
}

.page-title-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 38px;
  height: 38px;
  border-radius: 10px;
  background: var(--accent-soft);
  color: var(--accent);
  flex-shrink: 0;
}

/* ===== Section Card ===== */
.section-card {
  display: flex;
  flex-direction: column;
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  padding: 24px;
  animation: slideUp 0.5s var(--ease-out) backwards;
  transition: border-color 0.3s ease, background 0.3s ease, box-shadow 0.3s ease;
  box-shadow: var(--shadow-card-rest);
}

.section-card:hover {
  border-color: var(--border-hover);
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
}

.section-title {
  font-family: var(--font-display);
  font-size: 16px;
  font-weight: 600;
  color: var(--text-1);
  margin: 0;
  letter-spacing: -0.01em;
  display: flex;
  align-items: center;
  gap: 10px;
}

.section-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 30px;
  height: 30px;
  border-radius: 8px;
  background: var(--accent-soft);
  color: var(--accent);
  flex-shrink: 0;
}

.section-icon.danger {
  background: color-mix(in srgb, var(--danger) 10%, transparent);
  color: var(--danger);
}

.about-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 12px;
}

.info-card {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 14px 16px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  transition: border-color 0.2s ease, background 0.2s ease;
}

.info-card:hover {
  border-color: var(--border-hover);
  background: var(--bg-subtle-hover);
}

.info-card-icon {
  width: 36px;
  height: 36px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: var(--radius-sm);
  flex-shrink: 0;
  background: var(--accent-soft);
  color: var(--accent);
}


.info-card-content {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.info-label {
  font-size: 12px;
  font-weight: 500;
  color: var(--text-3);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.info-value {
  font-size: 14px;
  color: var(--text-1);
}

.info-value.mono {
  font-family: var(--font-mono);
  font-size: 13px;
  color: var(--text-2);
  word-break: break-all;
  line-height: 1.5;
}

/* ===== Settings Form ===== */
.settings-form {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.form-row {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.form-label {
  font-size: 13px;
  font-weight: 500;
  color: var(--text-2);
  display: flex;
  align-items: center;
  gap: 6px;
}

.form-hint {
  font-size: 12px;
  color: var(--text-3);
}

/* ===== Accent Color Swatches ===== */
.accent-color-row {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
}

.accent-swatch {
  width: 32px;
  height: 32px;
  border-radius: 50%;
  border: 2px solid transparent;
  background: var(--swatch-color);
  cursor: pointer;
  transition: all 0.2s var(--ease-out);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0;
}

.accent-swatch:hover {
  transform: scale(1.15);
  box-shadow: 0 2px 12px color-mix(in srgb, var(--swatch-color) 50%, transparent);
}

.accent-swatch.active {
  border-color: var(--text-1);
  transform: scale(1.1);
  box-shadow: 0 2px 12px color-mix(in srgb, var(--swatch-color) 40%, transparent);
}

.swatch-check {
  width: 14px;
  height: 14px;
}

/* ===== Danger Zone ===== */
.danger-card {
  flex-direction: row;
  padding: 0;
  overflow: hidden;
  border-color: color-mix(in srgb, var(--danger) 15%, transparent);
}

.danger-card:hover {
  border-color: color-mix(in srgb, var(--danger) 30%, transparent);
}

.danger-bar {
  width: 4px;
  flex-shrink: 0;
  background: var(--danger);
}

.danger-body {
  flex: 1;
  padding: 20px 24px;
  min-width: 0;
}

.danger-body .section-header {
  margin-bottom: 14px;
}

.danger-action {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 24px;
}

.danger-text {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.danger-description {
  font-size: 13px;
  color: var(--text-2);
  line-height: 1.5;
}

.danger-hint {
  font-size: 12px;
  color: var(--text-3);
}

/* ===== About Link ===== */
.about-link {
  color: var(--accent);
  text-decoration: none;
  transition: opacity 0.2s ease;
}

.about-link:hover {
  opacity: 0.8;
}

/* ===== Responsive ===== */
@media (max-width: 768px) {
  .section-card {
    padding: 16px;
  }

  .danger-body {
    padding: 16px;
  }

  .about-grid {
    grid-template-columns: repeat(2, 1fr);
    gap: 10px;
  }
}

@media (max-width: 480px) {
  .page-title {
    font-size: 20px;
    gap: 8px;
  }

  .page-title-icon {
    width: 32px;
    height: 32px;
    border-radius: 8px;
  }

  .section-card {
    padding: 14px;
    border-radius: var(--radius-md);
  }

  .about-grid {
    grid-template-columns: 1fr;
  }

  .danger-action {
    flex-direction: column;
    align-items: stretch;
    gap: 12px;
  }

  .danger-body {
    padding: 14px;
  }

}
</style>
