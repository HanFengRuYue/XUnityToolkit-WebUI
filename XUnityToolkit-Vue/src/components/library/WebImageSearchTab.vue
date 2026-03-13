<script setup lang="ts">
import { ref } from 'vue'
import { NInput, NButton, NIcon, NSelect, NSpin, useMessage } from 'naive-ui'
import { SearchRound } from '@vicons/material'
import { gamesApi } from '@/api/games'
import type { Game, WebImageResult } from '@/api/types'

const props = defineProps<{
  game: Game
  mode: 'cover' | 'icon' | 'background'
}>()

const emit = defineEmits<{
  saved: []
}>()

const message = useMessage()

const query = ref(props.game.name)
const engine = ref('Bing')
const sizeFilter = ref('auto')
const results = ref<WebImageResult[]>([])
const searching = ref(false)
const saving = ref(false)
const hasSearched = ref(false)
const error = ref<string | null>(null)

const engineOptions = [
  { label: 'Bing', value: 'Bing' },
  { label: 'Google', value: 'Google' },
]

const sizeFilterOptions = [
  { label: '自动', value: 'auto' },
  { label: '大图', value: 'large' },
  { label: '中图', value: 'medium' },
  { label: '小图', value: 'small' },
  { label: '方形', value: 'square' },
  { label: '竖版', value: 'tall' },
  { label: '壁纸', value: 'wallpaper' },
]

async function doSearch() {
  if (!query.value.trim()) return
  searching.value = true
  error.value = null
  results.value = []
  hasSearched.value = true
  try {
    if (props.mode === 'cover') {
      results.value = await gamesApi.searchWebImages(
        props.game.id, query.value.trim(), engine.value, sizeFilter.value)
    } else if (props.mode === 'background') {
      results.value = await gamesApi.searchWebBackgroundImages(
        props.game.id, query.value.trim(), engine.value, sizeFilter.value)
    } else {
      results.value = await gamesApi.searchWebIconImages(
        props.game.id, query.value.trim(), engine.value, sizeFilter.value)
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : '搜索失败'
  } finally {
    searching.value = false
  }
}

async function selectImage(result: WebImageResult) {
  if (saving.value) return
  saving.value = true
  try {
    if (props.mode === 'cover') {
      await gamesApi.selectWebCover(props.game.id, result.fullUrl)
    } else if (props.mode === 'background') {
      await gamesApi.selectWebBackground(props.game.id, result.fullUrl)
    } else {
      await gamesApi.selectWebIcon(props.game.id, result.fullUrl)
    }
    const labels = { cover: '封面已更新', icon: '图标已更新', background: '背景已更新' }
    message.success(labels[props.mode])
    emit('saved')
  } catch (e) {
    message.error(e instanceof Error ? e.message : '保存失败')
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div class="web-search-section">
    <div class="search-controls">
      <NInput
        v-model:value="query"
        placeholder="搜索图片..."
        size="small"
        style="flex: 1"
        @keyup.enter="doSearch"
      >
        <template #suffix>
          <NIcon :component="SearchRound" style="cursor: pointer" @click="doSearch" />
        </template>
      </NInput>
      <NSelect
        v-model:value="engine"
        :options="engineOptions"
        size="small"
        style="width: 95px"
      />
      <NSelect
        v-model:value="sizeFilter"
        :options="sizeFilterOptions"
        size="small"
        style="width: 85px"
      />
    </div>

    <div v-if="searching" class="center-state">
      <NSpin size="small" />
    </div>
    <div v-else-if="error" class="center-state">
      <span class="error-text">{{ error }}</span>
    </div>
    <div v-else-if="results.length === 0 && hasSearched" class="center-state">
      <span class="muted-text">未找到图片</span>
    </div>
    <div v-else-if="results.length === 0" class="center-state">
      <span class="muted-text">输入游戏名称并按 Enter 搜索</span>
    </div>
    <div
      v-else
      class="image-grid"
      :class="mode"
      :style="{ pointerEvents: saving ? 'none' : 'auto', opacity: saving ? 0.5 : 1 }"
    >
      <div
        v-for="(r, i) in results"
        :key="i"
        class="image-item"
        @click="selectImage(r)"
      >
        <img :src="r.thumbUrl" :alt="r.title || ''" loading="lazy" />
        <div v-if="r.width && r.height" class="dim-overlay">
          {{ r.width }}×{{ r.height }}
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.web-search-section {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-height: 300px;
}

.search-controls {
  display: flex;
  gap: 8px;
  align-items: center;
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

.error-text {
  font-size: 13px;
  color: var(--error);
}

.image-grid {
  display: grid;
  gap: 10px;
  max-height: 400px;
  overflow-y: auto;
}

.image-grid.cover {
  grid-template-columns: repeat(auto-fill, minmax(100px, 1fr));
}

.image-grid.icon {
  grid-template-columns: repeat(auto-fill, minmax(72px, 1fr));
}

.image-grid.background {
  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
}

.image-item {
  position: relative;
  border-radius: var(--radius-sm);
  overflow: hidden;
  cursor: pointer;
  border: 2px solid transparent;
  transition: all 0.2s ease;
}

.image-grid.cover .image-item {
  aspect-ratio: 2 / 3;
}

.image-grid.icon .image-item {
  aspect-ratio: 1 / 1;
}

.image-grid.background .image-item {
  aspect-ratio: 16 / 9;
}

.image-item:hover {
  border-color: var(--accent);
  transform: scale(1.03);
}

.image-item img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.dim-overlay {
  position: absolute;
  bottom: 0;
  left: 0;
  right: 0;
  padding: 3px 5px;
  background: rgba(0, 0, 0, 0.7);
  font-size: 10px;
  color: var(--text-2);
  text-align: center;
  opacity: 0;
  transition: opacity 0.2s ease;
}

.image-item:hover .dim-overlay {
  opacity: 1;
}
</style>
