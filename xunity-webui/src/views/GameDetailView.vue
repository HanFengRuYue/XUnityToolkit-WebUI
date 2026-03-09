<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NCard,
  NButton,
  NSpace,
  NAlert,
  NDescriptions,
  NDescriptionsItem,
  NIcon,
  useMessage,
  useDialog,
} from 'naive-ui'
import { GamepadFilled } from '@vicons/material'
import { useGamesStore } from '@/stores/games'
import { useInstallStore } from '@/stores/install'
import type { Game, XUnityConfig } from '@/api/types'
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
const detecting = ref(false)

const isInstalled = computed(
  () => game.value?.installState === 'FullyInstalled',
)
const hasBepInEx = computed(
  () =>
    game.value?.installState === 'BepInExOnly' ||
    game.value?.installState === 'FullyInstalled',
)

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

async function handleDetect() {
  detecting.value = true
  try {
    await gamesStore.detectGame(gameId)
    await loadGame()
    message.success('检测完成')
  } catch {
    message.error('检测失败')
  } finally {
    detecting.value = false
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
        <span class="status-indicator" :class="{
          'status-installed': isInstalled,
          'status-partial': hasBepInEx && !isInstalled,
          'status-none': !hasBepInEx,
        }"></span>
        <span class="status-text">
          {{ isInstalled ? '已安装' : hasBepInEx ? '仅 BepInEx' : '未安装' }}
        </span>
      </div>
    </div>

    <!-- Game Info Card -->
    <div class="section-card" style="animation-delay: 0.1s">
      <div class="section-header">
        <h2 class="section-title">游戏信息</h2>
        <NButton
          v-if="!game.detectedInfo"
          size="small"
          :loading="detecting"
          @click="handleDetect"
        >
          检测游戏
        </NButton>
      </div>

      <div class="info-grid">
        <div class="info-item">
          <span class="info-label">路径</span>
          <span class="info-value mono">{{ game.gamePath }}</span>
        </div>
        <div class="info-item">
          <span class="info-label">可执行文件</span>
          <span class="info-value" :class="{ muted: !game.executableName }">
            {{ game.executableName || '未检测' }}
          </span>
        </div>
        <div class="info-item">
          <span class="info-label">Unity 版本</span>
          <span class="info-value" :class="{ muted: !game.detectedInfo }">
            {{ game.detectedInfo?.unityVersion || '未检测' }}
          </span>
        </div>
        <div class="info-item">
          <span class="info-label">脚本后端</span>
          <span class="info-value" :class="{ muted: !game.detectedInfo }">
            {{ game.detectedInfo?.backend || '未检测' }}
          </span>
        </div>
        <div class="info-item">
          <span class="info-label">架构</span>
          <span class="info-value" :class="{ muted: !game.detectedInfo }">
            {{ game.detectedInfo?.architecture || '未检测' }}
          </span>
        </div>
      </div>
    </div>

    <!-- Install Management Card -->
    <div class="section-card" style="animation-delay: 0.15s">
      <div class="section-header">
        <h2 class="section-title">安装管理</h2>
      </div>

      <NAlert
        v-if="game.detectedInfo?.backend === 'IL2CPP'"
        type="warning"
        style="margin-bottom: 20px"
      >
        检测到 IL2CPP 游戏，将使用 BepInEx 6 (预发布版)。首次启动游戏可能需要 30-90 秒生成互操作程序集。
      </NAlert>

      <div v-if="!isInstalled" class="install-actions">
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
          一键安装 BepInEx + XUnity.AutoTranslator
        </NButton>
        <NButton v-if="!game.detectedInfo" @click="handleDetect" :loading="detecting">
          先检测游戏
        </NButton>
      </div>

      <div v-else class="installed-info">
        <div class="version-badges">
          <span class="version-badge">
            <span class="badge-label">BepInEx</span>
            <span class="badge-value">{{ game.installedBepInExVersion }}</span>
          </span>
          <span class="version-badge">
            <span class="badge-label">XUnity</span>
            <span class="badge-value">{{ game.installedXUnityVersion }}</span>
          </span>
        </div>
        <NButton type="error" @click="handleUninstall" ghost>
          卸载
        </NButton>
      </div>
    </div>

    <!-- Config Card -->
    <div class="section-card" style="animation-delay: 0.2s">
      <div class="section-header">
        <h2 class="section-title">翻译配置</h2>
      </div>
      <ConfigPanel
        :config="config"
        :disabled="!isInstalled"
        @save="handleSaveConfig"
      />
    </div>
  </div>
</template>

<style scoped>
.detail-page {
  max-width: 800px;
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
  background: rgba(255, 255, 255, 0.05);
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
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid rgba(255, 255, 255, 0.08);
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  position: relative;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.2);
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
  background: rgba(255, 255, 255, 0.04);
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
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
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
  font-size: 12px;
  color: var(--text-2);
  word-break: break-all;
  line-height: 1.5;
}

.info-value.muted {
  color: var(--text-3);
}

/* ===== Install Actions ===== */
.install-actions {
  display: flex;
  gap: 12px;
  align-items: center;
  flex-wrap: wrap;
}

.install-button {
  position: relative;
}

/* ===== Installed Info ===== */
.installed-info {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
}

.version-badges {
  display: flex;
  gap: 10px;
}

.version-badge {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 14px;
  background: rgba(255, 255, 255, 0.04);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
}

.badge-label {
  font-size: 12px;
  font-weight: 500;
  color: var(--text-3);
}

.badge-value {
  font-family: var(--font-mono);
  font-size: 12px;
  font-weight: 500;
  color: var(--accent);
}
</style>
