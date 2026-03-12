<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted, nextTick } from 'vue'
import { NButton, NIcon, NInput, NSwitch, NTooltip } from 'naive-ui'
import {
  TerminalOutlined,
  FilterListOutlined,
  SearchOutlined,
  FileDownloadOutlined,
  DeleteOutlined,
  HistoryOutlined,
  VerticalAlignBottomOutlined,
  InfoOutlined,
  WarningAmberOutlined,
  BugReportOutlined,
} from '@vicons/material'
import { useLogStore } from '@/stores/log'
import { logsApi } from '@/api/games'

const logStore = useLogStore()

const selectedLevels = ref<Set<string>>(new Set(['INF', 'WRN', 'ERR', 'CRI']))
const keyword = ref('')
const autoScroll = ref(true)
const loading = ref(false)
const historyLoading = ref(false)
const containerRef = ref<HTMLElement | null>(null)
const showFilters = ref(true)

interface LevelDef {
  key: string
  label: string
  icon: typeof InfoOutlined
  colorVar: string
}

const levelDefs: LevelDef[] = [
  { key: 'INF', label: 'INFO', icon: InfoOutlined, colorVar: '--accent' },
  { key: 'WRN', label: 'WARN', icon: WarningAmberOutlined, colorVar: '--warning' },
  { key: 'ERR', label: 'ERROR', icon: BugReportOutlined, colorVar: '--danger' },
  { key: 'CRI', label: 'CRIT', icon: BugReportOutlined, colorVar: '--danger' },
]

function toggleLevel(key: string) {
  const s = new Set(selectedLevels.value)
  if (s.has(key)) s.delete(key)
  else s.add(key)
  selectedLevels.value = s
}

const levelCounts = computed(() => {
  const counts: Record<string, number> = { INF: 0, WRN: 0, ERR: 0, CRI: 0 }
  for (const e of logStore.entries) {
    if (e.level in counts) counts[e.level] = (counts[e.level] ?? 0) + 1
  }
  return counts
})

const filteredEntries = computed(() => {
  const levels = selectedLevels.value
  const kw = keyword.value.toLowerCase()
  return logStore.entries.filter(e => {
    if (!levels.has(e.level)) return false
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

function formatTimestamp(ts: string) {
  return ts.substring(11, 23)
}
</script>

<template>
  <div class="log-page">
    <!-- Header -->
    <h1 class="page-title" style="animation-delay: 0s">
      <span class="page-title-icon">
        <NIcon :size="24"><TerminalOutlined /></NIcon>
      </span>
      运行日志
    </h1>

    <!-- Stats Row -->
    <div class="stats-row" style="animation-delay: 0.04s">
      <button
        v-for="def in levelDefs"
        :key="def.key"
        class="level-pill"
        :class="[`pill-${def.key.toLowerCase()}`, { active: selectedLevels.has(def.key), 'has-count': (levelCounts[def.key] ?? 0) > 0 }]"
        @click="toggleLevel(def.key)"
      >
        <NIcon :size="14"><component :is="def.icon" /></NIcon>
        <span class="pill-label">{{ def.label }}</span>
        <span class="pill-count">{{ levelCounts[def.key] }}</span>
      </button>
    </div>

    <!-- Toolbar -->
    <div class="toolbar-card" style="animation-delay: 0.06s">
      <div class="toolbar">
        <div class="toolbar-left">
          <div class="search-box">
            <NInput
              v-model:value="keyword"
              placeholder="搜索日志内容或分类..."
              clearable
              size="small"
            >
              <template #prefix>
                <NIcon :size="14" color="var(--text-3)"><SearchOutlined /></NIcon>
              </template>
            </NInput>
          </div>
        </div>
        <div class="toolbar-right">
          <div class="auto-scroll-toggle">
            <span class="toggle-label">自动滚动</span>
            <NSwitch v-model:value="autoScroll" size="small" />
          </div>
          <div class="toolbar-divider"></div>
          <NTooltip trigger="hover">
            <template #trigger>
              <NButton size="small" quaternary :loading="historyLoading" @click="handleLoadHistory">
                <template #icon><NIcon><HistoryOutlined /></NIcon></template>
              </NButton>
            </template>
            加载历史日志
          </NTooltip>
          <NTooltip trigger="hover">
            <template #trigger>
              <NButton size="small" quaternary @click="handleExport">
                <template #icon><NIcon><FileDownloadOutlined /></NIcon></template>
              </NButton>
            </template>
            导出日志文件
          </NTooltip>
          <NTooltip trigger="hover">
            <template #trigger>
              <NButton size="small" quaternary @click="handleClear">
                <template #icon><NIcon><DeleteOutlined /></NIcon></template>
              </NButton>
            </template>
            清空当前日志
          </NTooltip>
        </div>
      </div>
    </div>

    <!-- Log Container -->
    <div class="log-panel" style="animation-delay: 0.08s">
      <div ref="containerRef" class="log-container" @scroll="handleScroll">
        <!-- Loading State -->
        <div v-if="loading" class="log-empty-state">
          <div class="empty-icon loading-icon">
            <NIcon :size="32"><TerminalOutlined /></NIcon>
          </div>
          <span class="empty-text">连接日志流...</span>
        </div>

        <!-- Empty State -->
        <div v-else-if="filteredEntries.length === 0" class="log-empty-state">
          <div class="empty-icon">
            <NIcon :size="32"><TerminalOutlined /></NIcon>
          </div>
          <span class="empty-text">{{ keyword ? '没有匹配的日志' : '暂无日志记录' }}</span>
          <span class="empty-hint">{{ keyword ? '尝试调整搜索关键词或筛选条件' : '日志将在应用运行时实时显示' }}</span>
        </div>

        <!-- Log Entries -->
        <template v-else>
          <div
            v-for="(entry, i) in filteredEntries"
            :key="`${entry.timestamp}-${i}`"
            class="log-row"
            :class="levelClass(entry.level)"
          >
            <span class="log-line-no">{{ i + 1 }}</span>
            <span class="log-time">{{ formatTimestamp(entry.timestamp) }}</span>
            <span class="log-level-badge" :class="levelClass(entry.level)">{{ entry.level }}</span>
            <span class="log-category">{{ entry.category }}</span>
            <span class="log-message">{{ entry.message }}</span>
          </div>
        </template>
      </div>

      <!-- Footer -->
      <div class="log-footer">
        <div class="footer-left">
          <span class="log-count">
            <span class="count-filtered">{{ filteredEntries.length }}</span>
            <span class="count-sep">/</span>
            <span class="count-total">{{ logStore.entries.length }}</span>
            <span class="count-label">条日志</span>
          </span>
        </div>
        <div class="footer-right">
          <Transition name="fade-btn">
            <NButton
              v-if="!autoScroll"
              size="tiny"
              quaternary
              class="scroll-btn"
              @click="autoScroll = true; scrollToBottom()"
            >
              <template #icon><NIcon :size="14"><VerticalAlignBottomOutlined /></NIcon></template>
              回到底部
            </NButton>
          </Transition>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.log-page {
  display: flex;
  flex-direction: column;
  gap: 12px;
  animation: fadeIn 0.3s ease;
  height: calc(100vh - 48px);
}

/* ===== Header ===== */
.page-title {
  font-family: var(--font-display);
  font-size: 26px;
  font-weight: 600;
  color: var(--text-1);
  margin-bottom: 0;
  letter-spacing: -0.03em;
  display: flex;
  align-items: center;
  gap: 12px;
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

/* ===== Stats / Level Pills ===== */
.stats-row {
  display: flex;
  gap: 8px;
  flex-shrink: 0;
  animation: slideUp 0.45s var(--ease-out) backwards;
}

.level-pill {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 14px;
  border-radius: 10px;
  border: 1px solid var(--border);
  background: var(--bg-card);
  cursor: pointer;
  transition: all 0.2s ease;
  font-family: var(--font-body);
  font-size: 12px;
  font-weight: 500;
  color: var(--text-3);
  outline: none;
  flex: 1;
  justify-content: center;
  box-shadow: var(--shadow-card-rest);
}

.level-pill:hover {
  border-color: var(--border-hover);
  background: var(--bg-card-hover);
}

.level-pill .pill-count {
  font-family: var(--font-mono);
  font-size: 13px;
  font-weight: 700;
  min-width: 20px;
  text-align: center;
}

/* INF */
.level-pill.pill-inf.active {
  color: var(--accent);
  background: color-mix(in srgb, var(--accent) 6%, var(--bg-card));
  border-color: color-mix(in srgb, var(--accent) 20%, transparent);
}

.level-pill.pill-inf.active .pill-count {
  color: var(--accent);
}

/* WRN */
.level-pill.pill-wrn.active {
  color: var(--warning);
  background: color-mix(in srgb, var(--warning) 6%, var(--bg-card));
  border-color: color-mix(in srgb, var(--warning) 20%, transparent);
}

.level-pill.pill-wrn.active .pill-count {
  color: var(--warning);
}

/* ERR */
.level-pill.pill-err.active {
  color: var(--danger);
  background: color-mix(in srgb, var(--danger) 6%, var(--bg-card));
  border-color: color-mix(in srgb, var(--danger) 20%, transparent);
}

.level-pill.pill-err.active .pill-count {
  color: var(--danger);
}

/* CRI */
.level-pill.pill-cri.active {
  color: var(--danger);
  background: color-mix(in srgb, var(--danger) 8%, var(--bg-card));
  border-color: color-mix(in srgb, var(--danger) 25%, transparent);
}

.level-pill.pill-cri.active .pill-count {
  color: var(--danger);
}

.level-pill.pill-cri.active.has-count {
  animation: crit-pulse 3s ease-in-out infinite;
}

@keyframes crit-pulse {
  0%, 100% { box-shadow: 0 0 0 0 color-mix(in srgb, var(--danger) 15%, transparent); }
  50% { box-shadow: 0 0 12px 2px color-mix(in srgb, var(--danger) 10%, transparent); }
}

/* ===== Toolbar ===== */
.toolbar-card {
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  padding: 8px 14px;
  flex-shrink: 0;
  animation: slideUp 0.5s var(--ease-out) backwards;
  transition: border-color 0.3s ease, background 0.3s ease;
  box-shadow: var(--shadow-card-rest);
}

.toolbar-card:hover {
  border-color: var(--border-hover);
}

.toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.toolbar-left {
  display: flex;
  align-items: center;
  gap: 12px;
  flex: 1;
  min-width: 0;
}

.search-box {
  max-width: 280px;
  width: 100%;
}

.toolbar-right {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-shrink: 0;
}

.toolbar-divider {
  width: 1px;
  height: 20px;
  background: var(--border);
  margin: 0 4px;
}

.auto-scroll-toggle {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-right: 4px;
}

.toggle-label {
  font-size: 12px;
  color: var(--text-3);
  white-space: nowrap;
}

/* ===== Log Panel ===== */
.log-panel {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  overflow: hidden;
  animation: slideUp 0.55s var(--ease-out) backwards;
  transition: border-color 0.3s ease, background 0.3s ease;
  box-shadow: var(--shadow-card-rest);
  position: relative;
}

.log-panel:hover {
  border-color: var(--border-hover);
}

/* Subtle top-edge glow */
.log-panel::before {
  content: '';
  position: absolute;
  top: 0;
  left: 24px;
  right: 24px;
  height: 1px;
  background: linear-gradient(90deg, transparent, color-mix(in srgb, var(--accent) 15%, transparent), transparent);
  z-index: 1;
  opacity: 0.6;
}

.log-container {
  flex: 1;
  overflow-y: auto;
  padding: 6px 0;
  font-family: var(--font-mono);
  font-size: 12px;
  line-height: 1.7;
}

/* ===== Empty State ===== */
.log-empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100%;
  min-height: 240px;
  gap: 12px;
  padding: 40px;
}

.empty-icon {
  width: 64px;
  height: 64px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 16px;
  background: var(--bg-muted);
  color: var(--text-3);
  transition: all 0.3s ease;
}

.empty-icon.loading-icon {
  animation: breathe 2s ease-in-out infinite;
  color: var(--accent);
  background: var(--accent-soft);
}

.empty-text {
  font-family: var(--font-body);
  font-size: 15px;
  font-weight: 500;
  color: var(--text-2);
}

.empty-hint {
  font-family: var(--font-body);
  font-size: 13px;
  color: var(--text-3);
}

/* ===== Log Rows ===== */
.log-row {
  display: flex;
  gap: 0;
  padding: 0 16px 0 0;
  white-space: nowrap;
  transition: background 0.1s ease;
  border-left: 2px solid transparent;
}

.log-row:hover {
  background: var(--bg-subtle);
}

.log-row.level-err {
  background: color-mix(in srgb, var(--danger) 4%, transparent);
  border-left-color: var(--danger);
}

.log-row.level-err:hover {
  background: color-mix(in srgb, var(--danger) 8%, transparent);
}

.log-row.level-wrn {
  background: color-mix(in srgb, var(--warning) 2%, transparent);
  border-left-color: color-mix(in srgb, var(--warning) 50%, transparent);
}

.log-row.level-wrn:hover {
  background: color-mix(in srgb, var(--warning) 6%, transparent);
}

/* Line Number */
.log-line-no {
  color: var(--text-3);
  opacity: 0.4;
  min-width: 44px;
  padding: 0 10px 0 14px;
  text-align: right;
  user-select: none;
  font-size: 11px;
  flex-shrink: 0;
  border-right: 1px solid var(--border);
  margin-right: 10px;
}

/* Timestamp */
.log-time {
  color: var(--text-3);
  flex-shrink: 0;
  margin-right: 8px;
  font-size: 11px;
  opacity: 0.7;
}

/* Level Badge */
.log-level-badge {
  flex-shrink: 0;
  width: 32px;
  text-align: center;
  font-weight: 700;
  font-size: 10px;
  letter-spacing: 0.03em;
  padding: 0 2px;
  margin-right: 8px;
  border-radius: 3px;
}

.log-level-badge.level-inf {
  color: var(--accent);
}

.log-level-badge.level-wrn {
  color: var(--warning);
}

.log-level-badge.level-err {
  color: var(--danger);
}

.log-level-badge.level-dbg {
  color: var(--text-3);
}

/* Category */
.log-category {
  color: var(--secondary);
  flex-shrink: 0;
  max-width: 180px;
  overflow: hidden;
  text-overflow: ellipsis;
  margin-right: 10px;
  opacity: 0.7;
  font-size: 11px;
}

/* Message */
.log-message {
  color: var(--text-1);
  white-space: pre-wrap;
  word-break: break-all;
  min-width: 0;
}

.log-row.level-err .log-message {
  color: color-mix(in srgb, var(--danger) 80%, var(--text-1));
}

/* ===== Log Footer ===== */
.log-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 6px 16px;
  border-top: 1px solid var(--border);
  flex-shrink: 0;
  background: color-mix(in srgb, var(--bg-card) 80%, var(--bg-root));
}

.footer-left {
  display: flex;
  align-items: center;
  gap: 12px;
}

.log-count {
  font-size: 12px;
  font-family: var(--font-mono);
  display: flex;
  align-items: center;
  gap: 4px;
}

.count-filtered {
  color: var(--accent);
  font-weight: 600;
}

.count-sep {
  color: var(--text-3);
  opacity: 0.5;
}

.count-total {
  color: var(--text-3);
}

.count-label {
  color: var(--text-3);
  font-family: var(--font-body);
  margin-left: 2px;
}

.footer-right {
  display: flex;
  align-items: center;
}

.scroll-btn {
  font-size: 12px;
}

/* ===== Transitions ===== */
.fade-btn-enter-active,
.fade-btn-leave-active {
  transition: opacity 0.2s ease, transform 0.2s ease;
}

.fade-btn-enter-from,
.fade-btn-leave-to {
  opacity: 0;
  transform: translateY(4px);
}

/* ===== Responsive ===== */
@media (max-width: 768px) {
  .log-page {
    height: auto;
    min-height: calc(100vh - 120px);
  }

  .stats-row {
    flex-wrap: wrap;
  }

  .level-pill {
    flex: 0 1 auto;
    padding: 5px 10px;
    font-size: 11px;
  }

  .toolbar {
    flex-direction: column;
    align-items: stretch;
    gap: 8px;
  }

  .toolbar-left {
    flex: unset;
  }

  .toolbar-right {
    justify-content: flex-end;
  }

  .search-box {
    max-width: 100%;
  }

  .toolbar-card {
    padding: 10px 12px;
  }

  .log-row {
    padding: 0 10px 0 0;
    font-size: 11px;
  }

  .log-line-no {
    min-width: 34px;
    padding: 0 6px 0 8px;
  }
}

@media (max-width: 480px) {
  .page-title {
    font-size: 20px;
    gap: 8px;
  }

  .page-title-icon {
    width: 32px;
    height: 32px;
    border-radius: 8px;
  }

  .stats-row {
    gap: 6px;
  }

  .level-pill {
    padding: 4px 8px;
    gap: 4px;
    border-radius: 8px;
  }

  .pill-label {
    display: none;
  }

  .log-line-no {
    display: none;
  }

  .log-category {
    display: none;
  }

  .log-time {
    font-size: 10px;
  }

  .auto-scroll-toggle {
    display: none;
  }
}
</style>
