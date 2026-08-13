<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { formatBytes } from '@/utils/format'
import { useRoute, useRouter } from 'vue-router'
import { NAlert, NButton, NIcon, NInput, NSelect, NSpin, useMessage } from 'naive-ui'
import {
  ArrowBackOutlined,
  RefreshOutlined,
  FileDownloadOutlined,
  AutoFixHighOutlined,
  TerminalOutlined,
  SearchOutlined,
} from '@vicons/material'
import { bepinexLogApi, gamesApi, pluginHealthApi } from '@/api/games'
import type { Game, PluginHealthReport } from '@/api/types'
import PluginDiagnosticReport from '@/components/health/PluginDiagnosticReport.vue'

defineOptions({ name: 'BepInExLogView' })

const route = useRoute()
const router = useRouter()
const message = useMessage()

const gameId = computed(() => route.params.id as string)
const game = ref<Game | null>(null)

// State
const logContent = ref('')
const fileSize = ref(0)
const lastModified = ref('')
const loading = ref(false)
const analyzing = ref(false)
const healthReport = ref<PluginHealthReport | null>(null)
const searchQuery = ref('')
const levelFilter = ref<string>('All')
const loadedTailLines = 5000
const maxRenderedLines = 1000

// Level filter options
const levelOptions = [
  { label: '全部', value: 'All' },
  { label: 'Info', value: 'Info' },
  { label: 'Warning', value: 'Warning' },
  { label: 'Error', value: 'Error' },
]

// BepInEx log line format: [Level  : Source] Message
// Level can be: Info, Warning, Error, Fatal, Debug, Message
const logLevelRegex = /^\[(Info|Warning|Error|Fatal|Debug|Message)\s*:/i

interface LogLine {
  text: string
  level: string
}

// Parse log lines with level detection
const parsedLines = computed<LogLine[]>(() => {
  if (!logContent.value) return []
  const raw = logContent.value.split('\n')
  const result: LogLine[] = []
  let currentLevel = 'Info'

  for (const line of raw) {
    const match = line.match(logLevelRegex)
    if (match && match[1]) {
      currentLevel = match[1]
    }
    result.push({ text: line, level: currentLevel })
  }
  return result
})

// Filtered lines
const filteredLines = computed(() => {
  let lines = parsedLines.value

  // Level filter (Error includes Fatal)
  if (levelFilter.value !== 'All') {
    const filterVal = levelFilter.value.toLowerCase()
    lines = lines.filter((l) => {
      const lvl = l.level.toLowerCase()
      if (filterVal === 'error') return lvl === 'error' || lvl === 'fatal'
      return lvl === filterVal
    })
  }

  // Search filter
  if (searchQuery.value) {
    const q = searchQuery.value.toLowerCase()
    lines = lines.filter((l) => l.text.toLowerCase().includes(q))
  }

  return lines
})

const visibleLines = computed(() => {
  const lines = filteredLines.value
  return lines.length > maxRenderedLines ? lines.slice(-maxRenderedLines) : lines
})

const hiddenFilteredLineCount = computed(() => Math.max(0, filteredLines.value.length - visibleLines.value.length))

// Level to CSS class
function levelClass(level: string): string {
  switch (level.toLowerCase()) {
    case 'error':
    case 'fatal':
      return 'log-error'
    case 'warning':
      return 'log-warning'
    default:
      return 'log-info'
  }
}

// Load log
async function loadLog() {
  loading.value = true
  try {
    const resp = await bepinexLogApi.get(gameId.value, loadedTailLines)
    logContent.value = resp.content
    fileSize.value = resp.fileSize
    lastModified.value = resp.lastModified
    await loadHealthReport()
  } catch (e: unknown) {
    const msg = e instanceof Error ? e.message : '加载日志失败'
    message.error(msg)
  } finally {
    loading.value = false
  }
}

// Export / download
async function handleExport() {
  try {
    const url = bepinexLogApi.getDownloadUrl(gameId.value)
    const resp = await fetch(url)
    if (!resp.ok) throw new Error(`HTTP ${resp.status}`)
    const blob = await resp.blob()
    const blobUrl = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = blobUrl
    a.download = 'LogOutput.log'
    a.click()
    setTimeout(() => URL.revokeObjectURL(blobUrl), 1000)
  } catch (e: unknown) {
    const msg = e instanceof Error ? e.message : '导出失败'
    message.error(msg)
  }
}

// AI analysis
async function handleAnalyze() {
  analyzing.value = true
  try {
    healthReport.value = await pluginHealthApi.analyze(gameId.value)
    if (healthReport.value.analysisState === 'Completed') {
      message.success('AI 智能诊断已完成')
    } else {
      message.warning(healthReport.value.analysisMessage || '本地检查已完成，但 AI 诊断未完成')
    }
  } catch (e: unknown) {
    const msg = e instanceof Error ? e.message : 'AI 智能诊断失败'
    message.error(msg)
  } finally {
    analyzing.value = false
  }
}

async function loadHealthReport() {
  try {
    healthReport.value = await pluginHealthApi.check(gameId.value)
  } catch {
    // The log viewer remains usable even if the local health snapshot cannot be read.
  }
}

// Highlight search matches in text
function highlightText(text: string): string {
  if (!searchQuery.value) return escapeHtml(text)
  const escaped = escapeHtml(text)
  const q = escapeHtml(searchQuery.value)
  const regex = new RegExp(`(${q.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')})`, 'gi')
  return escaped.replace(regex, '<mark class="search-highlight">$1</mark>')
}

function escapeHtml(str: string): string {
  return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
}

onMounted(async () => {
  try {
    game.value = await gamesApi.get(gameId.value)
  } catch { /* ignore */ }
  await loadLog()
  if (!healthReport.value) await loadHealthReport()
})
</script>

<template>
  <div class="sub-page">
    <!-- Back navigation -->
    <div class="sub-page-header" style="animation-delay: 0s">
      <button class="back-button" @click="router.push(`/games/${gameId}`)">
        <NIcon :size="20"><ArrowBackOutlined /></NIcon>
        <span>{{ game?.name ?? '...' }}</span>
      </button>
    </div>

    <h1 class="page-title" style="animation-delay: 0.05s">
      <span class="page-title-icon">
        <NIcon :size="24"><TerminalOutlined /></NIcon>
      </span>
      BepInEx 日志
      <span v-if="fileSize" class="file-meta">
        {{ formatBytes(fileSize) }}
        <template v-if="lastModified"> · {{ new Date(lastModified).toLocaleString() }}</template>
      </span>
    </h1>

    <!-- Log Viewer Card -->
    <div class="section-card" style="animation-delay: 0.1s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><SearchOutlined /></NIcon>
          </span>
          日志查看
        </h2>
        <div class="header-actions">
          <NButton size="small" @click="loadLog" :loading="loading">
            <template #icon><NIcon><RefreshOutlined /></NIcon></template>
            刷新
          </NButton>
          <NButton size="small" @click="handleExport" :disabled="!logContent">
            <template #icon><NIcon><FileDownloadOutlined /></NIcon></template>
            导出
          </NButton>
          <NButton size="small" type="primary" @click="handleAnalyze" :loading="analyzing">
            <template #icon><NIcon><AutoFixHighOutlined /></NIcon></template>
            AI 智能诊断
          </NButton>
        </div>
      </div>

      <!-- Search & Filter -->
      <div class="filter-row">
        <NInput
          v-model:value="searchQuery"
          placeholder="搜索日志..."
          clearable
          size="small"
          class="search-input"
        >
          <template #prefix><NIcon :size="16"><SearchOutlined /></NIcon></template>
        </NInput>
        <NSelect
          v-model:value="levelFilter"
          :options="levelOptions"
          size="small"
          class="level-select"
        />
      </div>

      <!-- Log Content -->
      <div v-if="loading && !logContent" class="log-loading">
        <NSpin size="large" />
      </div>
      <div v-else-if="!logContent && !loading" class="log-empty">
        BepInEx 日志文件为空或不存在。请确认游戏已运行过至少一次。
      </div>
      <div v-else class="log-content">
        <div class="log-lines">
          <div
            v-if="hiddenFilteredLineCount > 0"
            class="log-line log-info"
          >
            已省略 {{ hiddenFilteredLineCount }} 条较早匹配日志，仅显示最后 {{ maxRenderedLines }} 条
          </div>
          <div
            v-for="(line, idx) in visibleLines"
            :key="idx"
            class="log-line"
            :class="levelClass(line.level)"
            v-html="highlightText(line.text)"
          />
        </div>
        <div v-if="filteredLines.length === 0 && logContent" class="log-empty">
          没有匹配的日志条目
        </div>
      </div>
    </div>

    <!-- Unified plugin diagnostic report -->
    <div v-if="analyzing || healthReport" class="section-card" style="animation-delay: 0.15s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><AutoFixHighOutlined /></NIcon>
          </span>
          插件智能诊断
        </h2>
      </div>

      <NAlert type="info" :bordered="false" class="analysis-cost-hint">
        智能诊断会先选择关键日志与配置，再进行第二阶段证据分析，可能产生模型 API 费用。
      </NAlert>

      <div v-if="analyzing" class="analysis-loading">
        <NSpin size="medium" />
        <span>正在选择关键资料并生成结构化诊断...</span>
      </div>
      <PluginDiagnosticReport v-else-if="healthReport" :report="healthReport" />
    </div>
  </div>
</template>

<style scoped>
.file-meta {
  font-size: 13px;
  font-weight: 400;
  color: var(--text-3);
  margin-left: auto;
}

.filter-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
}

.search-input {
  flex: 1;
  max-width: 280px;
}

.level-select {
  width: 120px;
}

.log-loading {
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: 200px;
}

.log-empty {
  text-align: center;
  padding: 48px 24px;
  color: var(--text-3);
  font-size: 14px;
}

.log-content {
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  overflow: auto;
  max-height: 600px;
  font-family: var(--font-mono);
  font-size: 12px;
  line-height: 1.6;
}

.log-lines {
  padding: 12px 16px;
}

.log-line {
  white-space: pre-wrap;
  word-break: break-all;
  padding: 1px 0;
}

.log-line.log-error {
  color: var(--danger);
}

.log-line.log-warning {
  color: var(--warning);
}

.log-line.log-info {
  color: var(--text-2);
}

/* Search highlight */
.log-line :deep(.search-highlight) {
  background: rgba(255, 213, 79, 0.4);
  border-radius: 2px;
  padding: 0 1px;
}

.analysis-loading {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 24px;
  justify-content: center;
  color: var(--text-3);
}

/* Responsive */
@media (max-width: 768px) {
  .filter-row {
    flex-direction: column;
    align-items: stretch;
  }

  .search-input {
    max-width: none;
  }

  .level-select {
    width: 100%;
  }

  .log-content {
    max-height: 400px;
  }

  .file-meta {
    display: block;
    margin-left: 0;
    margin-top: 4px;
    font-size: 12px;
  }

}

@media (max-width: 480px) {
  .log-lines {
    padding: 8px 10px;
  }

  .log-content {
    font-size: 11px;
    max-height: 350px;
  }
}
</style>
