<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted, nextTick } from 'vue'
import { NButton, NIcon, NInput, NCheckbox, NCheckboxGroup, NSwitch } from 'naive-ui'
import {
  ArticleOutlined,
  FilterListOutlined,
  SearchOutlined,
  FileDownloadOutlined,
  DeleteOutlined,
  HistoryOutlined,
  VerticalAlignBottomOutlined,
} from '@vicons/material'
import { useLogStore } from '@/stores/log'
import { logsApi } from '@/api/games'

const logStore = useLogStore()

const selectedLevels = ref(['INF', 'WRN', 'ERR', 'CRI'])
const keyword = ref('')
const autoScroll = ref(true)
const loading = ref(false)
const historyLoading = ref(false)
const containerRef = ref<HTMLElement | null>(null)

const levelOptions = [
  { label: 'INFO', value: 'INF' },
  { label: 'WARN', value: 'WRN' },
  { label: 'ERROR', value: 'ERR' },
  { label: 'CRITICAL', value: 'CRI' },
]

const filteredEntries = computed(() => {
  const levels = selectedLevels.value
  const kw = keyword.value.toLowerCase()
  return logStore.entries.filter(e => {
    if (!levels.includes(e.level)) return false
    if (kw && !e.message.toLowerCase().includes(kw) && !e.category.toLowerCase().includes(kw)) return false
    return true
  })
})

function scrollToBottom() {
  const el = containerRef.value
  if (el) el.scrollTop = el.scrollHeight
}

function handleScroll() {
  const el = containerRef.value
  if (!el) return
  const nearBottom = el.scrollTop + el.clientHeight >= el.scrollHeight - 50
  autoScroll.value = nearBottom
}

watch(() => logStore.entries.length, async () => {
  if (autoScroll.value) {
    await nextTick()
    scrollToBottom()
  }
})

async function handleLoadHistory() {
  historyLoading.value = true
  try {
    await logStore.fetchHistory()
  } finally {
    historyLoading.value = false
  }
  if (autoScroll.value) {
    await nextTick()
    scrollToBottom()
  }
}

function handleExport() {
  window.open(logsApi.getDownloadUrl(), '_blank')
}

function handleClear() {
  logStore.clearLocal()
}

onMounted(async () => {
  loading.value = true
  try {
    await logStore.fetchRecent()
    await logStore.connect()
  } finally {
    loading.value = false
  }
  await nextTick()
  scrollToBottom()
})

onUnmounted(() => {
  logStore.disconnect()
})

function levelClass(level: string) {
  switch (level) {
    case 'ERR': case 'CRI': return 'level-err'
    case 'WRN': return 'level-wrn'
    case 'DBG': case 'TRC': return 'level-dbg'
    default: return 'level-inf'
  }
}
</script>

<template>
  <div class="log-page">
    <h1 class="page-title" style="animation-delay: 0s">
      <span class="page-title-icon">
        <NIcon :size="24"><ArticleOutlined /></NIcon>
      </span>
      运行日志
    </h1>

    <!-- Toolbar -->
    <div class="section-card toolbar-card" style="animation-delay: 0.05s">
      <div class="toolbar">
        <div class="toolbar-left">
          <div class="toolbar-group">
            <NIcon :size="16" color="var(--text-3)"><FilterListOutlined /></NIcon>
            <NCheckboxGroup v-model:value="selectedLevels" class="level-filter">
              <NCheckbox v-for="opt in levelOptions" :key="opt.value" :value="opt.value" :label="opt.label" />
            </NCheckboxGroup>
          </div>
          <div class="toolbar-group search-group">
            <NInput
              v-model:value="keyword"
              placeholder="搜索日志..."
              clearable
              size="small"
            >
              <template #prefix>
                <NIcon :size="14"><SearchOutlined /></NIcon>
              </template>
            </NInput>
          </div>
        </div>
        <div class="toolbar-right">
          <div class="auto-scroll-toggle">
            <span class="toggle-label">自动滚动</span>
            <NSwitch v-model:value="autoScroll" size="small" />
          </div>
          <NButton size="small" quaternary :loading="historyLoading" @click="handleLoadHistory">
            <template #icon><NIcon><HistoryOutlined /></NIcon></template>
            加载历史
          </NButton>
          <NButton size="small" quaternary @click="handleExport">
            <template #icon><NIcon><FileDownloadOutlined /></NIcon></template>
            导出
          </NButton>
          <NButton size="small" quaternary @click="handleClear">
            <template #icon><NIcon><DeleteOutlined /></NIcon></template>
            清空
          </NButton>
        </div>
      </div>
    </div>

    <!-- Log Container -->
    <div class="section-card log-section" style="animation-delay: 0.1s">
      <div ref="containerRef" class="log-container" @scroll="handleScroll">
        <div v-if="loading" class="log-empty">加载中...</div>
        <div v-else-if="filteredEntries.length === 0" class="log-empty">暂无日志</div>
        <div
          v-for="(entry, i) in filteredEntries"
          :key="`${entry.timestamp}-${i}`"
          class="log-row"
          :class="levelClass(entry.level)"
        >
          <span class="log-time">{{ entry.timestamp.substring(11) }}</span>
          <span class="log-level" :class="levelClass(entry.level)">{{ entry.level }}</span>
          <span class="log-category">{{ entry.category }}</span>
          <span class="log-message">{{ entry.message }}</span>
        </div>
      </div>
      <div class="log-footer">
        <span class="log-count">{{ filteredEntries.length }} / {{ logStore.entries.length }} 条日志</span>
        <NButton
          v-if="!autoScroll"
          size="tiny"
          quaternary
          @click="autoScroll = true; scrollToBottom()"
        >
          <template #icon><NIcon :size="14"><VerticalAlignBottomOutlined /></NIcon></template>
          滚动到底部
        </NButton>
      </div>
    </div>
  </div>
</template>

<style scoped>
.log-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
  animation: fadeIn 0.3s ease;
  height: calc(100vh - 72px);
}

.page-title {
  font-family: var(--font-display);
  font-size: 30px;
  font-weight: 600;
  color: var(--text-1);
  margin-bottom: 0;
  letter-spacing: -0.03em;
  animation: slideUp 0.4s var(--ease-out) backwards;
  display: flex;
  align-items: center;
  gap: 14px;
  flex-shrink: 0;
}

.page-title-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 42px;
  height: 42px;
  border-radius: 12px;
  background: var(--accent-soft);
  color: var(--accent);
  flex-shrink: 0;
}

/* ===== Section Card ===== */
.section-card {
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  animation: slideUp 0.5s var(--ease-out) backwards;
  transition: border-color 0.3s ease, background 0.3s ease, box-shadow 0.3s ease;
  box-shadow: var(--shadow-card-rest);
}

.section-card:hover {
  border-color: var(--border-hover);
}

/* ===== Toolbar ===== */
.toolbar-card {
  padding: 12px 20px;
  flex-shrink: 0;
}

.toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
}

.toolbar-left {
  display: flex;
  align-items: center;
  gap: 16px;
  flex-wrap: wrap;
}

.toolbar-right {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.toolbar-group {
  display: flex;
  align-items: center;
  gap: 8px;
}

.level-filter {
  display: flex;
  gap: 12px;
}

.search-group {
  min-width: 180px;
}

.auto-scroll-toggle {
  display: flex;
  align-items: center;
  gap: 6px;
}

.toggle-label {
  font-size: 12px;
  color: var(--text-3);
  white-space: nowrap;
}

/* ===== Log Container ===== */
.log-section {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  padding: 0;
  overflow: hidden;
}

.log-container {
  flex: 1;
  overflow-y: auto;
  padding: 8px 0;
  font-family: var(--font-mono);
  font-size: 12px;
  line-height: 1.7;
}

.log-empty {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 200px;
  color: var(--text-3);
  font-family: var(--font-body);
  font-size: 14px;
}

.log-row {
  display: flex;
  gap: 8px;
  padding: 1px 16px;
  white-space: nowrap;
  transition: background 0.1s ease;
}

.log-row:hover {
  background: var(--bg-subtle);
}

.log-row.level-err {
  background: color-mix(in srgb, var(--danger) 5%, transparent);
}

.log-row.level-err:hover {
  background: color-mix(in srgb, var(--danger) 10%, transparent);
}

.log-row.level-wrn {
  background: color-mix(in srgb, var(--warning) 3%, transparent);
}

.log-row.level-wrn:hover {
  background: color-mix(in srgb, var(--warning) 8%, transparent);
}

.log-time {
  color: var(--text-3);
  flex-shrink: 0;
}

.log-level {
  flex-shrink: 0;
  width: 28px;
  text-align: center;
  font-weight: 600;
}

.log-level.level-inf {
  color: var(--accent);
}

.log-level.level-wrn {
  color: var(--warning);
}

.log-level.level-err {
  color: var(--danger);
}

.log-level.level-dbg {
  color: var(--text-3);
}

.log-category {
  color: var(--text-3);
  flex-shrink: 0;
  max-width: 160px;
  overflow: hidden;
  text-overflow: ellipsis;
}

.log-message {
  color: var(--text-1);
  white-space: pre-wrap;
  word-break: break-all;
  min-width: 0;
}

/* ===== Log Footer ===== */
.log-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 6px 16px;
  border-top: 1px solid var(--border);
  flex-shrink: 0;
}

.log-count {
  font-size: 12px;
  color: var(--text-3);
  font-family: var(--font-mono);
}

/* ===== Responsive ===== */
@media (max-width: 768px) {
  .log-page {
    height: auto;
    min-height: calc(100vh - 120px);
  }

  .toolbar {
    flex-direction: column;
    align-items: stretch;
  }

  .toolbar-left,
  .toolbar-right {
    flex-wrap: wrap;
  }

  .toolbar-card {
    padding: 12px 14px;
  }

  .log-row {
    padding: 1px 10px;
    font-size: 11px;
  }

  .search-group {
    min-width: 140px;
  }
}

@media (max-width: 480px) {
  .page-title {
    font-size: 22px;
    gap: 10px;
  }

  .page-title-icon {
    width: 36px;
    height: 36px;
    border-radius: 10px;
  }

  .level-filter {
    gap: 8px;
  }

  .log-category {
    display: none;
  }
}
</style>
