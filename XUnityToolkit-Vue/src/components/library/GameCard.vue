<script setup lang="ts">
import { ref, computed } from 'vue'
import { NIcon } from 'naive-ui'
import { PlayArrowRound, PhotoCameraOutlined, GamepadFilled } from '@vicons/material'
import type { Game } from '@/api/types'
import { gamesApi } from '@/api/games'

const props = defineProps<{
  game: Game
  index: number
}>()

const emit = defineEmits<{
  navigate: [id: string]
  editCover: [game: Game]
}>()

const coverLoaded = ref(false)
const coverError = ref(false)
const coverUrl = computed(() => `${gamesApi.getCoverUrl(props.game.id)}?t=${props.game.updatedAt}`)
const iconUrl = computed(() => `/api/games/${props.game.id}/icon`)

function onCoverLoad() {
  coverLoaded.value = true
  coverError.value = false
}

function onCoverError() {
  coverError.value = true
}

function getStatusInfo(state: string) {
  switch (state) {
    case 'FullyInstalled':
      return { label: '已安装', cssClass: 'status-success' }
    case 'BepInExOnly':
      return { label: 'BepInEx', cssClass: 'status-warning' }
    case 'PartiallyInstalled':
      return { label: '部分安装', cssClass: 'status-warning' }
    default:
      return { label: '未安装', cssClass: 'status-default' }
  }
}

// Generate a deterministic gradient from game name
function getPlaceholderGradient(name: string) {
  let hash = 0
  for (let i = 0; i < name.length; i++) {
    hash = name.charCodeAt(i) + ((hash << 5) - hash)
  }
  const hue1 = Math.abs(hash % 360)
  const hue2 = (hue1 + 40) % 360
  return `linear-gradient(135deg, hsl(${hue1}, 35%, 18%) 0%, hsl(${hue2}, 45%, 12%) 100%)`
}

function handlePlay(e: MouseEvent) {
  e.stopPropagation()
  gamesApi.launch(props.game.id)
  // Update locally
  const game = props.game
  game.lastPlayedAt = new Date().toISOString()
}

function handleEditCover(e: MouseEvent) {
  e.stopPropagation()
  emit('editCover', props.game)
}

const statusInfo = computed(() => getStatusInfo(props.game.installState))
const hasExe = computed(() => !!(props.game.executableName || props.game.detectedInfo?.detectedExecutable))
</script>

<template>
  <div
    class="game-card"
    :style="{ animationDelay: `${index * 0.03}s` }"
    @click="emit('navigate', game.id)"
  >
    <!-- Cover Area -->
    <div class="card-cover">
      <!-- Shimmer placeholder -->
      <div v-if="!coverLoaded && !coverError" class="cover-shimmer"></div>

      <!-- Cover image from SteamGridDB or uploaded -->
      <img
        v-show="coverLoaded && !coverError"
        :src="coverUrl"
        :alt="game.name"
        class="cover-img"
        loading="lazy"
        @load="onCoverLoad"
        @error="onCoverError"
      />

      <!-- Fallback: gradient + icon -->
      <div v-if="coverError" class="cover-fallback" :style="{ background: getPlaceholderGradient(game.name) }">
        <div class="fallback-icon-wrapper">
          <img
            :src="iconUrl"
            :alt="game.name"
            class="fallback-exe-icon"
            @error="($event.target as HTMLImageElement).style.display = 'none'"
          />
          <NIcon :size="48" color="var(--text-3)" class="fallback-gamepad">
            <GamepadFilled />
          </NIcon>
        </div>
        <span class="fallback-name">{{ game.name }}</span>
      </div>

      <!-- Hover overlay -->
      <div class="card-overlay">
        <!-- Play button -->
        <button
          v-if="hasExe"
          class="play-btn"
          title="启动游戏"
          @click="handlePlay"
        >
          <NIcon :size="28" color="#fff">
            <PlayArrowRound />
          </NIcon>
        </button>
      </div>

      <!-- Edit cover button -->
      <button class="edit-cover-btn" title="更换封面" @click="handleEditCover">
        <NIcon :size="14" color="#fff">
          <PhotoCameraOutlined />
        </NIcon>
      </button>

      <!-- Status indicator -->
      <div v-if="game.isUnityGame" class="status-badge" :class="statusInfo.cssClass">
        {{ statusInfo.label }}
      </div>
      <div v-else class="status-badge status-non-unity">非 Unity</div>
    </div>

    <!-- Card Info -->
    <div class="card-info">
      <h3 class="card-name" :title="game.name">{{ game.name }}</h3>
    </div>
  </div>
</template>

<style scoped>
.game-card {
  display: flex;
  flex-direction: column;
  border-radius: var(--radius-lg);
  overflow: hidden;
  background: var(--bg-card);
  border: 1px solid var(--border);
  cursor: pointer;
  transition: all 0.3s var(--ease-out);
  animation: floatIn 0.4s var(--ease-out) backwards;
}

.game-card:hover {
  transform: translateY(-6px) scale(1.02);
  border-color: var(--accent-border);
  box-shadow: 0 12px 40px rgba(0, 0, 0, 0.4), 0 0 0 1px var(--accent-border);
}

.game-card:active {
  transform: translateY(-2px) scale(1.01);
  transition-duration: 0.1s;
}

/* ===== Cover Area ===== */
.card-cover {
  position: relative;
  aspect-ratio: 2 / 3;
  overflow: hidden;
  background: var(--bg-subtle);
}

.cover-shimmer {
  position: absolute;
  inset: 0;
  background: linear-gradient(
    110deg,
    var(--bg-subtle) 0%,
    var(--bg-muted) 40%,
    var(--bg-subtle) 60%
  );
  background-size: 200% 100%;
  animation: shimmer 1.5s ease-in-out infinite;
}

.cover-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  transition: transform 0.4s var(--ease-out);
}

.game-card:hover .cover-img {
  transform: scale(1.06);
}

/* ===== Cover Fallback ===== */
.cover-fallback {
  position: absolute;
  inset: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 16px;
  padding: 20px;
}

.fallback-icon-wrapper {
  width: 80px;
  height: 80px;
  border-radius: 16px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  position: relative;
}

.fallback-exe-icon {
  width: 100%;
  height: 100%;
  object-fit: cover;
  position: absolute;
  inset: 0;
  border-radius: 15px;
}

.fallback-exe-icon + .fallback-gamepad {
  display: none;
}

.fallback-exe-icon[style*="display: none"] + .fallback-gamepad {
  display: flex;
}

.fallback-name {
  font-family: var(--font-display);
  font-size: 13px;
  font-weight: 500;
  color: var(--text-2);
  text-align: center;
  overflow: hidden;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
}

/* ===== Hover Overlay ===== */
.card-overlay {
  position: absolute;
  inset: 0;
  background: linear-gradient(
    to top,
    rgba(0, 0, 0, 0.7) 0%,
    rgba(0, 0, 0, 0.1) 40%,
    transparent 100%
  );
  display: flex;
  align-items: center;
  justify-content: center;
  opacity: 0;
  transition: opacity 0.25s ease;
}

.game-card:hover .card-overlay {
  opacity: 1;
}

/* ===== Play Button ===== */
.play-btn {
  width: 56px;
  height: 56px;
  border-radius: 50%;
  border: none;
  background: var(--accent);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  transform: scale(0.8);
  opacity: 0;
  transition: all 0.3s var(--ease-out);
  box-shadow: 0 4px 20px rgba(59, 130, 246, 0.4);
  padding-left: 3px;
}

.game-card:hover .play-btn {
  transform: scale(1);
  opacity: 1;
}

.play-btn:hover {
  transform: scale(1.1) !important;
  box-shadow: 0 6px 28px rgba(59, 130, 246, 0.6);
}

.play-btn:active {
  transform: scale(0.95) !important;
}

/* ===== Edit Cover Button ===== */
.edit-cover-btn {
  position: absolute;
  top: 8px;
  left: 8px;
  width: 28px;
  height: 28px;
  border-radius: 8px;
  border: none;
  background: rgba(0, 0, 0, 0.6);
  backdrop-filter: blur(8px);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  opacity: 0;
  transform: scale(0.8);
  transition: all 0.2s ease;
  z-index: 2;
}

.game-card:hover .edit-cover-btn {
  opacity: 1;
  transform: scale(1);
}

.edit-cover-btn:hover {
  background: rgba(0, 0, 0, 0.8);
}

/* ===== Status Badge ===== */
.status-badge {
  position: absolute;
  top: 8px;
  right: 8px;
  font-size: 10px;
  font-weight: 600;
  padding: 3px 8px;
  border-radius: 6px;
  backdrop-filter: blur(8px);
  letter-spacing: 0.02em;
  z-index: 2;
}

.status-success {
  background: rgba(52, 211, 153, 0.2);
  color: #34d399;
  border: 1px solid rgba(52, 211, 153, 0.3);
}

.status-warning {
  background: rgba(251, 191, 36, 0.2);
  color: #fbbf24;
  border: 1px solid rgba(251, 191, 36, 0.3);
}

.status-default {
  background: var(--bg-muted);
  color: var(--text-3);
  border: 1px solid var(--border);
}

.status-non-unity {
  background: rgba(167, 139, 250, 0.15);
  color: #a78bfa;
  border: 1px solid rgba(167, 139, 250, 0.25);
}

/* ===== Card Info ===== */
.card-info {
  padding: 10px 12px 12px;
}

.card-name {
  font-family: var(--font-display);
  font-size: 13px;
  font-weight: 500;
  color: var(--text-1);
  margin: 0;
  overflow: hidden;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  line-height: 1.4;
  letter-spacing: -0.01em;
}

/* ===== Responsive ===== */
@media (max-width: 480px) {
  .play-btn {
    width: 44px;
    height: 44px;
  }

  .card-info {
    padding: 8px 10px 10px;
  }

  .card-name {
    font-size: 12px;
  }
}
</style>
