<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NButton, NIcon, NInput, NSelect, NSpin, useMessage } from 'naive-ui'
import {
  ArrowBackOutlined,
  RefreshOutlined,
  FileDownloadOutlined,
  AutoFixHighOutlined,
  TerminalOutlined,
  SearchOutlined,
} from '@vicons/material'
import { bepinexLogApi, gamesApi } from '@/api/games'
import type { BepInExLogAnalysis, Game } from '@/api/types'
import { marked } from 'marked'

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
const analysisResult = ref<BepInExLogAnalysis | null>(null)
const searchQuery = ref('')
const levelFilter = ref<string>('All')

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

// File size formatting
function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

// Load log
async function loadLog() {
  loading.value = true
  try {
    const resp = await bepinexLogApi.get(gameId.value)
    logContent.value = resp.content
    fileSize.value = resp.fileSize
    lastModified.value = resp.lastModified
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
  analysisResult.value = null
  try {
    const result = await bepinexLogApi.analyze(gameId.value)
    analysisResult.value = result
  } catch (e: unknown) {
    const msg = e instanceof Error ? e.message : 'AI 分析失败'
    message.error(msg)
  } finally {
    analyzing.value = false
  }
}

// Render markdown
function renderMarkdown(md: string): string {
  return marked.parse(md, { async: false }) as string
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
  loadLog()
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
        {{ formatFileSize(fileSize) }}
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
          <NButton size="small" type="primary" @click="handleAnalyze" :loading="analyzing" :disabled="!logContent">
            <template #icon><NIcon><AutoFixHighOutlined /></NIcon></template>
            AI 分析
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
            v-for="(line, idx) in filteredLines"
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

    <!-- AI Analysis Card -->
    <div v-if="analyzing || analysisResult" class="section-card" style="animation-delay: 0.15s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><AutoFixHighOutlined /></NIcon>
          </span>
          AI 诊断分析
        </h2>
        <span v-if="analysisResult" class="analysis-meta">
          由 {{ analysisResult.endpointName }} 生成 · {{ new Date(analysisResult.analyzedAt).toLocaleString() }}
        </span>
      </div>

      <div v-if="analyzing" class="analysis-loading">
        <NSpin size="medium" />
        <span>正在分析日志...</span>
      </div>
      <div v-else-if="analysisResult" class="analysis-content markdown-body" v-html="renderMarkdown(analysisResult.report)" />
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

/* Analysis */
.analysis-meta {
  font-size: 12px;
  color: var(--text-3);
}

.analysis-loading {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 24px;
  justify-content: center;
  color: var(--text-3);
}

.analysis-content {
  color: var(--text-1);
  line-height: 1.7;
}

/* Markdown body styles */
.markdown-body :deep(h1),
.markdown-body :deep(h2),
.markdown-body :deep(h3),
.markdown-body :deep(h4) {
  color: var(--text-1);
  margin-top: 16px;
  margin-bottom: 8px;
}

.markdown-body :deep(h1) {
  font-size: 20px;
}

.markdown-body :deep(h2) {
  font-size: 16px;
  border-bottom: 1px solid var(--border);
  padding-bottom: 6px;
}

.markdown-body :deep(h3) {
  font-size: 14px;
}

.markdown-body :deep(h4) {
  font-size: 13px;
}

.markdown-body :deep(ul),
.markdown-body :deep(ol) {
  padding-left: 20px;
  margin: 8px 0;
}

.markdown-body :deep(li) {
  margin: 4px 0;
  line-height: 1.6;
}

.markdown-body :deep(li > ul),
.markdown-body :deep(li > ol) {
  margin: 4px 0;
}

.markdown-body :deep(code) {
  background: var(--bg-subtle);
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 13px;
  font-family: var(--font-mono);
}

.markdown-body :deep(pre) {
  background: var(--bg-subtle);
  padding: 12px 16px;
  border-radius: 6px;
  overflow-x: auto;
  margin: 8px 0;
}

.markdown-body :deep(pre code) {
  background: none;
  padding: 0;
  font-size: 12px;
}

.markdown-body :deep(p) {
  margin: 8px 0;
}

.markdown-body :deep(strong) {
  color: var(--text-1);
}

.markdown-body :deep(em) {
  color: var(--text-2);
}

.markdown-body :deep(blockquote) {
  margin: 8px 0;
  padding: 8px 16px;
  border-left: 3px solid var(--accent);
  background: var(--bg-subtle);
  border-radius: 0 var(--radius-sm) var(--radius-sm) 0;
  color: var(--text-2);
}

.markdown-body :deep(blockquote p) {
  margin: 4px 0;
}

.markdown-body :deep(hr) {
  border: none;
  border-top: 1px solid var(--border);
  margin: 16px 0;
}

.markdown-body :deep(table) {
  width: 100%;
  border-collapse: collapse;
  margin: 8px 0;
  font-size: 13px;
}

.markdown-body :deep(th),
.markdown-body :deep(td) {
  padding: 8px 12px;
  border: 1px solid var(--border);
  text-align: left;
}

.markdown-body :deep(th) {
  background: var(--bg-subtle);
  font-weight: 600;
  color: var(--text-1);
}

.markdown-body :deep(tr:nth-child(even)) {
  background: color-mix(in srgb, var(--bg-subtle) 50%, transparent);
}

.markdown-body :deep(a) {
  color: var(--accent);
  text-decoration: none;
}

.markdown-body :deep(a:hover) {
  text-decoration: underline;
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
}
</style>
