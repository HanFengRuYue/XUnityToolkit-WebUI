<script setup lang="ts">
import { ref, reactive, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import {
  NButton, NIcon, NUpload, NSelect, NProgress, NSpace, NAlert, useMessage, NPopconfirm,
  NCheckboxGroup, NCheckbox, NRadioGroup, NRadio, NCollapse, NCollapseItem, NSpin, NTag,
  NDescriptions, NDescriptionsItem, NInputNumber,
} from 'naive-ui'
import {
  FontDownloadOutlined, UploadFileOutlined, SettingsOutlined, DownloadOutlined,
  DeleteOutlined, SwapHorizOutlined, TextFieldsOutlined, AssessmentOutlined,
  ExpandMoreOutlined,
} from '@vicons/material'
import { HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr'
import { api } from '@/api/client'
import type {
  FontUploadInfo, FontGenerationStatus, GeneratedFontInfo, FontGenerationProgress,
  FontGenerationComplete, CharacterSetConfig, CharacterSetPreview, FontGenerationReport,
  CharsetInfo,
} from '@/api/types'

defineOptions({ name: 'FontGeneratorView' })

const collapsed = reactive({
  charset: true,
})

const message = useMessage()

// Upload state
const uploadedFont = ref<FontUploadInfo | null>(null)
const uploading = ref(false)

// Settings
const unityVersion = ref('2022')
const samplingSize = ref(64)
const atlasSize = ref(8192)
const renderMode = ref<'SDFAA' | 'SDF8' | 'SDF16' | 'SDF32'>('SDFAA')
const samplingSizeMode = ref<'auto' | 'manual'>('manual')
const paddingMode = ref<'percentage' | 'pixel'>('percentage')
const paddingValue = ref(10)

// Generation state
const isGenerating = ref(false)
const phase = ref('')
const current = ref(0)
const total = ref(0)
const progressPercent = computed(() => total.value > 0 ? Math.round(current.value / total.value * 100) : 0)

// History
const generatedFonts = ref<GeneratedFontInfo[]>([])
const games = ref<{ id: string; name: string }[]>([])

// Character set configuration
const builtinCharsets = ref<CharsetInfo[]>([])
const selectedCharsets = ref<string[]>(['GB2312'])
const customCharsetFile = ref<{ fileName: string; characterCount: number } | null>(null)
const uploadingCharset = ref(false)
const translationMode = ref<'none' | 'game' | 'upload'>('none')
const translationGameId = ref<string | null>(null)
const translationFile = ref<{ fileName: string; characterCount: number } | null>(null)
const uploadingTranslation = ref(false)

// Charset preview
const charsetPreview = ref<CharacterSetPreview | null>(null)
const loadingPreview = ref(false)
let previewDebounceTimer: ReturnType<typeof setTimeout> | null = null

// Generation report
const generationReport = ref<FontGenerationReport | null>(null)
const viewingReport = ref<FontGenerationReport | null>(null)
const activeReport = computed(() => viewingReport.value ?? generationReport.value)

const phaseLabels: Record<string, string> = {
  parsing: '解析字体',
  sdf: '生成 SDF 位图',
  packing: '打包 Atlas',
  compositing: '合成纹理',
  serializing: '序列化 Bundle',
}

const versionOptions = [
  { label: 'Unity 2018', value: '2018' },
  { label: 'Unity 2019', value: '2019' },
  { label: 'Unity 2020', value: '2020' },
  { label: 'Unity 2021', value: '2021' },
  { label: 'Unity 2022', value: '2022' },
  { label: 'Unity 6000', value: '6000' },
]

const samplingOptions = [
  { label: '32px', value: 32 },
  { label: '48px', value: 48 },
  { label: '64px (推荐)', value: 64 },
  { label: '96px', value: 96 },
]

const atlasOptions = [
  { label: '2048 x 2048', value: 2048 },
  { label: '4096 x 4096', value: 4096 },
  { label: '8192 x 8192 (推荐)', value: 8192 },
]

const renderModeOptions = [
  { label: 'SDFAA (默认)', value: 'SDFAA' },
  { label: 'SDF8 (8x 上采样)', value: 'SDF8' },
  { label: 'SDF16 (16x 上采样)', value: 'SDF16' },
  { label: 'SDF32 (32x 上采样，最高质量)', value: 'SDF32' },
]

// Upload handler
async function handleUpload({ file }: { file: { file: File } }) {
  uploading.value = true
  const formData = new FormData()
  formData.append('file', file.file)
  try {
    const resp = await fetch('/api/font-generation/upload', { method: 'POST', body: formData })
    const json = await resp.json()
    if (json.success) {
      uploadedFont.value = json.data
      message.success(`已上传: ${json.data.fontName}`)
    } else {
      message.error(json.error || '上传失败')
    }
  } catch (e: any) {
    message.error(e.message || '上传失败')
  } finally {
    uploading.value = false
  }
  return false
}

// Character set helpers
function buildCharsetConfig(): CharacterSetConfig {
  return {
    builtinSets: selectedCharsets.value,
    customCharsetFileName: customCharsetFile.value?.fileName,
    translationGameId: translationMode.value === 'game' ? translationGameId.value ?? undefined : undefined,
    translationFileName: translationMode.value === 'upload' ? translationFile.value?.fileName : undefined,
  }
}

function triggerPreview() {
  if (previewDebounceTimer) clearTimeout(previewDebounceTimer)
  previewDebounceTimer = setTimeout(loadPreview, 500)
}

async function loadPreview() {
  const config = buildCharsetConfig()
  if (config.builtinSets.length === 0 && !config.customCharsetFileName
      && !config.translationGameId && !config.translationFileName) {
    charsetPreview.value = null
    return
  }
  loadingPreview.value = true
  try {
    charsetPreview.value = await api.post<CharacterSetPreview>('/api/font-generation/charset/preview', {
      characterSet: config,
      atlasWidth: atlasSize.value,
      atlasHeight: atlasSize.value,
      samplingSize: samplingSize.value,
    })
  } catch (e: any) {
    message.error(e.message || '预览失败')
  } finally {
    loadingPreview.value = false
  }
}

async function handleCharsetUpload(options: { file: { file: File } }) {
  uploadingCharset.value = true
  try {
    const formData = new FormData()
    formData.append('file', options.file.file)
    const resp = await fetch('/api/font-generation/charset/upload-custom', { method: 'POST', body: formData })
    const result = await resp.json()
    if (!result.success) throw new Error(result.error)
    customCharsetFile.value = result.data
    message.success(`已上传自定义字符集（${result.data.characterCount} 字符）`)
  } catch (e: any) {
    message.error(e.message || '上传失败')
  } finally {
    uploadingCharset.value = false
  }
}

async function handleTranslationUpload(options: { file: { file: File } }) {
  uploadingTranslation.value = true
  try {
    const formData = new FormData()
    formData.append('file', options.file.file)
    const resp = await fetch('/api/font-generation/charset/upload-translation', { method: 'POST', body: formData })
    const result = await resp.json()
    if (!result.success) throw new Error(result.error)
    translationFile.value = result.data
    message.success(`已提取翻译文件字符（${result.data.characterCount} 字符）`)
  } catch (e: any) {
    message.error(e.message || '上传失败')
  } finally {
    uploadingTranslation.value = false
  }
}

async function startGeneration() {
  if (!uploadedFont.value) return
  try {
    await api.post('/api/font-generation/generate', {
      fileName: uploadedFont.value.fileName,
      unityVersion: unityVersion.value,
      samplingSize: samplingSize.value,
      atlasWidth: atlasSize.value,
      atlasHeight: atlasSize.value,
      characterSet: buildCharsetConfig(),
      renderMode: renderMode.value,
      samplingSizeMode: samplingSizeMode.value,
      paddingMode: paddingMode.value,
      paddingValue: paddingValue.value,
    })
    isGenerating.value = true
    phase.value = 'parsing'
    current.value = 0
    total.value = 0
    generationReport.value = null
  } catch (e: any) {
    message.error(e.message || '启动生成失败')
  }
}

async function cancelGeneration() {
  try {
    await api.post('/api/font-generation/cancel')
  } catch { /* ignore */ }
}

function downloadFont(fileName: string) {
  const a = document.createElement('a')
  a.href = `/api/font-generation/download/${encodeURIComponent(fileName)}`
  a.download = fileName
  a.click()
}

async function deleteFont(fileName: string) {
  try {
    await api.del(`/api/font-generation/${encodeURIComponent(fileName)}`)
    await loadHistory()
    message.success('已删除')
  } catch (e: any) {
    message.error(e.message || '删除失败')
  }
}

async function installTmpFont(fileName: string, gameId: string) {
  try {
    await api.post(`/api/font-generation/install-tmp-font/${gameId}`, { fileName })
    message.success('已安装为 XUnity TMP 字体')
  } catch (e: any) {
    message.error(e.message || '安装失败')
  }
}

function clearUploadedFont() {
  uploadedFont.value = null
}

async function loadHistory() {
  try {
    generatedFonts.value = await api.get<GeneratedFontInfo[]>('/api/font-generation/history')
  } catch { /* ignore */ }
}

async function viewReport(fileName: string) {
  try {
    viewingReport.value = await api.get<FontGenerationReport>(`/api/font-generation/report/${fileName}`)
  } catch (e: any) {
    message.error(e.message || '获取报告失败')
  }
}

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

function formatDate(dateStr: string): string {
  return new Date(dateStr).toLocaleString('zh-CN')
}

function formatDuration(ms: number): string {
  if (ms < 1000) return `${ms} 毫秒`
  const seconds = Math.floor(ms / 1000)
  if (seconds < 60) return `${seconds} 秒`
  const minutes = Math.floor(seconds / 60)
  const remainingSeconds = seconds % 60
  return `${minutes} 分 ${remainingSeconds} 秒`
}

// Watchers
watch(
  [selectedCharsets, customCharsetFile, translationMode, translationGameId, translationFile, atlasSize, samplingSize],
  triggerPreview,
  { deep: true },
)

// SignalR
const connection = new HubConnectionBuilder()
  .withUrl('/hubs/install')
  .withAutomaticReconnect()
  .build()

connection.on('FontGenerationProgress', (p: FontGenerationProgress) => {
  phase.value = p.phase
  current.value = p.current
  total.value = p.total
})

connection.on('FontGenerationComplete', (result: FontGenerationComplete) => {
  isGenerating.value = false
  if (result.success) {
    message.success(`字体 ${result.fontName} 生成成功（${result.glyphCount} 字形）`)
    generationReport.value = result.report ?? null
  } else {
    message.error(result.error || '生成失败')
    generationReport.value = null
  }
  loadHistory()
})

connection.onreconnected(async () => {
  await connection.invoke('JoinFontGenerationGroup')
})

onMounted(async () => {
  try {
    await connection.start()
    await connection.invoke('JoinFontGenerationGroup')
  } catch (e) {
    console.error('SignalR connection failed:', e)
  }
  await loadHistory()
  // Load games for "use as custom" dropdown
  try {
    const gamesData = await api.get<any[]>('/api/games')
    games.value = gamesData.map((g: any) => ({ id: g.id, name: g.name }))
  } catch { /* ignore */ }

  // Load built-in charsets
  try {
    builtinCharsets.value = await api.get<CharsetInfo[]>('/api/font-generation/charsets')
  } catch { /* ignore */ }

  // Check if generation is already in progress
  try {
    const status = await api.get<FontGenerationStatus>('/api/font-generation/status')
    if (status.isGenerating) {
      isGenerating.value = true
      phase.value = status.phase || ''
      current.value = status.current
      total.value = status.total
    }
  } catch { /* ignore */ }
})

onBeforeUnmount(async () => {
  if (previewDebounceTimer) clearTimeout(previewDebounceTimer)
  try {
    if (connection.state === HubConnectionState.Connected) {
      await connection.invoke('LeaveFontGenerationGroup')
      await connection.stop()
    }
  } catch { /* ignore */ }
})
</script>

<template>
  <div class="font-generator-page">
    <h1 class="page-title" style="animation-delay: 0s">
      <span class="page-title-icon">
        <NIcon :size="22"><FontDownloadOutlined /></NIcon>
      </span>
      TMP 字体生成
    </h1>
    <p class="page-subtitle" style="animation-delay: 0s">
      将 TTF/OTF 字体转换为 Unity TextMeshPro 字体资产，无需安装 Unity 引擎
    </p>

    <!-- Upload + Settings Card (merged) -->
    <div class="section-card" style="animation-delay: 0.05s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><UploadFileOutlined /></NIcon>
          </span>
          字体与设置
        </h2>
      </div>
      <NUpload
        v-if="!uploadedFont"
        accept=".ttf,.otf"
        :max="1"
        :custom-request="({ file }) => handleUpload({ file: file as any })"
        :show-file-list="false"
        :disabled="isGenerating"
        directory-dnd
      >
        <div class="upload-area">
          <NIcon :size="36" color="var(--text-3)"><UploadFileOutlined /></NIcon>
          <p class="upload-text">拖拽或点击上传</p>
          <p class="upload-hint">TTF / OTF · 最大 50MB</p>
        </div>
      </NUpload>
      <div v-else class="upload-settings-grid">
        <div class="upload-column">
          <div class="upload-result">
            <div class="upload-result-info">
              <NIcon :size="20" color="var(--accent)"><FontDownloadOutlined /></NIcon>
              <div>
                <div class="upload-font-name">{{ uploadedFont.fontName }}</div>
                <div class="upload-file-size">{{ formatFileSize(uploadedFont.fileSize) }}</div>
              </div>
            </div>
            <NButton size="small" quaternary type="error" @click="clearUploadedFont" :disabled="isGenerating">
              <template #icon><NIcon :size="14"><DeleteOutlined /></NIcon></template>
              删除
            </NButton>
          </div>
        </div>
        <div class="settings-column">
          <div class="form-row">
            <label class="form-label">Unity 版本</label>
            <NSelect v-model:value="unityVersion" :options="versionOptions" :disabled="isGenerating" size="small" />
          </div>
          <div class="form-row">
            <label class="form-label">渲染模式</label>
            <NSelect v-model:value="renderMode" :options="renderModeOptions" :disabled="isGenerating" size="small" />
          </div>
          <div class="form-row">
            <label class="form-label">采样大小</label>
            <NSpace align="center" :size="8">
              <NRadioGroup v-model:value="samplingSizeMode" :disabled="isGenerating" size="small">
                <NRadio value="manual">手动</NRadio>
                <NRadio value="auto">自动</NRadio>
              </NRadioGroup>
              <NSelect
                v-if="samplingSizeMode === 'manual'"
                v-model:value="samplingSize"
                :options="samplingOptions"
                :disabled="isGenerating"
                size="small"
                style="width: 140px"
              />
            </NSpace>
          </div>
          <div class="form-row">
            <label class="form-label">Atlas 尺寸</label>
            <NSelect v-model:value="atlasSize" :options="atlasOptions" :disabled="isGenerating" size="small" />
          </div>
          <div class="form-row">
            <label class="form-label">Padding</label>
            <NSpace align="center" :size="8">
              <NRadioGroup v-model:value="paddingMode" :disabled="isGenerating" size="small">
                <NRadio value="percentage">百分比</NRadio>
                <NRadio value="pixel">像素</NRadio>
              </NRadioGroup>
              <NInputNumber
                v-model:value="paddingValue"
                :min="1"
                :max="paddingMode === 'percentage' ? 50 : 100"
                :disabled="isGenerating"
                size="small"
                style="width: 80px"
              />
              <span style="font-size: 12px; color: var(--text-3)">{{ paddingMode === 'percentage' ? '%' : 'px' }}</span>
            </NSpace>
          </div>
        </div>
      </div>
    </div>

    <!-- Character Set Card -->
    <div class="section-card" :class="{ 'is-collapsed': collapsed.charset }" style="animation-delay: 0.15s">
      <div class="section-header collapsible" @click="collapsed.charset = !collapsed.charset">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><TextFieldsOutlined /></NIcon>
          </span>
          字符集配置
        </h2>
        <NIcon :size="18" class="collapse-chevron" :class="{ expanded: !collapsed.charset }">
          <ExpandMoreOutlined />
        </NIcon>
      </div>
      <div class="section-body" :class="{ collapsed: collapsed.charset }">
        <div class="section-body-inner">

      <!-- Built-in charsets -->
      <div class="charset-section-label">内置字符集</div>
      <NCheckboxGroup v-model:value="selectedCharsets" :disabled="isGenerating">
        <div class="charset-grid">
          <NCheckbox
            v-for="cs in builtinCharsets"
            :key="cs.id"
            :value="cs.id"
            :label="`${cs.name}（~${cs.characterCount.toLocaleString()} 字符）`"
          />
          <NCheckbox v-if="builtinCharsets.length === 0" value="GB2312" label="GB2312（~7500 字符）" />
        </div>
      </NCheckboxGroup>
      <p class="hint-text">选择需要包含的预设字符集，可同时选择多个</p>

      <!-- Custom charset upload -->
      <div class="charset-section-label">自定义字符集</div>
      <NUpload
        accept=".txt"
        :max="1"
        :custom-request="({ file }) => handleCharsetUpload({ file: file as any })"
        :show-file-list="false"
        :disabled="isGenerating"
      >
        <NButton :disabled="isGenerating" :loading="uploadingCharset" size="small">
          上传 TXT 文件
        </NButton>
      </NUpload>
      <div v-if="customCharsetFile" class="upload-info">
        <NTag size="small" type="success">{{ customCharsetFile.fileName }}</NTag>
        <span>{{ customCharsetFile.characterCount.toLocaleString() }} 字符</span>
        <NButton text size="tiny" @click="customCharsetFile = null">移除</NButton>
      </div>
      <p class="hint-text">上传包含所需字符的纯文本文件，文件中的所有唯一字符将被收集</p>

      <!-- Translation extraction -->
      <div class="charset-section-label">从翻译提取字符</div>
      <NRadioGroup v-model:value="translationMode" :disabled="isGenerating">
        <NSpace :size="16">
          <NRadio value="none">不使用</NRadio>
          <NRadio value="game">选择游戏</NRadio>
          <NRadio value="upload">上传文件</NRadio>
        </NSpace>
      </NRadioGroup>
      <div v-if="translationMode === 'game'" style="margin-top: 8px">
        <NSelect
          v-model:value="translationGameId"
          placeholder="选择已安装的游戏"
          :options="games.map(g => ({ label: g.name, value: g.id }))"
          :disabled="isGenerating"
          clearable
          filterable
          style="max-width: 400px"
        />
        <p class="hint-text">将从游戏的翻译缓存中提取已翻译文本的字符</p>
      </div>
      <div v-if="translationMode === 'upload'" style="margin-top: 8px">
        <NUpload
          accept=".txt"
          :max="1"
          :custom-request="({ file }) => handleTranslationUpload({ file: file as any })"
          :show-file-list="false"
          :disabled="isGenerating"
        >
          <NButton :disabled="isGenerating" :loading="uploadingTranslation" size="small">
            上传翻译文件
          </NButton>
        </NUpload>
        <div v-if="translationFile" class="upload-info">
          <NTag size="small" type="success">{{ translationFile.fileName }}</NTag>
          <span>{{ translationFile.characterCount.toLocaleString() }} 字符</span>
          <NButton text size="tiny" @click="translationFile = null">移除</NButton>
        </div>
        <p class="hint-text">上传 XUnity 翻译缓存文件（格式：原文=译文），将提取译文中的字符</p>
      </div>

      <!-- Charset preview -->
      <div v-if="charsetPreview || loadingPreview" class="charset-preview">
        <NSpin v-if="loadingPreview" :size="16" style="margin-right: 8px" />
        <template v-if="charsetPreview">
          <div class="preview-stats">
            <NTag v-for="(count, source) in charsetPreview.sourceBreakdown" :key="source" size="small">
              {{ source }}: {{ count.toLocaleString() }}
            </NTag>
          </div>
          <div class="preview-total">
            合计 <strong>{{ charsetPreview.totalCharacters.toLocaleString() }}</strong> 个唯一字符，
            预计需要 <strong>{{ charsetPreview.estimatedAtlasCount }}</strong> 页 Atlas
          </div>
          <NAlert
            v-for="(warning, idx) in charsetPreview.warnings"
            :key="idx"
            type="warning"
            :bordered="false"
            style="margin-top: 8px"
          >
            {{ warning }}
          </NAlert>
        </template>
      </div>

      <!-- Generate button -->
      <div class="form-actions">
        <NButton
          type="primary"
          :disabled="!uploadedFont || isGenerating"
          :loading="isGenerating"
          @click="startGeneration"
        >
          开始生成
        </NButton>
      </div>

        </div>
      </div>
    </div>

    <!-- Progress Card -->
    <div v-if="isGenerating" class="section-card" style="animation-delay: 0.2s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><FontDownloadOutlined /></NIcon>
          </span>
          生成进度
        </h2>
      </div>
      <div class="progress-info">
        <span class="progress-phase">{{ phaseLabels[phase] || phase }}</span>
        <span v-if="total > 0" class="progress-count">{{ current }} / {{ total }}</span>
      </div>
      <NProgress :percentage="progressPercent" :show-indicator="true" type="line" />
      <div class="form-actions" style="margin-top: 12px">
        <NButton @click="cancelGeneration">取消</NButton>
      </div>
    </div>

    <!-- Report Card -->
    <div v-if="activeReport && !isGenerating" class="section-card" style="animation-delay: 0.2s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><AssessmentOutlined /></NIcon>
          </span>
          生成报告
        </h2>
      </div>

      <NDescriptions label-placement="left" :column="2" bordered size="small">
        <NDescriptionsItem label="字体名称">{{ activeReport.fontName }}</NDescriptionsItem>
        <NDescriptionsItem label="耗时">{{ formatDuration(activeReport.elapsedMilliseconds) }}</NDescriptionsItem>
        <NDescriptionsItem label="总字符数">{{ activeReport.totalCharacters.toLocaleString() }}</NDescriptionsItem>
        <NDescriptionsItem label="成功渲染">{{ activeReport.successfulGlyphs.toLocaleString() }}</NDescriptionsItem>
        <NDescriptionsItem label="缺失字形">{{ activeReport.totalMissingCount.toLocaleString() }}</NDescriptionsItem>
        <NDescriptionsItem label="Atlas">
          {{ activeReport.atlasCount }} 页 &times; {{ activeReport.atlasWidth }}&times;{{ activeReport.atlasHeight }}
        </NDescriptionsItem>
        <NDescriptionsItem label="渲染模式">{{ activeReport.renderMode }}</NDescriptionsItem>
        <NDescriptionsItem v-if="activeReport.samplingSizeMode === 'auto'" label="实际采样大小">
          {{ activeReport.actualSamplingSize }}px
        </NDescriptionsItem>
        <NDescriptionsItem label="Padding">{{ activeReport.padding }}px</NDescriptionsItem>
        <NDescriptionsItem label="GradientScale">{{ activeReport.gradientScale }}</NDescriptionsItem>
      </NDescriptions>

      <div v-if="Object.keys(activeReport.sourceBreakdown).length" style="margin-top: 12px">
        <NTag
          v-for="(count, source) in activeReport.sourceBreakdown"
          :key="source"
          size="small"
          style="margin: 4px"
        >
          {{ source }}: {{ count.toLocaleString() }}
        </NTag>
      </div>

      <NCollapse v-if="activeReport.totalMissingCount > 0" style="margin-top: 12px">
        <NCollapseItem title="查看缺失字符" name="missing">
          <div class="missing-chars">
            {{ activeReport.missingCharacters.join('') }}
            <span v-if="activeReport.totalMissingCount > activeReport.missingCharacters.length">
              ...（共 {{ activeReport.totalMissingCount }} 个）
            </span>
          </div>
        </NCollapseItem>
      </NCollapse>

      <div style="margin-top: 16px">
        <NButton @click="viewingReport = null; generationReport = null" quaternary size="small">关闭报告</NButton>
      </div>
    </div>

    <!-- History Card -->
    <div class="section-card" style="animation-delay: 0.25s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><DownloadOutlined /></NIcon>
          </span>
          已生成字体
        </h2>
      </div>
      <div v-if="generatedFonts.length === 0" class="empty-state">
        暂无已生成的字体
      </div>
      <div v-else class="font-list">
        <div v-for="font in generatedFonts" :key="font.fileName" class="font-item">
          <div class="font-item-info">
            <span class="font-item-name">{{ font.fontName }}</span>
            <span class="font-item-meta">
              {{ formatFileSize(font.fileSize) }} · {{ formatDate(font.generatedAt) }}
            </span>
          </div>
          <NSpace :size="8">
            <NButton size="small" @click="downloadFont(font.fileName)">
              <template #icon><NIcon><DownloadOutlined /></NIcon></template>
              下载
            </NButton>
            <NButton v-if="font.hasReport" size="small" @click="viewReport(font.fileName)">
              查看报告
            </NButton>
            <NSelect
              v-if="games.length > 0"
              size="small"
              placeholder="安装 TMP 字体"
              :options="games.map(g => ({ label: g.name, value: g.id }))"
              style="width: 160px"
              @update:value="(gameId: string) => installTmpFont(font.fileName, gameId)"
            />
            <NPopconfirm @positive-click="deleteFont(font.fileName)">
              <template #trigger>
                <NButton size="small" type="error" quaternary>
                  <template #icon><NIcon><DeleteOutlined /></NIcon></template>
                </NButton>
              </template>
              确定删除此字体文件？
            </NPopconfirm>
          </NSpace>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.font-generator-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.page-subtitle {
  font-size: 14px;
  color: var(--text-3);
  margin: -12px 0 0 0;
  animation: slideUp 0.5s var(--ease-out) backwards;
}

/* NUpload full width */
:deep(.n-upload) {
  width: 100%;
}
:deep(.n-upload-trigger) {
  width: 100%;
}

/* Upload + Settings Grid */
.upload-settings-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 20px;
  align-items: start;
}

.upload-column {
  display: flex;
  flex-direction: column;
}

.settings-column {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

/* Upload */
.upload-area {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 24px 16px;
  border: 2px dashed var(--border);
  border-radius: var(--radius-md);
  cursor: pointer;
  transition: border-color 0.2s ease, background 0.2s ease;
}

.upload-area:hover {
  border-color: var(--accent);
  background: var(--bg-subtle);
}

.upload-text {
  margin: 8px 0 2px;
  font-size: 14px;
  color: var(--text-2);
}

.upload-hint {
  margin: 0;
  font-size: 12px;
  color: var(--text-3);
}

.upload-result {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 14px;
  background: color-mix(in srgb, var(--accent) 8%, transparent);
  border-radius: var(--radius-md);
  border: 1px solid color-mix(in srgb, var(--accent) 25%, transparent);
}

.upload-result-info {
  display: flex;
  align-items: center;
  gap: 10px;
}

.upload-font-name {
  font-weight: 600;
  font-size: 14px;
  color: var(--text-1);
}

.upload-file-size {
  font-size: 12px;
  color: var(--text-3);
}

/* Settings */
.form-row {
  display: flex;
  align-items: center;
  gap: 12px;
}

.form-label {
  min-width: 80px;
  font-size: 13px;
  color: var(--text-2);
  flex-shrink: 0;
}

.form-actions {
  display: flex;
  justify-content: flex-end;
  margin-top: 16px;
}

/* Character Set */
.charset-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: 8px;
}

.charset-section-label {
  font-size: 14px;
  font-weight: 500;
  margin: 16px 0 8px;
  color: var(--text-2);
}

.charset-section-label:first-child {
  margin-top: 0;
}

.hint-text {
  font-size: 12px;
  color: var(--text-3);
  margin-top: 4px;
}

.upload-info {
  font-size: 13px;
  color: var(--text-2);
  margin-top: 8px;
  display: flex;
  align-items: center;
  gap: 8px;
}

.charset-preview {
  margin-top: 16px;
  padding: 12px 16px;
  border-radius: 8px;
  background: var(--bg-subtle);
}

.preview-stats {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-bottom: 8px;
}

.preview-total {
  font-size: 14px;
  color: var(--text-2);
}

/* Progress */
.progress-info {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 10px;
}

.progress-phase {
  font-size: 14px;
  font-weight: 500;
  color: var(--text-1);
}

.progress-count {
  font-size: 13px;
  color: var(--text-3);
  font-variant-numeric: tabular-nums;
}

/* Report */
.missing-chars {
  font-family: monospace;
  font-size: 14px;
  line-height: 2;
  word-break: break-all;
  max-height: 200px;
  overflow-y: auto;
}

/* Font List */
.empty-state {
  text-align: center;
  padding: 32px;
  color: var(--text-3);
  font-size: 14px;
}

.font-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.font-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 16px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  transition: border-color 0.2s ease;
}

.font-item:hover {
  border-color: var(--border-hover);
}

.font-item-info {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.font-item-name {
  font-weight: 600;
  font-size: 14px;
  color: var(--text-1);
}

.font-item-meta {
  font-size: 12px;
  color: var(--text-3);
}

/* Responsive */
@media (max-width: 768px) {
  .upload-settings-grid {
    grid-template-columns: 1fr;
  }
  .form-row {
    flex-direction: column;
    align-items: stretch;
    gap: 6px;
  }
  .form-label {
    min-width: unset;
  }
  .font-item {
    flex-direction: column;
    align-items: stretch;
    gap: 10px;
  }
  .charset-grid {
    grid-template-columns: 1fr;
  }
  .font-item :deep(.n-space) {
    flex-wrap: wrap;
    width: 100%;
  }
}

@media (max-width: 480px) {
  .upload-area {
    padding: 16px 12px;
  }
}

@keyframes slideUp {
  from {
    opacity: 0;
    transform: translateY(10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
</style>
