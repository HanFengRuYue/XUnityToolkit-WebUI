<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NButton,
  NIcon,
  NProgress,
  NAlert,
  NInput,
  NDataTable,
  NTag,
  NSpin,
  NEmpty,
  NSelect,
  useMessage,
} from 'naive-ui'
import {
  ArrowBackOutlined,
  SearchOutlined,
  TranslateOutlined,
  LanguageOutlined,
  DescriptionOutlined,
  PlayArrowFilled,
  StopOutlined,
  DeleteOutlined,
  RefreshOutlined,
  DataObjectOutlined,
} from '@vicons/material'
import { useAssetExtractionStore } from '@/stores/assetExtraction'
import { gamesApi, settingsApi } from '@/api/games'
import type { Game, ExtractedText } from '@/api/types'

const route = useRoute()
const router = useRouter()
const message = useMessage()
const store = useAssetExtractionStore()

const gameId = route.params['id'] as string
const game = ref<Game | null>(null)
const loading = ref(true)
const searchKeyword = ref('')
const fromLang = ref('ja')
const toLang = ref('zh')
const hasAiProvider = ref(false)

const langOptions = [
  { label: '日语 (ja)', value: 'ja' },
  { label: '英语 (en)', value: 'en' },
  { label: '中文 (zh)', value: 'zh' },
  { label: '韩语 (ko)', value: 'ko' },
  { label: '俄语 (ru)', value: 'ru' },
]

const isPreTranslating = computed(() =>
  store.preTranslationStatus?.state === 'Running'
)

const preTranslationProgress = computed(() => {
  const s = store.preTranslationStatus
  if (!s || s.totalTexts === 0) return 0
  return Math.round((s.translatedTexts + s.failedTexts) / s.totalTexts * 100)
})

const filteredTexts = computed(() => {
  if (!store.extractionResult?.texts) return []
  const kw = searchKeyword.value.toLowerCase()
  if (!kw) return store.extractionResult.texts
  return store.extractionResult.texts.filter(
    t => t.text.toLowerCase().includes(kw) || t.source.toLowerCase().includes(kw)
  )
})

const tableColumns = [
  {
    title: '文本',
    key: 'text',
    ellipsis: { tooltip: true },
    resizable: true,
  },
  {
    title: '来源',
    key: 'source',
    width: 200,
    ellipsis: { tooltip: true },
  },
  {
    title: '资产文件',
    key: 'assetFile',
    width: 180,
    ellipsis: { tooltip: true },
  },
]

onMounted(async () => {
  try {
    game.value = await gamesApi.get(gameId)
    await store.loadCachedResult(gameId)
    if (store.extractionResult?.detectedLanguage) {
      fromLang.value = store.extractionResult.detectedLanguage
    }
    await store.fetchPreTranslationStatus(gameId)
    if (isPreTranslating.value) {
      await store.connect(gameId)
    }
    // Check if AI providers are configured
    try {
      const settings = await settingsApi.get()
      const endpoints = settings.aiTranslation?.endpoints ?? []
      hasAiProvider.value = endpoints.some(e => e.enabled && e.apiKey)
    } catch { /* ignore */ }
  } catch {
    message.error('加载失败')
  } finally {
    loading.value = false
  }
})

onUnmounted(async () => {
  await store.disconnect()
})

async function handleExtract() {
  try {
    await store.extractAssets(gameId)
    if (store.extractionResult?.detectedLanguage) {
      fromLang.value = store.extractionResult.detectedLanguage
    }
    message.success(`提取完成: ${store.extractionResult?.totalTextsExtracted ?? 0} 条文本`)
  } catch {
    message.error(store.extractError ?? '提取失败')
  }
}

async function handleStartPreTranslation() {
  try {
    await store.startPreTranslation(gameId, fromLang.value, toLang.value)
    message.info('预翻译已开始')
  } catch (e) {
    message.error(e instanceof Error ? e.message : '启动预翻译失败')
  }
}

async function handleCancelPreTranslation() {
  try {
    await store.cancelPreTranslation(gameId)
    message.info('已取消')
  } catch {
    message.error('取消失败')
  }
}

async function handleClearCache() {
  try {
    await store.clearCache(gameId)
    message.success('缓存已清理')
  } catch {
    message.error('清理失败')
  }
}

function langLabel(code: string): string {
  const map: Record<string, string> = {
    ja: '日语', en: '英语', zh: '中文', ko: '韩语', ru: '俄语',
    fr: '法语', de: '德语', es: '西班牙语', pt: '葡萄牙语',
  }
  return map[code] ?? code
}
</script>

<template>
  <div v-if="loading" class="loading-state">
    <NSpin size="large" />
  </div>

  <div v-else-if="game" class="sub-page">
    <!-- Header -->
    <div class="sub-page-header" style="animation-delay: 0s">
      <button class="back-button" @click="router.push(`/games/${gameId}`)">
        <NIcon :size="20"><ArrowBackOutlined /></NIcon>
        <span>{{ game.name }}</span>
      </button>
    </div>

    <h1 class="page-title" style="animation-delay: 0.05s">
      <span class="page-title-icon">
        <NIcon :size="24"><DataObjectOutlined /></NIcon>
      </span>
      资产提取与预翻译
    </h1>

    <!-- Extract Section -->
    <div class="section-card" style="animation-delay: 0.1s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><SearchOutlined /></NIcon>
          </span>
          资产提取
        </h2>
        <div class="header-actions">
          <NButton
            v-if="store.extractionResult"
            size="small"
            tertiary
            type="error"
            @click="handleClearCache"
          >
            <template #icon><NIcon :size="16"><DeleteOutlined /></NIcon></template>
            清除缓存
          </NButton>
          <NButton
            size="small"
            type="primary"
            :loading="store.extracting"
            @click="handleExtract"
          >
            <template #icon><NIcon :size="16"><RefreshOutlined /></NIcon></template>
            {{ store.extractionResult ? '重新提取' : '开始提取' }}
          </NButton>
        </div>
      </div>

      <p class="section-desc">
        扫描游戏资产文件，提取 MonoBehaviour 字符串和 TextAsset 文本。用于语言检测和预翻译。
      </p>

      <!-- Extraction Result -->
      <div v-if="store.extractionResult" class="result-summary">
        <div class="stat-cards">
          <div class="stat-card">
            <span class="stat-value">{{ store.extractionResult.totalTextsExtracted }}</span>
            <span class="stat-label">提取文本</span>
          </div>
          <div class="stat-card">
            <span class="stat-value">{{ store.extractionResult.totalAssetsScanned }}</span>
            <span class="stat-label">扫描文件</span>
          </div>
          <div class="stat-card accent">
            <NIcon :size="16" style="margin-right: 4px"><LanguageOutlined /></NIcon>
            <span class="stat-value">{{ langLabel(store.extractionResult.detectedLanguage ?? '?') }}</span>
            <span class="stat-label">检测语言</span>
          </div>
        </div>
      </div>

      <div v-if="store.extracting" class="extracting-hint">
        <NSpin size="small" />
        <span>正在提取游戏资产，这可能需要一些时间...</span>
      </div>
    </div>

    <!-- Pre-Translation Section -->
    <div class="section-card" style="animation-delay: 0.15s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><TranslateOutlined /></NIcon>
          </span>
          预翻译
        </h2>
      </div>

      <p class="section-desc">
        使用 AI 翻译引擎将提取的文本预先翻译，结果写入 XUnity 翻译缓存文件，游戏启动时即可使用。
      </p>

      <div v-if="!store.extractionResult" class="empty-hint">
        <NEmpty description="请先提取游戏资产" />
      </div>

      <template v-else>
        <!-- Language Selection -->
        <div class="lang-row">
          <div class="lang-select">
            <label>源语言</label>
            <NSelect
              v-model:value="fromLang"
              :options="langOptions"
              size="small"
              :disabled="isPreTranslating"
            />
          </div>
          <span class="lang-arrow">→</span>
          <div class="lang-select">
            <label>目标语言</label>
            <NSelect
              v-model:value="toLang"
              :options="langOptions"
              size="small"
              :disabled="isPreTranslating"
            />
          </div>
        </div>

        <!-- AI Provider Warning -->
        <NAlert
          v-if="!hasAiProvider"
          type="warning"
          style="margin-bottom: 12px"
        >
          尚未配置 AI 翻译提供商，请先前往
          <router-link to="/ai-translation" style="color: var(--accent); font-weight: 500">AI 翻译页面</router-link>
          添加至少一个提供商。
        </NAlert>

        <!-- Action Buttons -->
        <div class="action-row">
          <NButton
            v-if="!isPreTranslating"
            type="primary"
            :disabled="!hasAiProvider"
            @click="handleStartPreTranslation"
          >
            <template #icon><NIcon :size="16"><PlayArrowFilled /></NIcon></template>
            开始预翻译 ({{ store.extractionResult.totalTextsExtracted }} 条)
          </NButton>
          <NButton
            v-else
            type="warning"
            @click="handleCancelPreTranslation"
          >
            <template #icon><NIcon :size="16"><StopOutlined /></NIcon></template>
            取消
          </NButton>
        </div>

        <!-- Progress -->
        <div v-if="store.preTranslationStatus && store.preTranslationStatus.state !== 'Idle'" class="progress-section">
          <NProgress
            type="line"
            :percentage="preTranslationProgress"
            :status="
              store.preTranslationStatus.state === 'Failed' ? 'error'
              : store.preTranslationStatus.state === 'Completed' ? 'success'
              : 'default'
            "
            :indicator-placement="'inside'"
            :height="20"
            :border-radius="10"
          />
          <div class="progress-stats">
            <span>{{ store.preTranslationStatus.translatedTexts }} / {{ store.preTranslationStatus.totalTexts }}</span>
            <span v-if="store.preTranslationStatus.failedTexts > 0" class="failed-count">
              {{ store.preTranslationStatus.failedTexts }} 失败
            </span>
          </div>

          <NAlert
            v-if="store.preTranslationStatus.state === 'Completed'"
            type="success"
            style="margin-top: 12px"
          >
            预翻译完成！翻译结果已写入游戏翻译缓存，启动游戏即可使用。
          </NAlert>
          <NAlert
            v-if="store.preTranslationStatus.state === 'Failed'"
            type="error"
            style="margin-top: 12px"
          >
            {{ store.preTranslationStatus.error }}
          </NAlert>
          <NAlert
            v-if="store.preTranslationStatus.state === 'Cancelled'"
            type="warning"
            style="margin-top: 12px"
          >
            预翻译已取消。已完成的翻译仍然有效。
          </NAlert>
        </div>
      </template>
    </div>

    <!-- Extracted Texts Table -->
    <div v-if="store.extractionResult && store.extractionResult.texts.length > 0" class="section-card" style="animation-delay: 0.2s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><DescriptionOutlined /></NIcon>
          </span>
          提取文本预览
          <NTag size="small" :bordered="false" style="margin-left: 8px">
            {{ filteredTexts.length }} / {{ store.extractionResult.texts.length }}
          </NTag>
        </h2>
      </div>

      <NInput
        v-model:value="searchKeyword"
        placeholder="搜索文本..."
        clearable
        size="small"
        style="margin-bottom: 12px"
      >
        <template #prefix>
          <NIcon :size="16"><SearchOutlined /></NIcon>
        </template>
      </NInput>

      <NDataTable
        :columns="tableColumns"
        :data="filteredTexts"
        :max-height="480"
        :pagination="{ pageSize: 50 }"
        size="small"
        striped
      />
    </div>
  </div>
</template>

<style scoped>
.result-summary {
  margin-top: 16px;
}

.stat-cards {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
}

.stat-card {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 10px 16px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  min-width: 120px;
}
.stat-card.accent {
  background: var(--accent-soft);
  border-color: var(--accent-border);
}

.stat-value {
  font-size: 16px;
  font-weight: 600;
  color: var(--text-1);
  font-family: var(--font-mono);
}
.stat-card.accent .stat-value {
  color: var(--accent);
}

.stat-label {
  font-size: 12px;
  color: var(--text-3);
  margin-left: 4px;
}

.extracting-hint {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 12px 16px;
  background: var(--bg-subtle);
  border-radius: var(--radius-md);
  color: var(--text-2);
  font-size: 13px;
  margin-top: 12px;
}

.empty-hint {
  padding: 24px 0;
}

.lang-row {
  display: flex;
  align-items: flex-end;
  gap: 12px;
  margin-bottom: 16px;
}

.lang-select {
  flex: 1;
  max-width: 200px;
}
.lang-select label {
  display: block;
  font-size: 12px;
  color: var(--text-3);
  margin-bottom: 4px;
}

.lang-arrow {
  color: var(--text-3);
  font-size: 18px;
  padding-bottom: 4px;
}

.action-row {
  margin-bottom: 16px;
}

.progress-section {
  margin-top: 12px;
}

.progress-stats {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-top: 8px;
  font-size: 13px;
  font-family: var(--font-mono);
  color: var(--text-2);
}

.failed-count {
  color: var(--danger);
}

@media (max-width: 768px) {
  .stat-cards {
    flex-direction: column;
  }
  .lang-row {
    flex-direction: column;
    align-items: stretch;
  }
  .lang-select {
    max-width: none;
  }
  .lang-arrow {
    text-align: center;
  }
}
</style>
