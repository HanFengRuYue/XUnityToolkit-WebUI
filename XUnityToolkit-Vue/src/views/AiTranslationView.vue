<script setup lang="ts">
import { ref, reactive, computed, onMounted, onActivated, onBeforeUnmount, onDeactivated, nextTick, watch } from 'vue'
import {
  NIcon,
  NButton,
  NSwitch,
  NSelect,
  NTag,
  NProgress,
  NSlider,
  NCollapse,
  NCollapseItem,
  NPopover,
  useMessage,
} from 'naive-ui'
import {
  SmartToyOutlined,
  TranslateOutlined,
  HourglassEmptyOutlined,
  SyncOutlined,
  DashboardOutlined,
  TokenOutlined,
  SpeedOutlined,
  HistoryOutlined,
  ErrorOutlineOutlined,
  MoveToInboxOutlined,
  AutoFixHighOutlined,
  CheckCircleOutlined,
  ArrowRightAltOutlined,
  CloudOutlined,
  ComputerOutlined,
  SportsEsportsOutlined,
  CachedOutlined,
  ExpandMoreOutlined,
  StorageOutlined,
} from '@vicons/material'
import { useAiTranslationStore } from '@/stores/aiTranslation'
import { useGamesStore } from '@/stores/games'
import { settingsApi } from '@/api/games'
import type { AppSettings, AiTranslationSettings } from '@/api/types'
import AiTranslationCard from '@/components/settings/AiTranslationCard.vue'
import LocalAiPanel from '@/components/settings/LocalAiPanel.vue'
import { useAutoSave } from '@/composables/useAutoSave'
import { DEFAULT_SYSTEM_PROMPT } from '@/constants/prompts'

defineOptions({ name: 'AiTranslationView' })

const collapsed = reactive({
  settings: false,
  cache: true,
  recent: true,
  errors: true,
  termSettings: false,
  tmSettings: false,
})

const aiStore = useAiTranslationStore()
const gamesStore = useGamesStore()
const message = useMessage()

const DEFAULT_AI_TRANSLATION: AiTranslationSettings = {
  enabled: true,
  activeMode: 'cloud',
  maxConcurrency: 4,
  port: 51821,
  systemPrompt: DEFAULT_SYSTEM_PROMPT,
  temperature: 0.3,
  contextSize: 10,
  localContextSize: 0,
  endpoints: [],
  glossaryExtractionEnabled: false,
  enablePreTranslationCache: false,
  termAuditEnabled: true,
  naturalTranslationMode: true,
  enableTranslationMemory: true,
  fuzzyMatchThreshold: 85,
  enableLlmPatternAnalysis: true,
  enableMultiRoundTranslation: true,
  enableAutoTermExtraction: true,
  autoApplyExtractedTerms: false,
}

const settings = ref<AppSettings | null>(null)

const aiSettings = computed({
  get: () => settings.value?.aiTranslation ?? { ...DEFAULT_AI_TRANSLATION },
  set: (val: AiTranslationSettings) => {
    if (settings.value) {
      settings.value = { ...settings.value, aiTranslation: val }
    }
  },
})

const connectionStatus = computed(() => {
  if (!aiStore.stats?.lastRequestAt) return 'never'
  const last = new Date(aiStore.stats.lastRequestAt).getTime()
  const diff = Date.now() - last
  if (diff < 5 * 60 * 1000) return 'active'
  if (diff < 60 * 60 * 1000) return 'idle'
  return 'stale'
})

const connectionLabel = computed(() => {
  switch (connectionStatus.value) {
    case 'active': return '活跃'
    case 'idle': return '空闲'
    case 'stale': return '已断开'
    case 'never': return '无连接'
  }
})

const successRate = computed(() => {
  const received = aiStore.stats?.totalReceived ?? 0
  const errors = aiStore.stats?.totalErrors ?? 0
  if (received === 0) return null
  return ((1 - errors / received) * 100).toFixed(1)
})

const successRateNumber = computed(() => {
  if (successRate.value === null) return 100
  return Number(successRate.value)
})

const lastRequestFormatted = computed(() => {
  if (!aiStore.stats?.lastRequestAt) return '从未'
  const date = new Date(aiStore.stats.lastRequestAt)
  return date.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
})

const isEnabled = computed(() => aiStore.stats?.enabled ?? true)

const isActivelyTranslating = computed(() => (aiStore.stats?.translating ?? 0) > 0)

const tmTotalHits = computed(() => {
  const s = aiStore.stats
  if (!s) return 0
  return s.translationMemoryHits + s.translationMemoryFuzzyHits + s.translationMemoryPatternHits
})

const llmCompleted = computed(() => {
  return Math.max(0, (aiStore.stats?.totalTranslated ?? 0) - tmTotalHits.value)
})

const hasTmActivity = computed(() => aiSettings.value.enableTranslationMemory || tmTotalHits.value > 0 || (aiStore.stats?.translationMemoryMisses ?? 0) > 0)

const extractionStats = computed(() => aiStore.extractionStats)

const termAuditTotal = computed(() => {
  const s = aiStore.stats
  if (!s) return 0
  return s.termAuditPhase1PassCount + s.termAuditPhase2PassCount + s.termAuditForceCorrectedCount
})

const termAuditPassRate = computed(() => {
  const total = termAuditTotal.value
  if (total === 0) return null
  const s = aiStore.stats!
  return ((s.termAuditPhase1PassCount / total) * 100).toFixed(1)
})

const showTermAudit = computed(() =>
  (aiStore.stats?.termMatchedTextCount ?? 0) > 0 || termAuditTotal.value > 0
)

const showExtraction = computed(() =>
  aiSettings.value.glossaryExtractionEnabled && !!extractionStats.value
)

const allSettingsCollapsed = computed(() => collapsed.termSettings && collapsed.tmSettings)

const activeMode = computed(() => aiSettings.value.activeMode ?? 'cloud')
const isLocalMode = computed(() => activeMode.value === 'local')

function setMode(mode: 'cloud' | 'local') {
  aiSettings.value = { ...aiSettings.value, activeMode: mode }
}

const { enable: enableAutoSave, disable: disableAutoSave } = useAutoSave(
  () => settings.value,
  async () => {
    if (!settings.value) return
    try {
      await settingsApi.save(settings.value)
    } catch {
      message.error('保存设置失败')
    }
  },
  { debounceMs: 1000, deep: true },
)

function getGameName(gameId: string): string {
  const game = gamesStore.games.find(g => g.id === gameId)
  return game?.name ?? gameId
}

function formatTokens(n: number): string {
  if (n >= 1_000_000) return (n / 1_000_000).toFixed(1) + 'M'
  if (n >= 1_000) return (n / 1_000).toFixed(1) + 'K'
  return String(n)
}

function formatTime(ms: number): string {
  if (ms >= 1000) return (ms / 1000).toFixed(1) + 's'
  return Math.round(ms) + 'ms'
}

function formatRelativeTime(timestamp: string): string {
  const diff = Date.now() - new Date(timestamp).getTime()
  if (diff < 60_000) return '刚刚'
  if (diff < 3600_000) return Math.floor(diff / 60_000) + '分钟前'
  return Math.floor(diff / 3600_000) + '小时前'
}

async function handleToggle(enabled: boolean) {
  try {
    await aiStore.toggleEnabled(enabled)
    message.success(enabled ? 'AI 翻译已启用' : 'AI 翻译已停用')
  } catch {
    message.error('切换 AI 翻译状态失败')
  }
}

async function loadSettings() {
  disableAutoSave()
  try {
    const loaded = await settingsApi.get()
    settings.value = {
      ...loaded,
      aiTranslation: loaded.aiTranslation ?? { ...DEFAULT_AI_TRANSLATION },
    }
  } catch {
    // Use defaults
  }
  await nextTick()
  enableAutoSave()
}

onMounted(async () => {
  await aiStore.connect()
  await Promise.all([
    aiStore.fetchStats(),
    aiStore.fetchCacheStats(),
    loadSettings(),
    gamesStore.games.length === 0 ? gamesStore.fetchGames() : Promise.resolve(),
  ])
  await nextTick()
  computeConnections()
  setupPipelineObserver()
})

onActivated(async () => {
  await aiStore.connect()
  await Promise.all([aiStore.fetchStats(), aiStore.fetchCacheStats()])
  await nextTick()
  computeConnections()
  setupPipelineObserver()
})

onDeactivated(() => {
  aiStore.disconnect()
  cleanupPipeline()
})

onBeforeUnmount(() => {
  aiStore.disconnect()
  cleanupPipeline()
})

// ===== Pipeline SVG Connection Logic =====
const pipelineRef = ref<HTMLElement | null>(null)
const rootNodeRef = ref<HTMLElement | null>(null)
const queueNodeRef = ref<HTMLElement | null>(null)
const translatingNodeRef = ref<HTMLElement | null>(null)
const doneNodeRef = ref<HTMLElement | null>(null)
const extractionNodeRef = ref<HTMLElement | null>(null)
const tmChipsRef = ref<HTMLElement | null>(null)
const tmDoneRef = ref<HTMLElement | null>(null)

interface PipelineConnection {
  id: string
  pathD: string
  active: boolean
  particleCount: number
  duration: number
  isTm: boolean
}

const connections = ref<PipelineConnection[]>([])

function getRelPos(el: HTMLElement | null, container: DOMRect) {
  if (!el) return null
  const r = el.getBoundingClientRect()
  return {
    cx: r.left + r.width / 2 - container.left,
    cy: r.top + r.height / 2 - container.top,
    top: r.top - container.top,
    bottom: r.top + r.height - container.top,
    left: r.left - container.left,
    right: r.left + r.width - container.left,
    w: r.width,
    h: r.height,
  }
}

function particleParams(volume: number) {
  if (volume <= 0) return { count: 0, duration: 5 }
  const count = Math.min(6, Math.max(1, Math.ceil(volume / 5)))
  const duration = Math.max(1.5, 5 - Math.log2(volume + 1) * 0.8)
  return { count, duration }
}

function computeConnections() {
  if (!pipelineRef.value) return
  const container = pipelineRef.value.getBoundingClientRect()
  const root = getRelPos(rootNodeRef.value, container)
  const queue = getRelPos(queueNodeRef.value, container)
  const translating = getRelPos(translatingNodeRef.value, container)
  const done = getRelPos(doneNodeRef.value, container)
  const extraction = getRelPos(extractionNodeRef.value, container)
  const tmChips = getRelPos(tmChipsRef.value, container)
  const tmDone = getRelPos(tmDoneRef.value, container)

  const result: PipelineConnection[] = []
  const s = aiStore.stats

  // Helper: right-angle path (horizontal then vertical then horizontal)
  function rightAngleH(x1: number, y1: number, x2: number, y2: number) {
    const midX = x1 + (x2 - x1) * 0.5
    return `M ${x1} ${y1} L ${midX} ${y1} L ${midX} ${y2} L ${x2} ${y2}`
  }

  // root → queue
  if (root && queue) {
    const pathD = rightAngleH(root.right, root.cy, queue.left, queue.cy)
    const vol = (s?.queued ?? 0) + (s?.translating ?? 0)
    const pp = particleParams(vol)
    result.push({ id: 'root-queue', pathD, active: vol > 0, particleCount: pp.count, duration: pp.duration, isTm: false })
  }

  // queue → translating
  if (queue && translating) {
    const pathD = rightAngleH(queue.right, queue.cy, translating.left, translating.cy)
    const vol = s?.translating ?? 0
    const pp = particleParams(vol)
    result.push({ id: 'queue-translating', pathD, active: vol > 0, particleCount: pp.count, duration: pp.duration, isTm: false })
  }

  // translating → done
  if (translating && done) {
    const pathD = rightAngleH(translating.right, translating.cy, done.left, done.cy)
    const vol = s?.translating ?? 0
    const pp = particleParams(vol)
    result.push({ id: 'translating-done', pathD, active: vol > 0, particleCount: pp.count, duration: pp.duration, isTm: false })
  }

  // done → extraction (horizontal inline)
  if (done && extraction) {
    const pathD = rightAngleH(done.right, done.cy, extraction.left, extraction.cy)
    const vol = extractionStats.value?.activeExtractions ?? 0
    const pp = particleParams(vol)
    result.push({ id: 'done-extraction', pathD, active: vol > 0, particleCount: pp.count, duration: pp.duration, isTm: false })
  }

  // root → TM node (right-angle fork: right, down, right)
  if (root && tmChips) {
    const pathD = rightAngleH(root.right, root.cy, tmChips.left, tmChips.cy)
    const vol = Math.min(20, tmTotalHits.value)
    const pp = particleParams(vol)
    result.push({ id: 'tm-root-chips', pathD, active: tmTotalHits.value > 0, particleCount: pp.count, duration: pp.duration, isTm: true })
  }

  // TM node → TM done
  if (tmChips && tmDone) {
    const pathD = rightAngleH(tmChips.right, tmChips.cy, tmDone.left, tmDone.cy)
    const vol = Math.min(20, tmTotalHits.value)
    const pp = particleParams(vol)
    result.push({ id: 'tm-chips-done', pathD, active: tmTotalHits.value > 0, particleCount: pp.count, duration: pp.duration, isTm: true })
  }

  connections.value = result
}

let pipelineResizeObserver: ResizeObserver | null = null

function setupPipelineObserver() {
  if (!pipelineRef.value || pipelineResizeObserver) return
  pipelineResizeObserver = new ResizeObserver(() => {
    requestAnimationFrame(computeConnections)
  })
  pipelineResizeObserver.observe(pipelineRef.value)
}

function cleanupPipeline() {
  pipelineResizeObserver?.disconnect()
  pipelineResizeObserver = null
}

watch([hasTmActivity, showExtraction], () => {
  nextTick(computeConnections)
})

// Recompute when stats change (for particle density updates)
watch(() => aiStore.stats?.translating, () => {
  nextTick(computeConnections)
})
</script>

<template>
  <div class="ai-page">
    <!-- Title Row -->
    <div class="page-title-row" style="animation-delay: 0s">
      <h1 class="page-title">
        <span class="page-title-icon">
          <NIcon :size="24"><SmartToyOutlined /></NIcon>
        </span>
        AI 翻译
      </h1>
      <div class="enable-toggle">
        <span class="toggle-label">{{ isEnabled ? '已启用' : '已停用' }}</span>
        <NSwitch
          :value="isEnabled"
          @update:value="handleToggle"
        />
      </div>
    </div>

    <!-- Live Metrics Strip -->
    <div class="metrics-strip" style="animation-delay: 0.03s">
      <div class="metric-pill">
        <div class="metric-icon">
          <NIcon :size="14"><MoveToInboxOutlined /></NIcon>
        </div>
        <div class="metric-data">
          <span class="metric-value">{{ aiStore.stats?.totalReceived ?? 0 }}</span>
          <span class="metric-label">已接收</span>
        </div>
      </div>
      <div class="metric-pill" :class="{ 'rate-good': successRateNumber >= 95, 'rate-warn': successRateNumber < 95 && successRateNumber >= 80, 'rate-bad': successRateNumber < 80 }">
        <div class="metric-icon">
          <NIcon :size="14"><CheckCircleOutlined /></NIcon>
        </div>
        <div class="metric-data">
          <span class="metric-value">{{ successRate ?? '--' }}<small>%</small></span>
          <span class="metric-label">成功率</span>
        </div>
      </div>
      <div class="metric-pill">
        <div class="metric-icon">
          <NIcon :size="14"><SpeedOutlined /></NIcon>
        </div>
        <div class="metric-data">
          <span class="metric-value">{{ formatTime(aiStore.stats?.averageResponseTimeMs ?? 0) }}</span>
          <span class="metric-label">均速 · {{ aiStore.stats?.requestsPerMinute ?? 0 }}/min</span>
        </div>
      </div>
      <div class="metric-pill">
        <div class="metric-icon">
          <NIcon :size="14"><TokenOutlined /></NIcon>
        </div>
        <div class="metric-data">
          <span class="metric-value">{{ formatTokens(aiStore.stats?.totalTokensUsed ?? 0) }}</span>
          <span class="metric-label">Token</span>
        </div>
      </div>
    </div>

    <!-- Status Dashboard -->
        <div class="section-card" :class="{ 'is-translating': isActivelyTranslating }" style="animation-delay: 0.05s">
          <div class="section-header">
            <h2 class="section-title">
              <span class="section-icon">
                <NIcon :size="16"><DashboardOutlined /></NIcon>
              </span>
              翻译状态
            </h2>
            <div class="header-actions">
              <span class="connection-badge" :class="connectionStatus">
                <span class="connection-dot"></span>
                {{ connectionLabel }}
                <span class="connection-time">{{ lastRequestFormatted }}</span>
              </span>
              <NButton size="small" quaternary @click="aiStore.fetchStats()">
                刷新
              </NButton>
            </div>
          </div>

          <!-- Current Game Indicator -->
          <div v-if="aiStore.stats?.currentGameId" class="current-game-indicator">
            <NIcon :size="14"><SportsEsportsOutlined /></NIcon>
            <span class="current-game-label">当前翻译游戏</span>
            <span class="current-game-name">{{ getGameName(aiStore.stats.currentGameId) }}</span>
          </div>

          <!-- Pipeline Flow -->
          <div class="pipeline-flow" ref="pipelineRef">
            <!-- SVG Connection Overlay -->
            <svg class="pipeline-svg">
              <defs>
                <filter id="particle-glow" x="-50%" y="-50%" width="200%" height="200%">
                  <feGaussianBlur stdDeviation="3" result="blur" />
                  <feMerge>
                    <feMergeNode in="blur" />
                    <feMergeNode in="SourceGraphic" />
                  </feMerge>
                </filter>
              </defs>
              <path v-for="conn in connections" :key="conn.id"
                    :d="conn.pathD"
                    class="connection-path"
                    :class="{ active: conn.active, 'tm-path': conn.isTm }" />
              <template v-for="conn in connections" :key="'particles-' + conn.id">
                <circle v-for="i in (conn.active ? conn.particleCount : 0)" :key="conn.id + '-p-' + i"
                        class="flow-particle"
                        :class="{ 'tm-particle': conn.isTm }"
                        r="3"
                        filter="url(#particle-glow)"
                        :style="{
                          offsetPath: `path('${conn.pathD}')`,
                          animationDuration: conn.duration + 's',
                          animationDelay: ((i - 1) * (conn.duration / conn.particleCount)) + 's',
                        }" />
              </template>
            </svg>

            <!-- Horizontal Fork Layout -->
            <div class="pipeline-hbox">
              <!-- Root Node -->
              <div class="pipeline-root">
                <NPopover trigger="hover" placement="bottom">
                  <template #trigger>
                    <div ref="rootNodeRef" class="pipeline-node root-node">
                      <div class="node-icon root-icon">
                        <NIcon :size="20"><MoveToInboxOutlined /></NIcon>
                      </div>
                      <div class="node-data">
                        <span class="node-value root-value">{{ aiStore.stats?.totalReceived ?? 0 }}</span>
                        <span class="node-label">已接收</span>
                      </div>
                    </div>
                  </template>
                  <div class="pipeline-tooltip">所有发送到 AI 翻译服务的文本总数</div>
                </NPopover>
              </div>

              <!-- Fork Branches -->
              <div class="pipeline-branches">
                <!-- LLM Branch -->
                <div class="pipeline-branch">
                  <div class="branch-header">
                    <NIcon :size="13"><SmartToyOutlined /></NIcon>
                    <span>LLM 翻译</span>
                  </div>
                  <div class="branch-nodes">
                    <NPopover trigger="hover" placement="top">
                      <template #trigger>
                        <div ref="queueNodeRef" class="pipeline-node" :class="{ dimmed: (aiStore.stats?.queued ?? 0) === 0 }">
                          <div class="node-icon"><NIcon :size="14"><HourglassEmptyOutlined /></NIcon></div>
                          <div class="node-data">
                            <span class="node-value">{{ aiStore.stats?.queued ?? 0 }}</span>
                            <span class="node-label">排队</span>
                          </div>
                        </div>
                      </template>
                      <div class="pipeline-tooltip">等待 LLM 处理的翻译请求队列</div>
                    </NPopover>

                    <NPopover trigger="hover" placement="top">
                      <template #trigger>
                        <div ref="translatingNodeRef" class="pipeline-node" :class="{ 'is-active': isActivelyTranslating }">
                          <div class="node-icon translating-icon"><NIcon :size="14"><SyncOutlined /></NIcon></div>
                          <div class="node-data">
                            <span class="node-value">{{ aiStore.stats?.translating ?? 0 }}<small v-if="aiStore.stats?.maxConcurrency">/{{ aiStore.stats.maxConcurrency }}</small></span>
                            <span class="node-label">翻译中</span>
                          </div>
                        </div>
                      </template>
                      <div class="pipeline-tooltip">正在由大语言模型处理的翻译请求</div>
                    </NPopover>

                    <NPopover trigger="hover" placement="top">
                      <template #trigger>
                        <div ref="doneNodeRef" class="pipeline-node done-node">
                          <div class="node-icon done-icon"><NIcon :size="14"><TranslateOutlined /></NIcon></div>
                          <div class="node-data">
                            <span class="node-value">{{ llmCompleted }}</span>
                            <span class="node-label">已完成</span>
                          </div>
                        </div>
                      </template>
                      <div class="pipeline-tooltip">LLM 成功翻译的文本数（不含翻译记忆命中）</div>
                    </NPopover>

                    <!-- Term Extraction (inline after 已完成) -->
                    <NPopover v-if="showExtraction" trigger="hover" placement="top">
                      <template #trigger>
                        <div ref="extractionNodeRef" class="pipeline-node extraction-node">
                          <div class="node-icon extraction-icon"><NIcon :size="14"><AutoFixHighOutlined /></NIcon></div>
                          <div class="node-data">
                            <span class="node-value">{{ extractionStats!.totalExtracted }}</span>
                            <span class="node-label">术语提取</span>
                          </div>
                          <div class="extraction-details">
                            <span class="sub-stat">{{ extractionStats!.totalExtractionCalls }} 调用</span>
                            <span v-if="extractionStats!.activeExtractions > 0" class="sub-stat active">{{ extractionStats!.activeExtractions }} 提取中</span>
                            <span v-if="extractionStats!.totalErrors > 0" class="sub-stat error">{{ extractionStats!.totalErrors }} 错误</span>
                          </div>
                        </div>
                      </template>
                      <div class="pipeline-tooltip">翻译完成后自动提取专有名词、角色名等术语，用于后续翻译</div>
                    </NPopover>
                  </div>
                </div>

                <!-- TM Branch -->
                <div v-if="hasTmActivity" class="pipeline-branch tm-branch">
                  <div class="branch-header tm-header">
                    <NIcon :size="13"><StorageOutlined /></NIcon>
                    <span>翻译记忆</span>
                  </div>
                  <div class="branch-nodes">
                    <NPopover trigger="hover" placement="top">
                      <template #trigger>
                        <div ref="tmChipsRef" class="pipeline-node tm-node">
                          <div class="node-icon tm-icon"><NIcon :size="14"><StorageOutlined /></NIcon></div>
                          <div class="tm-chips-inline">
                            <span class="tm-inline-chip"><strong>{{ aiStore.stats?.translationMemoryHits ?? 0 }}</strong> 精确</span>
                            <span class="tm-inline-chip"><strong>{{ aiStore.stats?.translationMemoryFuzzyHits ?? 0 }}</strong> 模糊</span>
                            <span class="tm-inline-chip"><strong>{{ aiStore.stats?.translationMemoryPatternHits ?? 0 }}</strong> 模式</span>
                          </div>
                        </div>
                      </template>
                      <div class="pipeline-tooltip">翻译记忆三种匹配：精确（完全相同）、模糊（相似度阈值）、模式（正则匹配）</div>
                    </NPopover>

                    <NPopover trigger="hover" placement="top">
                      <template #trigger>
                        <div ref="tmDoneRef" class="pipeline-node tm-done-node">
                          <div class="node-icon tm-icon"><NIcon :size="14"><StorageOutlined /></NIcon></div>
                          <div class="node-data">
                            <span class="node-value">{{ tmTotalHits }}</span>
                            <span class="node-label">已命中</span>
                          </div>
                        </div>
                      </template>
                      <div class="pipeline-tooltip">翻译记忆总命中数 — 无需调用 LLM 直接复用已有翻译</div>
                    </NPopover>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- Error / Success Bar -->
          <div class="error-bar" :class="{ 'has-errors': (aiStore.stats?.totalErrors ?? 0) > 0 }">
            <div class="error-bar-left">
              <NIcon :size="15"><ErrorOutlineOutlined /></NIcon>
              <span v-if="(aiStore.stats?.totalErrors ?? 0) > 0" class="error-bar-text">
                {{ aiStore.stats!.totalErrors }} 次翻译失败
              </span>
              <span v-else class="error-bar-text">暂无错误</span>
            </div>
            <div class="error-bar-right" v-if="successRate !== null">
              <div class="success-track">
                <div
                  class="success-fill"
                  :class="{ warn: successRateNumber < 95, bad: successRateNumber < 80 }"
                  :style="{ width: successRate + '%' }"
                ></div>
              </div>
              <span class="error-bar-rate">{{ successRate }}%</span>
            </div>
          </div>

          <!-- Term Audit Stats -->
          <div v-if="showTermAudit" class="audit-strip">
            <div class="audit-header">
              <NIcon :size="13"><AutoFixHighOutlined /></NIcon>
              <span>术语审查</span>
              <span v-if="termAuditPassRate !== null" class="audit-rate" :class="{ good: Number(termAuditPassRate) >= 80, warn: Number(termAuditPassRate) < 80 }">
                {{ termAuditPassRate }}<small>%</small>
              </span>
            </div>
            <div class="audit-chips">
              <NPopover trigger="hover" placement="top">
                <template #trigger>
                  <div class="audit-chip">
                    <span class="audit-chip-value">{{ aiStore.stats?.termMatchedTextCount ?? 0 }}</span>
                    <span class="audit-chip-label">应用术语</span>
                  </div>
                </template>
                <div style="max-width: 240px; font-size: 13px">涉及术语或禁翻词的翻译文本总数</div>
              </NPopover>
              <NPopover trigger="hover" placement="top">
                <template #trigger>
                  <div class="audit-chip good">
                    <span class="audit-chip-value">{{ aiStore.stats?.termAuditPhase1PassCount ?? 0 }}</span>
                    <span class="audit-chip-label">自然通过</span>
                  </div>
                </template>
                <div style="max-width: 240px; font-size: 13px">在第一阶段（自然翻译模式，不使用占位符）中，LLM 直接正确应用术语的文本数</div>
              </NPopover>
              <NPopover trigger="hover" placement="top">
                <template #trigger>
                  <div class="audit-chip">
                    <span class="audit-chip-value">{{ aiStore.stats?.termAuditPhase2PassCount ?? 0 }}</span>
                    <span class="audit-chip-label">占位符通过</span>
                  </div>
                </template>
                <div style="max-width: 240px; font-size: 13px">在第二阶段（使用占位符替换术语）中通过审查的文本数</div>
              </NPopover>
              <NPopover trigger="hover" placement="top">
                <template #trigger>
                  <div class="audit-chip" :class="{ warn: (aiStore.stats?.termAuditForceCorrectedCount ?? 0) > 0 }">
                    <span class="audit-chip-value">{{ aiStore.stats?.termAuditForceCorrectedCount ?? 0 }}</span>
                    <span class="audit-chip-label">强制修正</span>
                  </div>
                </template>
                <div style="max-width: 240px; font-size: 13px">两阶段审查都未通过后，系统直接将原文替换为术语译文进行强制修正的文本数</div>
              </NPopover>
            </div>
            <div v-if="termAuditTotal > 0" class="audit-bar">
              <div class="audit-seg phase1" :style="{ flex: aiStore.stats?.termAuditPhase1PassCount ?? 0 }"></div>
              <div class="audit-seg phase2" :style="{ flex: aiStore.stats?.termAuditPhase2PassCount ?? 0 }"></div>
              <div class="audit-seg forced" :style="{ flex: aiStore.stats?.termAuditForceCorrectedCount ?? 0 }"></div>
            </div>
          </div>
        </div>

        <!-- Pre-Translation Cache Stats -->
        <div v-if="aiStore.cacheStats && aiStore.cacheStats.totalPreTranslated > 0" class="section-card" :class="{ 'is-collapsed': collapsed.cache }" style="animation-delay: 0.06s">
          <div class="section-header collapsible" @click="collapsed.cache = !collapsed.cache">
            <h2 class="section-title">
              <span class="section-icon">
                <NIcon :size="16"><CachedOutlined /></NIcon>
              </span>
              预翻译缓存
              <NTag size="small" type="warning" style="margin-left: 8px">实验性</NTag>
            </h2>
            <NIcon :size="18" class="collapse-chevron" :class="{ expanded: !collapsed.cache }">
              <ExpandMoreOutlined />
            </NIcon>
          </div>
          <div class="section-body" :class="{ collapsed: collapsed.cache }">
            <div class="section-body-inner">
              <div class="metrics-strip compact-metrics">
                <div class="metric-pill">
                  <div class="metric-icon">
                    <NIcon :size="14"><CachedOutlined /></NIcon>
                  </div>
                  <div class="metric-data">
                    <span class="metric-value">{{ aiStore.cacheStats.totalPreTranslated }}</span>
                    <span class="metric-label">总条目</span>
                  </div>
                </div>
                <div class="metric-pill rate-good">
                  <div class="metric-icon">
                    <NIcon :size="14"><CheckCircleOutlined /></NIcon>
                  </div>
                  <div class="metric-data">
                    <span class="metric-value">{{ aiStore.cacheStats.cacheHits }}</span>
                    <span class="metric-label">命中</span>
                  </div>
                </div>
                <div class="metric-pill" :class="{ 'rate-bad': aiStore.cacheStats.cacheMisses > 0 }">
                  <div class="metric-icon">
                    <NIcon :size="14"><ErrorOutlineOutlined /></NIcon>
                  </div>
                  <div class="metric-data">
                    <span class="metric-value">{{ aiStore.cacheStats.cacheMisses }}</span>
                    <span class="metric-label">未命中</span>
                  </div>
                </div>
                <div class="metric-pill" :class="{ 'rate-good': aiStore.cacheStats.hitRate > 80, 'rate-warn': aiStore.cacheStats.hitRate <= 80 && aiStore.cacheStats.hitRate > 50, 'rate-bad': aiStore.cacheStats.hitRate <= 50 }">
                  <div class="metric-icon">
                    <NIcon :size="14"><SpeedOutlined /></NIcon>
                  </div>
                  <div class="metric-data">
                    <span class="metric-value">{{ aiStore.cacheStats.hitRate }}<small>%</small></span>
                    <span class="metric-label">命中率</span>
                  </div>
                </div>
              </div>
              <NProgress
                type="line"
                :percentage="aiStore.cacheStats.hitRate"
                :color="aiStore.cacheStats.hitRate > 50 ? 'var(--success)' : 'var(--danger)'"
                class="cache-progress"
              />
              <NCollapse v-if="aiStore.cacheStats.recentMisses.length > 0">
                <NCollapseItem title="最近未命中详情" name="misses">
                  <div v-for="(miss, idx) in aiStore.cacheStats.recentMisses" :key="idx" class="cache-miss-item">
                    <div class="cache-miss-row">
                      <span class="cache-miss-label">预翻译键:</span>
                      <code class="cache-miss-code">{{ miss.preTranslatedKey }}</code>
                    </div>
                    <div class="cache-miss-row">
                      <span class="cache-miss-label">运行时文本:</span>
                      <code class="cache-miss-code">{{ miss.runtimeText }}</code>
                    </div>
                  </div>
                </NCollapseItem>
              </NCollapse>
            </div>
          </div>
        </div>

    <!-- AI Translation Settings (collapsible) -->
    <div v-if="settings" class="section-card" :class="{ 'is-collapsed': collapsed.settings }" style="animation-delay: 0.07s">
      <div class="section-header collapsible" @click="collapsed.settings = !collapsed.settings">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><SmartToyOutlined /></NIcon>
          </span>
          AI 翻译设置
        </h2>
        <NIcon :size="18" class="collapse-chevron" :class="{ expanded: !collapsed.settings }">
          <ExpandMoreOutlined />
        </NIcon>
      </div>

      <div class="section-body" :class="{ collapsed: collapsed.settings }">
        <div class="section-body-inner">
          <!-- Pipeline Settings Groups -->
          <div class="pipeline-settings" :class="{ 'all-collapsed': allSettingsCollapsed }">
            <!-- Term Settings Group -->
            <div class="settings-group">
              <div class="settings-group-header" @click.stop="collapsed.termSettings = !collapsed.termSettings">
                <div class="settings-group-title">
                  <NIcon :size="14"><AutoFixHighOutlined /></NIcon>
                  <span>术语设置</span>
                </div>
                <NIcon :size="16" class="collapse-chevron" :class="{ expanded: !collapsed.termSettings }">
                  <ExpandMoreOutlined />
                </NIcon>
              </div>
              <div class="settings-group-body" :class="{ collapsed: collapsed.termSettings }">
                <div class="settings-group-body-inner">
                  <div class="setting-row">
                    <div class="setting-info">
                      <span class="setting-label">术语审查</span>
                      <span class="setting-description">翻译后自动检查术语是否正确应用</span>
                    </div>
                    <NSwitch
                      :value="aiSettings.termAuditEnabled"
                      @update:value="(v: boolean) => { aiSettings = { ...aiSettings, termAuditEnabled: v } }"
                    />
                  </div>
                  <div class="setting-row">
                    <div class="setting-info">
                      <span class="setting-label">自然翻译模式</span>
                      <span class="setting-description">先让 LLM 自然应用术语，失败后回退到占位符方案</span>
                    </div>
                    <NSwitch
                      :value="aiSettings.naturalTranslationMode"
                      @update:value="(v: boolean) => { aiSettings = { ...aiSettings, naturalTranslationMode: v } }"
                    />
                  </div>
                  <div class="setting-row">
                    <div class="setting-info">
                      <span class="setting-label">自动术语提取</span>
                      <span class="setting-description">翻译过程中自动提取专有名词、角色名等术语</span>
                    </div>
                    <NSwitch
                      :value="aiSettings.glossaryExtractionEnabled"
                      @update:value="(v: boolean) => { aiSettings = { ...aiSettings, glossaryExtractionEnabled: v } }"
                    />
                  </div>
                </div>
              </div>
            </div>

            <!-- Translation Memory Group -->
            <div class="settings-group">
              <div class="settings-group-header" @click.stop="collapsed.tmSettings = !collapsed.tmSettings">
                <div class="settings-group-title">
                  <NIcon :size="14"><StorageOutlined /></NIcon>
                  <span>翻译记忆</span>
                </div>
                <NIcon :size="16" class="collapse-chevron" :class="{ expanded: !collapsed.tmSettings }">
                  <ExpandMoreOutlined />
                </NIcon>
              </div>
              <div class="settings-group-body" :class="{ collapsed: collapsed.tmSettings }">
                <div class="settings-group-body-inner">
                  <div class="setting-row">
                    <div class="setting-info">
                      <span class="setting-label">翻译记忆</span>
                      <span class="setting-description">缓存已翻译的文本，相同或相似文本复用翻译结果</span>
                    </div>
                    <NSwitch
                      :value="aiSettings.enableTranslationMemory"
                      @update:value="(v: boolean) => { aiSettings = { ...aiSettings, enableTranslationMemory: v } }"
                    />
                  </div>
                  <div v-if="aiSettings.enableTranslationMemory" class="sub-setting">
                    <span class="setting-label">模糊匹配阈值</span>
                    <NSlider
                      :value="aiSettings.fuzzyMatchThreshold"
                      @update:value="(v: number) => { aiSettings = { ...aiSettings, fuzzyMatchThreshold: v } }"
                      :min="0"
                      :max="100"
                      :step="1"
                      :tooltip="true"
                      :format-tooltip="(v: number) => v + '%'"
                      class="threshold-slider"
                    />
                    <span class="sub-setting-hint">阈值越高匹配越严格，建议 80-90</span>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- Mode Tabs -->
          <div class="mode-tabs">
            <button
              class="mode-tab"
              :class="{ active: activeMode === 'cloud' }"
              @click="setMode('cloud')"
            >
              <NIcon :size="16"><CloudOutlined /></NIcon>
              <span>云端 AI</span>
            </button>
            <button
              class="mode-tab"
              :class="{ active: activeMode === 'local' }"
              @click="setMode('local')"
            >
              <NIcon :size="16"><ComputerOutlined /></NIcon>
              <span>本地 AI</span>
            </button>
          </div>

          <!-- Cloud / Local Settings -->
          <Transition name="mode-switch" mode="out-in">
            <AiTranslationCard v-if="activeMode === 'cloud'" key="cloud" v-model="aiSettings" :embedded="true" />
            <LocalAiPanel v-else key="local" v-model="aiSettings" />
          </Transition>
        </div>
      </div>
    </div>

    <!-- Recent Translations -->
    <div
      v-if="(aiStore.stats?.recentTranslations?.length ?? 0) > 0"
      class="section-card"
      :class="{ 'is-collapsed': collapsed.recent }"
      style="animation-delay: 0.08s"
    >
      <div class="section-header collapsible" @click="collapsed.recent = !collapsed.recent">
        <h2 class="section-title">
          <span class="section-icon history">
            <NIcon :size="16"><HistoryOutlined /></NIcon>
          </span>
          最近翻译
        </h2>
        <span class="recent-count-badge">
          {{ aiStore.stats!.recentTranslations.length }} 条
        </span>
        <NIcon :size="18" class="collapse-chevron" :class="{ expanded: !collapsed.recent }">
          <ExpandMoreOutlined />
        </NIcon>
      </div>

      <div class="section-body" :class="{ collapsed: collapsed.recent }">
        <div class="section-body-inner">
          <div class="recent-list">
            <div
              v-for="(item, index) in aiStore.stats!.recentTranslations"
              :key="index"
              class="recent-item"
              :style="{ animationDelay: index * 0.03 + 's' }"
            >
              <span class="recent-index">{{ index + 1 }}</span>
              <div class="recent-body">
                <div class="recent-texts">
                  <span class="recent-original">{{ item.original }}</span>
                  <span class="recent-arrow">
                    <NIcon :size="12"><ArrowRightAltOutlined /></NIcon>
                  </span>
                  <span class="recent-translated">{{ item.translated }}</span>
                </div>
                <div class="recent-meta">
                  <span v-if="item.gameId" class="meta-tag game">{{ getGameName(item.gameId) }}</span>
                  <span class="meta-tag endpoint">{{ item.endpointName }}</span>
                  <span v-if="item.hasTerms" class="meta-tag term-tag">术语</span>
                  <span v-if="item.hasDnt" class="meta-tag dnt-tag">禁翻</span>
                  <span v-if="item.termAuditResult === 'phase1Pass'" class="meta-tag audit-pass">自然通过</span>
                  <span v-else-if="item.termAuditResult === 'phase2Pass'" class="meta-tag audit-pass">占位符通过</span>
                  <span v-else-if="item.termAuditResult === 'forceCorrected'" class="meta-tag audit-warn">强制修正</span>
                  <span v-else-if="item.termAuditResult === 'failed'" class="meta-tag audit-fail">审查失败</span>
                  <span v-if="item.translationSource === 'tmExact'" class="meta-tag tm-exact">TM 精确</span>
                  <span v-else-if="item.translationSource === 'tmFuzzy'" class="meta-tag tm-fuzzy">TM 模糊</span>
                  <span v-else-if="item.translationSource === 'tmPattern'" class="meta-tag tm-pattern">动态模式</span>
                  <span class="meta-tag">{{ item.tokensUsed }} tok</span>
                  <span class="meta-tag">{{ formatTime(item.responseTimeMs) }}</span>
                  <span class="meta-tag time">{{ formatRelativeTime(item.timestamp) }}</span>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Error Log -->
    <div
      v-if="(aiStore.stats?.recentErrors?.length ?? 0) > 0"
      class="section-card"
      :class="{ 'is-collapsed': collapsed.errors }"
      style="animation-delay: 0.09s"
    >
      <div class="section-header collapsible" @click="collapsed.errors = !collapsed.errors">
        <h2 class="section-title">
          <span class="section-icon error-log">
            <NIcon :size="16"><ErrorOutlineOutlined /></NIcon>
          </span>
          错误日志
        </h2>
        <span class="error-count-badge">{{ aiStore.stats!.recentErrors.length }} 条</span>
        <NIcon :size="18" class="collapse-chevron" :class="{ expanded: !collapsed.errors }">
          <ExpandMoreOutlined />
        </NIcon>
      </div>

      <div class="section-body" :class="{ collapsed: collapsed.errors }">
        <div class="section-body-inner">
          <div class="error-list">
            <div
              v-for="err in [...aiStore.stats!.recentErrors].reverse()"
              :key="err.timestamp + err.message"
              class="error-item"
            >
              <div class="error-message">{{ err.message }}</div>
              <div class="error-meta">
                <span v-if="err.gameId" class="error-game-tag">{{ getGameName(err.gameId) }}</span>
                <span v-if="err.endpointName">{{ err.endpointName }}</span>
                <span>{{ formatRelativeTime(err.timestamp) }}</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

  </div>
</template>

<style scoped>
.ai-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
  animation: fadeIn 0.3s ease;
}

.page-title-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  animation: slideUp 0.4s var(--ease-out) backwards;
}

.enable-toggle {
  display: flex;
  align-items: center;
  gap: 10px;
}

.toggle-label {
  font-size: 13px;
  font-weight: 500;
  color: var(--text-2);
}


/* ===== Metrics Strip ===== */
.metrics-strip {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(130px, 1fr));
  gap: 10px;
  animation: slideUp 0.45s var(--ease-out) backwards;
}

.compact-metrics {
  animation: none;
}

.metric-pill {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 14px 18px;
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  transition: all 0.25s ease;
  box-shadow: var(--shadow-card-rest);
  position: relative;
  overflow: hidden;
}

.metric-pill::before {
  content: '';
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  height: 2px;
  background: linear-gradient(90deg, transparent, var(--accent), transparent);
  opacity: 0;
  transition: opacity 0.3s ease;
}

.metric-pill:hover {
  border-color: var(--border-hover);
  transform: translateY(-1px);
}

.metric-pill:hover::before {
  opacity: 0.5;
}

.metric-pill.is-active {
  border-color: var(--accent-border);
  background: color-mix(in srgb, var(--accent) 4%, var(--bg-card));
}

.metric-pill.has-error {
  border-color: color-mix(in srgb, var(--danger) 15%, transparent);
  background: color-mix(in srgb, var(--danger) 4%, var(--bg-card));
}

.metric-pill.has-error .metric-value {
  color: var(--danger);
}

.metric-icon {
  width: 32px;
  height: 32px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 8px;
  background: var(--accent-soft);
  color: var(--accent);
  flex-shrink: 0;
}

.metric-pill.rate-good .metric-icon {
  background: color-mix(in srgb, var(--success) 10%, transparent);
  color: var(--success);
}

.metric-pill.rate-warn .metric-icon {
  background: color-mix(in srgb, var(--warning) 10%, transparent);
  color: var(--warning);
}

.metric-pill.rate-bad .metric-icon {
  background: color-mix(in srgb, var(--danger) 10%, transparent);
  color: var(--danger);
}

.metric-pill.has-error .metric-icon {
  background: color-mix(in srgb, var(--danger) 10%, transparent);
  color: var(--danger);
}

.metric-icon.active-pulse {
  animation: pulse 2s ease-in-out infinite;
}

.metric-icon.error {
  background: color-mix(in srgb, var(--danger) 10%, transparent);
  color: var(--danger);
}

.metric-data {
  display: flex;
  flex-direction: column;
  gap: 1px;
  min-width: 0;
}

.metric-value {
  font-family: var(--font-display);
  font-size: 18px;
  font-weight: 600;
  color: var(--text-1);
  letter-spacing: -0.02em;
  line-height: 1.2;
}

.metric-value small {
  font-size: 13px;
  font-weight: 500;
  opacity: 0.6;
}

.metric-label {
  font-size: 11px;
  color: var(--text-3);
  font-weight: 500;
  letter-spacing: 0.02em;
}

.metric-label-help {
  cursor: help;
  border-bottom: 1px dashed var(--text-3);
}

/* Section card: position needed for .is-translating::after pseudo-element */
.section-card {
  position: relative;
}

/* Active translating glow */
.section-card.is-translating {
  border-color: var(--accent-border);
  box-shadow: 0 0 40px color-mix(in srgb, var(--accent) 6%, transparent),
              var(--shadow-card-rest);
  animation: slideUp 0.5s var(--ease-out) backwards, translating-glow 3s ease-in-out infinite;
}

@keyframes translating-glow {
  0%, 100% {
    box-shadow: 0 0 30px color-mix(in srgb, var(--accent) 5%, transparent),
                var(--shadow-card-rest);
  }
  50% {
    box-shadow: 0 0 50px color-mix(in srgb, var(--accent) 10%, transparent),
                0 0 80px color-mix(in srgb, var(--accent) 3%, transparent),
                var(--shadow-card-rest);
  }
}

/* Scanning line for active state */
.section-card.is-translating::after {
  content: '';
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  height: 1px;
  background: linear-gradient(90deg, transparent, var(--accent), transparent);
  animation: scan-line 2.5s ease-in-out infinite;
}

@keyframes scan-line {
  0% { opacity: 0; transform: scaleX(0.3); }
  50% { opacity: 0.8; transform: scaleX(1); }
  100% { opacity: 0; transform: scaleX(0.3); }
}

/* ===== Connection Badge ===== */
.header-actions {
  gap: 10px;
}

.connection-badge {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  padding: 5px 12px;
  border-radius: 20px;
  font-size: 12px;
  font-weight: 500;
  color: var(--text-3);
  background: var(--bg-muted);
  border: 1px solid var(--border);
  transition: all 0.3s ease;
}

.connection-badge.active,
.connection-badge.idle {
  color: var(--accent);
  background: var(--accent-soft);
  border-color: var(--accent-border);
}

.connection-badge.idle {
  opacity: 0.7;
}

.connection-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: currentColor;
  flex-shrink: 0;
}

.connection-badge.active .connection-dot {
  animation: pulse-dot 2s ease-in-out infinite;
}

@keyframes pulse-dot {
  0%, 100% { opacity: 1; box-shadow: 0 0 0 0 currentColor; }
  50% { opacity: 0.7; box-shadow: 0 0 0 4px transparent; }
}

.connection-time {
  color: var(--text-3);
  font-weight: 400;
  padding-left: 4px;
  border-left: 1px solid var(--border);
  margin-left: 2px;
}

/* ===== Current Game Indicator ===== */
.current-game-indicator {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 16px;
  background: color-mix(in srgb, var(--accent) 6%, transparent);
  border: 1px solid var(--accent-border);
  border-radius: var(--radius-md);
  margin-bottom: 12px;
  color: var(--accent);
  font-size: 13px;
}

.current-game-label {
  font-weight: 500;
  color: var(--text-3);
}

.current-game-name {
  font-weight: 600;
  color: var(--text-1);
}

/* ===== Pipeline Flow ===== */
.pipeline-flow {
  position: relative;
  margin-bottom: 16px;
  padding: 4px 0;
}

/* SVG Connection Overlay */
.pipeline-svg {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  pointer-events: none;
  z-index: 1;
  overflow: visible;
}

.connection-path {
  fill: none;
  stroke: color-mix(in srgb, var(--accent) 15%, var(--border));
  stroke-width: 2;
  stroke-linecap: round;
  transition: stroke 0.4s ease;
}

.connection-path.active {
  stroke: color-mix(in srgb, var(--accent) 35%, var(--border));
}

.connection-path.tm-path {
  stroke: color-mix(in srgb, var(--accent) 10%, var(--border));
}

.connection-path.tm-path.active {
  stroke: color-mix(in srgb, var(--accent) 25%, var(--border));
}

/* Animated particles */
.flow-particle {
  fill: var(--accent);
  opacity: 0;
  offset-rotate: 0deg;
  animation: travel-along-path linear infinite;
}

.flow-particle.tm-particle {
  fill: color-mix(in srgb, var(--accent) 65%, var(--text-2));
}

@keyframes travel-along-path {
  0% {
    offset-distance: 0%;
    opacity: 0;
  }
  8% {
    opacity: 0.95;
  }
  85% {
    opacity: 0.95;
  }
  100% {
    offset-distance: 100%;
    opacity: 0;
  }
}

/* Horizontal Pipeline Layout */
.pipeline-hbox {
  display: flex;
  align-items: stretch;
  gap: 0;
  width: 100%;
  position: relative;
  z-index: 2;
}

/* Root Node */
.pipeline-root {
  flex-shrink: 0;
  display: flex;
  align-items: center;
}

.pipeline-node {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 14px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  transition: all 0.3s ease;
  position: relative;
  flex: 1 1 0;
  min-width: 0;
}

.pipeline-node:hover {
  border-color: var(--border-hover);
  background: color-mix(in srgb, var(--accent) 2%, var(--bg-subtle));
}

.root-node {
  border-left: 3px solid var(--accent);
  padding: 14px 18px;
  flex: 0 0 auto;
  background: color-mix(in srgb, var(--accent) 3%, var(--bg-subtle));
}

.node-icon {
  width: 32px;
  height: 32px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 8px;
  flex-shrink: 0;
  background: var(--accent-soft);
  color: var(--accent);
}

.root-icon {
  width: 38px;
  height: 38px;
  border-radius: 10px;
}

.node-data {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.node-value {
  font-family: var(--font-display);
  font-size: 18px;
  font-weight: 600;
  color: var(--text-1);
  letter-spacing: -0.02em;
  line-height: 1.2;
}

.node-value.root-value {
  font-size: 24px;
}

.node-value small {
  font-size: 12px;
  font-weight: 400;
  color: var(--text-3);
}

.node-label {
  font-size: 11px;
  font-weight: 500;
  color: var(--text-3);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

/* Fork Branches (right side, stacked vertically) */
.pipeline-branches {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding-left: 16px;
  min-width: 0;
}

.pipeline-branch {
  min-width: 0;
}

.branch-header {
  display: flex;
  align-items: center;
  gap: 5px;
  font-size: 11px;
  font-weight: 600;
  color: var(--accent);
  letter-spacing: 0.04em;
  text-transform: uppercase;
  margin-bottom: 6px;
  padding-top: 2px;
}

.tm-header {
  color: color-mix(in srgb, var(--accent) 65%, var(--text-2));
}

.branch-nodes {
  display: flex;
  align-items: center;
  gap: 8px;
}

/* Pipeline node states */
.pipeline-node.dimmed {
  opacity: 0.5;
}

.pipeline-node.is-active {
  background: color-mix(in srgb, var(--accent) 5%, var(--bg-subtle));
  border-color: var(--accent-border);
  box-shadow: inset 0 0 24px color-mix(in srgb, var(--accent) 5%, transparent);
  animation: node-glow 3s ease-in-out infinite;
}

@keyframes node-glow {
  0%, 100% { box-shadow: inset 0 0 24px color-mix(in srgb, var(--accent) 4%, transparent); }
  50% { box-shadow: inset 0 0 36px color-mix(in srgb, var(--accent) 8%, transparent); }
}

.pipeline-node.is-active .translating-icon :deep(.n-icon) {
  animation: spin-icon 2s linear infinite;
}

@keyframes spin-icon {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

.done-node {
  border-color: color-mix(in srgb, var(--success) 20%, var(--border));
}

.done-icon {
  background: color-mix(in srgb, var(--success) 10%, transparent);
  color: var(--success);
}

.tm-icon {
  background: color-mix(in srgb, var(--accent) 8%, transparent);
  color: color-mix(in srgb, var(--accent) 65%, var(--text-2));
}

.tm-done-node {
  background: color-mix(in srgb, var(--accent) 4%, var(--bg-subtle));
  border-color: color-mix(in srgb, var(--accent) 15%, var(--border));
}

/* TM node with inline chips */
.tm-node {
  gap: 8px;
  background: color-mix(in srgb, var(--accent) 3%, var(--bg-subtle));
  border-color: color-mix(in srgb, var(--accent) 12%, var(--border));
}

.tm-chips-inline {
  display: flex;
  align-items: center;
  gap: 10px;
}

.tm-inline-chip {
  font-size: 13px;
  color: var(--text-3);
  white-space: nowrap;
}

.tm-inline-chip strong {
  font-family: var(--font-display);
  font-size: 16px;
  font-weight: 600;
  color: color-mix(in srgb, var(--accent) 65%, var(--text-2));
  margin-right: 2px;
}

/* Extraction Node (inline in LLM flow) */
.extraction-node {
  border-color: color-mix(in srgb, var(--accent) 15%, var(--border));
  background: color-mix(in srgb, var(--accent) 2%, var(--bg-subtle));
}

.extraction-icon {
  background: color-mix(in srgb, var(--accent) 12%, transparent);
  color: var(--accent);
}

.extraction-details {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-left: auto;
  flex-wrap: wrap;
}

.sub-stat {
  font-family: var(--font-mono);
  font-size: 11px;
  font-weight: 600;
  color: var(--text-2);
  background: var(--bg-muted);
  padding: 2px 8px;
  border-radius: 4px;
  white-space: nowrap;
}

.sub-stat.active {
  color: var(--accent);
  background: var(--accent-soft);
}

.sub-stat.error {
  color: var(--danger);
  background: color-mix(in srgb, var(--danger) 10%, transparent);
}

/* Tooltip */
.pipeline-tooltip {
  max-width: 260px;
  font-size: 13px;
  line-height: 1.5;
  color: var(--text-2);
}

/* ===== Error Bar ===== */
.error-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 16px;
  border-radius: var(--radius-md);
  font-size: 13px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  color: var(--text-3);
  transition: all 0.3s ease;
}

.error-bar.has-errors {
  background: color-mix(in srgb, var(--danger) 5%, transparent);
  border-color: color-mix(in srgb, var(--danger) 12%, transparent);
  color: var(--danger);
}

.error-bar-left {
  display: flex;
  align-items: center;
  gap: 8px;
}

.error-bar-text {
  font-weight: 500;
}

.error-bar-right {
  display: flex;
  align-items: center;
  gap: 10px;
}

.success-track {
  width: 80px;
  height: 4px;
  background: var(--bg-muted);
  border-radius: 2px;
  overflow: hidden;
}

.success-fill {
  height: 100%;
  background: var(--success);
  border-radius: 2px;
  transition: width 0.6s var(--ease-out);
}

.success-fill.warn {
  background: var(--warning);
}

.success-fill.bad {
  background: var(--danger);
}

.error-bar-rate {
  font-size: 12px;
  font-weight: 600;
  font-family: var(--font-mono);
  opacity: 0.8;
}

/* ===== Audit Strip ===== */
.audit-strip {
  margin-top: 14px;
  padding-top: 14px;
  border-top: 1px solid var(--border);
}

.audit-header {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  font-weight: 600;
  color: var(--text-3);
  text-transform: uppercase;
  letter-spacing: 0.06em;
  margin-bottom: 10px;
}

.audit-rate {
  margin-left: auto;
  font-family: var(--font-mono);
  font-size: 13px;
  font-weight: 700;
  color: var(--text-2);
  background: var(--bg-subtle);
  padding: 2px 10px;
  border-radius: 10px;
}

.audit-rate small {
  font-size: 11px;
  font-weight: 600;
}

.audit-rate.good {
  color: var(--success);
  background: color-mix(in srgb, var(--success) 10%, transparent);
}

.audit-rate.warn {
  color: var(--warning);
  background: color-mix(in srgb, var(--warning) 10%, transparent);
}

.audit-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.audit-chip {
  display: flex;
  align-items: baseline;
  gap: 5px;
  padding: 6px 14px;
  background: var(--bg-subtle);
  border-radius: var(--radius-sm);
  cursor: default;
  transition: background 0.2s ease;
}

.audit-chip:hover {
  background: var(--bg-muted);
}

.audit-chip-value {
  font-size: 16px;
  font-weight: 700;
  color: var(--text-1);
  font-family: var(--font-mono);
}

.audit-chip-label {
  font-size: 12px;
  color: var(--text-3);
  white-space: nowrap;
}

.audit-chip.good .audit-chip-value {
  color: var(--success);
}

.audit-chip.warn .audit-chip-value {
  color: var(--warning);
}

/* Audit Progress Bar */
.audit-bar {
  display: flex;
  height: 4px;
  border-radius: 2px;
  overflow: hidden;
  margin-top: 10px;
  background: var(--bg-muted);
  gap: 1px;
}

.audit-seg {
  min-width: 0;
  border-radius: 2px;
  transition: flex 0.6s var(--ease-out);
}

.audit-seg.phase1 { background: var(--success); }
.audit-seg.phase2 { background: var(--accent); }
.audit-seg.forced { background: var(--warning); }


/* ===== Recent Translations ===== */
.recent-count-badge {
  font-size: 12px;
  font-weight: 500;
  color: var(--text-3);
  background: var(--bg-muted);
  padding: 2px 10px;
  border-radius: 12px;
}

.recent-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
  max-height: 360px;
  overflow-y: auto;
}

.recent-item {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 12px 16px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  transition: border-color 0.2s ease, background 0.2s ease;
  animation: fadeIn 0.3s ease backwards;
}

.recent-item:hover {
  border-color: var(--border-hover);
  background: var(--bg-subtle-hover);
}

.recent-index {
  font-family: var(--font-mono);
  font-size: 11px;
  font-weight: 600;
  color: var(--text-3);
  opacity: 0.5;
  min-width: 20px;
  text-align: right;
  padding-top: 2px;
  flex-shrink: 0;
}

.recent-body {
  flex: 1;
  min-width: 0;
}

.recent-texts {
  display: flex;
  align-items: baseline;
  gap: 8px;
  margin-bottom: 6px;
  flex-wrap: wrap;
}

.recent-original {
  font-size: 13px;
  color: var(--text-2);
  max-width: 300px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.recent-arrow {
  color: var(--accent);
  flex-shrink: 0;
  display: flex;
  align-items: center;
  opacity: 0.5;
}

.recent-translated {
  font-size: 13px;
  font-weight: 500;
  color: var(--text-1);
  max-width: 300px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.recent-meta {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}

.meta-tag {
  font-size: 11px;
  color: var(--text-3);
  padding: 1px 6px;
  background: var(--bg-muted);
  border-radius: 4px;
  white-space: nowrap;
}

.meta-tag.game {
  color: var(--text-1);
  background: color-mix(in srgb, var(--accent) 8%, var(--bg-muted));
  font-weight: 500;
}

.meta-tag.endpoint {
  color: var(--accent);
  background: var(--accent-soft);
}

.meta-tag.term-tag {
  color: var(--accent);
  background: color-mix(in srgb, var(--accent) 12%, transparent);
  font-weight: 500;
}

.meta-tag.dnt-tag {
  color: var(--warning);
  background: color-mix(in srgb, var(--warning) 12%, transparent);
  font-weight: 500;
}

.meta-tag.audit-pass {
  color: var(--success);
  background: color-mix(in srgb, var(--success) 12%, transparent);
}

.meta-tag.audit-warn {
  color: var(--warning);
  background: color-mix(in srgb, var(--warning) 12%, transparent);
}

.meta-tag.audit-fail {
  color: var(--danger);
  background: color-mix(in srgb, var(--danger) 12%, transparent);
}

.meta-tag.tm-exact,
.meta-tag.tm-fuzzy,
.meta-tag.tm-pattern {
  color: var(--accent);
  background: color-mix(in srgb, var(--accent) 12%, transparent);
  font-weight: 500;
}

.meta-tag.time {
  color: var(--text-3);
  background: transparent;
  padding: 1px 0;
  opacity: 0.7;
}

/* ===== Error Log ===== */
.section-icon.error-log {
  background: color-mix(in srgb, var(--danger) 10%, transparent);
  color: var(--danger);
}

.error-count-badge {
  font-size: 12px;
  font-weight: 500;
  color: var(--danger);
  background: color-mix(in srgb, var(--danger) 8%, transparent);
  padding: 2px 10px;
  border-radius: 12px;
}

.error-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
  max-height: 300px;
  overflow-y: auto;
}

.error-item {
  padding: 10px 14px;
  background: color-mix(in srgb, var(--danger) 4%, transparent);
  border: 1px solid color-mix(in srgb, var(--danger) 12%, transparent);
  border-radius: var(--radius-md);
}

.error-message {
  font-size: 13px;
  color: var(--text-1);
  margin-bottom: 4px;
  word-break: break-word;
}

.error-meta {
  display: flex;
  gap: 12px;
  font-size: 11px;
  color: var(--text-3);
}

.error-game-tag {
  font-weight: 500;
  color: var(--text-2);
}


/* ===== Pipeline Settings ===== */
.pipeline-settings {
  display: flex;
  flex-direction: column;
  gap: 12px;
  margin-bottom: 20px;
  padding-bottom: 20px;
  border-bottom: 1px solid var(--border);
  transition: gap 0.3s var(--ease-out), margin-bottom 0.3s var(--ease-out), padding-bottom 0.3s var(--ease-out);
}

.pipeline-settings.all-collapsed {
  gap: 8px;
  margin-bottom: 8px;
  padding-bottom: 8px;
}

.setting-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
.setting-info { flex: 1; }
.setting-label { font-size: 14px; font-weight: 500; display: block; }
.setting-description { font-size: 12px; color: var(--text-3); display: block; margin-top: 2px; }

.sub-setting {
  padding-left: 16px;
}

.threshold-slider {
  max-width: 320px;
}

.sub-setting-hint {
  font-size: 12px;
  color: var(--text-3);
  margin-top: 2px;
  display: block;
}

/* ===== Settings Groups ===== */
.settings-group {
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  overflow: hidden;
  transition: border-color 0.2s ease;
}

.settings-group:hover {
  border-color: var(--border-hover);
}

.settings-group-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 16px;
  cursor: pointer;
  user-select: none;
  background: var(--bg-subtle);
  transition: background 0.2s ease;
}

.settings-group-header:hover {
  background: var(--bg-subtle-hover);
}

.settings-group-title {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  font-weight: 600;
  color: var(--text-2);
  letter-spacing: 0.02em;
}

.settings-group-body {
  display: grid;
  grid-template-rows: 1fr;
  transition: grid-template-rows 0.3s var(--ease-out), opacity 0.25s ease;
  opacity: 1;
}

.settings-group-body.collapsed {
  grid-template-rows: 0fr;
  opacity: 0;
}

.settings-group-body-inner {
  overflow: hidden;
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 16px;
  transition: padding 0.3s var(--ease-out);
}

.settings-group-body.collapsed .settings-group-body-inner {
  padding-top: 0;
  padding-bottom: 0;
}

/* ===== Mode Tabs ===== */
.mode-tabs {
  display: flex;
  gap: 4px;
  padding: 4px;
  background: var(--bg-subtle);
  border-radius: var(--radius-md);
  margin-bottom: 20px;
}

.mode-tab {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 10px 16px;
  border: none;
  background: transparent;
  color: var(--text-3);
  font-size: 14px;
  font-weight: 500;
  font-family: var(--font-body);
  cursor: pointer;
  border-radius: calc(var(--radius-md) - 2px);
  transition: all 0.2s ease;
}

.mode-tab:hover {
  color: var(--text-2);
  background: color-mix(in srgb, var(--bg-card) 50%, transparent);
}

.mode-tab.active {
  background: var(--bg-card);
  color: var(--accent);
  box-shadow: 0 1px 3px color-mix(in srgb, #000 5%, transparent);
  font-weight: 600;
}

/* ===== Transition Animations ===== */
.mode-switch-enter-active,
.mode-switch-leave-active {
  transition: all 0.3s var(--ease-out);
}
.mode-switch-enter-from {
  opacity: 0;
  transform: translateY(12px);
}
.mode-switch-leave-to {
  opacity: 0;
  transform: translateY(-8px);
}

/* ===== Extraction Select ===== */
.extraction-select {
  max-width: 320px;
}

/* ===== Cache Stats ===== */
.cache-progress {
  margin: 12px 0;
}

.cache-miss-item {
  padding: 8px 0;
  border-bottom: 1px solid var(--border);
}

.cache-miss-item:last-child {
  border-bottom: none;
}

.cache-miss-row {
  display: flex;
  gap: 8px;
  font-size: 12px;
  margin: 3px 0;
}

.cache-miss-label {
  color: var(--text-3);
  white-space: nowrap;
  flex-shrink: 0;
}

.cache-miss-code {
  word-break: break-all;
  font-family: var(--font-mono);
  font-size: 11px;
  color: var(--text-2);
  background: var(--bg-subtle);
  padding: 1px 6px;
  border-radius: 4px;
}

/* ===== Responsive ===== */
@media (max-width: 768px) {
  .pipeline-svg {
    display: none;
  }

  .pipeline-hbox {
    flex-direction: column;
    gap: 12px;
  }

  .pipeline-branches {
    padding-left: 0;
  }

  .branch-nodes {
    flex-wrap: wrap;
    gap: 6px;
  }

  .tm-chips-inline {
    gap: 6px;
  }

  .audit-chips {
    flex-wrap: wrap;
  }

  .page-title-row {
    flex-direction: column;
    align-items: flex-start;
    gap: 12px;
  }

  .section-header {
    flex-wrap: wrap;
    gap: 10px;
  }

  .header-actions {
    flex-wrap: wrap;
    gap: 8px;
  }

  .connection-badge {
    font-size: 11px;
    padding: 4px 10px;
  }

  .connection-time {
    display: none;
  }

  .error-bar {
    flex-direction: column;
    gap: 8px;
    align-items: flex-start;
  }

  .error-bar-right {
    width: 100%;
  }

  .success-track {
    flex: 1;
  }

  .extraction-select {
    max-width: none;
  }

  .recent-original,
  .recent-translated {
    max-width: none;
  }
}

@media (max-width: 480px) {
  .metrics-strip {
    gap: 6px;
  }

  .metric-pill {
    padding: 10px 12px;
    gap: 8px;
  }

  .metric-icon {
    width: 28px;
    height: 28px;
    border-radius: 6px;
  }

  .metric-value {
    font-size: 15px;
  }

  .pipeline-node {
    padding: 8px 12px;
    gap: 8px;
  }

  .node-icon {
    width: 26px;
    height: 26px;
    border-radius: 6px;
  }

  .root-icon {
    width: 32px;
    height: 32px;
  }

  .node-value {
    font-size: 15px;
  }

  .node-value.root-value {
    font-size: 18px;
  }

  .extraction-details {
    gap: 4px;
  }

  .sub-stat {
    font-size: 10px;
  }

  .recent-texts {
    flex-direction: column;
    gap: 4px;
  }

  .recent-arrow {
    display: none;
  }

  .recent-meta {
    flex-wrap: wrap;
    gap: 4px;
  }

  .recent-index {
    display: none;
  }
}
</style>
