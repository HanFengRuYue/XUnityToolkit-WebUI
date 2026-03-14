<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NButton, NDataTable, NIcon, NProgress, NSpace, NTag,
  NUpload, useMessage, useDialog, type DataTableColumns, type UploadFileInfo
} from 'naive-ui'
import { ArrowBackOutlined, SearchOutlined, RestoreOutlined, FontDownloadOutlined, CloudUploadOutlined, DeleteOutlineOutlined } from '@vicons/material'
import { api } from '@/api/client'
import type {
  Game, TmpFontInfo, FontReplacementRequest, FontReplacementStatus,
  FontReplacementProgress, FontReplacementResult
} from '@/api/types'
import { HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr'

const route = useRoute()
const router = useRouter()
const message = useMessage()
const dialog = useDialog()

const gameId = computed(() => route.params.id as string)
const game = ref<Game | null>(null)
const fonts = ref<TmpFontInfo[]>([])
const checkedRowKeys = ref<string[]>([])
const status = ref<FontReplacementStatus | null>(null)
const scanning = ref(false)
const replacing = ref(false)
const restoring = ref(false)
const progress = ref<FontReplacementProgress | null>(null)

// SignalR connection
const connection = new HubConnectionBuilder()
  .withUrl('/hubs/install')
  .withAutomaticReconnect()
  .build()

const columns = computed<DataTableColumns<TmpFontInfo>>(() => [
  { type: 'selection', disabled: (row: TmpFontInfo) => !row.isSupported },
  { title: '字体名称', key: 'name', ellipsis: { tooltip: true } },
  {
    title: '所在文件', key: 'assetFile', ellipsis: { tooltip: true }, width: 240,
    render: (row) => row.assetFile
  },
  {
    title: '图集', key: 'atlas', width: 120,
    render: (row) => row.atlasWidth > 0 ? `${row.atlasWidth}×${row.atlasHeight}` : '-'
  },
  { title: '字形数', key: 'glyphCount', width: 80 },
  { title: '字符数', key: 'characterCount', width: 80 },
  {
    title: '状态', key: 'isSupported', width: 100,
    render: (row) => row.isSupported ? '支持' : '不支持'
  }
])

const rowClassName = (row: TmpFontInfo) => row.isSupported ? '' : 'font-unsupported'
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
    fonts.value = await api.post<TmpFontInfo[]>(
      `/api/games/${gameId.value}/font-replacement/scan`)
    checkedRowKeys.value = fonts.value
      .filter(f => f.isSupported)
      .map(f => `${f.assetFile}:${f.pathId}`)
    if (fonts.value.length === 0) {
      message.info('未在游戏资产中找到 TMP 字体')
    } else {
      message.success(`找到 ${fonts.value.length} 个 TMP 字体`)
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
      console.warn('字体替换失败详情:\n' + failedNames)
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

function handleUploadFinish({ event }: { file: UploadFileInfo, event?: ProgressEvent }) {
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

async function deleteCustomFont() {
  try {
    await api.del(`/api/games/${gameId.value}/font-replacement/custom-font`)
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
    await connection.start()
    await connection.invoke('JoinFontReplacementGroup', gameId.value)
    connection.on('fontReplacementProgress', (p: FontReplacementProgress) => {
      progress.value = p
    })
  } catch (e) {
    console.error('SignalR connection failed:', e)
  }
})

onBeforeUnmount(async () => {
  try {
    if (connection.state === HubConnectionState.Connected) {
      await connection.invoke('LeaveFontReplacementGroup', gameId.value)
      await connection.stop()
    }
  } catch { /* ignore */ }
})
</script>

<template>
  <div v-if="game" class="font-page">
    <!-- Back Button -->
    <div class="page-header" style="animation-delay: 0s">
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
    <div v-if="status?.isReplaced || status?.isExternallyRestored || status?.customFontFileName" class="section-card" style="animation-delay: 0.1s">
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
        <div v-if="status?.customFontFileName" class="status-row">
          <NTag type="info" size="small">自定义字体</NTag>
          <span class="status-text">{{ status.customFontFileName }}</span>
          <NButton text size="small" type="error" @click="deleteCustomFont" :disabled="replacing">
            <template #icon><NIcon :size="14"><DeleteOutlineOutlined /></NIcon></template>
            删除
          </NButton>
        </div>
      </div>
    </div>

    <!-- Scan & Replace Card -->
    <div class="section-card" :style="{ animationDelay: (status?.isReplaced || status?.isExternallyRestored || status?.customFontFileName) ? '0.15s' : '0.1s' }">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><SearchOutlined /></NIcon>
          </span>
          扫描与替换
        </h2>
        <div class="header-btn-group">
          <NUpload
            :action="`/api/games/${gameId}/font-replacement/upload`"
            :show-file-list="false"
            @finish="handleUploadFinish"
            @error="handleUploadError"
            accept="*"
          >
            <NButton size="small" :disabled="replacing">
              <template #icon><NIcon :size="16"><CloudUploadOutlined /></NIcon></template>
              上传自定义字体
            </NButton>
          </NUpload>
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
          {{ progress.phase === 'scanning' ? '扫描中' : progress.phase === 'replacing' ? '替换中' : '清除 CRC' }}
          {{ progress.currentFile ? `· ${progress.currentFile}` : '' }}
          ({{ progress.current }}/{{ progress.total }})
        </span>
      </div>

      <!-- Empty state when no fonts scanned yet -->
      <div v-if="fonts.length === 0 && !progress" class="empty-hint">
        点击"扫描字体"按钮检测游戏中的 TMP 字体资源
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
        <div class="header-btn-group">
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
          :row-key="(row: TmpFontInfo) => `${row.assetFile}:${row.pathId}`"
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
.font-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.page-header {
  display: flex;
  align-items: center;
  margin-bottom: -8px;
  animation: slideUp 0.5s var(--ease-out) backwards;
}

.back-button {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  background: none;
  border: none;
  color: var(--text-3);
  font-size: 13px;
  cursor: pointer;
  padding: 4px 8px;
  border-radius: var(--radius-sm);
  transition: color 0.2s, background 0.2s;
}
.back-button:hover {
  color: var(--text-1);
  background: var(--bg-subtle);
}

.page-title {
  display: flex;
  align-items: center;
  gap: 12px;
  font-family: var(--font-display);
  font-size: 24px;
  font-weight: 600;
  color: var(--text-1);
  margin: 0;
  letter-spacing: -0.03em;
  animation: slideUp 0.5s var(--ease-out) backwards;
}

.page-title-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 38px;
  height: 38px;
  border-radius: 10px;
  background: var(--accent-soft);
  color: var(--accent);
  flex-shrink: 0;
}

/* ===== Section Card ===== */
.section-card {
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  padding: 24px;
  animation: slideUp 0.5s var(--ease-out) backwards;
  transition: border-color 0.3s ease, background 0.3s ease, box-shadow 0.3s ease;
  box-shadow: var(--shadow-card-rest);
}
.section-card:hover {
  border-color: var(--border-hover);
}

.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 16px;
}

.section-title {
  display: flex;
  align-items: center;
  gap: 10px;
  font-family: var(--font-display);
  font-size: 16px;
  font-weight: 600;
  color: var(--text-1);
  margin: 0;
  letter-spacing: -0.01em;
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

.header-btn-group {
  display: flex;
  gap: 8px;
}

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

/* ===== Empty ===== */
.empty-hint {
  font-size: 13px;
  color: var(--text-3);
  text-align: center;
  padding: 24px 0;
}

/* ===== Table ===== */
.table-container {
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  overflow: hidden;
}

:deep(.font-unsupported) {
  opacity: 0.45;
}

/* ===== Animations ===== */
@keyframes slideUp {
  from {
    opacity: 0;
    transform: translateY(12px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

/* ===== Responsive ===== */
@media (max-width: 768px) {
  .section-card {
    padding: 16px;
  }
  .header-btn-group {
    flex-wrap: wrap;
  }
  .section-header {
    flex-direction: column;
    align-items: flex-start;
    gap: 12px;
  }
}

@media (max-width: 480px) {
  .section-card {
    padding: 14px;
    border-radius: var(--radius-md);
  }
  .page-title {
    font-size: 20px;
    gap: 8px;
  }
  .page-title-icon {
    width: 32px;
    height: 32px;
    border-radius: 8px;
  }
}
</style>
