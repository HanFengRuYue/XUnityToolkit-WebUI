<script setup lang="ts">
import { ref, reactive, computed, onMounted, onBeforeUnmount, watch } from 'vue'
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
  NSwitch,
  NModal,
  NCheckbox,
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
  ExpandMoreOutlined,
  CheckCircleOutlined,
} from '@vicons/material'
import { LockClosedOutline } from '@vicons/ionicons5'
import { useAssetExtractionStore } from '@/stores/assetExtraction'
import { gamesApi, settingsApi, scriptTagApi, termCandidatesApi } from '@/api/games'
import type { Game, ScriptTagRule, ScriptTagConfig, ScriptTagAction, TermCandidate } from '@/api/types'

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
const enablePreTranslationCache = ref(true)
const enableLlmPatternAnalysis = ref(false)
const enableMultiRoundTranslation = ref(false)
const enableAutoTermExtraction = ref(false)
const autoApplyExtractedTerms = ref(false)

const collapsed = reactive({
  scriptTags: true,
})

// Script tag cleaning
const scriptTagRules = ref<ScriptTagRule[]>([])
const scriptTagPresetVersion = ref(0)
const scriptTagSaving = ref(false)
const scriptTagDirty = ref(false)

const actionOptions = [
  { label: 'Extract (提取文本)', value: 'Extract' as ScriptTagAction },
  { label: 'Exclude (排除)', value: 'Exclude' as ScriptTagAction },
]

// ── Script tag cleaning ──

async function loadScriptTags() {
  try {
    const res = await scriptTagApi.get(gameId)
    scriptTagRules.value = res.rules
    scriptTagPresetVersion.value = res.presetVersion
    scriptTagDirty.value = false
  } catch {
    // Script tag loading failed — non-critical, continue with empty rules
  }
}

async function handleSaveScriptTags() {
  scriptTagSaving.value = true
  try {
    const config: ScriptTagConfig = {
      presetVersion: scriptTagPresetVersion.value,
      rules: scriptTagRules.value,
    }
    await scriptTagApi.save(gameId, config)
    scriptTagDirty.value = false
    message.success('脚本指令规则已保存')
  } catch (e) {
    message.error(e instanceof Error ? e.message : '保存失败')
  } finally {
    scriptTagSaving.value = false
  }
}

async function importPresetRules() {
  try {
    const preset = await scriptTagApi.getPresets()
    scriptTagRules.value = scriptTagRules.value.filter(r => !r.isBuiltin)
    const builtinRules: ScriptTagRule[] = preset.rules.map(r => ({
      pattern: r.pattern,
      action: r.action,
      description: r.description,
      isBuiltin: true,
    }))
    scriptTagRules.value.unshift(...builtinRules)
    scriptTagPresetVersion.value = preset.version
    scriptTagDirty.value = true
    message.success(`已导入 ${builtinRules.length} 条内置规则`)
  } catch (e) {
    message.error(e instanceof Error ? e.message : '导入失败')
  }
}

function addCustomRule() {
  scriptTagRules.value.push({
    pattern: '',
    action: 'Exclude',
    description: '',
    isBuiltin: false,
  })
  scriptTagDirty.value = true
}

function removeScriptTagRule(index: number) {
  scriptTagRules.value.splice(index, 1)
  scriptTagDirty.value = true
}

async function handleToggleCache(value: boolean) {
  try {
    const settings = await settingsApi.get()
    settings.aiTranslation.enablePreTranslationCache = value
    await settingsApi.save(settings)
  } catch {
    message.error('保存缓存设置失败')
  }
}

async function handlePreTranslationSettingChange(field: string, value: boolean) {
  try {
    const settings = await settingsApi.get()
    ;(settings.aiTranslation as unknown as Record<string, unknown>)[field] = value
    await settingsApi.save(settings)
  } catch {
    message.error('保存设置失败')
  }
}

const langOptions = [
  { label: '日语 (ja)', value: 'ja' },
  { label: '英语 (en)', value: 'en' },
  { label: '中文 (zh)', value: 'zh' },
  { label: '韩语 (ko)', value: 'ko' },
  { label: '俄语 (ru)', value: 'ru' },
]

// Term review
const showTermReview = ref(false)
const termCandidates = ref<TermCandidate[]>([])
const termCandidateSelected = ref<Set<string>>(new Set())
const termReviewLoading = ref(false)

// Phase tracking
const phases = [
  { key: 'round1', label: '第一轮翻译' },
  { key: 'patternAnalysis', label: '模式分析' },
  { key: 'termExtraction', label: '术语提取' },
  { key: 'termReview', label: '术语审核' },
  { key: 'round2', label: '第二轮润色' },
  { key: 'writeCache', label: '写入缓存' },
]

function getPhaseStatus(phaseKey: string): 'done' | 'active' | 'pending' {
  const s = store.preTranslationStatus
  if (!s || s.state === 'Idle') return 'pending'
  const currentPhase = s.currentPhase ?? ''
  const currentRound = s.currentRound ?? 1

  const phaseOrder = phases.map(p => p.key)
  const currentIdx = phaseOrder.indexOf(currentPhase)
  const thisIdx = phaseOrder.indexOf(phaseKey)

  if (s.state === 'Completed') return 'done'
  if (currentIdx < 0) {
    // Fallback: infer from round
    if (phaseKey === 'round1' && currentRound === 1) return 'active'
    if (phaseKey === 'round2' && currentRound === 2) return 'active'
    return 'pending'
  }
  if (thisIdx < currentIdx) return 'done'
  if (thisIdx === currentIdx) return 'active'
  return 'pending'
}

const hasCheckpoint = computed(() =>
  Boolean(store.preTranslationStatus?.checkpointUpdatedAt)
)

const hasResumableCheckpoint = computed(() =>
  hasCheckpoint.value && store.preTranslationStatus?.canResume === true
)

const hasBlockedCheckpoint = computed(() =>
  hasCheckpoint.value
  && !hasResumableCheckpoint.value
  && Boolean(store.preTranslationStatus?.resumeBlockedReason)
)

const isPreTranslating = computed(() =>
  store.preTranslationStatus?.state === 'Running'
  && !store.preTranslationStatus?.canResume
)

const isAwaitingTermReview = computed(() =>
  store.preTranslationStatus?.state === 'AwaitingTermReview'
  && !store.preTranslationStatus?.canResume
)

const isPhaseWithBatchProgress = computed(() => {
  const phase = store.preTranslationStatus?.currentPhase
  return (phase === 'patternAnalysis' || phase === 'termExtraction')
    && (store.preTranslationStatus?.phaseTotal ?? 0) > 0
})

const preTranslationProgress = computed(() => {
  const s = store.preTranslationStatus
  if (!s || s.totalTexts === 0) return 0

  const phase = s.currentPhase
  // For LLM analysis phases, use phaseProgress/phaseTotal
  if ((phase === 'patternAnalysis' || phase === 'termExtraction') && s.phaseTotal > 0) {
    return Math.round(s.phaseProgress / s.phaseTotal * 100)
  }

  // For writeCache, show indeterminate (99%) while active
  if (phase === 'writeCache' && s.state === 'Running') {
    return 99
  }

  // For translation rounds, use translatedTexts/totalTexts
  return Math.round((s.translatedTexts + s.failedTexts) / s.totalTexts * 100)
})

const searchableTexts = computed(() =>
  (store.extractionResult?.texts ?? []).map(text => ({
    ...text,
    _textLower: text.text.toLowerCase(),
    _sourceLower: text.source.toLowerCase(),
  })),
)

const filteredTexts = computed(() => {
  const texts = searchableTexts.value
  const kw = searchKeyword.value.trim().toLowerCase()
  if (!kw) return texts
  return texts.filter(
    t => t._textLower.includes(kw) || t._sourceLower.includes(kw),
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
    if (store.preTranslationStatus?.fromLang) {
      fromLang.value = store.preTranslationStatus.fromLang
    }
    if (store.preTranslationStatus?.toLang) {
      toLang.value = store.preTranslationStatus.toLang
    }
    if (isPreTranslating.value || isAwaitingTermReview.value) {
      await store.connect(gameId)
    }
    // Check if AI providers are configured
    try {
      const settings = await settingsApi.get()
      const endpoints = settings.aiTranslation?.endpoints ?? []
      hasAiProvider.value = endpoints.some(e => e.enabled && e.apiKey)
      enablePreTranslationCache.value = settings.aiTranslation?.enablePreTranslationCache ?? false
      enableLlmPatternAnalysis.value = settings.aiTranslation?.enableLlmPatternAnalysis ?? false
      enableMultiRoundTranslation.value = settings.aiTranslation?.enableMultiRoundTranslation ?? false
      enableAutoTermExtraction.value = settings.aiTranslation?.enableAutoTermExtraction ?? false
      autoApplyExtractedTerms.value = settings.aiTranslation?.autoApplyExtractedTerms ?? false
    } catch { /* ignore */ }
    await loadScriptTags()
  } catch {
    message.error('加载失败')
  } finally {
    loading.value = false
  }
})

watch(() => store.preTranslationStatus?.state, (newState) => {
  if (newState === 'AwaitingTermReview' && !store.preTranslationStatus?.canResume) {
    loadTermCandidates()
  }
})

watch(() => store.termExtractionComplete, async (val) => {
  if (!val) return

  const status = store.preTranslationStatus
  if (status?.state === 'AwaitingTermReview' && !status.canResume) {
    await loadTermCandidates()
  }

  store.resetTermExtractionComplete()
})

onBeforeUnmount(async () => {
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

/*
async function handleResumePreTranslation() {
  try {
    await store.resumePreTranslation(gameId)
    message.info('棰勭炕璇戝凡鎭㈠')
  } catch (e) {
    message.error(e instanceof Error ? e.message : '鎭㈠棰勭炕璇戝け璐?)
  }
}

async function handleRestartPreTranslation() {
  try {
    await store.startPreTranslation(gameId, fromLang.value, toLang.value, true)
    message.info('棰勭炕璇戝凡閲嶆柊寮€濮?)
  } catch (e) {
    message.error(e instanceof Error ? e.message : '閲嶆柊寮€濮嬮缈昏瘧澶辫触')
  }
}

*/

async function handleResumePreTranslation() {
  try {
    await store.resumePreTranslation(gameId)
    message.info('预翻译已恢复')
  } catch (e) {
    message.error(e instanceof Error ? e.message : '恢复预翻译失败')
  }
}

async function handleRestartPreTranslation() {
  try {
    await store.startPreTranslation(gameId, fromLang.value, toLang.value, true)
    message.info('预翻译已重新开始')
  } catch (e) {
    message.error(e instanceof Error ? e.message : '重新开始预翻译失败')
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

async function loadTermCandidates() {
  try {
    const result = await termCandidatesApi.get(gameId)
    termCandidates.value = result.candidates
    termCandidateSelected.value = new Set(result.candidates.map(c => c.original))
    showTermReview.value = true
  } catch {
    message.error('加载术语候选项失败')
  }
}

async function handleApplyTerms(originals: string[] | null) {
  termReviewLoading.value = true
  try {
    await termCandidatesApi.apply(gameId, originals)
    showTermReview.value = false
    message.success(originals === null ? '已应用全部术语' : originals.length > 0 ? `已应用 ${originals.length} 条术语` : '已跳过术语应用')
  } catch {
    message.error('应用术语失败')
  } finally {
    termReviewLoading.value = false
  }
}

async function handleApplySelected() {
  const selected = Array.from(termCandidateSelected.value)
  await handleApplyTerms(selected)
}

async function handleSkipTerms() {
  await handleApplyTerms([])
}

function toggleTermCandidate(original: string, checked: boolean) {
  if (checked) {
    termCandidateSelected.value.add(original)
  } else {
    termCandidateSelected.value.delete(original)
  }
  // Force reactivity
  termCandidateSelected.value = new Set(termCandidateSelected.value)
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
          <NTag size="small" type="warning" style="margin-left: 8px">实验性</NTag>
        </h2>
      </div>

      <p class="section-desc">
        使用 AI 翻译引擎将提取的文本预先翻译，结果写入 XUnity 翻译缓存文件，游戏启动时即可使用。
      </p>

      <NAlert type="warning" :bordered="false" style="margin-bottom: 12px">
        预翻译功能仍处于实验阶段，效果因游戏而异。如遇到问题，请尝试重新提取资产或调整翻译设置。
      </NAlert>

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

        <!-- Pre-Translation Pipeline Settings -->
        <div class="pre-translation-settings">
          <div class="setting-row">
            <div class="setting-info">
              <span class="setting-label">LLM 动态模式分析</span>
              <span class="setting-description">利用 LLM 分析翻译中的重复模式，加速后续翻译</span>
            </div>
            <NSwitch
              v-model:value="enableLlmPatternAnalysis"
              @update:value="(v: boolean) => handlePreTranslationSettingChange('enableLlmPatternAnalysis', v)"
            />
          </div>
          <div class="setting-row">
            <div class="setting-info">
              <span class="setting-label">多轮翻译</span>
              <span class="setting-description">预翻译时进行多轮翻译，第二轮利用翻译记忆进行润色</span>
            </div>
            <NSwitch
              v-model:value="enableMultiRoundTranslation"
              @update:value="(v: boolean) => handlePreTranslationSettingChange('enableMultiRoundTranslation', v)"
            />
          </div>
          <div class="setting-row">
            <div class="setting-info">
              <span class="setting-label">自动术语提取</span>
              <span class="setting-description">预翻译过程中自动提取术语候选项</span>
            </div>
            <NSwitch
              v-model:value="enableAutoTermExtraction"
              @update:value="(v: boolean) => handlePreTranslationSettingChange('enableAutoTermExtraction', v)"
            />
          </div>
          <div v-if="enableAutoTermExtraction" class="setting-row sub-setting">
            <div class="setting-info">
              <span class="setting-label">自动应用提取的术语</span>
              <span class="setting-description">自动将提取的术语加入术语表，无需手动确认</span>
            </div>
            <NSwitch
              v-model:value="autoApplyExtractedTerms"
              @update:value="(v: boolean) => handlePreTranslationSettingChange('autoApplyExtractedTerms', v)"
            />
          </div>
          <div class="setting-row">
            <div class="setting-info">
              <span class="setting-label">预翻译缓存优化</span>
              <span class="setting-description">修改 XUnity 配置并生成正则翻译模式以提高缓存命中率</span>
            </div>
            <NSwitch v-model:value="enablePreTranslationCache" @update:value="handleToggleCache" />
          </div>
        </div>

        <!-- Script Tag Cleaning Rules (collapsible) -->
        <div v-if="enablePreTranslationCache" class="script-tag-card" :class="{ 'is-collapsed': collapsed.scriptTags }">
          <div class="section-header collapsible" @click="collapsed.scriptTags = !collapsed.scriptTags">
            <h3 class="section-title">
              <span class="section-icon">
                <NIcon :size="16"><DataObjectOutlined /></NIcon>
              </span>
              脚本指令清洗规则
              <NTag v-if="scriptTagRules.length > 0" size="small" :bordered="false" style="margin-left: 8px">
                {{ scriptTagRules.length }} 条
              </NTag>
            </h3>
            <div class="header-actions" @click.stop>
              <NButton size="small" @click="importPresetRules">导入内置规则</NButton>
              <NButton size="small" @click="addCustomRule">+ 添加规则</NButton>
              <NButton size="small" type="primary" :loading="scriptTagSaving" :disabled="!scriptTagDirty" @click="handleSaveScriptTags">
                保存
              </NButton>
            </div>
            <NIcon :size="18" class="collapse-chevron" :class="{ expanded: !collapsed.scriptTags }">
              <ExpandMoreOutlined />
            </NIcon>
          </div>
          <div class="section-body" :class="{ collapsed: collapsed.scriptTags }">
            <div class="section-body-inner">
              <div v-if="scriptTagRules.length === 0" class="empty-hint">
                暂无规则。点击「导入内置规则」加载预设，或手动添加自定义规则。
              </div>

              <div v-for="(rule, index) in scriptTagRules" :key="index" class="rule-row">
                <NInput
                  v-model:value="rule.pattern"
                  placeholder="正则表达式"
                  :disabled="rule.isBuiltin"
                  class="rule-pattern"
                  @update:value="scriptTagDirty = true"
                />
                <NSelect
                  v-model:value="rule.action"
                  :options="actionOptions"
                  :disabled="rule.isBuiltin"
                  class="rule-action"
                  @update:value="scriptTagDirty = true"
                />
                <NInput
                  v-model:value="rule.description"
                  placeholder="说明"
                  :disabled="rule.isBuiltin"
                  class="rule-desc"
                  @update:value="scriptTagDirty = true"
                />
                <NButton v-if="!rule.isBuiltin" size="small" quaternary @click="removeScriptTagRule(index)">
                  <template #icon><NIcon :size="16"><DeleteOutlined /></NIcon></template>
                </NButton>
                <NIcon v-else :size="16" class="rule-lock-icon">
                  <LockClosedOutline />
                </NIcon>
              </div>

              <p v-if="scriptTagRules.length > 0" class="rule-hint">
                内置规则随应用更新自动刷新，自定义规则不受影响。需重新运行预翻译以生效。
              </p>
            </div>
          </div>
        </div>

        <!-- Action Buttons -->
        <div class="action-row">
          <NButton
            v-if="!isPreTranslating && !isAwaitingTermReview && !hasResumableCheckpoint && !hasBlockedCheckpoint"
            type="primary"
            :disabled="!hasAiProvider"
            @click="handleStartPreTranslation"
          >
            <template #icon><NIcon :size="16"><PlayArrowFilled /></NIcon></template>
            开始预翻译 ({{ store.extractionResult.totalTextsExtracted }} 条)
          </NButton>
          <div v-if="hasResumableCheckpoint" class="checkpoint-actions">
            <NButton
              type="primary"
              :disabled="!hasAiProvider"
              class="checkpoint-action-button"
              @click="handleResumePreTranslation"
            >
              <template #icon><NIcon :size="16"><PlayArrowFilled /></NIcon></template>
              继续预翻译
            </NButton>
            <NButton
              :disabled="!hasAiProvider"
              class="checkpoint-action-button"
              @click="handleRestartPreTranslation"
            >
              <template #icon><NIcon :size="16"><RefreshOutlined /></NIcon></template>
              重新开始
            </NButton>
          </div>
          <template v-if="false">
            <NButton type="primary" @click="handleResumePreTranslation">
              <template #icon><NIcon :size="16"><PlayArrowFilled /></NIcon></template>
              缁х画棰勭炕璇?
            </NButton>
            <NButton :disabled="!hasAiProvider" @click="handleRestartPreTranslation">
              閲嶆柊寮€濮?
            </NButton>
          </template>
          <NButton
            v-if="hasBlockedCheckpoint"
            type="primary"
            :disabled="!hasAiProvider"
            @click="handleRestartPreTranslation"
          >
            <template #icon><NIcon :size="16"><RefreshOutlined /></NIcon></template>
            重新开始
          </NButton>
          <NButton
            v-if="false"
            type="primary"
            :disabled="!hasAiProvider"
            @click="handleRestartPreTranslation"
          >
            <template #icon><NIcon :size="16"><RefreshOutlined /></NIcon></template>
            閲嶆柊寮€濮?
          </NButton>
          <NButton
            v-else-if="isPreTranslating || isAwaitingTermReview"
            type="warning"
            @click="handleCancelPreTranslation"
          >
            <template #icon><NIcon :size="16"><StopOutlined /></NIcon></template>
            取消
          </NButton>
        </div>

        <!-- Progress -->
        <div v-if="store.preTranslationStatus && store.preTranslationStatus.state !== 'Idle'" class="progress-section">
          <!-- Phase Steps -->
          <div v-if="isPreTranslating || isAwaitingTermReview || hasCheckpoint || store.preTranslationStatus.state === 'Completed'" class="phase-steps">
            <div
              v-for="(phase, idx) in phases"
              :key="phase.key"
              class="phase-step"
              :class="getPhaseStatus(phase.key)"
            >
              <div class="phase-indicator">
                <NIcon v-if="getPhaseStatus(phase.key) === 'done'" :size="14"><CheckCircleOutlined /></NIcon>
                <NSpin v-else-if="getPhaseStatus(phase.key) === 'active'" :size="14" />
                <span v-else class="phase-dot"></span>
              </div>
              <span class="phase-label">{{ phase.label }}</span>
              <span v-if="idx < phases.length - 1" class="phase-arrow">→</span>
            </div>
          </div>

          <!-- Round indicator -->
          <div v-if="(store.preTranslationStatus.currentRound ?? 0) > 0 && (isPreTranslating || hasCheckpoint)" class="round-indicator">
            <NTag size="small" :bordered="false">
              第 {{ store.preTranslationStatus.currentRound }} 轮
            </NTag>
            <span v-if="(store.preTranslationStatus.dynamicPatternCount ?? 0) > 0" class="round-stat">
              动态模式: {{ store.preTranslationStatus.dynamicPatternCount }}
            </span>
            <span v-if="(store.preTranslationStatus.extractedTermCount ?? 0) > 0" class="round-stat">
              提取术语: {{ store.preTranslationStatus.extractedTermCount }}
            </span>
          </div>

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
            <template v-if="isPhaseWithBatchProgress">
              <span>{{ store.preTranslationStatus.phaseProgress }} / {{ store.preTranslationStatus.phaseTotal }} 批次</span>
            </template>
            <template v-else-if="store.preTranslationStatus.currentPhase === 'writeCache' && store.preTranslationStatus.state === 'Running'">
              <span>写入缓存中...</span>
            </template>
            <template v-else>
              <span>{{ store.preTranslationStatus.translatedTexts }} / {{ store.preTranslationStatus.totalTexts }}</span>
              <span v-if="store.preTranslationStatus.failedTexts > 0" class="failed-count">
                {{ store.preTranslationStatus.failedTexts }} 失败
              </span>
            </template>
          </div>

          <NAlert
            v-if="store.preTranslationStatus.state === 'Completed'"
            type="success"
            style="margin-top: 12px"
          >
            预翻译完成！翻译结果已写入游戏翻译缓存，启动游戏即可使用。
          </NAlert>
          <NAlert
            v-if="hasResumableCheckpoint"
            type="info"
            style="margin-top: 12px"
          >
            检测到未完成的预翻译检查点。你可以继续按上次进度恢复，或重新开始覆盖旧检查点。
          </NAlert>
          <NAlert
            v-if="false"
            type="info"
            style="margin-top: 12px"
          >
            妫€娴嬪埌鍙户缁殑棰勭炕璇戜换鍔★紝璇风偣鍑汇€岀户缁缈昏瘧銆嶆仮澶嶄笂娆¤繘搴︺€?
          </NAlert>
          <NAlert
            v-if="hasBlockedCheckpoint"
            type="warning"
            style="margin-top: 12px"
          >
            {{ store.preTranslationStatus.resumeBlockedReason }}
          </NAlert>
          <NAlert
            v-if="isAwaitingTermReview"
            type="info"
            style="margin-top: 12px"
          >
            已提取术语候选项，请审核后继续翻译。
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

    <!-- Term Review Modal -->
    <NModal
      v-model:show="showTermReview"
      preset="card"
      title="术语候选项审核"
      style="width: 680px; max-width: 90vw"
      :mask-closable="false"
    >
      <div v-if="termCandidates.length === 0" class="empty-hint">
        暂无术语候选项。
      </div>
      <div v-else class="term-review-list">
        <div class="term-review-header">
          <span>共 {{ termCandidates.length }} 条候选术语，已选中 {{ termCandidateSelected.size }} 条</span>
        </div>
        <div class="term-review-items">
          <div
            v-for="candidate in termCandidates"
            :key="candidate.original"
            class="term-review-item"
          >
            <NCheckbox
              :checked="termCandidateSelected.has(candidate.original)"
              @update:checked="(v: boolean) => toggleTermCandidate(candidate.original, v)"
            />
            <span class="term-original">{{ candidate.original }}</span>
            <span class="term-arrow">→</span>
            <span class="term-translation">{{ candidate.translation }}</span>
            <NTag size="small" :bordered="false">{{ candidate.category }}</NTag>
            <span class="term-frequency">x{{ candidate.frequency }}</span>
          </div>
        </div>
      </div>
      <template #action>
        <div class="term-review-actions">
          <NButton @click="handleApplyTerms(null)" :loading="termReviewLoading" type="primary">
            全部接受
          </NButton>
          <NButton @click="handleApplySelected" :loading="termReviewLoading" :disabled="termCandidateSelected.size === 0">
            应用选中 ({{ termCandidateSelected.size }})
          </NButton>
          <NButton @click="handleSkipTerms" :loading="termReviewLoading">
            跳过
          </NButton>
        </div>
      </template>
    </NModal>
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
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px;
  margin-bottom: 16px;
}

.checkpoint-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
}

.checkpoint-action-button {
  min-width: 132px;
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

/* ===== Pre-Translation Pipeline Settings ===== */
.pre-translation-settings {
  display: flex;
  flex-direction: column;
  gap: 12px;
  margin-bottom: 16px;
  padding-bottom: 16px;
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

.sub-setting {
  padding-left: 16px;
}

/* ===== Script Tag Card (collapsible) ===== */
.script-tag-card {
  display: flex;
  flex-direction: column;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  padding: 16px;
  margin-bottom: 16px;
  transition: border-color 0.3s ease, padding 0.2s ease;
}

.script-tag-card:hover {
  border-color: var(--border-hover);
}

.script-tag-card.is-collapsed {
  padding: 12px 16px;
}

.script-tag-card.is-collapsed .section-header {
  margin-bottom: 0;
}

.script-tag-card .section-body {
  display: grid;
  grid-template-rows: 1fr;
  opacity: 1;
  transition: grid-template-rows 0.3s ease, opacity 0.2s ease;
}

.script-tag-card .section-body.collapsed {
  grid-template-rows: 0fr;
  opacity: 0;
}

.script-tag-card .section-body-inner {
  overflow: hidden;
  padding-top: 4px;
}

.rule-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
}

.rule-pattern {
  flex: 3;
  font-family: var(--font-mono);
}

.rule-action {
  flex: 1;
  min-width: 140px;
}

.rule-desc {
  flex: 1.5;
}

.rule-lock-icon {
  opacity: 0.5;
  min-width: 28px;
  display: flex;
  justify-content: center;
}

.rule-hint {
  margin: 8px 0 0;
  font-size: 12px;
  color: var(--text-3);
  line-height: 1.5;
}

/* ===== Phase Steps ===== */
.phase-steps {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-wrap: wrap;
  margin-bottom: 16px;
  padding: 12px 16px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
}

.phase-step {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
  color: var(--text-3);
  transition: color 0.2s ease;
}

.phase-step.done {
  color: var(--success);
}

.phase-step.active {
  color: var(--accent);
  font-weight: 500;
}

.phase-indicator {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 18px;
  height: 18px;
}

.phase-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--text-3);
  opacity: 0.3;
}

.phase-label {
  white-space: nowrap;
}

.phase-arrow {
  color: var(--text-3);
  opacity: 0.4;
  margin: 0 2px;
}

.round-indicator {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 12px;
  font-size: 13px;
  color: var(--text-2);
}

.round-stat {
  font-size: 12px;
  color: var(--text-3);
}

/* ===== Term Review Modal ===== */
.term-review-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.term-review-header {
  font-size: 13px;
  color: var(--text-2);
  padding-bottom: 8px;
  border-bottom: 1px solid var(--border);
}

.term-review-items {
  display: flex;
  flex-direction: column;
  gap: 8px;
  max-height: 400px;
  overflow-y: auto;
}

.term-review-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 12px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  font-size: 13px;
}

.term-original {
  font-weight: 500;
  color: var(--text-1);
}

.term-arrow {
  color: var(--text-3);
  flex-shrink: 0;
}

.term-translation {
  color: var(--accent);
}

.term-frequency {
  margin-left: auto;
  font-size: 12px;
  color: var(--text-3);
}

.term-review-actions {
  display: flex;
  gap: 8px;
  justify-content: flex-end;
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
  .script-tag-card .section-header {
    flex-wrap: wrap;
    gap: 8px;
  }
  .script-tag-card .header-actions {
    width: 100%;
    order: 1;
  }
  .script-tag-card .collapse-chevron {
    order: 0;
  }
  .rule-row {
    flex-direction: column;
    align-items: stretch;
  }
  .rule-action {
    min-width: unset;
  }
  .checkpoint-actions {
    width: 100%;
  }
  .checkpoint-action-button {
    flex: 1 1 0;
  }
}
</style>
