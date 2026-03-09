<script setup lang="ts">
import { onMounted } from 'vue'
import { NButton, NIcon, NTag, useMessage } from 'naive-ui'
import { Add } from '@vicons/ionicons5'
import { GamepadFilled } from '@vicons/material'
import { useRouter } from 'vue-router'
import { useGamesStore } from '@/stores/games'

const gamesStore = useGamesStore()
const router = useRouter()
const message = useMessage()

onMounted(() => {
  gamesStore.fetchGames()
})

async function handleAddGame() {
  const game = await gamesStore.addGameViaDialog()
  if (game) {
    message.success(`已添加: ${game.name}`)
  }
}

function navigateToGame(id: string) {
  router.push(`/games/${id}`)
}

function getStatusInfo(state: string) {
  switch (state) {
    case 'FullyInstalled':
      return { label: '已安装', color: 'var(--success)', dotClass: 'dot-success' }
    case 'BepInExOnly':
      return { label: 'BepInEx', color: 'var(--warning)', dotClass: 'dot-warning' }
    case 'PartiallyInstalled':
      return { label: '部分安装', color: 'var(--warning)', dotClass: 'dot-warning' }
    default:
      return { label: '未安装', color: 'var(--text-3)', dotClass: 'dot-default' }
  }
}
</script>

<template>
  <div class="library-page">
    <!-- Header -->
    <div class="library-header">
      <div class="header-left">
        <h1 class="library-title">游戏库</h1>
        <span v-if="gamesStore.games.length > 0" class="game-count">
          {{ gamesStore.games.length }} 款游戏
        </span>
      </div>
      <NButton type="primary" @click="handleAddGame" size="medium">
        <template #icon>
          <NIcon><Add /></NIcon>
        </template>
        添加游戏
      </NButton>
    </div>

    <!-- Loading State -->
    <div v-if="gamesStore.loading" class="loading-state">
      <div class="loading-spinner"></div>
      <span class="loading-text">加载中...</span>
    </div>

    <!-- Empty State -->
    <div v-else-if="gamesStore.games.length === 0" class="empty-state">
      <div class="empty-icon-wrapper">
        <NIcon :size="48" color="var(--text-3)">
          <GamepadFilled />
        </NIcon>
      </div>
      <h3 class="empty-title">还没有添加任何游戏</h3>
      <p class="empty-desc">选择 Unity 游戏的安装目录，即可一键安装翻译插件</p>
      <NButton type="primary" size="large" @click="handleAddGame">
        <template #icon>
          <NIcon><Add /></NIcon>
        </template>
        添加第一个游戏
      </NButton>
    </div>

    <!-- Game Grid -->
    <div v-else class="games-grid">
      <div
        v-for="(game, index) in gamesStore.games"
        :key="game.id"
        class="game-card"
        :style="{ animationDelay: `${index * 0.06}s` }"
        @click="navigateToGame(game.id)"
      >
        <!-- Card ambient glow -->
        <div class="card-glow"></div>

        <!-- Header row -->
        <div class="card-header">
          <h3 class="card-name">{{ game.name }}</h3>
          <div class="card-status">
            <span class="status-dot" :class="getStatusInfo(game.installState).dotClass"></span>
            <span class="status-label" :style="{ color: getStatusInfo(game.installState).color }">
              {{ getStatusInfo(game.installState).label }}
            </span>
          </div>
        </div>

        <!-- Path -->
        <p class="card-path">{{ game.gamePath }}</p>

        <!-- Tags -->
        <div v-if="game.detectedInfo" class="card-tags">
          <span class="info-pill">Unity {{ game.detectedInfo.unityVersion }}</span>
          <span class="info-pill">{{ game.detectedInfo.backend }}</span>
          <span class="info-pill">{{ game.detectedInfo.architecture }}</span>
        </div>
        <div v-else class="card-tags">
          <span class="info-pill muted">未检测</span>
        </div>

        <!-- Hover arrow indicator -->
        <div class="card-arrow">
          <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
            <path d="M6 4L10 8L6 12" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
          </svg>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.library-page {
  max-width: 1200px;
  animation: fadeIn 0.3s ease;
}

/* ===== Header ===== */
.library-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 28px;
  animation: slideUp 0.4s var(--ease-out) backwards;
}

.header-left {
  display: flex;
  align-items: baseline;
  gap: 14px;
}

.library-title {
  font-family: var(--font-display);
  font-size: 28px;
  font-weight: 600;
  color: var(--text-1);
  letter-spacing: -0.03em;
  margin: 0;
}

.game-count {
  font-size: 13px;
  font-weight: 500;
  color: var(--text-3);
  background: var(--accent-soft);
  padding: 3px 10px;
  border-radius: 20px;
  letter-spacing: 0.01em;
}

/* ===== Loading State ===== */
.loading-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 80px 0;
  gap: 16px;
  animation: fadeIn 0.3s ease;
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

.loading-text {
  font-size: 14px;
  color: var(--text-3);
}

/* ===== Empty State ===== */
.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 80px 0;
  text-align: center;
  animation: scaleIn 0.5s var(--ease-out) backwards;
}

.empty-icon-wrapper {
  width: 88px;
  height: 88px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 24px;
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid var(--border);
  margin-bottom: 20px;
  animation: breathe 3s ease-in-out infinite;
}

.empty-title {
  font-family: var(--font-display);
  font-size: 18px;
  font-weight: 500;
  color: var(--text-1);
  margin: 0 0 8px;
}

.empty-desc {
  font-size: 14px;
  color: var(--text-3);
  margin: 0 0 24px;
  max-width: 320px;
  line-height: 1.5;
}

/* ===== Game Grid ===== */
.games-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
  gap: 16px;
}

/* ===== Game Card ===== */
.game-card {
  position: relative;
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  padding: 22px 24px 18px;
  cursor: pointer;
  transition: all 0.35s var(--ease-out);
  animation: floatIn 0.5s var(--ease-out) backwards;
  overflow: hidden;
}

.card-glow {
  position: absolute;
  top: 0;
  right: 0;
  width: 140px;
  height: 100px;
  background: radial-gradient(circle at top right, rgba(34, 211, 167, 0.04), transparent 70%);
  border-radius: 0 var(--radius-lg) 0 0;
  pointer-events: none;
  transition: opacity 0.35s ease;
  opacity: 0.6;
}

.game-card:hover {
  transform: translateY(-4px);
  border-color: var(--accent-border);
  box-shadow: var(--shadow-card-hover);
  background: var(--bg-card-hover);
}

.game-card:hover .card-glow {
  opacity: 1;
}

.game-card:hover .card-arrow {
  opacity: 1;
  transform: translateX(0);
}

.game-card:active {
  transform: translateY(-2px);
  transition-duration: 0.1s;
}

/* Card Header */
.card-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 12px;
  margin-bottom: 10px;
}

.card-name {
  font-family: var(--font-display);
  font-size: 16px;
  font-weight: 500;
  color: var(--text-1);
  margin: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  flex: 1;
  min-width: 0;
  letter-spacing: -0.01em;
}

/* Status */
.card-status {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-shrink: 0;
}

.status-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  flex-shrink: 0;
}

.dot-success {
  background: var(--success);
  box-shadow: 0 0 8px rgba(52, 211, 153, 0.5);
  animation: pulse 2s ease-in-out infinite;
}

.dot-warning {
  background: var(--warning);
  box-shadow: 0 0 8px rgba(251, 191, 36, 0.4);
}

.dot-default {
  background: var(--text-3);
}

.status-label {
  font-size: 12px;
  font-weight: 500;
  letter-spacing: 0.01em;
}

/* Path */
.card-path {
  font-family: var(--font-mono);
  font-size: 11.5px;
  color: var(--text-3);
  margin: 0 0 14px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  line-height: 1.4;
}

/* Tags */
.card-tags {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}

.info-pill {
  font-size: 11px;
  font-weight: 500;
  color: var(--text-2);
  background: rgba(255, 255, 255, 0.05);
  padding: 3px 10px;
  border-radius: 20px;
  letter-spacing: 0.01em;
  border: 1px solid rgba(255, 255, 255, 0.04);
  transition: all 0.2s ease;
}

.info-pill.muted {
  color: var(--text-3);
}

.game-card:hover .info-pill {
  background: rgba(255, 255, 255, 0.07);
  border-color: rgba(255, 255, 255, 0.08);
}

/* Arrow */
.card-arrow {
  position: absolute;
  right: 20px;
  top: 50%;
  transform: translateX(-8px);
  margin-top: -8px;
  color: var(--accent);
  opacity: 0;
  transition: all 0.3s var(--ease-out);
}
</style>
