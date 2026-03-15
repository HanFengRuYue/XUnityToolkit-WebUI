<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, nextTick } from 'vue'
import {
  NIcon,
  NButton,
  NSwitch,
  NSelect,
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
} from '@vicons/material'
import { useAiTranslationStore } from '@/stores/aiTranslation'
import { useGamesStore } from '@/stores/games'
import { settingsApi } from '@/api/games'
import type { AppSettings, AiTranslationSettings } from '@/api/types'
import AiTranslationCard from '@/components/settings/AiTranslationCard.vue'
import LocalAiPanel from '@/components/settings/LocalAiPanel.vue'
import { useAutoSave } from '@/composables/useAutoSave'

defineOptions({ name: 'AiTranslationView' })

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

onMounted(async () => {
  await aiStore.connect()
  await Promise.all([
    aiStore.fetchStats(),
    loadSettings(),
    gamesStore.games.length === 0 ? gamesStore.fetchGames() : Promise.resolve(),
  ])
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
            <span class="stage-value">{{ aiStore.stats?.translating ?? 0 }}</span>
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
    </div>

    <!-- Recent Translations -->
    <div
      v-if="(aiStore.stats?.recentTranslations?.length ?? 0) > 0"
      class="section-card"
      style="animation-delay: 0.08s"
    >
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon history">
            <NIcon :size="16"><HistoryOutlined /></NIcon>
          </span>
          最近翻译
        </h2>
        <span class="recent-count-badge">
          {{ aiStore.stats!.recentTranslations.length }} 条
        </span>
      </div>

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
              <span class="meta-tag">{{ item.tokensUsed }} tok</span>
              <span class="meta-tag">{{ formatTime(item.responseTimeMs) }}</span>
              <span class="meta-tag time">{{ formatRelativeTime(item.timestamp) }}</span>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Error Log -->
    <div
      v-if="(aiStore.stats?.recentErrors?.length ?? 0) > 0"
      class="section-card"
      style="animation-delay: 0.09s"
    >
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon error-log">
            <NIcon :size="16"><ErrorOutlineOutlined /></NIcon>
          </span>
          错误日志
        </h2>
        <span class="error-count-badge">{{ aiStore.stats!.recentErrors.length }} 条</span>
      </div>

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

    <!-- Glossary Extraction (cloud mode only) -->
    <div class="section-card" style="animation-delay: 0.095s" v-if="settings && !isLocalMode">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon extraction">
            <NIcon :size="16"><AutoFixHighOutlined /></NIcon>
          </span>
          自动术语提取
        </h2>
        <NSwitch
          :value="aiSettings.glossaryExtractionEnabled"
          @update:value="(v: boolean) => { aiSettings = { ...aiSettings, glossaryExtractionEnabled: v } }"
          size="small"
        />
      </div>

      <div class="extraction-content">
        <p class="extraction-desc">
          翻译过程中自动从原文和译文中提取专有名词、角色名等术语，并加入对应游戏的术语表。
        </p>

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
      <AiTranslationCard
        v-if="activeMode === 'cloud'"
        v-model="aiSettings"
        :embedded="true"
      />

      <!-- Local AI Panel -->
      <LocalAiPanel
        v-if="activeMode === 'local'"
        v-model="aiSettings"
      />
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

.translating-stage.is-active .stage-icon {
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
