<script setup lang="ts">
import { ref, watch, onMounted } from 'vue'
import { NModal, NInput, NButton, NIcon, NTabs, NTabPane, NSpin, useMessage } from 'naive-ui'
import { SearchRound, CloudUploadOutlined } from '@vicons/material'
import { gamesApi } from '@/api/games'
import type { Game, SteamGridDbSearchResult, SteamGridDbImage, SteamStoreSearchResult } from '@/api/types'
import WebImageSearchTab from './WebImageSearchTab.vue'

const props = defineProps<{
  show: boolean
  game: Game
}>()

const emit = defineEmits<{
  'update:show': [value: boolean]
  saved: []
}>()

const message = useMessage()

// Steam search state
const steamQuery = ref(props.game.name)
const steamResults = ref<SteamStoreSearchResult[]>([])
const steamSearching = ref(false)
const steamSaving = ref(false)

// SteamGridDB search state
const searchQuery = ref(props.game.name)
const searchResults = ref<SteamGridDbSearchResult[]>([])
const searching = ref(false)
const selectedGameId = ref<number | null>(props.game.steamGridDbGameId ?? null)
const heroImages = ref<SteamGridDbImage[]>([])
const loadingHeroes = ref(false)
const savingBackground = ref(false)

// Upload state
const uploading = ref(false)
const fileInput = ref<HTMLInputElement | null>(null)

// Auto-search Steam on mount
onMounted(() => {
  if (steamQuery.value.trim()) {
    searchSteam()
  }
  if (props.game.steamGridDbGameId) {
    loadHeroes(props.game.steamGridDbGameId)
  }
})

watch(() => props.game, (game) => {
  steamQuery.value = game.name
  searchQuery.value = game.name
  selectedGameId.value = game.steamGridDbGameId ?? null
  if (game.steamGridDbGameId) {
    loadHeroes(game.steamGridDbGameId)
  }
})

function close() {
  emit('update:show', false)
}

// Steam search
async function searchSteam() {
  if (!steamQuery.value.trim()) return
  steamSearching.value = true
  steamResults.value = []
  try {
    steamResults.value = await gamesApi.searchSteamBackgrounds(props.game.id, steamQuery.value.trim())
  } catch (e) {
    message.error(e instanceof Error ? e.message : '搜索失败')
  } finally {
    steamSearching.value = false
  }
}

async function selectSteamGame(result: SteamStoreSearchResult) {
  steamSaving.value = true
  try {
    await gamesApi.selectSteamBackground(props.game.id, result.id)
    message.success('背景图已更新')
    emit('saved')
    close()
  } catch (e) {
    message.error(e instanceof Error ? e.message : '获取背景图失败')
  } finally {
    steamSaving.value = false
  }
}

// SteamGridDB search
async function searchSteamGridDb() {
  if (!searchQuery.value.trim()) return
  searching.value = true
  searchResults.value = []
  selectedGameId.value = null
  heroImages.value = []
  try {
    searchResults.value = await gamesApi.searchBackgroundGames(props.game.id, searchQuery.value.trim())
  } catch (e) {
    message.error(e instanceof Error ? e.message : '搜索失败')
  } finally {
    searching.value = false
  }
}

async function selectSearchResult(result: SteamGridDbSearchResult) {
  selectedGameId.value = result.id
  await loadHeroes(result.id)
}

async function loadHeroes(steamGridDbGameId: number) {
  loadingHeroes.value = true
  heroImages.value = []
  try {
    heroImages.value = await gamesApi.getBackgroundHeroes(props.game.id, steamGridDbGameId)
  } catch (e) {
    message.error(e instanceof Error ? e.message : '获取背景图列表失败')
  } finally {
    loadingHeroes.value = false
  }
}

async function selectHeroImage(image: SteamGridDbImage) {
  if (!selectedGameId.value) return
  savingBackground.value = true
  try {
    await gamesApi.selectBackground(props.game.id, image.url, selectedGameId.value)
    message.success('背景图已更新')
    emit('saved')
    close()
  } catch (e) {
    message.error(e instanceof Error ? e.message : '保存背景图失败')
  } finally {
    savingBackground.value = false
  }
}

// Web search
function handleWebSearchSaved() {
  emit('saved')
  close()
}

// Upload
function triggerUpload() {
  fileInput.value?.click()
}

async function handleFileSelect(e: Event) {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
    message.error('仅支持 JPEG、PNG 或 WebP 格式')
    return
  }
  if (file.size > 10 * 1024 * 1024) {
    message.error('图片文件不能超过 10 MB')
    return
  }
  uploading.value = true
  try {
    await gamesApi.uploadBackground(props.game.id, file)
    message.success('背景图已上传')
    emit('saved')
    close()
  } catch (err) {
    message.error(err instanceof Error ? err.message : '上传背景图失败')
  } finally {
    uploading.value = false
  }
}
</script>

<template>
  <NModal
    :show="show"
    preset="card"
    title="选择背景图"
    :bordered="false"
    :closable="true"
    :mask-closable="true"
    style="width: 680px; max-width: 95vw"
    @update:show="emit('update:show', $event)"
  >
    <NTabs type="segment" animated>
      <!-- Steam Store Search (free, no API key) -->
      <NTabPane name="steam" tab="Steam 搜索">
        <div class="search-section">
          <div class="search-bar">
            <NInput
              v-model:value="steamQuery"
              placeholder="搜索游戏名称..."
              size="small"
              @keyup.enter="searchSteam"
            >
              <template #suffix>
                <NIcon :component="SearchRound" style="cursor: pointer" @click="searchSteam" />
              </template>
            </NInput>
          </div>

          <div v-if="steamSearching" class="center-state">
            <NSpin size="small" />
          </div>
          <div v-else-if="steamResults.length > 0" class="search-results">
            <div
              v-for="result in steamResults"
              :key="result.id"
              class="search-result-item steam-result"
              :class="{ saving: steamSaving }"
              @click="selectSteamGame(result)"
            >
              <img
                v-if="result.tinyImage"
                :src="result.tinyImage"
                class="steam-thumb"
                loading="lazy"
              />
              <span class="result-name">{{ result.name }}</span>
            </div>
          </div>
          <div v-else-if="!steamSearching && steamQuery.trim()" class="center-state">
            <span class="muted-text">按 Enter 搜索 Steam 游戏</span>
          </div>
        </div>
      </NTabPane>

      <!-- SteamGridDB Search -->
      <NTabPane name="sgdb" tab="SteamGridDB 搜索">
        <div class="search-section">
          <div class="search-bar">
            <NInput
              v-model:value="searchQuery"
              placeholder="搜索游戏名称..."
              size="small"
              @keyup.enter="searchSteamGridDb"
            >
              <template #suffix>
                <NIcon :component="SearchRound" style="cursor: pointer" @click="searchSteamGridDb" />
              </template>
            </NInput>
          </div>

          <!-- Search results list -->
          <div v-if="searching" class="center-state">
            <NSpin size="small" />
          </div>
          <div v-else-if="searchResults.length > 0 && !selectedGameId" class="search-results">
            <div
              v-for="result in searchResults"
              :key="result.id"
              class="search-result-item"
              @click="selectSearchResult(result)"
            >
              <span class="result-name">{{ result.name }}</span>
              <span v-if="result.verified" class="result-verified">✓</span>
            </div>
          </div>

          <!-- Hero images grid -->
          <div v-if="selectedGameId" class="heroes-section">
            <div class="heroes-header">
              <NButton size="tiny" quaternary @click="selectedGameId = null; heroImages = []">
                ← 返回搜索结果
              </NButton>
            </div>
            <div v-if="loadingHeroes" class="center-state">
              <NSpin size="small" />
            </div>
            <div v-else-if="heroImages.length === 0" class="center-state">
              <span class="muted-text">未找到背景图片</span>
            </div>
            <div v-else class="heroes-grid">
              <div
                v-for="image in heroImages"
                :key="image.id"
                class="hero-image-item"
                :class="{ saving: savingBackground }"
                @click="selectHeroImage(image)"
              >
                <img :src="image.thumb" :alt="`Hero ${image.id}`" loading="lazy" />
                <div class="hero-image-overlay">
                  <span>{{ image.width }}×{{ image.height }}</span>
                </div>
              </div>
            </div>
          </div>
        </div>
      </NTabPane>

      <!-- Web Image Search -->
      <NTabPane name="web" tab="网络搜索">
        <WebImageSearchTab :game="game" mode="background" @saved="handleWebSearchSaved" />
      </NTabPane>

      <!-- Local Upload -->
      <NTabPane name="upload" tab="本地上传">
        <div class="upload-section">
          <div class="upload-hint">
            建议使用 16:9 横屏图片，如游戏截图、宣传图或壁纸。
          </div>
          <NButton :loading="uploading" @click="triggerUpload">
            <template #icon><NIcon><CloudUploadOutlined /></NIcon></template>
            选择图片文件
          </NButton>
          <input
            ref="fileInput"
            type="file"
            accept="image/jpeg,image/png,image/webp"
            style="display: none"
            @change="handleFileSelect"
          />
        </div>
      </NTabPane>
    </NTabs>
  </NModal>
</template>

<style scoped>
.search-section {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-height: 300px;
}

.search-bar {
  display: flex;
  gap: 8px;
}

.center-state {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 40px 0;
}

.muted-text {
  font-size: 13px;
  color: var(--text-3);
}

/* Search results */
.search-results {
  display: flex;
  flex-direction: column;
  gap: 2px;
  max-height: 300px;
  overflow-y: auto;
}

.search-result-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 14px;
  border-radius: var(--radius-sm);
  cursor: pointer;
  transition: background 0.15s ease;
}

.search-result-item:hover {
  background: var(--bg-muted);
}

.result-name {
  font-size: 14px;
  color: var(--text-1);
}

.result-verified {
  font-size: 12px;
  color: var(--success);
  font-weight: 600;
}

/* Steam search results */
.steam-result {
  gap: 10px;
}

.steam-result.saving {
  opacity: 0.5;
  pointer-events: none;
}

.steam-thumb {
  width: 120px;
  height: 45px;
  object-fit: cover;
  border-radius: 4px;
  flex-shrink: 0;
}

/* Hero images grid */
.heroes-section {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.heroes-header {
  display: flex;
}

.heroes-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: 10px;
  max-height: 400px;
  overflow-y: auto;
}

.hero-image-item {
  position: relative;
  aspect-ratio: 16 / 5;
  border-radius: var(--radius-sm);
  overflow: hidden;
  cursor: pointer;
  border: 2px solid transparent;
  transition: all 0.2s ease;
}

.hero-image-item:hover {
  border-color: var(--accent);
  transform: scale(1.03);
}

.hero-image-item.saving {
  opacity: 0.5;
  pointer-events: none;
}

.hero-image-item img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.hero-image-overlay {
  position: absolute;
  bottom: 0;
  left: 0;
  right: 0;
  padding: 4px 6px;
  background: rgba(0, 0, 0, 0.7);
  font-size: 10px;
  color: var(--text-2);
  text-align: center;
  opacity: 0;
  transition: opacity 0.2s ease;
}

.hero-image-item:hover .hero-image-overlay {
  opacity: 1;
}

/* Upload section */
.upload-section {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 16px;
  padding: 40px 20px;
}

.upload-hint {
  font-size: 13px;
  color: var(--text-2);
  text-align: center;
  line-height: 1.6;
}

/* Tabs equal width */
:deep(.n-tabs-tab) {
  flex: 1;
  justify-content: center;
}
</style>
