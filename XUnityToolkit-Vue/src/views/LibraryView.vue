<script setup lang="ts">
import { onMounted, ref, h, computed } from 'vue'
import { NButton, NIcon, NSelect, NButtonGroup, NDropdown, NModal, NInput, useMessage } from 'naive-ui'
import { Add } from '@vicons/ionicons5'
import { GamepadFilled, GridViewRound, ViewListRound, PlayArrowRound, DriveFileRenameOutlineOutlined, PhotoCameraOutlined } from '@vicons/material'
import { useRouter } from 'vue-router'
import { useGamesStore } from '@/stores/games'
import { useAddGameFlow } from '@/composables/useAddGameFlow'
import { gamesApi } from '@/api/games'
import GameCard from '@/components/library/GameCard.vue'
import LibraryCustomizer from '@/components/library/LibraryCustomizer.vue'
import type { Game } from '@/api/types'

const gamesStore = useGamesStore()
const router = useRouter()
const message = useMessage()
const { addGame } = useAddGameFlow(message)

const showCoverPicker = ref(false)
const coverPickerGame = ref<Game | null>(null)

// Context menu state
const showContextMenu = ref(false)
const contextMenuX = ref(0)
const contextMenuY = ref(0)
const contextMenuGame = ref<Game | null>(null)
const contextMenuOptions = [
  { label: '重命名', key: 'rename', icon: () => h(NIcon, { size: 16 }, { default: () => h(DriveFileRenameOutlineOutlined) }) },
  { label: '更换封面', key: 'cover', icon: () => h(NIcon, { size: 16 }, { default: () => h(PhotoCameraOutlined) }) },
]

// Rename modal state
const showRenameModal = ref(false)
const renameValue = ref('')
const renameGameId = ref('')
const renameSaving = ref(false)

onMounted(async () => {
  await gamesStore.loadPreferences()
  gamesStore.fetchGames()
})

async function handleAddGame() {
  await addGame()
}

function navigateToGame(id: string) {
  router.push(`/games/${id}`)
}

function setViewMode(mode: 'grid' | 'list') {
  gamesStore.viewMode = mode
  gamesStore.savePreferences()
}

function handleSortChange(value: string) {
  gamesStore.sortBy = value as 'name' | 'recent' | 'added'
  gamesStore.savePreferences()
}

function openCoverPicker(game: Game) {
  coverPickerGame.value = game
  showCoverPicker.value = true
}

function handleCardContextMenu(e: MouseEvent, game: Game) {
  contextMenuGame.value = game
  contextMenuX.value = e.clientX
  contextMenuY.value = e.clientY
  showContextMenu.value = true
}

function handleContextMenuSelect(key: string) {
  showContextMenu.value = false
  if (!contextMenuGame.value) return
  if (key === 'rename') {
    renameGameId.value = contextMenuGame.value.id
    renameValue.value = contextMenuGame.value.name
    showRenameModal.value = true
  } else if (key === 'cover') {
    openCoverPicker(contextMenuGame.value)
  }
}

async function handleRenameConfirm() {
  const name = renameValue.value.trim()
  if (!name || !renameGameId.value) {
    showRenameModal.value = false
    return
  }
  renameSaving.value = true
  try {
    await gamesStore.renameGame(renameGameId.value, name)
    message.success('游戏名称已更新')
  } catch {
    message.error('重命名失败')
  } finally {
    renameSaving.value = false
    showRenameModal.value = false
  }
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

async function handleLaunchFromList(e: MouseEvent, id: string) {
  e.stopPropagation()
  await gamesStore.launchGame(id)
}

const sortOptions = [
  { label: '名称', value: 'name' },
  { label: '最近游玩', value: 'recent' },
  { label: '添加时间', value: 'added' },
]

const cardSizeMap = { small: 120, medium: 160, large: 200, xlarge: 240 }
const gapMap = { compact: 8, normal: 16, spacious: 24 }

const gridStyle = computed(() => {
  const minWidth = cardSizeMap[gamesStore.cardSize] || 160
  const gapPx = gapMap[gamesStore.gap] || 16
  return {
    gridTemplateColumns: `repeat(auto-fill, minmax(${minWidth}px, 1fr))`,
    gap: `${gapPx}px`,
  }
})
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
      <div class="header-actions">
        <!-- View mode toggle -->
        <NButtonGroup size="small">
          <NButton
            :type="gamesStore.viewMode === 'grid' ? 'primary' : 'default'"
            :tertiary="gamesStore.viewMode !== 'grid'"
            @click="setViewMode('grid')"
          >
            <template #icon>
              <NIcon><GridViewRound /></NIcon>
            </template>
          </NButton>
          <NButton
            :type="gamesStore.viewMode === 'list' ? 'primary' : 'default'"
            :tertiary="gamesStore.viewMode !== 'list'"
            @click="setViewMode('list')"
          >
            <template #icon>
              <NIcon><ViewListRound /></NIcon>
            </template>
          </NButton>
        </NButtonGroup>

        <!-- Sort -->
        <NSelect
          :value="gamesStore.sortBy"
          :options="sortOptions"
          size="small"
          style="width: 120px"
          @update:value="handleSortChange"
        />

        <!-- Customizer -->
        <LibraryCustomizer />

        <!-- Add game -->
        <NButton type="primary" @click="handleAddGame" size="small">
          <template #icon>
            <NIcon><Add /></NIcon>
          </template>
          添加游戏
        </NButton>
      </div>
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

    <!-- Grid View -->
    <div v-else-if="gamesStore.viewMode === 'grid'" class="games-grid" :style="gridStyle">
      <GameCard
        v-for="(game, index) in gamesStore.sortedGames"
        :key="game.id"
        :game="game"
        :index="index"
        :show-label="gamesStore.showLabels"
        @navigate="navigateToGame"
        @edit-cover="openCoverPicker"
        @context-menu="handleCardContextMenu"
      />
    </div>

    <!-- List View -->
    <div v-else class="games-list">
      <div
        v-for="(game, index) in gamesStore.sortedGames"
        :key="game.id"
        class="game-row"
        :style="{ animationDelay: `${index * 0.04}s` }"
        @click="navigateToGame(game.id)"
        @contextmenu.prevent="handleCardContextMenu($event, game)"
      >
        <!-- Icon -->
        <div class="row-icon">
          <img
            :src="`/api/games/${game.id}/icon?t=${game.updatedAt}`"
            :alt="game.name"
            class="icon-img"
            @error="($event.target as HTMLImageElement).style.display = 'none'; ($event.target as HTMLImageElement).nextElementSibling?.classList.add('visible')"
          />
          <div class="icon-fallback">
            <NIcon :size="22" color="var(--text-3)">
              <GamepadFilled />
            </NIcon>
          </div>
        </div>

        <!-- Name + Path -->
        <div class="row-info">
          <h3 class="row-name">{{ game.name }}</h3>
          <p class="row-path">{{ game.gamePath }}</p>
        </div>

        <!-- Tags -->
        <div class="row-tags">
          <template v-if="!game.isUnityGame">
            <span class="info-pill non-unity">非 Unity</span>
          </template>
          <template v-else-if="game.detectedInfo">
            <span class="info-pill">Unity {{ game.detectedInfo.unityVersion }}</span>
            <span class="info-pill">{{ game.detectedInfo.backend }}</span>
            <span class="info-pill">{{ game.detectedInfo.architecture }}</span>
          </template>
          <span v-else class="info-pill muted">未检测</span>
        </div>

        <!-- Play button -->
        <button
          v-if="game.executableName || game.detectedInfo?.detectedExecutable"
          class="row-play-btn"
          title="启动游戏"
          @click="handleLaunchFromList($event, game.id)"
        >
          <NIcon :size="18">
            <PlayArrowRound />
          </NIcon>
        </button>

        <!-- Status -->
        <div v-if="game.isUnityGame" class="row-status">
          <span class="status-dot" :class="getStatusInfo(game.installState).dotClass"></span>
          <span class="status-label" :style="{ color: getStatusInfo(game.installState).color }">
            {{ getStatusInfo(game.installState).label }}
          </span>
        </div>

        <!-- Arrow -->
        <div class="row-arrow">
          <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
            <path d="M6 4L10 8L6 12" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
          </svg>
        </div>
      </div>
    </div>

    <!-- Context Menu -->
    <NDropdown
      trigger="manual"
      :show="showContextMenu"
      :options="contextMenuOptions"
      :x="contextMenuX"
      :y="contextMenuY"
      placement="bottom-start"
      @clickoutside="showContextMenu = false"
      @select="handleContextMenuSelect"
    />

    <!-- Rename Modal -->
    <NModal
      :show="showRenameModal"
      preset="dialog"
      title="重命名游戏"
      positive-text="确定"
      negative-text="取消"
      :positive-button-props="{ loading: renameSaving }"
      @positive-click="handleRenameConfirm"
      @negative-click="showRenameModal = false"
      @mask-click="showRenameModal = false"
    >
      <NInput
        v-model:value="renameValue"
        placeholder="输入新名称"
        @keyup.enter="handleRenameConfirm"
        autofocus
      />
    </NModal>

    <!-- Cover Picker Modal (lazy loaded) -->
    <CoverPickerModal
      v-if="showCoverPicker && coverPickerGame"
      :show="showCoverPicker"
      :game="coverPickerGame"
      @update:show="showCoverPicker = $event"
      @saved="gamesStore.refreshGame(coverPickerGame!.id)"
    />
  </div>
</template>

<script lang="ts">
import { defineAsyncComponent } from 'vue'

const CoverPickerModal = defineAsyncComponent(
  () => import('@/components/library/CoverPickerModal.vue')
)

export default {
  components: { CoverPickerModal }
}
</script>

<style scoped>
.library-page {
  animation: fadeIn 0.3s ease;
}

/* ===== Header ===== */
.library-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 24px;
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

.header-actions {
  display: flex;
  align-items: center;
  gap: 10px;
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
  background: var(--bg-subtle);
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

/* ===== Grid View ===== */
.games-grid {
  display: grid;
  /* grid-template-columns and gap are set dynamically via :style */
}

/* ===== List View ===== */
.games-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.game-row {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 14px 16px 14px 20px;
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  cursor: pointer;
  transition: all 0.25s var(--ease-out);
  animation: floatIn 0.4s var(--ease-out) backwards;
}

.game-row:hover {
  background: var(--bg-card-hover);
  border-color: var(--accent-border);
  box-shadow: var(--shadow-card-hover);
  transform: translateX(4px);
}

.game-row:active {
  transform: translateX(2px);
  transition-duration: 0.1s;
}

/* Row Icon */
.row-icon {
  width: 42px;
  height: 42px;
  flex-shrink: 0;
  border-radius: 10px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  position: relative;
  transition: all 0.25s var(--ease-out);
}

.game-row:hover .row-icon {
  border-color: var(--accent-border);
  box-shadow: 0 0 12px var(--accent-soft);
}

.icon-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  image-rendering: -webkit-optimize-contrast;
  border-radius: 9px;
}

.icon-fallback {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  opacity: 0;
}

.icon-fallback.visible,
.icon-img[style*="display: none"] + .icon-fallback {
  opacity: 1;
}

/* Row Info */
.row-info {
  flex: 1;
  min-width: 0;
}

.row-name {
  font-family: var(--font-display);
  font-size: 15px;
  font-weight: 500;
  color: var(--text-1);
  margin: 0 0 3px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  letter-spacing: -0.01em;
}

.row-path {
  font-family: var(--font-mono);
  font-size: 11px;
  color: var(--text-3);
  margin: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  line-height: 1.4;
}

/* Row Tags */
.row-tags {
  display: flex;
  gap: 6px;
  flex-shrink: 0;
  margin-left: auto;
}

.info-pill {
  font-size: 11px;
  font-weight: 500;
  color: var(--text-2);
  background: var(--bg-muted);
  padding: 3px 10px;
  border-radius: 20px;
  letter-spacing: 0.01em;
  border: 1px solid var(--border);
  transition: all 0.2s ease;
  white-space: nowrap;
}

.info-pill.muted {
  color: var(--text-3);
}

.info-pill.non-unity {
  color: var(--text-3);
  border-color: var(--accent-border);
  background: var(--accent-soft);
}

.game-row:hover .info-pill {
  background: var(--bg-muted-hover);
  border-color: var(--border-hover);
}

/* Row Play Button */
.row-play-btn {
  flex-shrink: 0;
  width: 32px;
  height: 32px;
  border-radius: 50%;
  border: 1px solid var(--border);
  background: var(--bg-muted);
  color: var(--text-2);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  opacity: 0;
  transform: scale(0.8);
  transition: all 0.2s var(--ease-out);
  padding-left: 2px;
}

.game-row:hover .row-play-btn {
  opacity: 1;
  transform: scale(1);
}

.row-play-btn:hover {
  background: var(--accent);
  border-color: var(--accent);
  color: #fff;
  box-shadow: 0 2px 12px var(--accent-glow);
}

/* Row Status */
.row-status {
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
  white-space: nowrap;
}

/* Row Arrow */
.row-arrow {
  flex-shrink: 0;
  color: var(--accent);
  opacity: 0;
  width: 0;
  overflow: visible;
  transform: translateX(-10px);
  transition: all 0.25s var(--ease-out);
}

.game-row:hover .row-arrow {
  opacity: 1;
  transform: translateX(-4px);
}

/* ===== Responsive ===== */
@media (max-width: 768px) {
  .library-header {
    flex-wrap: wrap;
    gap: 12px;
    margin-bottom: 20px;
  }

  .header-actions {
    flex-wrap: wrap;
    gap: 8px;
  }

  .library-title {
    font-size: 22px;
  }

  .games-grid {
    gap: 12px !important;
  }

  .game-row {
    flex-wrap: wrap;
    gap: 10px;
    padding: 12px 14px;
  }

  .row-tags {
    display: none;
  }

  .row-arrow {
    display: none;
  }

  .row-play-btn {
    opacity: 1;
    transform: scale(1);
  }

  .row-status {
    margin-left: auto;
  }
}

@media (max-width: 480px) {
  .library-title {
    font-size: 20px;
  }

  .games-grid {
    grid-template-columns: repeat(2, 1fr) !important;
    gap: 10px !important;
  }

  .game-row {
    padding: 10px 12px;
    gap: 10px;
  }

  .row-icon {
    width: 36px;
    height: 36px;
    border-radius: 8px;
  }

  .row-name {
    font-size: 14px;
  }

  .row-path {
    font-size: 10px;
  }

  .status-label {
    font-size: 11px;
  }
}
</style>
