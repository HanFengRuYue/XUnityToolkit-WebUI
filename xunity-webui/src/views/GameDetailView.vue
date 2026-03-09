<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NButton,
  NAlert,
  NIcon,
  useMessage,
  useDialog,
} from 'naive-ui'
import {
  GamepadFilled,
  FolderOpenOutlined,
  PlayArrowFilled,
  InfoOutlined,
  FolderOutlined,
  InsertDriveFileOutlined,
  CodeOutlined,
  MemoryOutlined,
  CloudDownloadOutlined,
  TranslateOutlined,
  WarningAmberOutlined,
  ExtensionOutlined,
  ViewInArOutlined,
} from '@vicons/material'
import { useGamesStore } from '@/stores/games'
import { useInstallStore } from '@/stores/install'
import type { Game, XUnityConfig, ModFrameworkType } from '@/api/types'
import { gamesApi } from '@/api/games'
import ConfigPanel from '@/components/config/ConfigPanel.vue'

const route = useRoute()
const router = useRouter()
const gamesStore = useGamesStore()
const installStore = useInstallStore()
const message = useMessage()
const dialog = useDialog()

const gameId = route.params['id'] as string
const game = ref<Game | null>(null)
const config = ref<XUnityConfig | null>(null)
const loading = ref(true)

const isInstalled = computed(
  () => game.value?.installState === 'FullyInstalled',
)
const hasBepInEx = computed(
  () =>
    game.value?.installState === 'BepInExOnly' ||
    game.value?.installState === 'FullyInstalled',
)

const otherFrameworks = computed(() =>
  game.value?.detectedFrameworks?.filter((f) => f.framework !== 'BepInEx') ?? [],
)

const frameworkDisplayNames: Record<ModFrameworkType, string> = {
  BepInEx: 'BepInEx',
  MelonLoader: 'MelonLoader',
  IPA: 'IPA / BSIPA',
  ReiPatcher: 'ReiPatcher',
  Sybaris: 'Sybaris',
  UnityInjector: 'UnityInjector',
  Standalone: 'Standalone',
}

onMounted(async () => {
  await loadGame()
})

async function loadGame() {
  loading.value = true
  try {
    game.value = await gamesApi.get(gameId)
    if (isInstalled.value) {
      config.value = await gamesApi.getConfig(gameId)
    }
  } catch {
    message.error('加载游戏信息失败')
  } finally {
    loading.value = false
  }
}

async function handleInstall() {
  try {
    await installStore.startInstall(gameId, config.value ?? undefined)
  } catch (e) {
    message.error(e instanceof Error ? e.message : '安装失败')
  }
}

function handleUninstall() {
  dialog.warning({
    title: '确认卸载',
    content: '将移除 BepInEx 和 XUnity.AutoTranslator 的所有文件。确定要继续吗？',
    positiveText: '确认卸载',
    negativeText: '取消',
    onPositiveClick: async () => {
      try {
        await installStore.startUninstall(gameId)
      } catch (e) {
        message.error(e instanceof Error ? e.message : '卸载失败')
      }
    },
  })
}

async function handleSaveConfig(cfg: XUnityConfig) {
  try {
    await gamesApi.saveConfig(gameId, cfg)
    config.value = cfg
    message.success('配置已保存')
  } catch {
    message.error('保存配置失败')
  }
}

async function handleOpenFolder() {
  try {
    await gamesApi.openFolder(gameId)
  } catch {
    message.error('打开目录失败')
  }
}

async function handleLaunch() {
  try {
    await gamesApi.launch(gameId)
    message.success('游戏已启动')
  } catch (e) {
    message.error(e instanceof Error ? e.message : '启动游戏失败')
  }
}

function handleRemoveGame() {
  dialog.error({
    title: '移除游戏',
    content: '将从游戏库中移除此游戏（不会删除游戏文件）。确定吗？',
    positiveText: '确认移除',
    negativeText: '取消',
    onPositiveClick: async () => {
      const ok = await gamesStore.removeGame(gameId)
      if (ok) {
        message.success('已移除')
        router.push('/')
      }
    },
  })
}

function handleUninstallFramework(framework: ModFrameworkType) {
  dialog.warning({
    title: `卸载 ${frameworkDisplayNames[framework]}`,
    content: `将移除 ${frameworkDisplayNames[framework]} 框架及其关联的 XUnity 插件文件。确定要继续吗？`,
    positiveText: '确认卸载',
    negativeText: '取消',
    onPositiveClick: async () => {
      try {
        const updated = await gamesApi.uninstallFramework(gameId, framework)
        game.value = updated
        await gamesStore.refreshGame(gameId)
        message.success(`${frameworkDisplayNames[framework]} 已卸载`)
      } catch (e) {
        message.error(e instanceof Error ? e.message : '卸载失败')
      }
    },
  })
}

const stopWatch = watch(
  () => installStore.status?.step,
  async (step) => {
    if (step === 'Complete') {
      await loadGame()
      await gamesStore.refreshGame(gameId)
    }
  },
)
onUnmounted(() => stopWatch())
</script>

<template>
  <!-- Loading -->
  <div v-if="loading" class="detail-loading">
    <div class="loading-spinner"></div>
  </div>

  <div v-else-if="game" class="detail-page">
    <!-- Back Navigation & Title -->
    <div class="detail-header" style="animation-delay: 0s">
      <button class="back-button" @click="router.push('/')">
        <svg width="20" height="20" viewBox="0 0 20 20" fill="none">
          <path d="M12 5L7 10L12 15" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
        </svg>
        <span>返回</span>
      </button>
      <div class="header-actions">
        <NButton tertiary type="error" size="small" @click="handleRemoveGame">
          从库中移除
        </NButton>
      </div>
    </div>

    <!-- Game Title -->
    <div class="game-title-section" style="animation-delay: 0.05s">
      <div class="title-icon">
        <img
          :src="`/api/games/${gameId}/icon`"
          :alt="game.name"
          class="title-icon-img"
          @error="($event.target as HTMLImageElement).style.display = 'none'; ($event.target as HTMLImageElement).nextElementSibling?.classList.add('visible')"
        />
        <div class="title-icon-fallback">
          <NIcon :size="28" color="var(--text-3)">
            <GamepadFilled />
          </NIcon>
        </div>
      </div>
      <h1 class="game-title">{{ game.name }}</h1>
      <div class="title-status">
        <template v-if="game.isUnityGame">
          <span class="status-indicator" :class="{
            'status-installed': isInstalled,
            'status-partial': hasBepInEx && !isInstalled,
            'status-none': !hasBepInEx,
          }"></span>
          <span class="status-text">
            {{ isInstalled ? '已安装' : hasBepInEx ? '仅 BepInEx' : '未安装' }}
          </span>
        </template>
        <template v-else>
          <span class="status-text" style="color: var(--text-3)">非 Unity</span>
        </template>
      </div>
    </div>

    <!-- Game Info Card -->
    <div class="section-card" style="animation-delay: 0.1s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><InfoOutlined /></NIcon>
          </span>
          游戏信息
        </h2>
        <div class="header-btn-group">
          <NButton size="small" @click="handleOpenFolder">
            <template #icon><NIcon :size="16"><FolderOpenOutlined /></NIcon></template>
            打开目录
          </NButton>
          <NButton size="small" @click="handleLaunch">
            <template #icon><NIcon :size="16"><PlayArrowFilled /></NIcon></template>
            运行游戏
          </NButton>
        </div>
      </div>

      <div class="info-grid">
        <div class="info-card">
          <div class="info-card-icon folder">
            <NIcon :size="18"><FolderOutlined /></NIcon>
          </div>
          <div class="info-card-content">
            <span class="info-label">路径</span>
            <span class="info-value mono">{{ game.gamePath }}</span>
          </div>
        </div>
        <div class="info-card">
          <div class="info-card-icon file">
            <NIcon :size="18"><InsertDriveFileOutlined /></NIcon>
          </div>
          <div class="info-card-content">
            <span class="info-label">可执行文件</span>
            <span class="info-value" :class="{ muted: !game.executableName }">
              {{ game.executableName || '未知' }}
            </span>
          </div>
        </div>
        <template v-if="game.isUnityGame">
          <div class="info-card">
            <div class="info-card-icon unity">
              <NIcon :size="18"><ViewInArOutlined /></NIcon>
            </div>
            <div class="info-card-content">
              <span class="info-label">Unity 版本</span>
              <span class="info-value" :class="{ muted: !game.detectedInfo }">
                {{ game.detectedInfo?.unityVersion || '未知' }}
              </span>
            </div>
          </div>
          <div class="info-card">
            <div class="info-card-icon code">
              <NIcon :size="18"><CodeOutlined /></NIcon>
            </div>
            <div class="info-card-content">
              <span class="info-label">脚本后端</span>
              <span class="info-value" :class="{ muted: !game.detectedInfo }">
                {{ game.detectedInfo?.backend || '未知' }}
              </span>
            </div>
          </div>
          <div class="info-card">
            <div class="info-card-icon arch">
              <NIcon :size="18"><MemoryOutlined /></NIcon>
            </div>
            <div class="info-card-content">
              <span class="info-label">架构</span>
              <span class="info-value" :class="{ muted: !game.detectedInfo }">
                {{ game.detectedInfo?.architecture || '未知' }}
              </span>
            </div>
          </div>
        </template>
        <div v-if="!game.isUnityGame" class="info-card">
          <div class="info-card-icon">
            <NIcon :size="18"><InfoOutlined /></NIcon>
          </div>
          <div class="info-card-content">
            <span class="info-label">类型</span>
            <span class="info-value muted">非 Unity 游戏</span>
          </div>
        </div>
      </div>
    </div>

    <!-- Detected Frameworks Card (non-BepInEx) -->
    <div v-if="otherFrameworks.length > 0" class="section-card" style="animation-delay: 0.15s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon warning">
            <NIcon :size="16"><WarningAmberOutlined /></NIcon>
          </span>
          检测到的模组框架
        </h2>
      </div>
      <NAlert type="warning" style="margin-bottom: 16px">
        检测到其他模组框架，建议卸载后再安装 BepInEx 版本，避免插件冲突。
      </NAlert>
      <div class="framework-list">
        <div v-for="fw in otherFrameworks" :key="fw.framework" class="framework-item">
          <div class="framework-info">
            <NIcon :size="16" color="var(--warning)"><ExtensionOutlined /></NIcon>
            <span class="framework-name">{{ frameworkDisplayNames[fw.framework] }}</span>
            <span v-if="fw.version" class="framework-version">v{{ fw.version }}</span>
            <span v-if="fw.hasXUnityPlugin" class="framework-xunity">
              含 XUnity{{ fw.xUnityVersion ? ` v${fw.xUnityVersion}` : '' }}
            </span>
          </div>
          <NButton size="small" type="error" ghost @click="handleUninstallFramework(fw.framework)">
            卸载
          </NButton>
        </div>
      </div>
    </div>

    <!-- Install + Config two-column layout (Unity only) -->
    <div v-if="game.isUnityGame" class="detail-columns">
      <!-- Install Management Card -->
      <div class="section-card" :style="{ animationDelay: otherFrameworks.length > 0 ? '0.2s' : '0.15s' }">
        <div class="section-header">
          <h2 class="section-title">
            <span class="section-icon download">
              <NIcon :size="16"><CloudDownloadOutlined /></NIcon>
            </span>
            安装管理
          </h2>
        </div>

        <NAlert
          v-if="game.detectedInfo?.backend === 'IL2CPP'"
          type="warning"
          style="margin-bottom: 20px"
        >
          检测到 IL2CPP 游戏，将使用 BepInEx 6 (预发布版)。首次启动游戏可能需要 30-90 秒生成互操作程序集。
        </NAlert>

        <!-- Uninstalled State -->
        <div v-if="!isInstalled" class="install-cta">
          <div class="cta-visual">
            <svg class="cta-icon" width="40" height="40" viewBox="0 0 40 40" fill="none">
              <rect x="4" y="4" width="32" height="32" rx="8" stroke="currentColor" stroke-width="1.5" opacity="0.2"/>
              <path d="M20 12V24M20 24L14 18M20 24L26 18" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
              <path d="M12 28H28" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
            </svg>
          </div>
          <div class="cta-text">
            <span class="cta-title">准备安装翻译插件</span>
            <span class="cta-desc">将自动下载并安装 BepInEx 框架和 XUnity.AutoTranslator 翻译插件</span>
          </div>
          <div class="cta-actions">
            <NButton
              type="primary"
              size="large"
              :disabled="!game.detectedInfo"
              @click="handleInstall"
              class="install-button"
            >
              <template #icon>
                <svg width="18" height="18" viewBox="0 0 18 18" fill="none">
                  <path d="M9 3V12M9 12L5 8M9 12L13 8M3 15H15" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
                </svg>
              </template>
              一键安装
            </NButton>
          </div>
        </div>

        <!-- Installed State -->
        <div v-else class="installed-info">
          <div class="version-grid">
            <div class="version-card">
              <div class="version-card-header">
                <svg width="18" height="18" viewBox="0 0 18 18" fill="none">
                  <rect x="2" y="2" width="14" height="14" rx="3" stroke="currentColor" stroke-width="1.2" opacity="0.5"/>
                  <path d="M6 9L8 11L12 7" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
                </svg>
                <span class="version-card-label">BepInEx</span>
              </div>
              <span class="version-card-value">{{ game.installedBepInExVersion }}</span>
              <span class="version-card-desc">模组框架</span>
            </div>
            <div class="version-card">
              <div class="version-card-header">
                <svg width="18" height="18" viewBox="0 0 18 18" fill="none">
                  <rect x="2" y="2" width="14" height="14" rx="3" stroke="currentColor" stroke-width="1.2" opacity="0.5"/>
                  <path d="M6 9L8 11L12 7" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
                </svg>
                <span class="version-card-label">XUnity.AutoTranslator</span>
              </div>
              <span class="version-card-value">{{ game.installedXUnityVersion }}</span>
              <span class="version-card-desc">翻译插件</span>
            </div>
          </div>
          <div class="installed-footer">
            <span class="installed-hint">如需重新安装，请先卸载当前版本</span>
            <NButton type="error" @click="handleUninstall" ghost size="small">
              卸载插件
            </NButton>
          </div>
        </div>
      </div>

      <!-- Config Card -->
      <div class="section-card" :style="{ animationDelay: otherFrameworks.length > 0 ? '0.25s' : '0.2s' }">
        <div class="section-header">
          <h2 class="section-title">
            <span class="section-icon translate">
              <NIcon :size="16"><TranslateOutlined /></NIcon>
            </span>
            翻译配置
          </h2>
        </div>
        <ConfigPanel
          :config="config"
          :disabled="!isInstalled"
          @save="handleSaveConfig"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.detail-page {
  /* Full width - no max-width restriction */
}

.detail-loading {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 300px;
}

.loading-spinner {
  width: 36px;
  height: 36px;
  border: 2px solid var(--border);
  border-top-color: var(--accent);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  to { transform: rotate(360deg); }
}

/* ===== Header ===== */
.detail-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
  animation: slideUp 0.4s var(--ease-out) backwards;
}

.back-button {
  display: flex;
  align-items: center;
  gap: 6px;
  background: none;
  border: none;
  color: var(--text-2);
  cursor: pointer;
  padding: 8px 12px 8px 8px;
  border-radius: var(--radius-sm);
  font-size: 14px;
  font-family: var(--font-body);
  transition: all 0.2s ease;
}

.back-button:hover {
  background: var(--bg-muted);
  color: var(--text-1);
}

.back-button:hover svg {
  transform: translateX(-2px);
}

.back-button svg {
  transition: transform 0.2s ease;
}

/* ===== Game Title ===== */
.game-title-section {
  display: flex;
  align-items: center;
  gap: 16px;
  margin-bottom: 28px;
  animation: slideUp 0.4s var(--ease-out) backwards;
}

.title-icon {
  width: 56px;
  height: 56px;
  flex-shrink: 0;
  border-radius: 14px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  position: relative;
  box-shadow: var(--shadow-card);
}

.title-icon-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  image-rendering: -webkit-optimize-contrast;
  border-radius: 13px;
}

.title-icon-fallback {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  opacity: 0;
}

.title-icon-fallback.visible {
  opacity: 1;
}

.game-title {
  font-family: var(--font-display);
  font-size: 30px;
  font-weight: 600;
  color: var(--text-1);
  margin: 0;
  letter-spacing: -0.03em;
}

.title-status {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 5px 14px;
  border-radius: 20px;
  background: var(--bg-subtle-hover);
  border: 1px solid var(--border);
  flex-shrink: 0;
}

.status-indicator {
  width: 8px;
  height: 8px;
  border-radius: 50%;
}

.status-installed {
  background: var(--success);
  box-shadow: 0 0 10px rgba(52, 211, 153, 0.5);
  animation: pulse 2s ease-in-out infinite;
}

.status-partial {
  background: var(--warning);
  box-shadow: 0 0 8px rgba(251, 191, 36, 0.4);
}

.status-none {
  background: var(--text-3);
}

.status-text {
  font-size: 13px;
  font-weight: 500;
  color: var(--text-2);
}

/* ===== Section Card ===== */
.section-card {
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  padding: 24px;
  margin-bottom: 16px;
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

.section-icon.warning {
  background: rgba(251, 191, 36, 0.10);
  color: #fbbf24;
}

.section-icon.download {
  background: rgba(96, 165, 250, 0.10);
  color: #60a5fa;
}

.section-icon.translate {
  background: rgba(167, 139, 250, 0.10);
  color: #a78bfa;
}

.header-btn-group {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
  align-items: center;
}

/* ===== Info Grid (card-style items) ===== */
.info-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(250px, 1fr));
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

.info-card-icon.folder {
  background: rgba(251, 191, 36, 0.10);
  color: #fbbf24;
}

.info-card-icon.file {
  background: rgba(96, 165, 250, 0.10);
  color: #60a5fa;
}

.info-card-icon.unity {
  background: rgba(34, 211, 167, 0.10);
  color: #22d3a7;
}

.info-card-icon.code {
  background: rgba(167, 139, 250, 0.10);
  color: #a78bfa;
}

.info-card-icon.arch {
  background: rgba(251, 146, 60, 0.10);
  color: #fb923c;
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
  font-size: 12px;
  color: var(--text-2);
  word-break: break-all;
  line-height: 1.5;
}

.info-value.muted {
  color: var(--text-3);
}

/* ===== Two-column layout ===== */
.detail-columns {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
  align-items: start;
}

/* ===== Framework List ===== */
.framework-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.framework-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 14px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  gap: 12px;
}

.framework-info {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}

.framework-name {
  font-size: 14px;
  font-weight: 500;
  color: var(--text-1);
}

.framework-version {
  font-family: var(--font-mono);
  font-size: 12px;
  color: var(--text-2);
}

.framework-xunity {
  font-size: 11px;
  color: var(--warning);
  background: rgba(251, 191, 36, 0.08);
  padding: 2px 8px;
  border-radius: 10px;
  border: 1px solid rgba(251, 191, 36, 0.15);
}

/* ===== Install CTA (Uninstalled State) ===== */
.install-cta {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  padding: 12px 0 4px;
  gap: 16px;
}

.cta-visual {
  width: 64px;
  height: 64px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 16px;
  background: var(--accent-soft);
  border: 1px solid var(--accent-border);
  color: var(--accent);
  animation: breathe 3s ease-in-out infinite;
}

.cta-text {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.cta-title {
  font-family: var(--font-display);
  font-size: 16px;
  font-weight: 600;
  color: var(--text-1);
  letter-spacing: -0.01em;
}

.cta-desc {
  font-size: 13px;
  color: var(--text-3);
  line-height: 1.5;
  max-width: 360px;
}

.cta-actions {
  display: flex;
  gap: 10px;
  width: 100%;
  justify-content: center;
  margin-top: 4px;
}

.install-button {
  position: relative;
}

/* ===== Installed State ===== */
.installed-info {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.version-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}

.version-card {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 14px 16px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  transition: border-color 0.2s ease;
}

.version-card:hover {
  border-color: var(--accent-border);
}

.version-card-header {
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--accent);
}

.version-card-label {
  font-size: 12px;
  font-weight: 500;
  color: var(--text-2);
  letter-spacing: 0.01em;
}

.version-card-value {
  font-family: var(--font-mono);
  font-size: 18px;
  font-weight: 600;
  color: var(--text-1);
  letter-spacing: -0.02em;
}

.version-card-desc {
  font-size: 11px;
  color: var(--text-3);
  letter-spacing: 0.02em;
}

.installed-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding-top: 14px;
  border-top: 1px solid var(--border);
  gap: 12px;
}

.installed-hint {
  font-size: 12px;
  color: var(--text-3);
}

/* ===== Responsive ===== */
@media (max-width: 960px) {
  .detail-columns {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 768px) {
  .game-title-section {
    flex-wrap: wrap;
    gap: 12px;
  }

  .game-title {
    font-size: 22px;
    flex: 1;
    min-width: 0;
    word-break: break-word;
  }

  .title-status {
    flex-shrink: 0;
  }

  .section-card {
    padding: 16px;
  }

  .info-grid {
    grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
    gap: 10px;
  }

  .version-card-value {
    font-size: 15px;
  }

  .installed-footer {
    flex-wrap: wrap;
  }
}

@media (max-width: 480px) {
  .section-header {
    flex-wrap: wrap;
    gap: 10px;
  }

  .header-btn-group {
    gap: 6px;
    width: 100%;
  }

  .game-title-section {
    gap: 10px;
  }

  .title-icon {
    width: 44px;
    height: 44px;
    border-radius: 10px;
  }

  .game-title {
    font-size: 20px;
  }

  .section-card {
    padding: 14px;
    border-radius: var(--radius-md);
  }

  .info-grid {
    grid-template-columns: 1fr;
  }

  .version-grid {
    grid-template-columns: 1fr;
  }

  .cta-actions {
    flex-direction: column;
  }

  .cta-actions .n-button {
    width: 100%;
  }

  .installed-footer {
    flex-direction: column;
    align-items: stretch;
    gap: 10px;
  }

  .installed-hint {
    text-align: center;
  }
}
</style>
