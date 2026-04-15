<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { NAlert, NButton, NEmpty, NSelect, NTag, useMessage } from 'naive-ui'
import type { FontInfo, FontReplacementStatus, ManualTranslationAssetEntry, ReplacementSource } from '@/api/types'
import { fontReplacementApi } from '@/api/games'
import { useFileExplorer } from '@/composables/useFileExplorer'
import { formatBytes } from '@/utils/format'

const props = defineProps<{
  gameId: string
  asset: ManualTranslationAssetEntry
}>()

const emit = defineEmits<{
  (event: 'updated'): void
}>()

const router = useRouter()
const message = useMessage()
const { selectFile } = useFileExplorer()

const loading = ref(false)
const replacing = ref(false)
const restoring = ref(false)
const uploading = ref(false)
const status = ref<FontReplacementStatus | null>(null)
const fontInfo = ref<FontInfo | null>(null)
const selectedSourceId = ref<string | null>(null)

const sources = computed<ReplacementSource[]>(() => {
  if (!status.value || !fontInfo.value)
    return []

  return fontInfo.value.fontType === 'TMP'
    ? status.value.availableSources.tmp
    : status.value.availableSources.ttf
})

const replacementStateText = computed(() => {
  if (!status.value)
    return '未加载'
  if (status.value.isReplaced)
    return '已替换'
  if (status.value.isExternallyRestored)
    return '检测到外部还原'
  return '未替换'
})

watch(
  () => [props.gameId, props.asset.assetId].join('::'),
  () => {
    void load()
  },
  { immediate: true },
)

async function load() {
  if (!props.asset.pathId) {
    fontInfo.value = null
    status.value = null
    return
  }

  loading.value = true
  try {
    const [nextStatus, fonts] = await Promise.all([
      fontReplacementApi.getStatus(props.gameId),
      fontReplacementApi.scan(props.gameId),
    ])

    status.value = nextStatus
    fontInfo.value = fonts.find(font =>
      font.pathId === props.asset.pathId
      && font.assetFile === props.asset.assetFile,
    ) ?? null

    selectedSourceId.value = getPreferredSourceId(selectedSourceId.value)
  }
  catch (error) {
    const messageText = error instanceof Error ? error.message : '加载字体信息失败'
    message.error(messageText)
  }
  finally {
    loading.value = false
  }
}

function getPreferredSourceId(current?: string | null) {
  if (current && sources.value.some(source => source.id === current))
    return current

  return sources.value.find(source => source.isDefault)?.id ?? sources.value[0]?.id ?? null
}

function getFontModeText(font: FontInfo) {
  if (font.fontType === 'TMP')
    return `${font.glyphCount} 个字形 / ${font.characterCount} 个字符`

  switch (font.ttfMode) {
    case 'dynamicEmbedded':
      return '内嵌动态字体，可直接替换'
    case 'osFallback':
      return '系统回退字体，替换时会转成内嵌字体'
    case 'staticAtlas':
      return '静态图集字体，仅展示结构'
    default:
      return '字体结构未识别'
  }
}

async function handleReplace() {
  if (!fontInfo.value || !selectedSourceId.value)
    return

  replacing.value = true
  try {
    await fontReplacementApi.replace(props.gameId, {
      fonts: [{
        pathId: fontInfo.value.pathId,
        assetFile: fontInfo.value.assetFile,
        sourceId: selectedSourceId.value,
      }],
    })
    message.success('字体替换已完成')
    emit('updated')
    await load()
  }
  catch (error) {
    message.error(error instanceof Error ? error.message : '字体替换失败')
  }
  finally {
    replacing.value = false
  }
}

async function handleRestore() {
  restoring.value = true
  try {
    await fontReplacementApi.restore(props.gameId)
    message.success('字体备份已还原')
    emit('updated')
    await load()
  }
  catch (error) {
    message.error(error instanceof Error ? error.message : '还原字体失败')
  }
  finally {
    restoring.value = false
  }
}

async function handleUploadSource() {
  if (!fontInfo.value)
    return

  const filePath = await selectFile({
    title: fontInfo.value.fontType === 'TTF' ? '选择 TTF/OTF 字体' : '选择 TMP 字体资源',
    filters: fontInfo.value.fontType === 'TTF'
      ? [{ label: 'Font Files', extensions: ['.ttf', '.otf'] }]
      : [],
  })

  if (!filePath)
    return

  uploading.value = true
  try {
    await fontReplacementApi.uploadSourceFromPath(props.gameId, filePath, fontInfo.value.fontType)
    message.success('已添加新的字体替换源')
    await load()
  }
  catch (error) {
    message.error(error instanceof Error ? error.message : '上传替换源失败')
  }
  finally {
    uploading.value = false
  }
}
</script>

<template>
  <div class="font-panel">
    <div class="font-panel__header">
      <div>
        <span class="font-panel__eyebrow">字体面板</span>
        <h3 class="font-panel__title">按字体资源替换</h3>
      </div>
      <div class="font-panel__actions">
        <NButton size="small" secondary @click="router.push(`/games/${gameId}/font-replacement`)">
          打开完整字体页
        </NButton>
        <NButton size="small" :loading="loading" @click="load">
          刷新
        </NButton>
      </div>
    </div>

    <div v-if="fontInfo" class="font-panel__body">
      <div class="font-panel__summary">
        <NTag size="small" :bordered="false">{{ fontInfo.fontType }}</NTag>
        <NTag size="small" :bordered="false" :type="status?.isReplaced ? 'success' : 'default'">
          {{ replacementStateText }}
        </NTag>
        <span>{{ getFontModeText(fontInfo) }}</span>
      </div>

      <div class="font-panel__grid">
        <div class="font-panel__stat">
          <span>资源文件</span>
          <strong>{{ fontInfo.assetFile }}</strong>
        </div>
        <div class="font-panel__stat">
          <span>Path ID</span>
          <strong>{{ fontInfo.pathId }}</strong>
        </div>
        <div class="font-panel__stat">
          <span>大小</span>
          <strong>{{ formatBytes(fontInfo.fontDataSize) }}</strong>
        </div>
      </div>

      <NAlert v-if="!fontInfo.replacementSupported" type="warning" :show-icon="false">
        {{ fontInfo.unsupportedReason || '当前字体结构不支持直接替换。' }}
      </NAlert>
      <NAlert v-else type="info" :show-icon="false">
        当前页只处理这个字体资源。还原会恢复整个游戏的字体备份。
      </NAlert>

      <div class="font-panel__controls">
        <div class="font-panel__field">
          <span class="font-panel__label">替换源</span>
          <NSelect
            v-model:value="selectedSourceId"
            :options="sources.map(source => ({ label: source.isDefault ? `${source.displayName} · 默认` : source.displayName, value: source.id }))"
            placeholder="选择替换源"
            :disabled="!fontInfo.replacementSupported"
          />
        </div>

        <div class="font-panel__source-meta" v-if="selectedSourceId">
          <span v-for="source in sources.filter(item => item.id === selectedSourceId)" :key="source.id">
            {{ source.fileName }} · {{ formatBytes(source.fileSize) }}
          </span>
        </div>
      </div>

      <div class="font-panel__footer">
        <NButton size="small" :loading="uploading" @click="handleUploadSource">
          上传新的替换源
        </NButton>
        <NButton
          size="small"
          type="primary"
          :disabled="!fontInfo.replacementSupported || !selectedSourceId"
          :loading="replacing"
          @click="handleReplace"
        >
          替换这个字体
        </NButton>
        <NButton
          size="small"
          secondary
          :disabled="!status?.backupExists"
          :loading="restoring"
          @click="handleRestore"
        >
          还原字体备份
        </NButton>
      </div>
    </div>

    <div v-else class="font-panel__empty">
      <NEmpty
        :description="loading ? '正在扫描字体信息…' : '没有在字体替换工作流中找到这个资源。'"
      />
    </div>
  </div>
</template>

<style scoped>
.font-panel {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.font-panel__header,
.font-panel__actions,
.font-panel__summary,
.font-panel__footer,
.font-panel__source-meta {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}

.font-panel__header {
  justify-content: space-between;
}

.font-panel__eyebrow,
.font-panel__label {
  font-size: 12px;
  color: var(--text-3);
}

.font-panel__title {
  margin: 4px 0 0;
  font-size: 18px;
  color: var(--text-1);
}

.font-panel__body {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.font-panel__summary,
.font-panel__source-meta {
  color: var(--text-2);
  font-size: 13px;
}

.font-panel__grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 12px;
}

.font-panel__stat {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 12px 14px;
  border-radius: var(--radius-md);
  border: 1px solid var(--border);
  background: var(--bg-subtle);
}

.font-panel__stat span {
  font-size: 12px;
  color: var(--text-3);
}

.font-panel__controls {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.font-panel__field {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.font-panel__empty {
  min-height: 200px;
  display: grid;
  place-items: center;
}

@media (max-width: 720px) {
  .font-panel__grid {
    grid-template-columns: 1fr;
  }
}
</style>
