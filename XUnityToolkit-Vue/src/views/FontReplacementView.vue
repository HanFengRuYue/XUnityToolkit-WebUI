<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount, h } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NButton, NDataTable, NIcon, NProgress, NTag,
  useMessage, useDialog, type DataTableColumns,
} from 'naive-ui'
import { ArrowBackOutlined, SearchOutlined, RestoreOutlined, FontDownloadOutlined, CloudUploadOutlined, DeleteOutlineOutlined } from '@vicons/material'
import { api } from '@/api/client'
import { useFileExplorer } from '@/composables/useFileExplorer'
import type {
  Game, FontInfo, FontReplacementRequest, FontReplacementStatus,
  FontReplacementProgress, FontReplacementResult
} from '@/api/types'
import { HubConnectionBuilder, HubConnectionState, type HubConnection } from '@microsoft/signalr'
import { formatBytes } from '@/utils/format'

const route = useRoute()
const router = useRouter()
const message = useMessage()
const dialog = useDialog()
const { selectFile } = useFileExplorer()

const gameId = computed(() => route.params.id as string)
const game = ref<Game | null>(null)
const fonts = ref<FontInfo[]>([])
const checkedRowKeys = ref<string[]>([])
const status = ref<FontReplacementStatus | null>(null)
const scanning = ref(false)
const replacing = ref(false)
const restoring = ref(false)
const progress = ref<FontReplacementProgress | null>(null)

const phaseLabels: Record<string, string> = {
  loading: '加载资源',
  scanning: '扫描中',
  replacing: '替换中',
  saving: '写入文件',
  'clearing-crc': '清除 CRC',
  completed: '已完成'
}

// SignalR connection (created in onMounted)
let connection: HubConnection | null = null

const columns = computed<DataTableColumns<FontInfo>>(() => [
  { type: 'selection', disabled: (row: FontInfo) => !row.isSupported },
  { title: '字体名称', key: 'name', minWidth: 150, ellipsis: { tooltip: true } },
  {
    title: '类型', key: 'fontType', width: 80,
    render: (row) => h(NTag, { size: 'small', bordered: false, type: row.fontType === 'TMP' ? 'info' : 'success' }, { default: () => row.fontType })
  },
  {
    title: '所在文件', key: 'assetFile', ellipsis: { tooltip: true }, width: 220,
    render: (row) => row.assetFile
  },
  {
    title: '图集', key: 'atlas', width: 120,
    render: (row) => row.fontType === 'TMP' && row.atlasWidth > 0 ? `${row.atlasWidth}×${row.atlasHeight}` : '—'
  },
  {
    title: '字形数', key: 'glyphCount', width: 80,
    render: (row) => row.fontType === 'TMP' ? row.glyphCount : '—'
  },
  {
    title: '字符数', key: 'characterCount', width: 80,
    render: (row) => row.fontType === 'TMP' ? row.characterCount : '—'
  },
  {
    title: '大小', key: 'fontDataSize', width: 100,
    render: (row) => row.fontType === 'TTF' ? formatBytes(row.fontDataSize) : '—'
  },
  {
    title: '状态', key: 'isSupported', width: 100,
    render: (row) => row.isSupported ? '支持' : '不支持'
  }
])

const rowClassName = (row: FontInfo) => row.isSupported ? '' : 'font-unsupported'
const selectedCount = computed(() => checkedRowKeys.value.length)

async function loadGame() {
  try {
    game.value = await api.get<Game>(`/api/games/${gameId.value}`)
  } catch (e: any) {
    message.error(e.message)
  }
}

async function loadStatus() {
  try {
    status.value = await api.get<FontReplacementStatus>(
      `/api/games/${gameId.value}/font-replacement/status`)
  } catch { /* ignore */ }
}

async function scanFonts() {
  scanning.value = true
  fonts.value = []
  checkedRowKeys.value = []
  try {
    fonts.value = await api.post<FontInfo[]>(
      `/api/games/${gameId.value}/font-replacement/scan`)
    checkedRowKeys.value = fonts.value
      .filter(f => f.isSupported)
      .map(f => `${f.assetFile}:${f.pathId}`)
    if (fonts.value.length === 0) {
      message.info('未在游戏资产中找到字体')
    } else {
      message.success(`找到 ${fonts.value.length} 个字体`)
    }
  } catch (e: any) {
    message.error(`扫描失败: ${e.message}`)
  } finally {
    scanning.value = false
  }
}

async function doReplace() {
  replacing.value = true
  progress.value = null
  try {
    const request: FontReplacementRequest = {
      fonts: fonts.value
        .filter(f => checkedRowKeys.value.includes(`${f.assetFile}:${f.pathId}`))
        .map(f => ({ pathId: f.pathId, assetFile: f.assetFile }))
    }
    const result = await api.post<FontReplacementResult>(
      `/api/games/${gameId.value}/font-replacement/replace`, request)
    if (result.failedFonts.length > 0) {
      const failedNames = result.failedFonts
        .map(f => `PathId=${f.pathId} (${f.assetFile}): ${f.error}`)
        .join('\n')
      message.warning(
        `字体替换部分完成：成功 ${result.successCount} 个，失败 ${result.failedFonts.length} 个`,
        { duration: 6000 }
      )
      // Font replacement partial failure details available in failedNames
    } else {
      message.success(`字体替换完成：已替换 ${result.successCount} 个字体`)
    }
    await loadStatus()
  } catch (e: any) {
    message.error(`替换失败: ${e.message}`)
  } finally {
    replacing.value = false
    progress.value = null
  }
}

function replaceFonts() {
  if (selectedCount.value === 0) {
    message.warning('请先选择要替换的字体')
    return
  }
  dialog.warning({
    title: '确认替换',
    content: `此操作将修改游戏资产文件中的 ${selectedCount.value} 个字体，是否继续？`,
    positiveText: '替换',
    negativeText: '取消',
    onPositiveClick: () => { doReplace() }
  })
}

async function restoreFonts() {
  dialog.warning({
    title: '确认还原',
    content: '将还原所有被修改的资产文件到原始状态，是否继续？',
    positiveText: '还原',
    negativeText: '取消',
    onPositiveClick: async () => {
      restoring.value = true
      try {
        await api.post(`/api/games/${gameId.value}/font-replacement/restore`)
        message.success('字体已还原')
        status.value = null
        fonts.value = []
        checkedRowKeys.value = []
      } catch (e: any) {
        message.error(`还原失败: ${e.message}`)
      } finally {
        restoring.value = false
      }
    }
  })
}

async function handleSelectCustomFont() {
  const path = await selectFile({ title: '选择自定义字体文件' })
  if (!path) return
  try {
    await api.post(`/api/games/${gameId.value}/font-replacement/upload-from-path`, { filePath: path })
    message.success('自定义字体上传成功')
    loadStatus()
  } catch (e: any) {
    message.error(e.message || '字体上传失败')
  }
}

function handleUploadFinish({ event }: { file: never, event?: ProgressEvent }) {
  const response = (event?.target as XMLHttpRequest)?.response
  try {
    const result = JSON.parse(response)
    if (result.success) {
      message.success('自定义字体上传成功')
      loadStatus()
    } else {
      message.error(result.error || '上传失败')
    }
  } catch {
    message.success('自定义字体上传成功')
    loadStatus()
  }
}

function handleUploadError() {
  message.error('字体上传失败')
}

const hasCustomFont = computed(() =>
  !!(status.value?.customTtfFileName || status.value?.customTmpFileName)
)

async function deleteCustomFont(type: 'ttf' | 'tmp') {
  try {
    await api.del(`/api/games/${gameId.value}/font-replacement/custom-font?type=${type}`)
    message.success('已删除自定义字体')
    await loadStatus()
  } catch (e: any) {
    message.error(`删除失败: ${e.message}`)
  }
}

onMounted(async () => {
  await loadGame()
  await loadStatus()

  try {
    connection = new HubConnectionBuilder()
      .withUrl('/hubs/install')
      .withAutomaticReconnect()
      .build()

    connection.on('fontReplacementProgress', (p: FontReplacementProgress) => {
      progress.value = p
    })

    connection.onreconnected(async () => {
      try { await connection?.invoke('JoinFontReplacementGroup', gameId.value) } catch { /* ignore */ }
    })

    await connection.start()
    await connection.invoke('JoinFontReplacementGroup', gameId.value)
  } catch {
    // SignalR connection failed — silently ignore
  }
})

onBeforeUnmount(async () => {
  try {
    if (connection) {
      if (connection.state === HubConnectionState.Connected) {
        await connection.invoke('LeaveFontReplacementGroup', gameId.value)
      }
      await connection.stop()
    }
  } catch { /* ignore */ }
  connection = null
})
</script>

<template>
  <div v-if="game" class="sub-page">
    <!-- Back Button -->
    <div class="sub-page-header" style="animation-delay: 0s">
      <button class="back-button" @click="router.push(`/games/${gameId}`)">
        <NIcon :size="20"><ArrowBackOutlined /></NIcon>
        <span>{{ game.name }}</span>
      </button>
    </div>

    <!-- Page Title -->
    <h1 class="page-title" style="animation-delay: 0.05s">
      <span class="page-title-icon">
        <NIcon :size="24"><FontDownloadOutlined /></NIcon>
      </span>
      字体替换
    </h1>

    <!-- Status & Custom Font Card -->
    <div v-if="status?.isReplaced || status?.isExternallyRestored || hasCustomFont" class="section-card" style="animation-delay: 0.1s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><FontDownloadOutlined /></NIcon>
          </span>
          当前状态
        </h2>
      </div>
      <div class="status-list">
        <div v-if="status?.isReplaced" class="status-row">
          <NTag type="success" size="small">已替换</NTag>
          <span class="status-text">
            {{ status.replacedFonts.length }} 个字体已替换
            <template v-if="status.fontSource">（{{ status.fontSource }}）</template>
            <template v-if="status.replacedAt">
              · {{ new Date(status.replacedAt).toLocaleDateString() }}
            </template>
          </span>
        </div>
        <div v-else-if="status?.isExternallyRestored" class="status-row">
          <NTag type="warning" size="small">已被外部还原</NTag>
          <span class="status-text">字体可能已通过 Steam 验证文件完整性还原，备份数据仍保留</span>
        </div>
        <div v-if="status?.customTtfFileName" class="status-row">
          <NTag type="success" size="small">TTF 字体</NTag>
          <span class="status-text">{{ status.customTtfFileName }}</span>
          <NButton text size="small" type="error" @click="deleteCustomFont('ttf')" :disabled="replacing">
            <template #icon><NIcon :size="14"><DeleteOutlineOutlined /></NIcon></template>
            删除
          </NButton>
        </div>
        <div v-if="status?.customTmpFileName" class="status-row">
          <NTag type="info" size="small">TMP 资产</NTag>
          <span class="status-text">{{ status.customTmpFileName }}</span>
          <NButton text size="small" type="error" @click="deleteCustomFont('tmp')" :disabled="replacing">
            <template #icon><NIcon :size="14"><DeleteOutlineOutlined /></NIcon></template>
            删除
          </NButton>
        </div>
      </div>
    </div>

    <!-- Scan & Replace Card -->
    <div class="section-card" :style="{ animationDelay: (status?.isReplaced || status?.isExternallyRestored || hasCustomFont) ? '0.15s' : '0.1s' }">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><SearchOutlined /></NIcon>
          </span>
          扫描与替换
        </h2>
        <div class="header-actions">
          <NButton size="small" :disabled="replacing" @click="handleSelectCustomFont">
            <template #icon><NIcon :size="16"><CloudUploadOutlined /></NIcon></template>
            上传自定义字体
          </NButton>
          <NButton size="small" type="primary" :loading="scanning" :disabled="replacing" @click="scanFonts">
            <template #icon><NIcon :size="16"><SearchOutlined /></NIcon></template>
            扫描字体
          </NButton>
        </div>
      </div>

      <!-- Progress -->
      <div v-if="progress" class="progress-section">
        <NProgress
          type="line"
          :percentage="progress.total > 0 ? Math.round(progress.current / progress.total * 100) : 0"
          :status="'default'"
        />
        <span class="progress-text">
          {{ phaseLabels[progress.phase] || progress.phase }}
          {{ progress.currentFile ? `· ${progress.currentFile}` : '' }}
          ({{ progress.current }}/{{ progress.total }})
        </span>
      </div>

      <!-- Empty state when no fonts scanned yet -->
      <div v-if="fonts.length === 0 && !progress" class="empty-hint">
        点击"扫描字体"按钮检测游戏中的字体资源
      </div>
    </div>

    <!-- Font Table Card -->
    <div v-if="fonts.length > 0" class="section-card" style="animation-delay: 0.2s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><FontDownloadOutlined /></NIcon>
          </span>
          字体列表
          <NTag size="small" :bordered="false">{{ fonts.length }} 个</NTag>
        </h2>
        <div class="header-actions">
          <NButton
            size="small"
            type="primary"
            :loading="replacing"
            :disabled="selectedCount === 0 || scanning"
            @click="replaceFonts"
          >
            替换选中 ({{ selectedCount }})
          </NButton>
          <NButton
            v-if="status?.backupExists"
            size="small"
            :loading="restoring"
            :disabled="replacing"
            @click="restoreFonts"
          >
            <template #icon><NIcon :size="16"><RestoreOutlined /></NIcon></template>
            还原所有
          </NButton>
        </div>
      </div>
      <div class="table-container">
        <NDataTable
          :columns="columns"
          :data="fonts"
          :row-key="(row: FontInfo) => `${row.assetFile}:${row.pathId}`"
          v-model:checked-row-keys="checkedRowKeys"
          :row-class-name="rowClassName"
          :max-height="500"
          :bordered="false"
          size="small"
        />
      </div>
    </div>

    <!-- Restore-only Card (when no scan but backup exists) -->
    <div v-else-if="status?.backupExists" class="section-card" style="animation-delay: 0.2s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><RestoreOutlined /></NIcon>
          </span>
          备份还原
        </h2>
        <NButton
          size="small"
          :loading="restoring"
          :disabled="replacing"
          @click="restoreFonts"
        >
          <template #icon><NIcon :size="16"><RestoreOutlined /></NIcon></template>
          还原所有
        </NButton>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* ===== Status ===== */
.status-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.status-row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 14px;
  background: var(--bg-subtle);
  border-radius: var(--radius-md);
  border: 1px solid var(--border);
}

.status-text {
  font-size: 13px;
  color: var(--text-2);
  flex: 1;
}

/* ===== Progress ===== */
.progress-section {
  margin-top: 4px;
}

.progress-text {
  font-size: 12px;
  color: var(--text-3);
  margin-top: 4px;
  display: block;
}

:deep(.font-unsupported) {
  opacity: 0.45;
}

/* ===== Upload Button Alignment ===== */
.header-actions :deep(.n-upload) {
  display: flex;
  align-items: center;
}

.header-actions :deep(.n-upload-trigger) {
  display: flex;
  align-items: center;
}

/* ===== Responsive ===== */
@media (max-width: 768px) {
  .section-header {
    flex-direction: column;
    align-items: flex-start;
    gap: 12px;
  }
  .status-row {
    flex-wrap: wrap;
  }
}

@media (max-width: 480px) {
  .status-row {
    flex-direction: column;
    align-items: flex-start;
    padding: 8px 10px;
    gap: 8px;
  }

  .header-actions {
    width: 100%;
    flex-wrap: wrap;
  }

  .header-actions :deep(.n-upload),
  .header-actions :deep(.n-upload-trigger) {
    width: 100%;
  }

  .header-actions :deep(.n-button) {
    flex: 1;
  }

  :deep(.n-data-table) {
    font-size: 12px;
  }
}
</style>
