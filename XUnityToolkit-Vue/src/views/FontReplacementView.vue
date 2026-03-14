<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NButton, NDataTable, NIcon, NProgress, NSpace, NTag,
  NUpload, useMessage, useDialog, type DataTableColumns, type UploadFileInfo
} from 'naive-ui'
import { ArrowBackOutlined, SearchOutlined, RestoreOutlined, FontDownloadOutlined } from '@vicons/material'
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

function handleBack() {
  router.push(`/games/${gameId.value}`)
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
  <div class="font-replacement-view">
    <!-- Header -->
    <div class="page-header">
      <NButton text @click="handleBack" class="back-btn">
        <template #icon>
          <NIcon :size="20"><ArrowBackOutlined /></NIcon>
        </template>
      </NButton>
      <div class="page-title-area">
        <h1 class="page-title">字体替换</h1>
        <span v-if="game" class="game-name">{{ game.name }}</span>
      </div>
    </div>

    <!-- Operation Bar -->
    <div class="section-card">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><SearchOutlined /></NIcon>
          </span>
          扫描与替换
        </h2>
        <NSpace>
          <NButton type="primary" :loading="scanning" :disabled="replacing" @click="scanFonts">
            扫描字体
          </NButton>
          <NUpload
            :action="`/api/games/${gameId}/font-replacement/upload`"
            :show-file-list="false"
            @finish="handleUploadFinish"
            @error="handleUploadError"
            accept="*"
          >
            <NButton :disabled="replacing">
              上传自定义字体
            </NButton>
          </NUpload>
        </NSpace>
      </div>

      <!-- Status -->
      <div v-if="status?.isReplaced" class="status-bar">
        <NTag type="success">已替换</NTag>
        <span class="status-text">
          {{ status.replacedFonts.length }} 个字体已替换
          <template v-if="status.fontSource">（{{ status.fontSource }}）</template>
          <template v-if="status.replacedAt">
            · {{ new Date(status.replacedAt).toLocaleDateString() }}
          </template>
        </span>
      </div>
      <div v-else-if="status?.isExternallyRestored" class="status-bar">
        <NTag type="warning">已被外部还原</NTag>
        <span class="status-text">字体可能已通过 Steam 验证文件完整性还原，备份数据仍保留</span>
      </div>

      <!-- Custom Font -->
      <div v-if="status?.customFontFileName" class="status-bar">
        <NTag type="info">自定义字体</NTag>
        <span class="status-text">{{ status.customFontFileName }}</span>
        <NButton text size="small" type="error" @click="deleteCustomFont" :disabled="replacing">
          删除
        </NButton>
      </div>

      <!-- Progress -->
      <div v-if="progress" class="progress-bar">
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
    </div>

    <!-- Font Table -->
    <div v-if="fonts.length > 0" class="section-card">
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

    <!-- Bottom Actions -->
    <div v-if="fonts.length > 0 || status?.backupExists" class="section-card action-bar">
      <NSpace align="center" justify="space-between" style="width: 100%">
        <span v-if="fonts.length > 0" class="selected-info">
          已选择 {{ selectedCount }} 个字体
        </span>
        <span v-else />
        <NSpace>
          <NButton
            v-if="fonts.length > 0"
            type="primary"
            :loading="replacing"
            :disabled="selectedCount === 0 || scanning"
            @click="replaceFonts"
          >
            替换选中字体
          </NButton>
          <NButton
            v-if="status?.backupExists"
            :loading="restoring"
            :disabled="replacing"
            @click="restoreFonts"
          >
            <template #icon>
              <NIcon><RestoreOutlined /></NIcon>
            </template>
            还原所有
          </NButton>
        </NSpace>
      </NSpace>
    </div>
  </div>
</template>

<style scoped>
.font-replacement-view {
  padding: 24px 28px;
  max-width: 1200px;
}

.page-header {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 24px;
}

.back-btn {
  color: var(--text-2);
}
.back-btn:hover {
  color: var(--accent);
}

.page-title-area {
  display: flex;
  align-items: baseline;
  gap: 12px;
}

.page-title {
  font-size: 1.5rem;
  font-weight: 700;
  color: var(--text-1);
  margin: 0;
}

.game-name {
  font-size: 0.9rem;
  color: var(--text-3);
}

.section-card {
  background: var(--bg-card);
  border-radius: var(--radius-lg);
  padding: 20px 24px;
  margin-bottom: 16px;
  backdrop-filter: blur(20px);
  border: 1px solid var(--border);
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
  gap: 8px;
  font-size: 1rem;
  font-weight: 600;
  color: var(--text-1);
  margin: 0;
}

.section-icon {
  color: var(--accent);
  display: flex;
  align-items: center;
}

.status-bar {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 0;
}

.status-text {
  font-size: 0.85rem;
  color: var(--text-2);
}

.progress-bar {
  margin-top: 12px;
}

.progress-text {
  font-size: 0.8rem;
  color: var(--text-3);
  margin-top: 4px;
  display: block;
}

.action-bar {
  display: flex;
  align-items: center;
}

.selected-info {
  font-size: 0.85rem;
  color: var(--text-2);
}

:deep(.font-unsupported) {
  opacity: 0.45;
}

@media (max-width: 768px) {
  .font-replacement-view {
    padding: 20px 20px;
  }
}

@media (max-width: 480px) {
  .font-replacement-view {
    padding: 16px 12px;
  }
}
</style>
