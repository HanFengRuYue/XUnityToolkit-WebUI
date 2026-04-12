<script setup lang="ts">
import { ref, reactive, computed, onMounted, onActivated, watch, nextTick } from 'vue'
import {
  NButton,
  NInput,
  NSelect,
  NIcon,
  NProgress,
  NSpin,
  NSwitch,
  NSlider,
  NColorPicker,
  NTag,
  useMessage,
  useDialog,
} from 'naive-ui'
import {
  CloudDownloadOutlined,
  InfoOutlined,
  PaletteOutlined,
  TuneOutlined,
  LayersOutlined,
  DisplaySettingsOutlined,
  PersonOutlined,
  OpenInNewOutlined,
  WarningAmberOutlined,
  ImageOutlined,
  ColorLensOutlined,
  SystemUpdateAltOutlined,
  FolderOpenOutlined,
  FileDownloadOutlined,
  FileUploadOutlined,
  StorageOutlined,
  ExpandMoreOutlined,
  ZoomInOutlined,
} from '@vicons/material'
import { LogoGithub } from '@vicons/ionicons5'
import { settingsApi } from '@/api/games'
import { useFileExplorer } from '@/composables/useFileExplorer'
import type { AppSettings } from '@/api/types'
import { useThemeStore, accentPresets } from '@/stores/theme'
import type { ThemeMode } from '@/stores/theme'
import { useAutoSave } from '@/composables/useAutoSave'
import { Marked } from 'marked'
import { useUpdateStore } from '@/stores/update'

function escapeHtml(str: string): string {
  return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
}
const safeMarked = new Marked({
  renderer: {
    html({ text }: { text: string }) {
      return escapeHtml(text)
    },
  },
})

defineOptions({ name: 'SettingsView' })

const collapsed = reactive({
  covers: true,
  data: true,
})

const message = useMessage()
const dialog = useDialog()
const { selectFile } = useFileExplorer()
const themeStore = useThemeStore()
const updateStore = useUpdateStore()

const showColorPicker = ref(false)
const isCustomAccent = computed(() =>
  !accentPresets.some(p => p.hex === settings.value.accentColor)
)

// Settings
const modelDownloadSourceOptions = [
  { label: 'HuggingFace', value: 'HuggingFace' },
  { label: 'ModelScope（魔搭社区，默认）', value: 'ModelScope' },
]

const settings = ref<AppSettings>({
  hfMirrorUrl: 'https://hf-mirror.com',
  modelDownloadSource: 'ModelScope',
  theme: themeStore.mode,
  aiTranslation: {
    enabled: true,
    activeMode: 'cloud',
    maxConcurrency: 4,
    port: 51821,
    systemPrompt: '',
    temperature: 0.3,
    contextSize: 10,
    localContextSize: 3,
    localMinP: 0.05,
    localRepeatPenalty: 1.0,
    endpoints: [],
    glossaryExtractionEnabled: false,
    enablePreTranslationCache: true,
    termAuditEnabled: true,
    naturalTranslationMode: true,
    enableTranslationMemory: true,
    fuzzyMatchThreshold: 85,
    enableLlmPatternAnalysis: true,
    enableMultiRoundTranslation: true,
    enableAutoTermExtraction: true,
    autoApplyExtractedTerms: false,
  },
  steamGridDbApiKey: undefined,
  libraryViewMode: 'grid',
  librarySortBy: 'name',
  accentColor: themeStore.accentColor,
  libraryCardSize: 'medium',
  libraryGap: 'normal',
  libraryShowLabels: true,
  pageZoom: 0,
  receivePreReleaseUpdates: false,
  installOptions: {
    autoInstallTmpFont: true,
    autoDeployAiEndpoint: true,
    autoGenerateConfig: true,
    autoApplyOptimalConfig: true,
    autoExtractAssets: true,
    autoVerifyHealth: true,
  },
})

const themeOptions = [
  { label: '跟随系统', value: 'system' },
  { label: '深色主题', value: 'dark' },
  { label: '浅色主题', value: 'light' },
]

const { enable: enableAutoSave, disable: disableAutoSave } = useAutoSave(
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

// Sync page zoom changes immediately
watch(() => settings.value.pageZoom, (newZoom) => {
  if (typeof newZoom === 'number') themeStore.setPageZoom(newZoom)
})

function handleZoomChange(value: number | null) {
  if (value === null) return
  settings.value.pageZoom = value
}

function handleZoomReset() {
  settings.value.pageZoom = 0
}

async function loadSettings() {
  disableAutoSave()
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
  const match = version.value.match(/^(\d+\.\d+)/)
  return match ? match[1] : version.value
})

const buildNumber = computed(() => {
  const match = version.value.match(/^\d+\.\d+\.(.+)/)
  return match ? match[1] : ''
})

const changelogHtml = computed(() => {
  const md = updateStore.availableInfo?.changelog
  if (!md) return ''
  return safeMarked.parse(md, { async: false }) as string
})

interface ChangelogEntry {
  type: string
  text: string
  hash?: string
}

const typeLabels: Record<string, string> = {
  feat: '新功能',
  fix: '修复',
  docs: '文档',
  refactor: '重构',
  perf: '性能',
  ci: 'CI',
  chore: '杂项',
  style: '样式',
  test: '测试',
}

const changelogEntries = computed<ChangelogEntry[]>(() => {
  const md = updateStore.availableInfo?.changelog
  if (!md) return []
  const entries: ChangelogEntry[] = []
  for (const line of md.split('\n')) {
    // Match conventional commit: * type: description
    const match = line.match(/^[\*\-]\s+(\w+):\s+(.+)\s*$/)
    if (match && match[1] && match[2]) {
      let text = match[2]
      let hash: string | undefined
      // Extract trailing hash: (abc1234) or (`abc1234`)
      const hashMatch = text.match(/\s*\(?`?([a-f0-9]{6,40})`?\)?\s*$/)
      if (hashMatch && hashMatch[1]) {
        hash = hashMatch[1]
        text = text.slice(0, -hashMatch[0].length).trim()
      }
      entries.push({ type: match[1], text, hash })
    } else {
      // Fallback for lines without conventional commit format
      const plain = line.replace(/^[\*\-]\s+/, '').trim()
      if (plain) {
        entries.push({ type: '', text: plain })
      }
    }
  }
  return entries
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
        localStorage.clear()
        setTimeout(() => window.location.reload(), 500)
      } catch {
        message.error('重置失败')
      } finally {
        resetLoading.value = false
      }
    },
  })
}

// Data management
const dataPath = ref('')
const exportLoading = ref(false)
const importLoading = ref(false)

async function loadDataPath() {
  try {
    const info = await settingsApi.getDataPath()
    dataPath.value = info.path
  } catch {
    dataPath.value = '(unknown)'
  }
}

async function handleOpenDataFolder() {
  try {
    await settingsApi.openDataFolder()
  } catch {
    message.error('无法打开文件夹')
  }
}

async function handleExport() {
  exportLoading.value = true
  try {
    await settingsApi.exportData()
    message.success('导出成功')
  } catch {
    message.error('导出失败')
  } finally {
    exportLoading.value = false
  }
}

async function handleImport() {
  const path = await selectFile({
    title: '导入配置文件',
    filters: [{ label: 'ZIP 文件', extensions: ['.zip'] }],
  })
  if (!path) return
  dialog.warning({
    title: '导入配置',
    content: '导入将覆盖当前所有配置数据，导入完成后需要重启程序。确定要继续吗？',
    positiveText: '确认导入',
    negativeText: '取消',
    onPositiveClick: async () => {
      importLoading.value = true
      try {
        await settingsApi.importFromPath(path)
        message.success('导入成功，请重启程序以使配置生效')
      } catch (e) {
        message.error(e instanceof Error ? e.message : '导入失败')
      } finally {
        importLoading.value = false
      }
    },
  })
}

const hasChecked = ref(false)

async function handleCheckUpdate() {
  await updateStore.checkForUpdate()
  hasChecked.value = true
  if (updateStore.state === 'None') {
    message.success('已是最新版本')
  }
}

onMounted(() => {
  loadSettings()
  loadVersion()
  loadDataPath()
})

onActivated(() => loadSettings())
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
            <NColorPicker
              :show="showColorPicker"
              :value="settings.accentColor"
              :modes="['hex']"
              :show-alpha="false"
              :swatches="accentPresets.map(p => p.hex)"
              :actions="[]"
              @update:show="showColorPicker = $event"
              @update:value="settings.accentColor = $event"
            >
              <template #trigger>
                <button
                  class="accent-swatch custom-swatch"
                  :class="{ active: isCustomAccent }"
                  :style="isCustomAccent ? { '--swatch-color': settings.accentColor } : {}"
                  title="自定义颜色"
                  @click="showColorPicker = !showColorPicker"
                >
                  <svg v-if="isCustomAccent" class="swatch-check" viewBox="0 0 16 16" fill="none">
                    <path d="M4 8.5L7 11.5L12 5" stroke="white" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
                  </svg>
                </button>
              </template>
            </NColorPicker>
          </div>
          <span class="form-hint">选择应用的主题色，将影响所有页面的高亮和按钮颜色</span>
        </div>
        <div class="form-row">
          <label class="form-label">
            <NIcon :size="14" color="var(--text-3)"><ZoomInOutlined /></NIcon>
            页面缩放（{{ themeStore.effectiveZoom }}%）
          </label>
          <div class="zoom-slider-row">
            <NSlider
              :value="themeStore.effectiveZoom"
              @update:value="handleZoomChange"
              :min="50"
              :max="200"
              :step="5"
              :format-tooltip="(v: number) => `${v}%`"
            />
            <NButton
              size="tiny"
              quaternary
              :disabled="settings.pageZoom === 0"
              @click="handleZoomReset"
            >
              重置
            </NButton>
          </div>
          <span class="form-hint">调整界面整体缩放比例，重置将恢复为系统默认缩放</span>
        </div>
        <div class="form-row">
          <label class="form-label">
            <NIcon :size="14" color="var(--text-3)"><CloudDownloadOutlined /></NIcon>
            模型下载源
          </label>
          <NSelect
            v-model:value="settings.modelDownloadSource"
            :options="modelDownloadSourceOptions"
            style="width: 320px"
          />
          <span class="form-hint">选择 AI 模型的下载来源，ModelScope 适合国内网络环境</span>
        </div>
        <div v-if="settings.modelDownloadSource === 'HuggingFace'" class="form-row">
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
    <div class="section-card" :class="{ 'is-collapsed': collapsed.covers }" style="animation-delay: 0.15s">
      <div class="section-header collapsible" @click="collapsed.covers = !collapsed.covers">
        <h2 class="section-title">
          <span class="section-icon covers">
            <NIcon :size="16"><ImageOutlined /></NIcon>
          </span>
          游戏封面
        </h2>
        <NIcon :size="18" class="collapse-chevron" :class="{ expanded: !collapsed.covers }">
          <ExpandMoreOutlined />
        </NIcon>
      </div>
      <div class="section-body" :class="{ collapsed: collapsed.covers }">
        <div class="section-body-inner">
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
      </div>
    </div>

    <!-- Data Management -->
    <div class="section-card" :class="{ 'is-collapsed': collapsed.data }" style="animation-delay: 0.2s">
      <div class="section-header collapsible" @click="collapsed.data = !collapsed.data">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><StorageOutlined /></NIcon>
          </span>
          数据管理
        </h2>
        <NIcon :size="18" class="collapse-chevron" :class="{ expanded: !collapsed.data }">
          <ExpandMoreOutlined />
        </NIcon>
      </div>
      <div class="section-body" :class="{ collapsed: collapsed.data }">
        <div class="section-body-inner">
          <div class="settings-form">
            <div class="form-row">
              <label class="form-label">
                <NIcon :size="14" color="var(--text-3)"><FolderOpenOutlined /></NIcon>
                配置文件夹路径
              </label>
              <div class="data-path-row">
                <span class="data-path-text">{{ dataPath }}</span>
                <NButton size="small" @click="handleOpenDataFolder">打开文件夹</NButton>
              </div>
              <span class="form-hint">所有应用配置、游戏库、术语表等数据存储在此文件夹中</span>
            </div>
            <div class="data-actions">
              <NButton :loading="exportLoading" @click="handleExport">
                <template #icon><NIcon><FileDownloadOutlined /></NIcon></template>
                导出配置
              </NButton>
              <NButton :loading="importLoading" @click="handleImport">
                <template #icon><NIcon><FileUploadOutlined /></NIcon></template>
                导入配置
              </NButton>
            </div>
            <span class="form-hint">导出不包含 AI 模型文件、生成的字体、日志等大文件。导入会覆盖现有配置。</span>
          </div>
        </div>
      </div>
    </div>

    <!-- Data Reset -->
    <div class="section-card" style="animation-delay: 0.25s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon danger">
            <NIcon :size="16"><WarningAmberOutlined /></NIcon>
          </span>
          数据重置
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

    <!-- Update -->
    <div class="section-card" style="animation-delay: 0.3s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><SystemUpdateAltOutlined /></NIcon>
          </span>
          更新
        </h2>
        <div class="header-actions">
          <NButton
            size="small"
            :loading="updateStore.state === 'Checking'"
            :disabled="updateStore.isDownloading || updateStore.isApplying"
            @click="handleCheckUpdate"
          >
            检查更新
          </NButton>
        </div>
      </div>

      <!-- Version & Status -->
      <div class="update-version-block">
        <div class="update-version-info">
          <span class="update-version-number">v{{ shortVersion }}</span>
          <span v-if="buildNumber" class="update-build-number">Build {{ buildNumber }}</span>
        </div>
        <div v-if="updateStore.state === 'Checking'" class="update-status-badge checking">
          <NSpin :size="12" />
          <span>正在检查...</span>
        </div>
        <div v-else-if="updateStore.isUpdateAvailable" class="update-status-badge available">
          新版本可用
        </div>
        <div v-else-if="updateStore.isDownloading" class="update-status-badge checking">
          <NSpin :size="12" />
          <span>正在下载...</span>
        </div>
        <div v-else-if="updateStore.isReady" class="update-status-badge ready">
          更新已就绪
        </div>
        <div v-else-if="updateStore.isApplying" class="update-status-badge checking">
          <NSpin :size="12" />
          <span>正在更新...</span>
        </div>
        <div v-else-if="updateStore.hasError" class="update-status-badge error-badge">
          更新失败
        </div>
        <div v-else-if="hasChecked" class="update-status-badge latest">
          &#10003; 已是最新
        </div>
      </div>

      <!-- Update Available Details -->
      <template v-if="updateStore.isUpdateAvailable && updateStore.availableInfo">
        <div class="update-details">
          <div class="update-details-header">
            <div class="update-new-version-row">
              <span class="update-new-version">v{{ updateStore.availableInfo.version }}</span>
              <span class="update-size-badge">
                <NIcon :size="13"><CloudDownloadOutlined /></NIcon>
                {{ updateStore.formatBytes(updateStore.availableInfo.downloadSize) }}
              </span>
            </div>
          </div>

          <div v-if="changelogEntries.length" class="update-changelog">
            <div class="changelog-entries">
              <div v-for="(entry, i) in changelogEntries" :key="i" class="changelog-entry">
                <span v-if="entry.type" :class="['changelog-type-badge', `type-${entry.type}`]">
                  {{ typeLabels[entry.type] || entry.type }}
                </span>
                <span class="changelog-text">{{ entry.text }}</span>
                <code v-if="entry.hash" class="changelog-hash">{{ entry.hash.slice(0, 7) }}</code>
              </div>
            </div>
          </div>
          <div v-else-if="changelogHtml" class="update-changelog">
            <div class="changelog-content" v-html="changelogHtml"></div>
          </div>

          <div v-if="updateStore.availableInfo.changedPackages?.length" class="update-packages">
            <div class="packages-label">需要下载</div>
            <div class="packages-tags">
              <NTag
                v-for="pkg in updateStore.availableInfo.changedPackages"
                :key="pkg"
                size="small"
                round
                :bordered="false"
              >
                {{ pkg }}
              </NTag>
            </div>
          </div>

          <div class="update-actions">
            <NButton type="primary" @click="updateStore.downloadUpdate()">
              <template #icon><NIcon><CloudDownloadOutlined /></NIcon></template>
              开始更新
            </NButton>
            <NButton quaternary @click="updateStore.dismissUpdate()">暂不更新</NButton>
          </div>
        </div>
      </template>

      <!-- Download Progress -->
      <template v-if="updateStore.isDownloading">
        <div class="update-progress">
          <div class="progress-top">
            <div class="progress-header">
              正在下载
              <span v-if="updateStore.currentPackage" class="progress-package">{{ updateStore.currentPackage }}</span>
            </div>
            <div class="progress-bytes">
              {{ updateStore.formatBytes(updateStore.downloadedBytes) }} / {{ updateStore.formatBytes(updateStore.totalBytes) }}
            </div>
          </div>
          <NProgress
            type="line"
            :percentage="Math.round(updateStore.progress)"
            :show-indicator="false"
            :height="6"
            :border-radius="3"
          />
          <div class="progress-actions">
            <NButton size="small" quaternary @click="updateStore.cancelDownload()">取消下载</NButton>
          </div>
        </div>
      </template>

      <!-- Ready to Apply -->
      <template v-if="updateStore.isReady">
        <div class="update-ready">
          <div class="ready-info">
            <span class="ready-icon">&#10003;</span>
            <span>更新已下载完成，重启应用以完成更新</span>
          </div>
          <NButton type="primary" size="small" @click="updateStore.applyUpdate()">立即重启</NButton>
        </div>
      </template>

      <!-- Applying -->
      <template v-if="updateStore.isApplying">
        <div class="update-applying">
          <NSpin :size="14" />
          <span>{{ updateStore.message || '正在应用更新，应用即将重启...' }}</span>
        </div>
      </template>

      <!-- Error -->
      <template v-if="updateStore.hasError">
        <div class="update-error">
          <span class="error-text">{{ updateStore.error || '更新过程中出现错误' }}</span>
          <NButton size="small" @click="handleCheckUpdate">重试</NButton>
        </div>
      </template>

      <!-- Prerelease Toggle -->
      <div class="update-divider"></div>
      <div class="setting-row">
        <div class="setting-info">
          <span class="setting-label">接收预发布更新</span>
          <span class="setting-description">接收尚未正式发布的测试版本，可能包含新功能但稳定性较低</span>
        </div>
        <NSwitch v-model:value="settings.receivePreReleaseUpdates" />
      </div>
    </div>

    <!-- About (full width, non-collapsible) -->
    <div class="section-card" style="animation-delay: 0.35s">
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
                href="https://github.com/HanFengRuYue/UnityLocalizationToolkit-WebUI"
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

.about-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
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

.zoom-slider-row {
  display: flex;
  align-items: center;
  gap: 12px;
}

.zoom-slider-row :deep(.n-slider) {
  flex: 1;
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
  appearance: none;
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

.custom-swatch:not(.active) {
  background: conic-gradient(
    #f43f5e, #f97316, #f59e0b, #10b981, #06b6d4, #3b82f6, #8b5cf6, #f43f5e
  ) border-box;
}

/* ===== Data Management ===== */
.data-path-row {
  display: flex;
  align-items: center;
  gap: 10px;
}

.data-path-text {
  flex: 1;
  font-size: 13px;
  color: var(--text-2);
  padding: 6px 10px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  word-break: break-all;
  user-select: all;
}

.data-actions {
  display: flex;
  gap: 10px;
}

/* ===== Data Reset ===== */
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

/* ===== Update Section ===== */
.update-version-block {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
}

.update-version-info {
  display: flex;
  align-items: baseline;
  gap: 10px;
}

.update-version-number {
  font-size: 26px;
  font-weight: 700;
  color: var(--text-1);
  letter-spacing: -0.5px;
  line-height: 1;
}

.update-build-number {
  font-size: 11px;
  color: var(--text-3);
  font-family: var(--font-mono);
}

.update-status-badge {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 4px 12px;
  border-radius: 16px;
  font-size: 12px;
  font-weight: 500;
  white-space: nowrap;
}

.update-status-badge.checking {
  background: color-mix(in srgb, var(--accent) 12%, transparent);
  color: var(--accent);
}

.update-status-badge.available {
  background: color-mix(in srgb, var(--accent) 15%, transparent);
  color: var(--accent);
  font-weight: 600;
}

.update-status-badge.ready {
  background: color-mix(in srgb, var(--accent) 15%, transparent);
  color: var(--accent);
}

.update-status-badge.error-badge {
  background: color-mix(in srgb, var(--danger) 12%, transparent);
  color: var(--danger);
}

.update-status-badge.latest {
  background: color-mix(in srgb, var(--success) 12%, transparent);
  color: var(--success);
}

.update-divider {
  height: 1px;
  background: var(--border);
  margin: 16px 0;
}

/* Update Available Details Card */
.update-details {
  margin-top: 16px;
  padding: 20px;
  border-radius: var(--radius-md);
  background: color-mix(in srgb, var(--accent) 4%, var(--bg-subtle));
  border: 1px solid color-mix(in srgb, var(--accent) 15%, transparent);
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.update-details-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.update-new-version-row {
  display: flex;
  align-items: center;
  gap: 12px;
  width: 100%;
  justify-content: space-between;
}

.update-new-version {
  font-size: 18px;
  font-weight: 700;
  color: var(--accent);
  letter-spacing: -0.3px;
}

.update-size-badge {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  padding: 3px 10px;
  border-radius: 12px;
  font-size: 12px;
  font-weight: 500;
  color: var(--text-2);
  background: var(--bg-muted);
}

/* Changelog */
.update-changelog {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.changelog-content {
  font-size: 13px;
  :deep(ul) { margin: 0; padding-left: 20px; }
  :deep(li) { margin: 2px 0; }
  :deep(code) { font-size: 12px; padding: 1px 4px; border-radius: 3px; background: var(--bg-muted); }
  :deep(p) { margin: 4px 0; }
}

.changelog-entries {
  display: flex;
  flex-direction: column;
  gap: 0;
}

.changelog-entry {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 7px 0;
  border-bottom: 1px solid color-mix(in srgb, var(--border) 60%, transparent);
}

.changelog-entry:last-child {
  border-bottom: none;
  padding-bottom: 0;
}

.changelog-entry:first-child {
  padding-top: 0;
}

.changelog-type-badge {
  display: inline-flex;
  align-items: center;
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 11px;
  font-weight: 600;
  white-space: nowrap;
  flex-shrink: 0;
  background: color-mix(in srgb, var(--accent) 12%, transparent);
  color: var(--accent);
}

.changelog-type-badge.type-fix {
  background: color-mix(in srgb, var(--danger) 12%, transparent);
  color: var(--danger);
}

.changelog-type-badge.type-docs {
  background: color-mix(in srgb, var(--text-3) 15%, transparent);
  color: var(--text-2);
}

.changelog-type-badge.type-perf {
  background: color-mix(in srgb, var(--success) 12%, transparent);
  color: var(--success);
}

.changelog-type-badge.type-refactor {
  background: color-mix(in srgb, #a78bfa 12%, transparent);
  color: #a78bfa;
}

.changelog-type-badge.type-ci,
.changelog-type-badge.type-chore,
.changelog-type-badge.type-style,
.changelog-type-badge.type-test {
  background: color-mix(in srgb, var(--text-3) 12%, transparent);
  color: var(--text-3);
}

.changelog-text {
  font-size: 13px;
  color: var(--text-1);
  flex: 1;
  min-width: 0;
}

.changelog-hash {
  font-family: var(--font-mono);
  font-size: 11px;
  color: var(--text-3);
  background: var(--bg-muted);
  padding: 2px 6px;
  border-radius: 4px;
  flex-shrink: 0;
}

/* Packages */
.update-packages {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.packages-label {
  font-size: 11px;
  color: var(--text-3);
  font-weight: 500;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.packages-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.update-actions {
  display: flex;
  gap: 8px;
  padding-top: 4px;
}

/* Download Progress */
.update-progress {
  margin-top: 16px;
  padding: 16px;
  border-radius: var(--radius-md);
  background: color-mix(in srgb, var(--accent) 4%, var(--bg-subtle));
  border: 1px solid color-mix(in srgb, var(--accent) 12%, transparent);
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.progress-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.progress-header {
  font-size: 13px;
  font-weight: 500;
  color: var(--text-1);
}

.progress-package {
  color: var(--accent);
  font-weight: 600;
}

.progress-bytes {
  font-size: 12px;
  font-family: var(--font-mono);
  color: var(--text-3);
}

.progress-actions {
  display: flex;
  justify-content: flex-end;
}

/* Applying */
.update-applying {
  margin-top: 16px;
  padding: 14px 16px;
  border-radius: var(--radius-md);
  background: color-mix(in srgb, var(--accent) 5%, var(--bg-subtle));
  border: 1px solid color-mix(in srgb, var(--accent) 15%, transparent);
  display: flex;
  align-items: center;
  gap: 10px;
  font-size: 13px;
  color: var(--accent);
}

/* Ready & Error */
.update-ready {
  margin-top: 16px;
  padding: 14px 16px;
  border-radius: var(--radius-md);
  background: color-mix(in srgb, var(--success) 5%, var(--bg-subtle));
  border: 1px solid color-mix(in srgb, var(--success) 15%, transparent);
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  font-size: 13px;
}

.ready-info {
  display: flex;
  align-items: center;
  gap: 8px;
}

.ready-icon {
  color: var(--success);
  font-weight: 700;
  font-size: 14px;
}

.update-error {
  margin-top: 16px;
  padding: 14px 16px;
  border-radius: var(--radius-md);
  background: color-mix(in srgb, var(--danger) 5%, var(--bg-subtle));
  border: 1px solid color-mix(in srgb, var(--danger) 15%, transparent);
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  font-size: 13px;
}

.error-text {
  color: var(--danger);
}

.setting-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
.setting-info { flex: 1; }
.setting-label { font-size: 14px; font-weight: 500; display: block; }
.setting-description { font-size: 12px; color: var(--text-3); display: block; margin-top: 2px; }

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
  .about-grid {
    grid-template-columns: repeat(2, 1fr);
    gap: 10px;
  }

  .data-actions {
    flex-direction: column;
  }
  .data-actions :deep(.n-button) {
    width: 100%;
  }
  .setting-row {
    flex-direction: column;
    align-items: flex-start;
    gap: 8px;
  }
}

@media (max-width: 480px) {
  .about-grid {
    grid-template-columns: 1fr;
  }

  .danger-action {
    flex-direction: column;
    align-items: stretch;
    gap: 12px;
  }

  .data-path-row {
    flex-direction: column;
    align-items: stretch;
  }
  .update-ready, .update-error {
    flex-direction: column;
    align-items: stretch;
    text-align: center;
  }
}
</style>
