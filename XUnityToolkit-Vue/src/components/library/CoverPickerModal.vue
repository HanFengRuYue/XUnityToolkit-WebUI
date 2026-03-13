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
const gridImages = ref<SteamGridDbImage[]>([])
const loadingGrids = ref(false)
const savingCover = ref(false)

// Upload state
const dragOver = ref(false)
const uploadPreview = ref<string | null>(null)
const uploadFile = ref<File | null>(null)
const uploading = ref(false)

// Auto-search Steam on mount
onMounted(() => {
  if (steamQuery.value.trim()) {
    searchSteam()
  }
})

watch(() => props.game, (game) => {
  steamQuery.value = game.name
  searchQuery.value = game.name
  selectedGameId.value = game.steamGridDbGameId ?? null
  if (game.steamGridDbGameId) {
    loadGrids(game.steamGridDbGameId)
  }
})

async function searchSteam() {
  if (!steamQuery.value.trim()) return
  steamSearching.value = true
  steamResults.value = []
  try {
    steamResults.value = await gamesApi.searchSteamGames(props.game.id, steamQuery.value.trim())
  } catch (e) {
    message.error(e instanceof Error ? e.message : '搜索失败')
  } finally {
    steamSearching.value = false
  }
}

async function selectSteamGame(result: SteamStoreSearchResult) {
  steamSaving.value = true
  try {
    await gamesApi.selectSteamCover(props.game.id, result.id)
    message.success('封面已更新')
    emit('saved')
    emit('update:show', false)
  } catch (e) {
    message.error(e instanceof Error ? e.message : '获取封面失败')
  } finally {
    steamSaving.value = false
  }
}

async function searchSteamGridDb() {
  if (!searchQuery.value.trim()) return
  searching.value = true
  searchResults.value = []
  selectedGameId.value = null
  gridImages.value = []
  try {
    searchResults.value = await gamesApi.searchCovers(props.game.id, searchQuery.value.trim())
  } catch (e) {
    message.error(e instanceof Error ? e.message : '搜索失败')
  } finally {
    searching.value = false
  }
}

async function selectSearchResult(result: SteamGridDbSearchResult) {
  selectedGameId.value = result.id
  await loadGrids(result.id)
}

async function loadGrids(steamGridDbGameId: number) {
  loadingGrids.value = true
  gridImages.value = []
  try {
    gridImages.value = await gamesApi.getCoverGrids(props.game.id, steamGridDbGameId)
  } catch (e) {
    message.error(e instanceof Error ? e.message : '获取封面列表失败')
  } finally {
    loadingGrids.value = false
  }
}

async function selectGridImage(image: SteamGridDbImage) {
  if (!selectedGameId.value) return
  savingCover.value = true
  try {
    await gamesApi.selectCover(props.game.id, image.url, selectedGameId.value)
    message.success('封面已更新')
    emit('saved')
    emit('update:show', false)
  } catch (e) {
    message.error(e instanceof Error ? e.message : '保存封面失败')
  } finally {
    savingCover.value = false
  }
}

function handleFileSelect(e: Event) {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  if (file) prepareUpload(file)
}

function handleDrop(e: DragEvent) {
  dragOver.value = false
  const file = e.dataTransfer?.files?.[0]
  if (file) prepareUpload(file)
}

function prepareUpload(file: File) {
  if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
    message.error('仅支持 JPEG、PNG 或 WebP 格式')
    return
  }
  if (file.size > 5 * 1024 * 1024) {
    message.error('图片文件不能超过 5 MB')
    return
  }
  uploadFile.value = file
  const reader = new FileReader()
  reader.onload = (e) => { uploadPreview.value = e.target?.result as string }
  reader.readAsDataURL(file)
}

async function confirmUpload() {
  if (!uploadFile.value) return
  uploading.value = true
  try {
    await gamesApi.uploadCover(props.game.id, uploadFile.value)
    message.success('封面已更新')
    emit('saved')
    emit('update:show', false)
  } catch (e) {
    message.error(e instanceof Error ? e.message : '上传失败')
  } finally {
    uploading.value = false
  }
}

function clearUpload() {
  uploadFile.value = null
  uploadPreview.value = null
}

function onWebSaved() {
  emit('saved')
  emit('update:show', false)
}

async function deleteCover() {
  try {
    await gamesApi.deleteCover(props.game.id)
    message.success('封面已删除')
    emit('saved')
    emit('update:show', false)
  } catch (e) {
    message.error(e instanceof Error ? e.message : '删除失败')
  }
}
</script>

<template>
  <NModal
    :show="show"
    preset="card"
    title="更换游戏封面"
    style="width: 680px; max-width: 95vw;"
    :mask-closable="true"
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
      <NTabPane name="search" tab="SteamGridDB 搜索">
        <div class="search-section">
          <!-- Search input -->
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

          <!-- Grid images -->
          <div v-if="selectedGameId" class="grids-section">
            <div class="grids-header">
              <NButton size="tiny" quaternary @click="selectedGameId = null; gridImages = []">
                ← 返回搜索结果
              </NButton>
            </div>
            <div v-if="loadingGrids" class="center-state">
              <NSpin size="small" />
            </div>
            <div v-else-if="gridImages.length === 0" class="center-state">
              <span class="muted-text">未找到封面图片</span>
            </div>
            <div v-else class="grids-grid">
              <div
                v-for="image in gridImages"
                :key="image.id"
                class="grid-image-item"
                :class="{ saving: savingCover }"
                @click="selectGridImage(image)"
              >
                <img :src="image.thumb" :alt="`Cover ${image.id}`" loading="lazy" />
                <div class="grid-image-overlay">
                  <span>{{ image.width }}×{{ image.height }}</span>
                </div>
              </div>
            </div>
          </div>
        </div>
      </NTabPane>

      <!-- Web Image Search -->
      <NTabPane name="web" tab="网络搜索">
        <WebImageSearchTab :game="game" mode="cover" @saved="onWebSaved" />
      </NTabPane>

      <!-- Custom Upload -->
      <NTabPane name="upload" tab="自定义上传">
        <div class="upload-section">
          <div v-if="!uploadPreview" class="upload-zone"
            :class="{ 'drag-over': dragOver }"
            @dragover.prevent="dragOver = true"
            @dragleave="dragOver = false"
            @drop.prevent="handleDrop"
            @click="($refs.fileInput as HTMLInputElement)?.click()"
          >
            <NIcon :size="36" color="var(--text-3)">
              <CloudUploadOutlined />
            </NIcon>
            <p class="upload-hint">拖拽图片到此处，或点击选择文件</p>
            <p class="upload-formats">支持 JPEG、PNG、WebP，最大 5 MB</p>
            <input
              ref="fileInput"
              type="file"
              accept="image/jpeg,image/png,image/webp"
              style="display: none"
              @change="handleFileSelect"
            />
          </div>

          <div v-else class="upload-preview">
            <img :src="uploadPreview" alt="Preview" class="preview-img" />
            <div class="preview-actions">
              <NButton size="small" @click="clearUpload">取消</NButton>
              <NButton type="primary" size="small" :loading="uploading" @click="confirmUpload">
                确认上传
              </NButton>
            </div>
          </div>
        </div>
      </NTabPane>
    </NTabs>

    <!-- Footer actions -->
    <template #footer>
      <div class="modal-footer">
        <NButton size="small" type="error" quaternary @click="deleteCover">删除封面</NButton>
      </div>
    </template>
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

/* Grid images */
.grids-section {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.grids-header {
  display: flex;
}

.grids-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(100px, 1fr));
  gap: 10px;
  max-height: 400px;
  overflow-y: auto;
}

.grid-image-item {
  position: relative;
  aspect-ratio: 2 / 3;
  border-radius: var(--radius-sm);
  overflow: hidden;
  cursor: pointer;
  border: 2px solid transparent;
  transition: all 0.2s ease;
}

.grid-image-item:hover {
  border-color: var(--accent);
  transform: scale(1.03);
}

.grid-image-item.saving {
  opacity: 0.5;
  pointer-events: none;
}

.grid-image-item img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.grid-image-overlay {
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

.grid-image-item:hover .grid-image-overlay {
  opacity: 1;
}

/* Upload section */
.upload-section {
  min-height: 250px;
}

.upload-zone {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 48px 20px;
  border: 2px dashed var(--border);
  border-radius: var(--radius-lg);
  cursor: pointer;
  transition: all 0.2s ease;
  gap: 8px;
}

.upload-zone:hover,
.upload-zone.drag-over {
  border-color: var(--accent);
  background: var(--bg-subtle);
}

.upload-hint {
  font-size: 14px;
  color: var(--text-2);
  margin: 0;
}

.upload-formats {
  font-size: 12px;
  color: var(--text-3);
  margin: 0;
}

.upload-preview {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 16px;
  padding: 20px;
}

.preview-img {
  max-width: 200px;
  max-height: 300px;
  border-radius: var(--radius-md);
  object-fit: contain;
  border: 1px solid var(--border);
}

.preview-actions {
  display: flex;
  gap: 10px;
}

/* Tabs equal width */
:deep(.n-tabs-tab) {
  flex: 1;
  justify-content: center;
}

/* Footer */
.modal-footer {
  display: flex;
  justify-content: flex-start;
}
</style>
