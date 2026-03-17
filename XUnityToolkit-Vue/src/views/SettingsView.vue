<script setup lang="ts">
import { ref, reactive, computed, onMounted, watch, nextTick } from 'vue'
import {
  NButton,
  NInput,
  NSelect,
  NIcon,
  NProgress,
  NSpin,
  NSwitch,
  NColorPicker,
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
} from '@vicons/material'
import { LogoGithub } from '@vicons/ionicons5'
import { settingsApi } from '@/api/games'
import type { AppSettings } from '@/api/types'
import { useThemeStore, accentPresets } from '@/stores/theme'
import type { ThemeMode } from '@/stores/theme'
import { useAutoSave } from '@/composables/useAutoSave'
import { marked } from 'marked'
import { useUpdateStore } from '@/stores/update'

defineOptions({ name: 'SettingsView' })

const collapsed = reactive({
  covers: true,
  data: true,
})

const message = useMessage()
const dialog = useDialog()
const themeStore = useThemeStore()
const updateStore = useUpdateStore()

const showColorPicker = ref(false)
const isCustomAccent = computed(() =>
  !accentPresets.some(p => p.hex === settings.value.accentColor)
)

// Settings
const modelDownloadSourceOptions = [
  { label: 'HuggingFace（默认）', value: 'HuggingFace' },
  { label: 'ModelScope（魔搭社区，国内加速）', value: 'ModelScope' },
]

const settings = ref<AppSettings>({
  hfMirrorUrl: 'https://hf-mirror.com',
  modelDownloadSource: 'HuggingFace',
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
    enablePreTranslationCache: false,
    termAuditEnabled: true,
    naturalTranslationMode: true,
  },
  steamGridDbApiKey: undefined,
  libraryViewMode: 'grid',
  librarySortBy: 'name',
  accentColor: themeStore.accentColor,
  libraryCardSize: 'medium',
  libraryGap: 'normal',
  libraryShowLabels: true,
  receivePreReleaseUpdates: false,
})

const themeOptions = [
  { label: '跟随系统', value: 'system' },
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
  return marked.parse(md, { async: false }) as string
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

function handleImport() {
  const input = document.createElement('input')
  input.type = 'file'
  input.accept = '.zip'
  input.onchange = async () => {
    const file = input.files?.[0]
    if (!file) return
    dialog.warning({
      title: '导入配置',
      content: '导入将覆盖当前所有配置数据，导入完成后需要重启程序。确定要继续吗？',
      positiveText: '确认导入',
      negativeText: '取消',
      onPositiveClick: async () => {
        importLoading.value = true
        try {
          await settingsApi.importData(file)
          message.success('导入成功，请重启程序以使配置生效')
        } catch (e) {
          message.error(e instanceof Error ? e.message : '导入失败')
        } finally {
          importLoading.value = false
        }
      },
    })
  }
  input.click()
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
          <div class="update-header">
            <span class="update-title">v{{ updateStore.availableInfo.version }} 可用</span>
          </div>

          <div v-if="changelogHtml" class="update-changelog">
            <div class="changelog-label">更新内容</div>
            <div class="changelog-content" v-html="changelogHtml"></div>
          </div>

          <div class="update-packages">
            <div class="packages-label">需要下载</div>
            <div v-for="pkg in updateStore.availableInfo.changedPackages" :key="pkg" class="package-item">
              {{ pkg }}.zip
            </div>
            <div class="packages-total">
              总计 {{ updateStore.formatBytes(updateStore.availableInfo.downloadSize) }}
            </div>
          </div>

          <div class="update-actions">
            <NButton type="primary" @click="updateStore.downloadUpdate()">开始更新</NButton>
            <NButton @click="updateStore.dismissUpdate()">暂不更新</NButton>
          </div>
        </div>
      </template>

      <!-- Download Progress -->
      <template v-if="updateStore.isDownloading">
        <div class="update-progress">
          <div class="progress-header">
            正在下载更新
            <span v-if="updateStore.currentPackage">: {{ updateStore.currentPackage }}</span>
          </div>
          <NProgress
            type="line"
            :percentage="Math.round(updateStore.progress)"
            :show-indicator="true"
          />
          <div class="progress-detail">
            {{ updateStore.formatBytes(updateStore.downloadedBytes) }} / {{ updateStore.formatBytes(updateStore.totalBytes) }}
          </div>
          <NButton size="small" @click="updateStore.cancelDownload()">取消</NButton>
        </div>
      </template>

      <!-- Ready to Apply -->
      <template v-if="updateStore.isReady">
        <div class="update-ready">
          <span>更新已下载完成，需要重启应用以完成更新</span>
          <NButton type="primary" @click="updateStore.applyUpdate()">立即重启更新</NButton>
        </div>
      </template>

      <!-- Applying -->
      <template v-if="updateStore.isApplying">
        <div class="update-applying">
          <NSpin :size="16" />
          <span>{{ updateStore.message || '正在应用更新，应用即将重启...' }}</span>
        </div>
      </template>

      <!-- Error -->
      <template v-if="updateStore.hasError">
        <div class="update-error">
          <span>{{ updateStore.error || '更新过程中出现错误' }}</span>
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

.custom-swatch:not(.active) {
  background: conic-gradient(
    #f43f5e, #f97316, #f59e0b, #10b981, #06b6d4, #3b82f6, #8b5cf6, #f43f5e
  );
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
  flex-direction: column;
  gap: 2px;
}

.update-version-number {
  font-size: 22px;
  font-weight: 600;
  color: var(--text-1);
  letter-spacing: -0.5px;
  line-height: 1.2;
}

.update-build-number {
  font-size: 12px;
  color: var(--text-3);
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

.update-details {
  margin-top: 16px;
  padding: 16px;
  border-radius: var(--radius-md);
  background: color-mix(in srgb, var(--accent) 5%, var(--bg-subtle));
  border: 1px solid color-mix(in srgb, var(--accent) 20%, transparent);
}
.update-header { margin-bottom: 12px; }
.update-title { font-weight: 600; font-size: 14px; color: var(--accent); }
.update-changelog { margin-bottom: 12px; }
.changelog-label {
  font-size: 12px;
  color: var(--text-3);
  margin-bottom: 6px;
  font-weight: 500;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}
.changelog-content {
  font-size: 13px;
  :deep(ul) { margin: 0; padding-left: 20px; }
  :deep(li) { margin: 2px 0; }
  :deep(code) { font-size: 12px; padding: 1px 4px; border-radius: 3px; background: var(--bg-muted); }
  :deep(p) { margin: 4px 0; }
}
.packages-label {
  font-size: 12px;
  color: var(--text-3);
  margin-bottom: 6px;
  font-weight: 500;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}
.package-item { font-size: 13px; padding: 2px 0; }
.packages-total { font-size: 13px; font-weight: 500; margin-top: 6px; color: var(--text-2); }
.update-actions { display: flex; gap: 8px; margin-top: 16px; }

.update-progress {
  margin-top: 16px;
  padding: 16px;
  border-radius: var(--radius-md);
  background: color-mix(in srgb, var(--accent) 5%, var(--bg-subtle));
  border: 1px solid var(--border);
}
.progress-header { font-size: 14px; margin-bottom: 8px; font-weight: 500; }
.progress-detail {
  font-size: 12px;
  color: var(--text-3);
  margin-top: 4px;
  margin-bottom: 8px;
}

.update-applying {
  margin-top: 16px;
  padding: 16px;
  border-radius: var(--radius-md);
  background: color-mix(in srgb, var(--accent) 5%, var(--bg-subtle));
  border: 1px solid color-mix(in srgb, var(--accent) 20%, transparent);
  display: flex;
  align-items: center;
  gap: 12px;
  font-size: 13px;
  color: var(--accent);
}

.update-ready, .update-error {
  margin-top: 16px;
  padding: 16px;
  border-radius: var(--radius-md);
  background: color-mix(in srgb, var(--accent) 5%, var(--bg-subtle));
  border: 1px solid var(--border);
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  font-size: 13px;
}
.update-error {
  background: color-mix(in srgb, var(--danger) 5%, var(--bg-subtle));
  border-color: color-mix(in srgb, var(--danger) 20%, transparent);
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
