<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NButton, NIcon, NInput, NSelect, NSpin, useMessage } from 'naive-ui'
import {
  ArrowBackOutlined,
  RefreshOutlined,
  FileDownloadOutlined,
  AutoFixHighOutlined,
} from '@vicons/material'
import { bepinexLogApi } from '@/api/games'
import type { BepInExLogAnalysis } from '@/api/types'
import { marked } from 'marked'

defineOptions({ name: 'BepInExLogView' })

const route = useRoute()
const router = useRouter()
const message = useMessage()

const gameId = computed(() => route.params.id as string)

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
  return marked.parse(md, { async: false })
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

onMounted(() => {
  loadLog()
})
</script>

<template>
  <div class="log-viewer-page">
    <!-- Header -->
    <div class="page-header">
      <NButton text @click="router.back()">
        <template #icon><NIcon :size="20"><ArrowBackOutlined /></NIcon></template>
      </NButton>
      <h1 class="page-title">BepInEx 日志</h1>
      <span v-if="fileSize" class="file-meta">
        {{ formatFileSize(fileSize) }}
        <template v-if="lastModified"> · {{ new Date(lastModified).toLocaleString() }}</template>
      </span>
    </div>

    <!-- Toolbar -->
    <div class="toolbar">
      <NInput
        v-model:value="searchQuery"
        placeholder="搜索日志..."
        clearable
        size="small"
        class="search-input"
      />
      <NSelect
        v-model:value="levelFilter"
        :options="levelOptions"
        size="small"
        class="level-select"
      />
      <NButton size="small" @click="loadLog" :loading="loading">
        <template #icon><NIcon><RefreshOutlined /></NIcon></template>
        刷新
      </NButton>
      <NButton size="small" @click="handleExport">
        <template #icon><NIcon><FileDownloadOutlined /></NIcon></template>
        导出
      </NButton>
      <NButton size="small" type="primary" @click="handleAnalyze" :loading="analyzing">
        <template #icon><NIcon><AutoFixHighOutlined /></NIcon></template>
        AI 分析
      </NButton>
    </div>

    <!-- Log Content -->
    <div v-if="loading && !logContent" class="loading-container">
      <NSpin size="large" />
    </div>
    <div v-else-if="!logContent && !loading" class="empty-state">
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
      <div v-if="filteredLines.length === 0 && logContent" class="empty-filter">
        没有匹配的日志条目
      </div>
    </div>

    <!-- AI Analysis Result -->
    <div v-if="analyzing" class="analysis-section">
      <div class="analysis-header">
        <h2 class="section-title">AI 诊断分析</h2>
      </div>
      <div class="analysis-loading">
        <NSpin size="medium" />
        <span>正在分析日志...</span>
      </div>
    </div>
    <div v-else-if="analysisResult" class="analysis-section">
      <div class="analysis-header">
        <h2 class="section-title">AI 诊断分析</h2>
        <span class="analysis-meta">
          由 {{ analysisResult.endpointName }} 生成 · {{ new Date(analysisResult.analyzedAt).toLocaleString() }}
        </span>
      </div>
      <div class="analysis-content markdown-body" v-html="renderMarkdown(analysisResult.report)" />
    </div>
  </div>
</template>

<style scoped>
.log-viewer-page {
  max-width: 1200px;
}

.page-header {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 16px;
}

.page-title {
  font-size: 20px;
  font-weight: 600;
  color: var(--text-1);
  margin: 0;
}

.file-meta {
  font-size: 13px;
  color: var(--text-3);
  margin-left: auto;
}

.toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 16px;
  flex-wrap: wrap;
}

.search-input {
  width: 240px;
}

.level-select {
  width: 120px;
}

.loading-container {
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: 200px;
}

.empty-state,
.empty-filter {
  text-align: center;
  padding: 48px 24px;
  color: var(--text-3);
  font-size: 14px;
}

.log-content {
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: 8px;
  overflow: auto;
  max-height: 600px;
  font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
  font-size: 12px;
  line-height: 1.5;
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
  color: #e74c3c;
}

.log-line.log-warning {
  color: #e6a23c;
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

/* Analysis section */
.analysis-section {
  margin-top: 20px;
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 20px;
}

.analysis-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 16px;
}

.section-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--text-1);
  margin: 0;
}

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
.markdown-body :deep(h3) {
  color: var(--text-1);
  margin-top: 16px;
  margin-bottom: 8px;
}

.markdown-body :deep(h2) {
  font-size: 16px;
  border-bottom: 1px solid var(--border);
  padding-bottom: 6px;
}

.markdown-body :deep(h3) {
  font-size: 14px;
}

.markdown-body :deep(ul),
.markdown-body :deep(ol) {
  padding-left: 20px;
  margin: 8px 0;
}

.markdown-body :deep(li) {
  margin: 4px 0;
}

.markdown-body :deep(code) {
  background: var(--bg-subtle);
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 13px;
}

.markdown-body :deep(pre) {
  background: var(--bg-subtle);
  padding: 12px 16px;
  border-radius: 6px;
  overflow-x: auto;
}

.markdown-body :deep(p) {
  margin: 8px 0;
}

.markdown-body :deep(strong) {
  color: var(--text-1);
}

/* Responsive */
@media (max-width: 768px) {
  .toolbar {
    flex-direction: column;
    align-items: stretch;
  }

  .search-input,
  .level-select {
    width: 100%;
  }

  .log-content {
    max-height: 400px;
  }
}
</style>
