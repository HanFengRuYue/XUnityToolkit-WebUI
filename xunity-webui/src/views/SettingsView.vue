<script setup lang="ts">
import { ref, onMounted } from 'vue'
import {
  NButton,
  NInput,
  NPopconfirm,
  NSelect,
  useMessage,
} from 'naive-ui'
import { cacheApi, settingsApi } from '@/api/games'
import type { AppSettings } from '@/api/types'

const message = useMessage()

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
  theme: 'dark',
})
const settingsLoading = ref(false)

const themeOptions = [
  { label: '深色主题', value: 'dark' },
  { label: '浅色主题（即将推出）', value: 'light', disabled: true },
]

async function loadSettings() {
  try {
    settings.value = await settingsApi.get()
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
    <h1 class="page-title" style="animation-delay: 0s">设置</h1>

    <!-- Cache Management -->
    <div class="section-card" style="animation-delay: 0.05s">
      <div class="section-header">
        <h2 class="section-title">下载缓存</h2>
      </div>
      <div class="info-grid">
        <div class="info-item">
          <span class="info-label">缓存文件</span>
          <span class="info-value">{{ cacheFileCount }} 个文件</span>
        </div>
        <div class="info-item">
          <span class="info-label">占用空间</span>
          <span class="info-value mono">{{ cacheSize }}</span>
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
        <h2 class="section-title">下载设置</h2>
      </div>
      <div class="settings-form">
        <div class="form-row">
          <label class="form-label">GitHub 镜像地址</label>
          <NInput
            v-model:value="settings.mirrorUrl"
            placeholder="https://ghfast.top/"
            clearable
          />
          <span class="form-hint">加速 GitHub 资源下载，留空使用直连</span>
        </div>
        <div class="form-row">
          <label class="form-label">界面主题</label>
          <NSelect
            v-model:value="settings.theme"
            :options="themeOptions"
            style="max-width: 240px"
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

    <!-- About -->
    <div class="section-card" style="animation-delay: 0.15s">
      <div class="section-header">
        <h2 class="section-title">关于</h2>
      </div>
      <div class="info-grid">
        <div class="info-item">
          <span class="info-label">版本</span>
          <span class="info-value mono">{{ version }}</span>
        </div>
        <div class="info-item">
          <span class="info-label">技术栈</span>
          <span class="info-value">.NET 10 + Vue 3</span>
        </div>
        <div class="info-item">
          <span class="info-label">项目</span>
          <span class="info-value">
            <a
              href="https://github.com"
              target="_blank"
              rel="noopener noreferrer"
              class="about-link"
            >
              XUnityToolkit-WebUI
            </a>
          </span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.settings-page {
  max-width: 800px;
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
}

/* ===== Section Card ===== */
.section-card {
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  padding: 24px;
  margin-bottom: 16px;
  animation: slideUp 0.5s var(--ease-out) backwards;
  transition: border-color 0.3s ease;
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
}

/* ===== Info Grid ===== */
.info-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
  gap: 16px;
}

.info-item {
  display: flex;
  flex-direction: column;
  gap: 5px;
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
}

/* ===== Section Footer ===== */
.section-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding-top: 16px;
  margin-top: 16px;
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
  .section-card {
    padding: 16px;
  }

  .info-grid {
    grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
    gap: 12px;
  }
}

@media (max-width: 480px) {
  .page-title {
    font-size: 22px;
    margin-bottom: 20px;
  }

  .section-card {
    padding: 14px;
    border-radius: var(--radius-md);
  }

  .info-grid {
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
