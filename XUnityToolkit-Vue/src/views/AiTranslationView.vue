<script setup lang="ts">
import { ref, reactive, computed, watch, onMounted, onUnmounted, onDeactivated, nextTick } from 'vue'
import {
  NIcon,
  NButton,
  NSwitch,
  NSelect,
  NTag,
  NProgress,
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
import { settingsApi, translationMemoryApi } from '@/api/games'
import type { AppSettings, AiTranslationSettings, TranslationMemoryStats } from '@/api/types'
import AiTranslationCard from '@/components/settings/AiTranslationCard.vue'
import LocalAiPanel from '@/components/settings/LocalAiPanel.vue'
import { useAutoSave } from '@/composables/useAutoSave'

defineOptions({ name: 'AiTranslationView' })

const collapsed = reactive({
  cache: true,
  recent: true,
  errors: true,
  extraction: true,
})

const aiStore = useAiTranslationStore()
const gamesStore = useGamesStore()
const message = useMessage()

const DEFAULT_AI_TRANSLATION: AiTranslationSettings = {
  enabled: true,
  activeMode: 'cloud',
  maxConcurrency: 4,
  port: 51821,
  systemPrompt:
    '你是一名专业的游戏文本翻译家。将以下 {from} 文本翻译为 {to}。\n\n' +
    '要求：\n' +
    '1. 仅返回翻译结果的 JSON 数组，保持与输入相同的顺序和数量。不要添加任何解释、说明或 markdown 格式。\n' +
    '2. 不要增加或省略信息，不擅自添加原文中没有的主语、代词或句子。\n' +
    '3. 保持与原文一致的格式：尽量保留行数、标点和特殊符号，仅在必要时做符合目标语言语法的微调。\n' +
    '4. 严格保留所有占位符、控制符和变量名（如 {0}、%s、%d、<b>、</b>、\\n、【SPECIAL_*】等），不要翻译、删除或改动其位置。\n' +
    '5. 若待翻译内容仅为单个字母、数字、符号或空字符串，请原样返回。\n' +
    '6. 翻译准确自然，忠于原文。结合上下文正确使用人称代词和称呼，使对白自然符合游戏语境，不随意改变说话人。\n' +
    '7. 在忠实原文含义的前提下，使译文符合目标语言的表达习惯，并考虑游戏类型和角色性格，力求达到"信、达、雅"。\n\n' +
    '输入示例：["Hello","World"] → 输出：["你好","世界"]',
  temperature: 0.3,
  contextSize: 10,
  localContextSize: 0,
  endpoints: [],
  glossaryExtractionEnabled: false,
  glossaryExtractionEndpointId: undefined,
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

const extractionEndpointOptions = computed(() => {
  const endpoints = aiSettings.value.endpoints ?? []
  return [
    { label: '自动选择（第一个可用）', value: '' },
    ...endpoints
      .filter(e => e.enabled && e.apiKey)
      .map(e => ({ label: e.name || e.provider, value: e.id })),
  ]
})

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

const tmStats = ref<TranslationMemoryStats | null>(null)

const tmHitRate = computed(() => {
  const s = tmStats.value
  if (!s) return null
  const total = s.exactHits + s.fuzzyHits + s.patternHits + s.misses
  if (total === 0) return null
  return (((s.exactHits + s.fuzzyHits + s.patternHits) / total) * 100).toFixed(1)
})

const activeMode = computed(() => aiSettings.value.activeMode ?? 'cloud')
const isLocalMode = computed(() => activeMode.value === 'local')

function setMode(mode: 'cloud' | 'local') {
  aiSettings.value = { ...aiSettings.value, activeMode: mode }
}

const { enable: enableAutoSave } = useAutoSave(
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

async function loadTmStats() {
  const gameId = aiStore.stats?.currentGameId
  if (gameId) {
    try {
      tmStats.value = await translationMemoryApi.getStats(gameId)
    } catch { tmStats.value = null }
  }
}

watch(() => aiStore.stats?.currentGameId, (id) => {
  if (id) loadTmStats()
})

onMounted(async () => {
  await aiStore.connect()
  await Promise.all([
    aiStore.fetchStats(),
    aiStore.fetchCacheStats(),
    loadSettings(),
    gamesStore.games.length === 0 ? gamesStore.fetchGames() : Promise.resolve(),
  ])
  await loadTmStats()
})

onDeactivated(() => {
  aiStore.disconnect()
})

onUnmounted(() => {
  aiStore.disconnect()
})
</script>

<template>
  <div class="ai-page">
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
          <span class="section-icon status">
            <NIcon :size="16">
              <DashboardOutlined />
            </NIcon>
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
      <div class="stats-group-label">处理进度</div>
      <div class="pipeline-flow">
        <div class="pipeline-stage queued-stage">
          <div class="stage-icon queued">
            <NIcon :size="18"><HourglassEmptyOutlined /></NIcon>
          </div>
          <div class="stage-content">
            <span class="stage-value">{{ aiStore.stats?.queued ?? 0 }}</span>
            <span class="stage-label">排队等待</span>
          </div>
        </div>

        <div class="pipeline-arrow" :class="{ active: isActivelyTranslating }">
          <NIcon :size="18"><ArrowRightAltOutlined /></NIcon>
        </div>

        <div class="pipeline-stage translating-stage" :class="{ 'is-active': isActivelyTranslating }">
          <div class="stage-icon translating">
            <NIcon :size="18"><SyncOutlined /></NIcon>
          </div>
          <div class="stage-content">
            <span class="stage-value">{{ aiStore.stats?.translating ?? 0 }}<span v-if="aiStore.stats?.maxConcurrency" class="stage-max">/{{ aiStore.stats.maxConcurrency }}</span></span>
            <span class="stage-label">正在翻译</span>
          </div>
        </div>

        <div class="pipeline-arrow" :class="{ active: isActivelyTranslating }">
          <NIcon :size="18"><ArrowRightAltOutlined /></NIcon>
        </div>

        <div class="pipeline-stage translated-stage">
          <div class="stage-icon translated">
            <NIcon :size="18"><TranslateOutlined /></NIcon>
          </div>
          <div class="stage-content">
            <span class="stage-value">{{ aiStore.stats?.totalTranslated ?? 0 }}</span>
            <span class="stage-label">已翻译</span>
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
      <div v-if="(aiStore.stats?.termMatchedTextCount ?? 0) > 0 || termAuditTotal > 0" class="term-audit-strip">
        <div class="stats-group-label">术语审查</div>
        <div class="metrics-strip">
          <div class="metric-pill">
            <div class="metric-data">
              <span class="metric-value">{{ aiStore.stats?.termMatchedTextCount ?? 0 }}</span>
              <NPopover trigger="hover" placement="top">
                <template #trigger>
                  <span class="metric-label metric-label-help">应用术语</span>
                </template>
                <div style="max-width: 240px; font-size: 13px">涉及术语或禁翻词的翻译文本总数</div>
              </NPopover>
            </div>
          </div>
          <div class="metric-pill rate-good">
            <div class="metric-data">
              <span class="metric-value">{{ aiStore.stats?.termAuditPhase1PassCount ?? 0 }}</span>
              <NPopover trigger="hover" placement="top">
                <template #trigger>
                  <span class="metric-label metric-label-help">自然通过</span>
                </template>
                <div style="max-width: 240px; font-size: 13px">在第一阶段（自然翻译模式，不使用占位符）中，LLM 直接正确应用术语的文本数</div>
              </NPopover>
            </div>
          </div>
          <div class="metric-pill">
            <div class="metric-data">
              <span class="metric-value">{{ aiStore.stats?.termAuditPhase2PassCount ?? 0 }}</span>
              <NPopover trigger="hover" placement="top">
                <template #trigger>
                  <span class="metric-label metric-label-help">占位符通过</span>
                </template>
                <div style="max-width: 240px; font-size: 13px">在第二阶段（使用占位符替换术语）中通过审查的文本数。通常说明 LLM 在自然模式下未能正确应用术语</div>
              </NPopover>
            </div>
          </div>
          <div class="metric-pill" :class="{ 'rate-warn': (aiStore.stats?.termAuditForceCorrectedCount ?? 0) > 0 }">
            <div class="metric-data">
              <span class="metric-value">{{ aiStore.stats?.termAuditForceCorrectedCount ?? 0 }}</span>
              <NPopover trigger="hover" placement="top">
                <template #trigger>
                  <span class="metric-label metric-label-help">强制修正</span>
                </template>
                <div style="max-width: 240px; font-size: 13px">两阶段审查都未通过后，系统直接将原文替换为术语译文进行强制修正的文本数。强制修正可能导致翻译不够自然</div>
              </NPopover>
            </div>
          </div>
          <div v-if="termAuditPassRate !== null" class="metric-pill" :class="{ 'rate-good': Number(termAuditPassRate) >= 80, 'rate-warn': Number(termAuditPassRate) < 80 }">
            <div class="metric-data">
              <span class="metric-value">{{ termAuditPassRate }}<small>%</small></span>
              <NPopover trigger="hover" placement="top">
                <template #trigger>
                  <span class="metric-label metric-label-help">自然通过率</span>
                </template>
                <div style="max-width: 240px; font-size: 13px">自然翻译阶段通过审查的比率。越高说明 LLM 对术语的理解和应用越好</div>
              </NPopover>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Translation Memory Stats -->
    <div v-if="tmStats && tmStats.entryCount > 0" class="section-card" style="animation-delay: 0.055s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><StorageOutlined /></NIcon>
          </span>
          翻译记忆
        </h2>
        <div class="header-actions">
          <NButton size="small" quaternary @click="loadTmStats()">刷新</NButton>
        </div>
      </div>
      <div class="metrics-strip">
        <div class="metric-pill">
          <div class="metric-data">
            <span class="metric-value">{{ tmStats.entryCount }}</span>
            <span class="metric-label">TM 条目数</span>
          </div>
        </div>
        <div class="metric-pill rate-good">
          <div class="metric-data">
            <span class="metric-value">{{ tmStats.exactHits }}</span>
            <span class="metric-label">精确命中</span>
          </div>
        </div>
        <div class="metric-pill">
          <div class="metric-data">
            <span class="metric-value">{{ tmStats.fuzzyHits }}</span>
            <span class="metric-label">模糊命中</span>
          </div>
        </div>
        <div class="metric-pill">
          <div class="metric-data">
            <span class="metric-value">{{ tmStats.patternHits }}</span>
            <span class="metric-label">模式命中</span>
          </div>
        </div>
        <div v-if="tmHitRate !== null" class="metric-pill" :class="{ 'rate-good': Number(tmHitRate) >= 60, 'rate-warn': Number(tmHitRate) < 60 && Number(tmHitRate) >= 30, 'rate-bad': Number(tmHitRate) < 30 }">
          <div class="metric-data">
            <span class="metric-value">{{ tmHitRate }}<small>%</small></span>
            <span class="metric-label">命中率</span>
          </div>
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
      <div class="metrics-strip">
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

    <!-- AI Settings with Mode Tabs -->
    <div class="section-card" style="animation-delay: 0.1s" v-if="settings">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon ai">
            <NIcon :size="16"><SmartToyOutlined /></NIcon>
          </span>
          AI 翻译设置
        </h2>
      </div>

      <!-- Translation Pipeline Settings -->
      <div class="pipeline-settings">
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

      <!-- Cloud Settings -->
      <Transition name="mode-switch" mode="out-in">
        <AiTranslationCard v-if="activeMode === 'cloud'" key="cloud" v-model="aiSettings" :embedded="true" />
        <LocalAiPanel v-else key="local" v-model="aiSettings" />
      </Transition>
    </div>

    <!-- Glossary Extraction (cloud mode only) -->
    <Transition name="card-slide">
      <div class="section-card" :class="{ 'is-collapsed': collapsed.extraction }" style="animation-delay: 0.12s" v-if="settings && !isLocalMode">
        <div class="section-header collapsible" @click="collapsed.extraction = !collapsed.extraction">
          <h2 class="section-title">
            <span class="section-icon extraction">
              <NIcon :size="16"><AutoFixHighOutlined /></NIcon>
            </span>
            自动术语提取
          </h2>
          <div class="header-actions">
            <NSwitch
              :value="aiSettings.glossaryExtractionEnabled"
              @update:value="(v: boolean) => { aiSettings = { ...aiSettings, glossaryExtractionEnabled: v } }"
              size="small"
              @click.stop
            />
            <NIcon :size="18" class="collapse-chevron" :class="{ expanded: !collapsed.extraction }">
              <ExpandMoreOutlined />
            </NIcon>
          </div>
        </div>

        <div class="section-body" :class="{ collapsed: collapsed.extraction }">
          <div class="section-body-inner">
        <div class="extraction-content">
          <p class="extraction-desc">
            翻译过程中自动从原文和译文中提取专有名词、角色名等术语，并加入对应游戏的术语表。
          </p>

          <Transition name="expand-fade">
            <div v-if="aiSettings.glossaryExtractionEnabled" class="extraction-config">
              <div class="extraction-field">
                <label class="extraction-label">提取使用的 AI 提供商</label>
                <NSelect
                  :value="aiSettings.glossaryExtractionEndpointId ?? ''"
                  @update:value="(v: string) => { aiSettings = { ...aiSettings, glossaryExtractionEndpointId: v || undefined } }"
                  :options="extractionEndpointOptions"
                  size="small"
                  class="extraction-select"
                />
              </div>

              <div v-if="extractionStats" class="extraction-stats">
                <div class="ext-metrics">
                  <div class="ext-metric-card">
                    <div class="ext-metric-icon">
                      <NIcon :size="16"><AutoFixHighOutlined /></NIcon>
                    </div>
                    <div class="ext-metric-data">
                      <span class="ext-metric-value">{{ extractionStats.totalExtracted }}</span>
                      <span class="ext-metric-label">已提取术语</span>
                    </div>
                  </div>
                  <div class="ext-metric-card">
                    <div class="ext-metric-icon">
                      <NIcon :size="16"><SyncOutlined /></NIcon>
                    </div>
                    <div class="ext-metric-data">
                      <span class="ext-metric-value">{{ extractionStats.totalExtractionCalls }}</span>
                      <span class="ext-metric-label">提取调用</span>
                    </div>
                  </div>
                  <div v-if="extractionStats.activeExtractions > 0" class="ext-metric-card is-active">
                    <div class="ext-metric-icon active">
                      <NIcon :size="16"><HourglassEmptyOutlined /></NIcon>
                    </div>
                    <div class="ext-metric-data">
                      <span class="ext-metric-value">{{ extractionStats.activeExtractions }}</span>
                      <span class="ext-metric-label">正在提取</span>
                    </div>
                  </div>
                  <div v-if="extractionStats.totalErrors > 0" class="ext-metric-card has-error">
                    <div class="ext-metric-icon error">
                      <NIcon :size="16"><ErrorOutlineOutlined /></NIcon>
                    </div>
                    <div class="ext-metric-data">
                      <span class="ext-metric-value">{{ extractionStats.totalErrors }}</span>
                      <span class="ext-metric-label">错误</span>
                    </div>
                  </div>
                </div>

                <div v-if="extractionStats.recentExtractions.length > 0" class="extraction-recent">
                  <span class="extraction-recent-title">最近提取</span>
                  <div
                    v-for="(item, index) in extractionStats.recentExtractions.slice().reverse().slice(0, 5)"
                    :key="index"
                    class="extraction-recent-item"
                  >
                    <div class="ext-recent-left">
                      <span class="ext-recent-game">{{ getGameName(item.gameId) }}</span>
                      <span class="ext-recent-time">{{ formatRelativeTime(item.timestamp) }}</span>
                    </div>
                    <span class="ext-recent-badge">+{{ item.termsExtracted }}</span>
                  </div>
                </div>
              </div>
            </div>
          </Transition>
        </div>
          </div>
        </div>
      </div>
    </Transition>
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
  grid-template-columns: repeat(4, 1fr);
  gap: 10px;
  animation: slideUp 0.45s var(--ease-out) backwards;
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

/* ===== Stats Group Label ===== */
.stats-group-label {
  font-size: 11px;
  font-weight: 600;
  color: var(--text-3);
  text-transform: uppercase;
  letter-spacing: 0.08em;
  margin-top: 8px;
  margin-bottom: 8px;
}

.stats-group-label:first-of-type {
  margin-top: 0;
}

/* ===== Pipeline Flow ===== */
.pipeline-flow {
  display: flex;
  align-items: stretch;
  gap: 0;
  margin-bottom: 16px;
}

.pipeline-stage {
  flex: 1;
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 18px 20px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  transition: all 0.3s ease;
}

.pipeline-arrow {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  flex-shrink: 0;
  color: var(--text-3);
  opacity: 0.25;
  transition: all 0.3s ease;
}

.pipeline-arrow.active {
  opacity: 0.6;
  color: var(--accent);
  animation: arrow-pulse 1.5s ease-in-out infinite;
}

@keyframes arrow-pulse {
  0%, 100% { opacity: 0.4; transform: translateX(0); }
  50% { opacity: 0.8; transform: translateX(3px); }
}

.pipeline-stage:hover {
  border-color: var(--border-hover);
  background: var(--bg-subtle-hover);
}

.queued-stage {
  border-left: 3px solid var(--accent);
  padding-left: 17px;
  opacity: 0.75;
}

.translating-stage {
  border-left: 3px solid var(--accent);
  padding-left: 17px;
}

.translating-stage.is-active {
  background: color-mix(in srgb, var(--accent) 4%, var(--bg-subtle));
  border-color: var(--accent-border);
  border-left-color: var(--accent);
  box-shadow: inset 0 0 20px color-mix(in srgb, var(--accent) 3%, transparent);
  animation: active-glow 3s ease-in-out infinite;
}

@keyframes active-glow {
  0%, 100% { box-shadow: inset 0 0 20px color-mix(in srgb, var(--accent) 3%, transparent); }
  50% { box-shadow: inset 0 0 30px color-mix(in srgb, var(--accent) 6%, transparent); }
}

.translated-stage {
  border-left: 3px solid var(--accent);
  padding-left: 17px;
  opacity: 0.9;
}

.stage-icon {
  width: 42px;
  height: 42px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 10px;
  flex-shrink: 0;
  background: var(--accent-soft);
  color: var(--accent);
}

.translating-stage.is-active .stage-icon :deep(.n-icon) {
  animation: spin-icon 2s linear infinite;
}

@keyframes spin-icon {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

.stage-content {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.stage-value {
  font-family: var(--font-display);
  font-size: 22px;
  font-weight: 600;
  color: var(--text-1);
  letter-spacing: -0.02em;
  line-height: 1.2;
}

.stage-max {
  font-size: 14px;
  font-weight: 400;
  color: var(--text-3);
}

.stage-label {
  font-size: 12px;
  font-weight: 500;
  color: var(--text-3);
  text-transform: uppercase;
  letter-spacing: 0.05em;
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

/* ===== Term Audit Strip ===== */
.term-audit-strip {
  margin-top: 12px;
  padding-top: 12px;
  border-top: 1px solid var(--border);
}

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

.meta-tag.tm-exact {
  color: var(--success);
  background: color-mix(in srgb, var(--success) 12%, transparent);
  font-weight: 500;
}

.meta-tag.tm-fuzzy {
  color: var(--accent);
  background: color-mix(in srgb, var(--accent) 12%, transparent);
  font-weight: 500;
}

.meta-tag.tm-pattern {
  color: #a855f7;
  background: color-mix(in srgb, #a855f7 12%, transparent);
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

/* ===== AI Icon ===== */
.section-icon.ai {
  background: var(--accent-soft);
  color: var(--accent);
}

/* ===== Pipeline Settings ===== */
.pipeline-settings {
  display: flex;
  flex-direction: column;
  gap: 12px;
  margin-bottom: 20px;
  padding-bottom: 20px;
  border-bottom: 1px solid var(--border);
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

/* Mode switch transition (cloud <-> local) */
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

/* Expand fade for extraction config */
.expand-fade-enter-active,
.expand-fade-leave-active {
  transition: all 0.35s var(--ease-out);
  overflow: hidden;
}
.expand-fade-enter-from,
.expand-fade-leave-to {
  opacity: 0;
  transform: translateY(-10px);
  max-height: 0;
}
.expand-fade-enter-to,
.expand-fade-leave-from {
  max-height: 600px;
}

/* Card slide for glossary extraction card */
.card-slide-enter-active {
  transition: all 0.4s var(--ease-out);
}
.card-slide-leave-active {
  transition: all 0.25s ease-in;
}
.card-slide-enter-from {
  opacity: 0;
  transform: translateY(16px);
}
.card-slide-leave-to {
  opacity: 0;
  transform: translateY(-12px);
}

/* ===== Extraction Section ===== */
.section-icon.extraction {
  background: var(--accent-soft);
  color: var(--accent);
}

.extraction-content {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.extraction-desc {
  font-size: 13px;
  color: var(--text-2);
  margin: 0;
  line-height: 1.6;
}

.extraction-config {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.extraction-field {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.extraction-label {
  font-size: 12px;
  font-weight: 500;
  color: var(--text-3);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.extraction-select {
  max-width: 320px;
}

.extraction-stats {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

/* Extraction mini metric cards */
.ext-metrics {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
}

.ext-metric-card {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 16px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  transition: all 0.2s ease;
  flex: 1;
  min-width: 130px;
}

.ext-metric-card:hover {
  border-color: var(--border-hover);
  background: var(--bg-subtle-hover);
}

.ext-metric-card.is-active {
  border-color: var(--accent-border);
  background: color-mix(in srgb, var(--accent) 4%, var(--bg-subtle));
}

.ext-metric-card.has-error {
  border-color: color-mix(in srgb, var(--danger) 15%, transparent);
  background: color-mix(in srgb, var(--danger) 4%, transparent);
}

.ext-metric-icon {
  width: 30px;
  height: 30px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 8px;
  flex-shrink: 0;
  background: var(--accent-soft);
  color: var(--accent);
}

.ext-metric-icon.active {
  background: var(--accent-soft);
  color: var(--accent);
  animation: pulse 2s ease-in-out infinite;
}

.ext-metric-icon.error {
  background: color-mix(in srgb, var(--danger) 10%, transparent);
  color: var(--danger);
}

.ext-metric-data {
  display: flex;
  flex-direction: column;
  gap: 1px;
  min-width: 0;
}

.ext-metric-value {
  font-family: var(--font-display);
  font-size: 17px;
  font-weight: 600;
  color: var(--text-1);
  letter-spacing: -0.02em;
  line-height: 1.2;
}

.ext-metric-card.has-error .ext-metric-value {
  color: var(--danger);
}

.ext-metric-label {
  font-size: 11px;
  color: var(--text-3);
  font-weight: 500;
  letter-spacing: 0.02em;
}

/* Extraction recent list */
.extraction-recent {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.extraction-recent-title {
  font-size: 11px;
  font-weight: 600;
  color: var(--text-3);
  text-transform: uppercase;
  letter-spacing: 0.08em;
}

.extraction-recent-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 9px 14px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  transition: border-color 0.2s ease;
}

.extraction-recent-item:hover {
  border-color: var(--border-hover);
}

.ext-recent-left {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
  flex: 1;
}

.ext-recent-game {
  font-size: 13px;
  font-weight: 500;
  color: var(--text-1);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ext-recent-time {
  font-size: 11px;
  color: var(--text-3);
  flex-shrink: 0;
}

.ext-recent-badge {
  font-family: var(--font-mono);
  font-size: 12px;
  font-weight: 600;
  color: var(--accent);
  background: var(--accent-soft);
  padding: 2px 10px;
  border-radius: 10px;
  flex-shrink: 0;
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
@media (max-width: 960px) {
  .metrics-strip {
    grid-template-columns: repeat(2, 1fr);
  }
}

@media (max-width: 768px) {
  .metrics-strip {
    grid-template-columns: repeat(2, 1fr);
  }

  .pipeline-flow {
    flex-direction: column;
    gap: 0;
  }

  .pipeline-arrow {
    width: auto;
    height: 24px;
    transform: rotate(90deg);
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

  .ext-metrics {
    flex-direction: column;
  }

  .recent-original,
  .recent-translated {
    max-width: none;
  }
}

@media (max-width: 480px) {
  .metrics-strip {
    grid-template-columns: repeat(2, 1fr);
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

  .pipeline-stage {
    padding: 14px 16px;
  }

  .pipeline-stage {
    padding-left: 13px;
  }

  .stage-icon {
    width: 36px;
    height: 36px;
    border-radius: 8px;
  }

  .stage-value {
    font-size: 18px;
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
