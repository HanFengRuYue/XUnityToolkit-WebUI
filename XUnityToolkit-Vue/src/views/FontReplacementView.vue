<script setup lang="ts">
import { computed, h, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NAlert,
  NButton,
  NDataTable,
  NEmpty,
  NIcon,
  NProgress,
  NSelect,
  NTag,
  useDialog,
  useMessage,
  type DataTableColumns,
  type SelectOption,
} from 'naive-ui'
import {
  ArrowBackOutlined,
  CloudUploadOutlined,
  DeleteOutlineOutlined,
  FontDownloadOutlined,
  RestoreOutlined,
  SearchOutlined,
} from '@vicons/material'
import { HubConnectionBuilder, HubConnectionState, type HubConnection } from '@microsoft/signalr'
import { api } from '@/api/client'
import { useFileExplorer } from '@/composables/useFileExplorer'
import { formatBytes } from '@/utils/format'
import type {
  FontInfo,
  FontReplacementProgress,
  FontReplacementRequest,
  FontReplacementResult,
  FontReplacementStatus,
  Game,
  ReplacementSource,
} from '@/api/types'

const route = useRoute()
const router = useRouter()
const message = useMessage()
const dialog = useDialog()
const { selectFile } = useFileExplorer()

const gameId = computed(() => route.params.id as string)
const game = ref<Game | null>(null)
const status = ref<FontReplacementStatus | null>(null)
const fonts = ref<FontInfo[]>([])
const checkedTmpRowKeys = ref<string[]>([])
const checkedTtfRowKeys = ref<string[]>([])
const selectedSourceIds = ref<Record<string, string>>({})
const scanning = ref(false)
const replacing = ref(false)
const restoring = ref(false)
const progress = ref<FontReplacementProgress | null>(null)
const tmpBatchSourceId = ref<string | null>(null)
const ttfBatchSourceId = ref<string | null>(null)

const phaseLabels: Record<string, string> = {
  loading: '加载资源',
  scanning: '扫描中',
  replacing: '替换中',
  saving: '写入文件',
  'clearing-crc': '更新 CRC',
  completed: '已完成',
}

let connection: HubConnection | null = null

function fontKey(font: FontInfo): string {
  return `${font.assetFile}:${font.pathId}`
}

function getSourcesByKind(kind: 'TMP' | 'TTF'): ReplacementSource[] {
  if (!status.value) return []
  return kind === 'TMP' ? status.value.availableSources.tmp : status.value.availableSources.ttf
}

function getDefaultSourceId(kind: 'TMP' | 'TTF'): string | null {
  const sources = getSourcesByKind(kind)
  return sources.find(source => source.isDefault)?.id ?? sources[0]?.id ?? null
}

function getPreferredSourceId(kind: 'TMP' | 'TTF', current?: string | null): string | null {
  const sources = getSourcesByKind(kind)
  if (current && sources.some(source => source.id === current)) {
    return current
  }

  return getDefaultSourceId(kind)
}

function canReplaceRow(font: FontInfo): boolean {
  return font.replacementSupported && !!getPreferredSourceId(font.fontType)
}

function getStatusText(font: FontInfo): string {
  if (!font.replacementSupported) {
    return font.unsupportedReason ?? '不支持替换'
  }

  if (!getPreferredSourceId(font.fontType)) {
    return '当前没有可用的替换源'
  }

  return '可替换'
}

function getReplacementStateLabel(): string {
  if (status.value?.isReplaced) return '已替换'
  if (status.value?.isExternallyRestored) return '检测到外部还原'
  return '未替换'
}

function getReplacementStateClass(): string {
  if (status.value?.isReplaced) return 'status-chip--success'
  if (status.value?.isExternallyRestored) return 'status-chip--warning'
  return 'status-chip--neutral'
}

function getBackupStateLabel(): string {
  return status.value?.backupExists ? '可恢复' : '无备份'
}

function getBackupStateClass(): string {
  return status.value?.backupExists ? 'status-chip--success' : 'status-chip--neutral'
}

function getTtfModeLabel(mode: FontInfo['ttfMode']): string {
  switch (mode) {
    case 'dynamicEmbedded':
      return '内嵌动态字体'
    case 'staticAtlas':
      return '静态图集'
    case 'osFallback':
      return '系统回退'
    case 'unknown':
      return '未知'
    default:
      return '未识别'
  }
}

function getTtfDetails(font: FontInfo): string {
  const parts: string[] = []
  if (font.characterRectCount > 0) parts.push(`Rects ${font.characterRectCount}`)
  if (font.fontNamesCount > 0) parts.push(`Names ${font.fontNamesCount}`)
  if (font.hasTextureRef) parts.push('Texture')
  return parts.length > 0 ? parts.join(' / ') : '无额外结构'
}

function formatTime(value?: string | null): string {
  if (!value) return '未记录'
  return new Date(value).toLocaleString()
}

function formatSourceTime(source: ReplacementSource): string {
  return source.uploadedAt ? new Date(source.uploadedAt).toLocaleString() : '内置'
}

function buildSourceOptions(kind: 'TMP' | 'TTF'): SelectOption[] {
  return getSourcesByKind(kind).map(source => ({
    label: source.isDefault ? `${source.displayName} · 默认` : source.displayName,
    value: source.id,
  }))
}

function setFontSource(font: FontInfo, sourceId: string | null) {
  if (!sourceId) return
  selectedSourceIds.value = {
    ...selectedSourceIds.value,
    [fontKey(font)]: sourceId,
  }
}

function reconcileSelections() {
  if (!fonts.value.length) {
    tmpBatchSourceId.value = getPreferredSourceId('TMP', tmpBatchSourceId.value)
    ttfBatchSourceId.value = getPreferredSourceId('TTF', ttfBatchSourceId.value)
    return
  }

  const nextMap: Record<string, string> = { ...selectedSourceIds.value }
  for (const font of fonts.value) {
    const preferredSourceId = getPreferredSourceId(font.fontType, nextMap[fontKey(font)])
    if (preferredSourceId) {
      nextMap[fontKey(font)] = preferredSourceId
    } else {
      delete nextMap[fontKey(font)]
    }
  }

  selectedSourceIds.value = nextMap
  tmpBatchSourceId.value = getPreferredSourceId('TMP', tmpBatchSourceId.value)
  ttfBatchSourceId.value = getPreferredSourceId('TTF', ttfBatchSourceId.value)
}

function selectSupportedRows() {
  checkedTmpRowKeys.value = fonts.value
    .filter(font => font.fontType === 'TMP' && canReplaceRow(font))
    .map(fontKey)
  checkedTtfRowKeys.value = fonts.value
    .filter(font => font.fontType === 'TTF' && canReplaceRow(font))
    .map(fontKey)
}

const tmpFonts = computed(() => fonts.value.filter(font => font.fontType === 'TMP'))
const ttfFonts = computed(() => fonts.value.filter(font => font.fontType === 'TTF'))
const tmpSourceOptions = computed(() => buildSourceOptions('TMP'))
const ttfSourceOptions = computed(() => buildSourceOptions('TTF'))
const tmpCustomSources = computed(() => getSourcesByKind('TMP').filter(source => !source.isDefault))
const ttfCustomSources = computed(() => getSourcesByKind('TTF').filter(source => !source.isDefault))

const tmpSelectedCount = computed(() => checkedTmpRowKeys.value.length)
const ttfSelectedCount = computed(() => checkedTtfRowKeys.value.length)
const totalSelectedCount = computed(() => tmpSelectedCount.value + ttfSelectedCount.value)
const usedSourceSummary = computed(() => {
  if (!status.value) return '尚未执行替换'
  if (status.value.usedSources.length > 0) return status.value.usedSources.join('、')
  return status.value.fontSource || '尚未执行替换'
})

function getSelectedFonts(kind?: 'TMP' | 'TTF'): FontInfo[] {
  const keys = new Set(
    kind === 'TMP'
      ? checkedTmpRowKeys.value
      : kind === 'TTF'
        ? checkedTtfRowKeys.value
        : [...checkedTmpRowKeys.value, ...checkedTtfRowKeys.value],
  )

  return fonts.value.filter(font => keys.has(fontKey(font)) && canReplaceRow(font))
}

function buildReplaceRequest(kind?: 'TMP' | 'TTF'): FontReplacementRequest {
  const targets = getSelectedFonts(kind).flatMap((font) => {
    const sourceId = selectedSourceIds.value[fontKey(font)]
    if (!sourceId) {
      return []
    }

    return [{
      pathId: font.pathId,
      assetFile: font.assetFile,
      sourceId,
    }]
  })

  return { fonts: targets }
}

function applyBatchSource(kind: 'TMP' | 'TTF') {
  const sourceId = kind === 'TMP' ? tmpBatchSourceId.value : ttfBatchSourceId.value
  if (!sourceId) {
    message.warning('请先选择要批量应用的替换源')
    return
  }

  const targets = getSelectedFonts(kind)
  if (targets.length === 0) {
    message.warning('当前分区没有已选中的可替换字体')
    return
  }

  const nextMap = { ...selectedSourceIds.value }
  for (const font of targets) {
    nextMap[fontKey(font)] = sourceId
  }
  selectedSourceIds.value = nextMap
  message.success(`已为 ${targets.length} 个${kind === 'TMP' ? ' TMP ' : ' TTF '}字体应用新的替换源`)
}

async function loadGame() {
  try {
    game.value = await api.get<Game>(`/api/games/${gameId.value}`)
  } catch (error: any) {
    message.error(error.message)
  }
}

async function loadStatus() {
  try {
    status.value = await api.get<FontReplacementStatus>(
      `/api/games/${gameId.value}/font-replacement/status`,
    )
    reconcileSelections()
  } catch {
    status.value = null
  }
}

async function scanFonts() {
  scanning.value = true
  fonts.value = []
  checkedTmpRowKeys.value = []
  checkedTtfRowKeys.value = []
  progress.value = null

  try {
    fonts.value = await api.post<FontInfo[]>(`/api/games/${gameId.value}/font-replacement/scan`)
    reconcileSelections()
    selectSupportedRows()

    if (fonts.value.length === 0) {
      message.info('未在游戏资源中找到可识别的字体资源')
    } else {
      message.success(`扫描完成，共找到 ${fonts.value.length} 个字体资源`)
    }
  } catch (error: any) {
    message.error(`扫描失败: ${error.message}`)
  } finally {
    scanning.value = false
  }
}

async function doReplace(kind?: 'TMP' | 'TTF') {
  const request = buildReplaceRequest(kind)
  if (request.fonts.length === 0) {
    message.warning('没有可执行替换的字体')
    return
  }

  replacing.value = true
  progress.value = null

  try {
    const result = await api.post<FontReplacementResult>(
      `/api/games/${gameId.value}/font-replacement/replace`,
      request,
    )

    if (result.failedFonts.length > 0) {
      message.warning(
        `替换已完成，成功 ${result.successCount} 个，失败 ${result.failedFonts.length} 个`,
        { duration: 6000 },
      )
    } else {
      message.success(`替换完成，共处理 ${result.successCount} 个字体`)
    }

    await loadStatus()
  } catch (error: any) {
    message.error(`替换失败: ${error.message}`)
  } finally {
    replacing.value = false
    progress.value = null
  }
}

function replaceFonts(kind?: 'TMP' | 'TTF') {
  const targets = getSelectedFonts(kind)
  if (targets.length === 0) {
    message.warning('请先选择要替换的字体')
    return
  }

  const scopeLabel = kind === 'TMP' ? 'TMP 字体' : kind === 'TTF' ? 'TTF 字体' : '已选字体'
  dialog.warning({
    title: '确认替换',
    content: `将使用当前设置的替换源处理 ${targets.length} 个${scopeLabel}，是否继续？`,
    positiveText: '开始替换',
    negativeText: '取消',
    onPositiveClick: () => {
      void doReplace(kind)
    },
  })
}

function restoreFonts() {
  dialog.warning({
    title: '确认还原',
    content: '将把已经修改过的字体资源恢复到备份状态，是否继续？',
    positiveText: '还原',
    negativeText: '取消',
    onPositiveClick: async () => {
      restoring.value = true
      try {
        await api.post(`/api/games/${gameId.value}/font-replacement/restore`)
        message.success('字体资源已还原')
        fonts.value = []
        checkedTmpRowKeys.value = []
        checkedTtfRowKeys.value = []
        selectedSourceIds.value = {}
        await loadStatus()
      } catch (error: any) {
        message.error(`还原失败: ${error.message}`)
      } finally {
        restoring.value = false
      }
    },
  })
}

async function uploadSource(kind: 'TMP' | 'TTF') {
  const path = await selectFile({
    title: kind === 'TTF' ? '选择 TTF/OTF 字体' : '选择 TMP 字体资源',
    filters: kind === 'TTF'
      ? [{ label: 'Font Files', extensions: ['.ttf', '.otf'] }]
      : [],
  })

  if (!path) return

  try {
    await api.post(`/api/games/${gameId.value}/font-replacement/upload-from-path`, {
      filePath: path,
      kind: kind.toLowerCase(),
    })
    message.success(`已添加${kind === 'TTF' ? ' TTF/OTF ' : ' TMP '}替换源`)
    await loadStatus()
  } catch (error: any) {
    message.error(error.message || '上传失败')
  }
}

async function deleteSource(source: ReplacementSource) {
  dialog.warning({
    title: '删除替换源',
    content: `确认删除替换源“${source.displayName}”？`,
    positiveText: '删除',
    negativeText: '取消',
    onPositiveClick: async () => {
      try {
        await api.del(`/api/games/${gameId.value}/font-replacement/custom-fonts/${encodeURIComponent(source.id)}`)
        message.success('替换源已删除')
        await loadStatus()
        reconcileSelections()
      } catch (error: any) {
        message.error(`删除失败: ${error.message}`)
      }
    },
  })
}

function renderSourceSelect(font: FontInfo) {
  return h(NSelect, {
    value: selectedSourceIds.value[fontKey(font)] ?? null,
    options: font.fontType === 'TMP' ? tmpSourceOptions.value : ttfSourceOptions.value,
    disabled: !canReplaceRow(font) || replacing.value,
    size: 'small',
    style: { width: '220px' },
    placeholder: '选择替换源',
    onUpdateValue: (value: string | null) => setFontSource(font, value),
  })
}

const tmpColumns = computed<DataTableColumns<FontInfo>>(() => [
  {
    type: 'selection',
    disabled: (row: FontInfo) => !canReplaceRow(row),
  },
  {
    title: '字体名称',
    key: 'name',
    minWidth: 180,
    ellipsis: { tooltip: true },
  },
  {
    title: '所在文件',
    key: 'assetFile',
    minWidth: 220,
    ellipsis: { tooltip: true },
  },
  {
    title: '图集',
    key: 'atlas',
    width: 120,
    render: (row) => row.atlasWidth > 0 ? `${row.atlasWidth} × ${row.atlasHeight}` : '—',
  },
  {
    title: '字形数',
    key: 'glyphCount',
    width: 90,
  },
  {
    title: '字符数',
    key: 'characterCount',
    width: 90,
  },
  {
    title: '替换为',
    key: 'replaceSource',
    width: 250,
    render: (row) => renderSourceSelect(row),
  },
  {
    title: '状态',
    key: 'status',
    minWidth: 220,
    ellipsis: { tooltip: true },
    render: (row) =>
      h(
        NTag,
        {
          size: 'small',
          bordered: false,
          type: canReplaceRow(row) ? 'success' : 'warning',
        },
        {
          default: () => getStatusText(row),
        },
      ),
  },
])

const ttfColumns = computed<DataTableColumns<FontInfo>>(() => [
  {
    type: 'selection',
    disabled: (row: FontInfo) => !canReplaceRow(row),
  },
  {
    title: '字体名称',
    key: 'name',
    minWidth: 180,
    ellipsis: { tooltip: true },
  },
  {
    title: 'TTF 模式',
    key: 'ttfMode',
    width: 140,
    render: (row) =>
      h(
        NTag,
        {
          size: 'small',
          bordered: false,
          type: row.ttfMode === 'dynamicEmbedded' ? 'success' : 'warning',
        },
        { default: () => getTtfModeLabel(row.ttfMode) },
      ),
  },
  {
    title: '所在文件',
    key: 'assetFile',
    minWidth: 220,
    ellipsis: { tooltip: true },
  },
  {
    title: '内嵌大小',
    key: 'fontDataSize',
    width: 120,
    render: (row) => row.fontDataSize > 0 ? formatBytes(row.fontDataSize) : '0 B',
  },
  {
    title: '结构说明',
    key: 'ttfDetails',
    width: 180,
    render: (row) => getTtfDetails(row),
  },
  {
    title: '替换为',
    key: 'replaceSource',
    width: 250,
    render: (row) => renderSourceSelect(row),
  },
  {
    title: '状态',
    key: 'status',
    minWidth: 240,
    ellipsis: { tooltip: true },
    render: (row) =>
      h(
        NTag,
        {
          size: 'small',
          bordered: false,
          type: canReplaceRow(row) ? 'success' : 'warning',
        },
        {
          default: () => getStatusText(row),
        },
      ),
  },
])

const rowClassName = (row: FontInfo) => (canReplaceRow(row) ? '' : 'font-disabled')

onMounted(async () => {
  await loadGame()
  await loadStatus()

  try {
    connection = new HubConnectionBuilder()
      .withUrl('/hubs/install')
      .withAutomaticReconnect()
      .build()

    connection.on('fontReplacementProgress', (payload: FontReplacementProgress) => {
      progress.value = payload
    })

    connection.onreconnected(async () => {
      try {
        await connection?.invoke('JoinFontReplacementGroup', gameId.value)
      } catch {
        // Ignore reconnect errors.
      }
    })

    await connection.start()
    await connection.invoke('JoinFontReplacementGroup', gameId.value)
  } catch {
    // Ignore SignalR connection failures.
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
  } catch {
    // Ignore shutdown errors.
  }
  connection = null
})
</script>

<template>
  <div v-if="game" class="sub-page">
    <div class="sub-page-header" style="animation-delay: 0s">
      <button class="back-button" @click="router.push(`/games/${gameId}`)">
        <NIcon :size="20"><ArrowBackOutlined /></NIcon>
        <span>{{ game.name }}</span>
      </button>
    </div>

    <h1 class="page-title" style="animation-delay: 0.05s">
      <span class="page-title-icon">
        <NIcon :size="24"><FontDownloadOutlined /></NIcon>
      </span>
      字体替换
    </h1>

    <div class="section-card" style="animation-delay: 0.1s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><RestoreOutlined /></NIcon>
          </span>
          当前状态 / 恢复
        </h2>
        <div class="header-actions">
          <NButton
            size="small"
            :loading="restoring"
            :disabled="!status?.backupExists || replacing"
            @click="restoreFonts"
          >
            <template #icon><NIcon :size="16"><RestoreOutlined /></NIcon></template>
            还原备份
          </NButton>
        </div>
      </div>

      <div class="status-grid">
        <div class="status-block">
          <span class="status-label">替换状态</span>
          <div class="status-value">
            <span class="status-chip" :class="getReplacementStateClass()">
              {{ getReplacementStateLabel() }}
            </span>
            <span class="status-text">
              {{
                status?.isReplaced
                  ? `上次替换时间 ${formatTime(status.replacedAt)}`
                  : status?.isExternallyRestored
                    ? '资源文件可能已由外部工具或平台验证恢复'
                    : '当前没有执行中的替换记录'
              }}
            </span>
          </div>
        </div>

        <div class="status-block">
          <span class="status-label">已使用替换源</span>
          <div class="status-value">
            <span class="status-text">{{ usedSourceSummary }}</span>
          </div>
        </div>

        <div class="status-block">
          <span class="status-label">备份状态</span>
          <div class="status-value">
            <span class="status-chip" :class="getBackupStateClass()">
              {{ getBackupStateLabel() }}
            </span>
            <span class="status-text">
              {{ status?.backupExists ? '已检测到字体替换备份文件' : '执行替换后会自动创建备份' }}
            </span>
          </div>
        </div>
      </div>
    </div>

    <div class="section-card" style="animation-delay: 0.14s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><CloudUploadOutlined /></NIcon>
          </span>
          替换源字体库
        </h2>
        <div class="header-actions">
          <NButton size="small" :disabled="replacing" @click="uploadSource('TTF')">
            <template #icon><NIcon :size="16"><CloudUploadOutlined /></NIcon></template>
            上传 TTF/OTF
          </NButton>
          <NButton size="small" :disabled="replacing" @click="uploadSource('TMP')">
            <template #icon><NIcon :size="16"><CloudUploadOutlined /></NIcon></template>
            上传 TMP 资产
          </NButton>
        </div>
      </div>

      <div class="source-library">
        <section class="source-group">
          <div class="source-group-header">
            <h3>TMP 替换源</h3>
            <span class="source-group-meta">{{ getSourcesByKind('TMP').length }} 个</span>
          </div>

          <div v-if="getSourcesByKind('TMP').length > 0" class="source-list">
            <div
              v-for="source in getSourcesByKind('TMP')"
              :key="source.id"
              class="source-item"
            >
              <div class="source-main">
                <div class="source-title-line">
                  <span class="source-title">{{ source.displayName }}</span>
                  <NTag size="small" :bordered="false" :type="source.isDefault ? 'success' : 'default'">
                    {{ source.isDefault ? '默认' : '自定义' }}
                  </NTag>
                </div>
                <div class="source-meta">
                  <span>{{ source.fileName }}</span>
                  <span>{{ formatBytes(source.fileSize) }}</span>
                  <span>{{ formatSourceTime(source) }}</span>
                </div>
              </div>

              <NButton
                v-if="!source.isDefault"
                text
                size="small"
                type="error"
                @click="deleteSource(source)"
              >
                <template #icon><NIcon :size="14"><DeleteOutlineOutlined /></NIcon></template>
                删除
              </NButton>
            </div>
          </div>
          <NEmpty v-else size="small" description="没有可用的 TMP 替换源" />
        </section>

        <section class="source-group">
          <div class="source-group-header">
            <h3>TTF / OTF 替换源</h3>
            <span class="source-group-meta">{{ getSourcesByKind('TTF').length }} 个</span>
          </div>

          <div v-if="getSourcesByKind('TTF').length > 0" class="source-list">
            <div
              v-for="source in getSourcesByKind('TTF')"
              :key="source.id"
              class="source-item"
            >
              <div class="source-main">
                <div class="source-title-line">
                  <span class="source-title">{{ source.displayName }}</span>
                  <NTag size="small" :bordered="false" :type="source.isDefault ? 'success' : 'default'">
                    {{ source.isDefault ? '默认' : '自定义' }}
                  </NTag>
                </div>
                <div class="source-meta">
                  <span>{{ source.fileName }}</span>
                  <span>{{ formatBytes(source.fileSize) }}</span>
                  <span>{{ formatSourceTime(source) }}</span>
                </div>
              </div>

              <NButton
                v-if="!source.isDefault"
                text
                size="small"
                type="error"
                @click="deleteSource(source)"
              >
                <template #icon><NIcon :size="14"><DeleteOutlineOutlined /></NIcon></template>
                删除
              </NButton>
            </div>
          </div>
          <NEmpty v-else size="small" description="没有可用的 TTF / OTF 替换源" />
        </section>
      </div>
    </div>

    <div class="section-card" style="animation-delay: 0.18s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><SearchOutlined /></NIcon>
          </span>
          扫描结果
        </h2>
        <div class="header-actions">
          <NButton
            size="small"
            type="primary"
            :loading="scanning"
            :disabled="replacing"
            @click="scanFonts"
          >
            <template #icon><NIcon :size="16"><SearchOutlined /></NIcon></template>
            扫描字体
          </NButton>
        </div>
      </div>

      <div v-if="progress" class="progress-section">
        <NProgress
          type="line"
          :percentage="progress.total > 0 ? Math.round((progress.current / progress.total) * 100) : 0"
        />
        <span class="progress-text">
          {{ phaseLabels[progress.phase] || progress.phase }}
          {{ progress.currentFile ? ` · ${progress.currentFile}` : '' }}
          ({{ progress.current }}/{{ progress.total }})
        </span>
      </div>

      <div v-if="fonts.length === 0 && !progress" class="empty-state">
        点击“扫描字体”后，这里会按 TMP 和 TTF / Legacy Font 分开展示扫描结果。
      </div>
    </div>

    <div v-if="fonts.length > 0" class="section-card" style="animation-delay: 0.22s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><FontDownloadOutlined /></NIcon>
          </span>
          TMP 字体
          <NTag size="small" :bordered="false">{{ tmpFonts.length }}</NTag>
        </h2>
        <div class="header-actions group-actions">
          <NSelect
            v-model:value="tmpBatchSourceId"
            size="small"
            :options="tmpSourceOptions"
            placeholder="批量设置替换源"
            style="width: 220px"
          />
          <NButton size="small" :disabled="tmpSelectedCount === 0" @click="applyBatchSource('TMP')">
            应用到已选
          </NButton>
          <NButton
            size="small"
            type="primary"
            :disabled="tmpSelectedCount === 0 || replacing || scanning"
            @click="replaceFonts('TMP')"
          >
            替换已选 ({{ tmpSelectedCount }})
          </NButton>
        </div>
      </div>

      <div v-if="tmpFonts.length > 0" class="table-container">
        <NDataTable
          v-model:checked-row-keys="checkedTmpRowKeys"
          :columns="tmpColumns"
          :data="tmpFonts"
          :row-key="fontKey"
          :row-class-name="rowClassName"
          :max-height="420"
          :bordered="false"
          size="small"
        />
      </div>
      <div v-else class="empty-state compact">
        没有扫描到 TMP 字体资源。
      </div>
    </div>

    <div v-if="fonts.length > 0" class="section-card" style="animation-delay: 0.26s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><FontDownloadOutlined /></NIcon>
          </span>
          TTF / Legacy Font
          <NTag size="small" :bordered="false">{{ ttfFonts.length }}</NTag>
        </h2>
        <div class="header-actions group-actions">
          <NSelect
            v-model:value="ttfBatchSourceId"
            size="small"
            :options="ttfSourceOptions"
            placeholder="批量设置替换源"
            style="width: 220px"
          />
          <NButton size="small" :disabled="ttfSelectedCount === 0" @click="applyBatchSource('TTF')">
            应用到已选
          </NButton>
          <NButton
            size="small"
            type="primary"
            :disabled="ttfSelectedCount === 0 || replacing || scanning"
            @click="replaceFonts('TTF')"
          >
            替换已选 ({{ ttfSelectedCount }})
          </NButton>
        </div>
      </div>

      <NAlert type="info" :show-icon="false" class="mode-note">
        只有“内嵌动态字体”模式支持直接替换。静态图集字体、系统回退字体以及未知模式会保留展示，但不可勾选。
      </NAlert>

      <div v-if="ttfFonts.length > 0" class="table-container">
        <NDataTable
          v-model:checked-row-keys="checkedTtfRowKeys"
          :columns="ttfColumns"
          :data="ttfFonts"
          :row-key="fontKey"
          :row-class-name="rowClassName"
          :max-height="420"
          :bordered="false"
          size="small"
        />
      </div>
      <div v-else class="empty-state compact">
        没有扫描到 TTF / Legacy Font 资源。
      </div>
    </div>

    <div v-if="fonts.length > 0" class="section-card" style="animation-delay: 0.3s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><FontDownloadOutlined /></NIcon>
          </span>
          执行替换
        </h2>
        <div class="header-actions">
          <NButton
            size="small"
            type="primary"
            :loading="replacing"
            :disabled="totalSelectedCount === 0 || scanning"
            @click="replaceFonts()"
          >
            替换全部已选 ({{ totalSelectedCount }})
          </NButton>
        </div>
      </div>

      <div class="selection-summary">
        <div class="summary-item">
          <span class="summary-label">TMP 已选</span>
          <span class="summary-value">{{ tmpSelectedCount }}</span>
        </div>
        <div class="summary-item">
          <span class="summary-label">TTF 已选</span>
          <span class="summary-value">{{ ttfSelectedCount }}</span>
        </div>
        <div class="summary-item">
          <span class="summary-label">自定义 TMP 源</span>
          <span class="summary-value">{{ tmpCustomSources.length }}</span>
        </div>
        <div class="summary-item">
          <span class="summary-label">自定义 TTF 源</span>
          <span class="summary-value">{{ ttfCustomSources.length }}</span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.status-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 14px;
}

.status-block {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 14px 16px;
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  background: var(--bg-subtle);
}

.status-label {
  font-size: 12px;
  color: var(--text-3);
}

.status-value {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.status-chip {
  display: inline-flex;
  align-items: center;
  align-self: flex-start;
  gap: 8px;
  padding: 5px 11px;
  border-radius: 999px;
  border: 1px solid var(--border);
  background: var(--bg-muted);
  color: var(--text-2);
  font-size: 12px;
  font-weight: 600;
  line-height: 1;
}

.status-chip::before {
  content: '';
  width: 6px;
  height: 6px;
  border-radius: 999px;
  background: currentColor;
  opacity: 0.9;
}

.status-chip--neutral {
  background: var(--bg-muted);
  color: var(--text-2);
}

.status-chip--success {
  background: color-mix(in srgb, var(--success) 12%, transparent);
  border-color: color-mix(in srgb, var(--success) 28%, var(--border));
  color: var(--success);
}

.status-chip--warning {
  background: color-mix(in srgb, var(--warning) 12%, transparent);
  border-color: color-mix(in srgb, var(--warning) 28%, var(--border));
  color: var(--warning);
}

.status-text {
  font-size: 13px;
  color: var(--text-2);
  line-height: 1.5;
}

.source-library {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
}

.source-group {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 0;
}

.source-group-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.source-group-header h3 {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
  color: var(--text-1);
}

.source-group-meta {
  font-size: 12px;
  color: var(--text-3);
}

.source-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.source-item {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 14px;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--bg-subtle);
}

.source-main {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 0;
  flex: 1;
}

.source-title-line {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.source-title {
  font-size: 14px;
  font-weight: 600;
  color: var(--text-1);
  word-break: break-all;
}

.source-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  font-size: 12px;
  color: var(--text-3);
}

.progress-section {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.progress-text {
  font-size: 12px;
  color: var(--text-3);
}

.mode-note {
  margin-bottom: 14px;
}

.group-actions {
  flex-wrap: wrap;
}

.selection-summary {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
}

.summary-item {
  padding: 12px 14px;
  border-radius: var(--radius-md);
  border: 1px solid var(--border);
  background: var(--bg-subtle);
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.summary-label {
  font-size: 12px;
  color: var(--text-3);
}

.summary-value {
  font-size: 18px;
  font-weight: 700;
  color: var(--text-1);
}

.empty-state {
  padding: 18px 4px 2px;
  color: var(--text-3);
  font-size: 13px;
}

.empty-state.compact {
  padding-top: 4px;
}

:deep(.font-disabled) {
  opacity: 0.52;
}

@media (max-width: 1100px) {
  .status-grid,
  .source-library,
  .selection-summary {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 768px) {
  .status-grid,
  .source-library,
  .selection-summary {
    grid-template-columns: 1fr;
  }

  .source-item {
    flex-direction: column;
    align-items: flex-start;
  }
}
</style>
