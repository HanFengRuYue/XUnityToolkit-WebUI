<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import {
  NButton,
  NInput,
  NPopconfirm,
  NSelect,
  NIcon,
  useMessage,
} from 'naive-ui'
import {
  StorageOutlined,
  CloudDownloadOutlined,
  InfoOutlined,
  SpeedOutlined,
  PaletteOutlined,
  TuneOutlined,
  CodeOutlined,
  DeleteOutlined,
  PersonOutlined,
  OpenInNewOutlined,
} from '@vicons/material'
import { LogoGithub } from '@vicons/ionicons5'
import { cacheApi, settingsApi } from '@/api/games'
import type { AppSettings } from '@/api/types'
import { useThemeStore } from '@/stores/theme'
import type { ThemeMode } from '@/stores/theme'

const message = useMessage()
const themeStore = useThemeStore()

// Cache
const cacheFileCount = ref(0)
const cacheSize = ref('')
const cacheLoading = ref(false)

function formatBytes(bytes: number): string {
  if (bytes >= 1_048_576) return `${(bytes / 1_048_576).toFixed(1)} MB`
  if (bytes >= 1024) return `${(bytes / 1024).toFixed(0)} KB`
  return `${bytes} B`
}

async function loadCacheInfo() {
  try {
    const info = await cacheApi.getInfo()
    cacheFileCount.value = info.fileCount
    cacheSize.value = info.totalBytes > 0 ? formatBytes(info.totalBytes) : '0 B'
  } catch {
    cacheSize.value = '加载失败'
  }
}

async function handleClearCache() {
  cacheLoading.value = true
  try {
    const result = await cacheApi.clear()
    message.success(`已清除 ${result.fileCount} 个文件（${formatBytes(result.totalBytes)}）`)
    cacheFileCount.value = 0
    cacheSize.value = '0 B'
  } catch {
    message.error('清除缓存失败')
  } finally {
    cacheLoading.value = false
  }
}

// Settings
const settings = ref<AppSettings>({
  mirrorUrl: 'https://ghfast.top/',
  theme: themeStore.mode,
})
const settingsLoading = ref(false)

const themeOptions = [
  { label: '深色主题', value: 'dark' },
  { label: '浅色主题', value: 'light' },
]

// Sync theme changes immediately when user selects
watch(() => settings.value.theme, (newTheme) => {
  themeStore.setTheme(newTheme as ThemeMode)
})

async function loadSettings() {
  try {
    const loaded = await settingsApi.get()
    settings.value = loaded
    // Sync loaded theme with theme store
    themeStore.setTheme(loaded.theme as ThemeMode)
  } catch {
    // Use defaults
  }
}

async function handleSaveSettings() {
  settingsLoading.value = true
  try {
    await settingsApi.save(settings.value)
    message.success('设置已保存')
  } catch {
    message.error('保存设置失败')
  } finally {
    settingsLoading.value = false
  }
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

onMounted(() => {
  loadCacheInfo()
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

    <!-- Top row: Cache + Download Settings side by side -->
    <div class="settings-grid">
      <!-- Cache Management -->
      <div class="section-card" style="animation-delay: 0.05s">
        <div class="section-header">
          <h2 class="section-title">
            <span class="section-icon storage">
              <NIcon :size="16"><StorageOutlined /></NIcon>
            </span>
            下载缓存
          </h2>
        </div>
        <div class="info-grid">
          <div class="info-card">
            <div class="info-card-icon file-count">
              <NIcon :size="18"><DeleteOutlined /></NIcon>
            </div>
            <div class="info-card-content">
              <span class="info-label">缓存文件</span>
              <span class="info-value">{{ cacheFileCount }} 个文件</span>
            </div>
          </div>
          <div class="info-card">
            <div class="info-card-icon size">
              <NIcon :size="18"><StorageOutlined /></NIcon>
            </div>
            <div class="info-card-content">
              <span class="info-label">占用空间</span>
              <span class="info-value mono">{{ cacheSize }}</span>
            </div>
          </div>
        </div>
        <div class="section-footer">
          <span class="footer-hint">清除已缓存的 BepInEx 和 XUnity 安装包</span>
          <NPopconfirm
            @positive-click="handleClearCache"
            negative-text="取消"
            positive-text="确认清除"
          >
            <template #trigger>
              <NButton size="small" :loading="cacheLoading" ghost type="warning">
                清除缓存
              </NButton>
            </template>
            确定要清除所有下载缓存？
          </NPopconfirm>
        </div>
      </div>

      <!-- Mirror & Theme Settings -->
      <div class="section-card" style="animation-delay: 0.1s">
        <div class="section-header">
          <h2 class="section-title">
            <span class="section-icon download">
              <NIcon :size="16"><CloudDownloadOutlined /></NIcon>
            </span>
            下载设置
          </h2>
        </div>
        <div class="settings-form">
          <div class="form-row">
            <label class="form-label">
              <NIcon :size="14" color="var(--text-3)"><SpeedOutlined /></NIcon>
              GitHub 镜像地址
            </label>
            <NInput
              v-model:value="settings.mirrorUrl"
              placeholder="https://ghfast.top/"
              clearable
            />
            <span class="form-hint">加速 GitHub 资源下载，留空使用直连</span>
          </div>
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
        </div>
        <div class="section-footer">
          <span></span>
          <NButton type="primary" :loading="settingsLoading" @click="handleSaveSettings">
            保存设置
          </NButton>
        </div>
      </div>
    </div>

    <!-- About (full width) -->
    <div class="section-card" style="animation-delay: 0.15s">
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
            <NIcon :size="18"><CodeOutlined /></NIcon>
          </div>
          <div class="info-card-content">
            <span class="info-label">版本</span>
            <span class="info-value mono">v{{ shortVersion }}</span>
          </div>
        </div>
        <div class="info-card">
          <div class="info-card-icon tech">
            <NIcon :size="18"><InfoOutlined /></NIcon>
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
            <span class="info-value">寒枫如玥</span>
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
                href="https://github.com/XUnityToolkit/XUnityToolkit-WebUI"
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
  animation: fadeIn 0.3s ease;
}

.page-title {
  font-family: var(--font-display);
  font-size: 30px;
  font-weight: 600;
  color: var(--text-1);
  margin-bottom: 28px;
  letter-spacing: -0.03em;
  animation: slideUp 0.4s var(--ease-out) backwards;
  display: flex;
  align-items: center;
  gap: 14px;
}

.page-title-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 42px;
  height: 42px;
  border-radius: 12px;
  background: var(--accent-soft);
  color: var(--accent);
  flex-shrink: 0;
}

/* ===== Settings Grid (two-column, equal height) ===== */
.settings-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
  align-items: stretch;
  margin-bottom: 16px;
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
  margin-bottom: 20px;
}

.section-title {
  font-family: var(--font-display);
  font-size: 17px;
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

.section-icon.storage {
  background: rgba(251, 191, 36, 0.10);
  color: #fbbf24;
}

.section-icon.download {
  background: rgba(96, 165, 250, 0.10);
  color: #60a5fa;
}

.section-icon.about {
  background: rgba(167, 139, 250, 0.10);
  color: #a78bfa;
}

/* ===== Info Grid & Cards ===== */
.info-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
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

.info-card-icon.file-count {
  background: rgba(251, 146, 60, 0.10);
  color: #fb923c;
}

.info-card-icon.size {
  background: rgba(251, 191, 36, 0.10);
  color: #fbbf24;
}

.info-card-icon.version {
  background: rgba(59, 130, 246, 0.10);
  color: #3b82f6;
}

.info-card-icon.tech {
  background: rgba(96, 165, 250, 0.10);
  color: #60a5fa;
}

.info-card-icon.author {
  background: rgba(251, 146, 60, 0.10);
  color: #fb923c;
}

.info-card-icon.github {
  background: rgba(167, 139, 250, 0.10);
  color: #a78bfa;
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

/* ===== Section Footer ===== */
.section-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding-top: 16px;
  margin-top: auto;
  border-top: 1px solid var(--border);
  gap: 12px;
}


.footer-hint {
  font-size: 12px;
  color: var(--text-3);
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
  .settings-grid {
    grid-template-columns: 1fr;
  }

  .section-card {
    padding: 16px;
  }

  .info-grid {
    grid-template-columns: 1fr 1fr;
    gap: 10px;
  }

  .about-grid {
    grid-template-columns: repeat(2, 1fr);
    gap: 10px;
  }
}

@media (max-width: 480px) {
  .page-title {
    font-size: 22px;
    margin-bottom: 20px;
    gap: 10px;
  }

  .page-title-icon {
    width: 36px;
    height: 36px;
    border-radius: 10px;
  }

  .section-card {
    padding: 14px;
    border-radius: var(--radius-md);
  }

  .info-grid {
    grid-template-columns: 1fr;
  }

  .about-grid {
    grid-template-columns: 1fr;
  }

  .section-footer {
    flex-direction: column;
    align-items: stretch;
    gap: 10px;
  }

  .footer-hint {
    text-align: center;
  }
}
</style>
