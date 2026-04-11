<script setup lang="ts">
import { computed, h, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { onBeforeRouteLeave, onBeforeRouteUpdate, useRoute, useRouter } from 'vue-router'
import {
  NAlert,
  NButton,
  NDataTable,
  NEmpty,
  NIcon,
  NInput,
  NSelect,
  NSpin,
  NTag,
  useDialog,
  useMessage,
} from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import {
  AddOutlined,
  ArrowBackOutlined,
  DeleteOutlined,
  DriveFileRenameOutlineOutlined,
  FileDownloadOutlined,
  FileUploadOutlined,
  FolderOutlined,
  RefreshOutlined,
  SaveOutlined,
  TranslateOutlined,
} from '@vicons/material'
import { filesystemApi } from '@/api/filesystem'
import { gamesApi, translationEditorApi } from '@/api/games'
import { useFileExplorer } from '@/composables/useFileExplorer'
import RegexRuleEditor from '@/components/translation/RegexRuleEditor.vue'
import type {
  Game,
  RegexTranslationRule,
  TranslationEditorSource,
  TranslationEditorTextSource,
  TranslationEntry,
} from '@/api/types'

interface TranslationRow extends TranslationEntry {
  _id: number
}

const SOURCE_OPTIONS = [
  { label: '普通译文', value: 'default' },
  { label: '预翻译文本', value: 'pretranslated' },
  { label: '预翻译正则', value: 'pretranslated-regex' },
]

const route = useRoute()
const router = useRouter()
const message = useMessage()
const dialog = useDialog()
const { selectFile } = useFileExplorer()

const gameId = route.params['id'] as string

const game = ref<Game | null>(null)
const loading = ref(true)
const saving = ref(false)
const importing = ref(false)
const filePath = ref('')
const fileExists = ref(false)
const currentSource = ref<TranslationEditorSource>('default')
const currentLanguage = ref('')
const availableLanguages = ref<string[]>([])
const entries = ref<TranslationRow[]>([])
const regexRules = ref<RegexTranslationRule[]>([])
const newOriginal = ref('')
const newTranslation = ref('')
const loadError = ref('')

let savedTextSnapshot = '[]'
let savedRegexSnapshot = '[]'
let nextRowId = 1
let loadingKey = ''

const isRegexMode = computed(() => currentSource.value === 'pretranslated-regex')
const entryCount = computed(() => (isRegexMode.value ? regexRules.value.length : entries.value.length))
const isDirty = computed(() =>
  isRegexMode.value
    ? serializeRegexRules(regexRules.value) !== savedRegexSnapshot
    : serializeTextEntries(entries.value) !== savedTextSnapshot,
)

const languageOptions = computed(() => {
  const values = new Set(
    availableLanguages.value
      .map(value => value.trim())
      .filter(Boolean),
  )

  if (currentLanguage.value.trim()) {
    values.add(currentLanguage.value.trim())
  }

  return [...values]
    .sort((left, right) => left.localeCompare(right))
    .map(value => ({
      label: value.toUpperCase(),
      value,
    }))
})

const tableColumns = computed<DataTableColumns<TranslationRow>>(() => [
  {
    title: '原文',
    key: 'original',
    minWidth: 320,
    resizable: true,
    render(row) {
      return h(NInput, {
        value: row.original,
        size: 'small',
        type: 'textarea',
        autosize: { minRows: 1, maxRows: 4 },
        'onUpdate:value': (value: string) => {
          row.original = value
        },
      })
    },
  },
  {
    title: '译文',
    key: 'translation',
    minWidth: 360,
    resizable: true,
    render(row) {
      return h(NInput, {
        value: row.translation,
        size: 'small',
        type: 'textarea',
        autosize: { minRows: 1, maxRows: 4 },
        'onUpdate:value': (value: string) => {
          row.translation = value
        },
      })
    },
  },
  {
    title: '状态',
    key: 'status',
    width: 92,
    render(row) {
      return h(
        NTag,
        {
          size: 'small',
          bordered: false,
          type: row.translation.trim() ? 'success' : 'warning',
        },
        {
          default: () => (row.translation.trim() ? '已翻译' : '未翻译'),
        },
      )
    },
  },
  {
    title: '',
    key: 'actions',
    width: 72,
    render(row) {
      return h(
        NButton,
        {
          size: 'tiny',
          quaternary: true,
          type: 'error',
          onClick: () => {
            entries.value = entries.value.filter(item => item._id !== row._id)
          },
        },
        {
          icon: () => h(NIcon, { size: 16 }, () => h(DeleteOutlined)),
        },
      )
    },
  },
])

function normalizeSource(value: unknown): TranslationEditorSource {
  return value === 'pretranslated' || value === 'pretranslated-regex' ? value : 'default'
}

function normalizeLang(value: unknown): string | undefined {
  const text = Array.isArray(value) ? value[0] : value
  return typeof text === 'string' && text.trim() ? text : undefined
}

function getRouteSource() {
  return normalizeSource(route.query.source)
}

function getRouteLang() {
  return normalizeLang(route.query.lang)
}

function buildRouteQuery(source: TranslationEditorSource, lang?: string) {
  const query = { ...route.query } as Record<string, string>
  if (source === 'default') {
    delete query.source
    delete query.lang
  } else {
    query.source = source
    if (lang) query.lang = lang
    else delete query.lang
  }
  return query
}

function createRow(entry: TranslationEntry): TranslationRow {
  return {
    ...entry,
    _id: nextRowId++,
  }
}

function serializeTextEntries(items: TranslationEntry[]) {
  return JSON.stringify(items.map(item => ({
    original: item.original,
    translation: item.translation,
  })))
}

function serializeRegexRules(items: RegexTranslationRule[]) {
  return JSON.stringify(items.map(item => ({
    section: item.section,
    kind: item.kind,
    pattern: item.pattern,
    replacement: item.replacement,
  })))
}

function captureSnapshots() {
  savedTextSnapshot = serializeTextEntries(entries.value)
  savedRegexSnapshot = serializeRegexRules(regexRules.value)
}

function buildTextOptions(source = currentSource.value, lang = currentLanguage.value) {
  const textSource: TranslationEditorTextSource = source === 'pretranslated' ? 'pretranslated' : 'default'
  if (textSource === 'pretranslated') {
    return {
      source: textSource,
      ...(lang ? { lang } : {}),
    }
  }

  return { source: textSource }
}

async function loadEditorForRoute() {
  const source = getRouteSource()
  const lang = getRouteLang()
  const key = `${source}|${lang ?? ''}`
  loadingKey = key
  loading.value = true

  try {
    currentSource.value = source

    if (source === 'pretranslated-regex') {
      const editorData = await translationEditorApi.getRegex(gameId, lang)
      if (loadingKey !== key) return

      currentLanguage.value = editorData.language
      availableLanguages.value = editorData.availablePreTranslationLanguages
      filePath.value = editorData.filePath
      fileExists.value = editorData.fileExists
      regexRules.value = editorData.rules
      entries.value = []
      captureSnapshots()
      loadError.value = ''
      return
    }

    const editorData = await translationEditorApi.getEntries(gameId, buildTextOptions(source, lang))
    if (loadingKey !== key) return

    currentSource.value = editorData.source
    currentLanguage.value = editorData.language
    availableLanguages.value = editorData.availablePreTranslationLanguages
    filePath.value = editorData.filePath
    fileExists.value = editorData.fileExists
    entries.value = editorData.entries.map(createRow)
    regexRules.value = []
    captureSnapshots()
    loadError.value = ''
  } finally {
    if (loadingKey === key) {
      loading.value = false
    }
  }
}

function getErrorMessage(error: unknown, fallback: string) {
  return error instanceof Error ? error.message : fallback
}

async function loadPage() {
  loading.value = true
  loadError.value = ''
  try {
    game.value = await gamesApi.get(gameId)
    await loadEditorForRoute()
  } catch (error) {
    const errorMessage = getErrorMessage(error, '加载译文编辑器失败')
    loadError.value = errorMessage
    message.error(errorMessage)
  } finally {
    loading.value = false
  }
}

function handleBeforeUnload(event: BeforeUnloadEvent) {
  if (!isDirty.value) return
  event.preventDefault()
  event.returnValue = ''
}

async function confirmDiscardChanges(content: string) {
  if (!isDirty.value) return true

  return await new Promise<boolean>((resolve) => {
    dialog.warning({
      title: '存在未保存修改',
      content,
      positiveText: '继续',
      negativeText: '取消',
      onPositiveClick: () => resolve(true),
      onNegativeClick: () => resolve(false),
      onClose: () => resolve(false),
    })
  })
}

async function handleSave() {
  saving.value = true
  try {
    if (isRegexMode.value) {
      for (const rule of regexRules.value) {
        if (!rule.pattern.trim()) {
          message.error('存在空的正则表达式，请填写或删除')
          return
        }
      }

      await translationEditorApi.saveRegex(gameId, regexRules.value, currentLanguage.value)
      fileExists.value = true
      captureSnapshots()
      message.success('预翻译正则已保存')
      return
    }

    const seen = new Set<string>()

    for (const row of entries.value) {
      const original = row.original.trim()
      if (!original) {
        message.error('存在空白原文，请填写或删除后再保存')
        return
      }

      if (seen.has(original)) {
        message.error(`存在重复原文: ${original.slice(0, 60)}`)
        return
      }

      seen.add(original)
    }

    await translationEditorApi.saveEntries(
      gameId,
      entries.value.map(row => ({
        original: row.original,
        translation: row.translation,
      })),
      buildTextOptions(),
    )
    fileExists.value = true
    captureSnapshots()
    message.success(currentSource.value === 'pretranslated' ? '预翻译文本已保存' : '译文已保存')
  } catch (error) {
    message.error(error instanceof Error ? error.message : '保存失败')
  } finally {
    saving.value = false
  }
}

function handleAddEntry() {
  const original = newOriginal.value.trim()
  if (!original) {
    message.warning('请输入原文')
    return
  }

  if (entries.value.some(row => row.original === original)) {
    message.warning('该原文已存在')
    return
  }

  entries.value = [
    createRow({
      original,
      translation: newTranslation.value,
    }),
    ...entries.value,
  ]
  newOriginal.value = ''
  newTranslation.value = ''
}

async function handleImport() {
  const path = await selectFile({
    title: isRegexMode.value ? '导入预翻译正则文件' : '导入翻译文件',
    filters: [{ label: '文本文件', extensions: ['.txt'] }],
  })
  if (!path) return

  importing.value = true
  try {
    const { content } = await filesystemApi.readText(path)

    if (isRegexMode.value) {
      regexRules.value = await translationEditorApi.importRegex(gameId, content, currentLanguage.value)
      message.success(`导入完成，共 ${regexRules.value.length} 条规则`)
      return
    }

    const importedEntries = await translationEditorApi.parseImport(gameId, content)
    const existingOriginals = new Set(entries.value.map(row => row.original))

    let added = 0
    for (const entry of importedEntries) {
      if (existingOriginals.has(entry.original)) continue
      entries.value = [...entries.value, createRow(entry)]
      existingOriginals.add(entry.original)
      added++
    }

    message.success(`导入完成，新增 ${added} 条`)
  } catch (error) {
    message.error(error instanceof Error ? error.message : '导入失败')
  } finally {
    importing.value = false
  }
}

function handleExport() {
  const url = isRegexMode.value
    ? translationEditorApi.getRegexExportUrl(gameId, currentLanguage.value)
    : translationEditorApi.getExportUrl(gameId, buildTextOptions())

  window.open(url, '_blank', 'noopener')
}

async function handleReload() {
  const confirmed = await confirmDiscardChanges('重新加载将丢失当前未保存修改，是否继续？')
  if (!confirmed) return

  try {
    await loadEditorForRoute()
    message.success('已重新加载文件内容')
  } catch (error) {
    message.error(error instanceof Error ? error.message : '重新加载失败')
  }
}

async function handleSourceChange(value: string) {
  const nextSource = normalizeSource(value)
  if (nextSource === currentSource.value) return

  const nextLanguage = nextSource === 'default'
    ? undefined
    : currentLanguage.value || availableLanguages.value[0]

  await router.replace({
    query: buildRouteQuery(nextSource, nextLanguage),
  })
}

async function handleLanguageChange(value: string | null) {
  const nextLanguage = value ?? ''
  if (!nextLanguage || nextLanguage === currentLanguage.value) return

  await router.replace({
    query: buildRouteQuery(currentSource.value, nextLanguage),
  })
}

onMounted(async () => {
  window.addEventListener('beforeunload', handleBeforeUnload)
  await loadPage()
})

watch(
  () => `${getRouteSource()}|${getRouteLang() ?? ''}`,
  async () => {
    if (!game.value) return
    try {
      await loadEditorForRoute()
    } catch (error) {
      const errorMessage = getErrorMessage(error, '加载译文编辑器失败')
      loadError.value = errorMessage
      message.error(errorMessage)
    }
  },
)

onBeforeUnmount(() => {
  window.removeEventListener('beforeunload', handleBeforeUnload)
})

onBeforeRouteUpdate(async (to, from) => {
  const nextKey = `${normalizeSource(to.query.source)}|${normalizeLang(to.query.lang) ?? ''}`
  const currentKey = `${normalizeSource(from.query.source)}|${normalizeLang(from.query.lang) ?? ''}`
  if (nextKey === currentKey) return true
  return await confirmDiscardChanges('切换来源或语言会丢失当前未保存修改，是否继续？')
})

onBeforeRouteLeave(async () => {
  return await confirmDiscardChanges('离开页面会丢失未保存修改，是否继续？')
})
</script>

<template>
  <div v-if="loading" class="loading-state">
    <NSpin size="large" />
  </div>

  <div v-else-if="game || loadError" class="sub-page">
    <div class="sub-page-header" style="animation-delay: 0s">
      <button class="back-button" @click="router.push(game ? `/games/${gameId}` : '/')">
        <NIcon :size="20"><ArrowBackOutlined /></NIcon>
        <span>{{ game?.name ?? '返回游戏库' }}</span>
      </button>
    </div>

    <h1 class="page-title" style="animation-delay: 0.05s">
      <span class="page-title-icon">
        <NIcon :size="24"><DriveFileRenameOutlineOutlined /></NIcon>
      </span>
      译文编辑器
      <span v-if="game && isDirty" class="unsaved-badge">未保存</span>
    </h1>

    <div v-if="loadError" class="section-card" style="animation-delay: 0.1s">
      <NAlert type="error" title="加载失败">
        {{ loadError }}
      </NAlert>
      <div class="header-actions" style="margin-top: 16px">
        <NButton size="small" @click="loadPage">
          <template #icon><NIcon :size="16"><RefreshOutlined /></NIcon></template>
          重新加载
        </NButton>
      </div>
    </div>

    <template v-else>
      <div class="section-card" style="animation-delay: 0.1s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><FolderOutlined /></NIcon>
          </span>
          文件信息
        </h2>
        <div class="header-actions">
          <NButton size="small" :loading="importing" @click="handleImport">
            <template #icon><NIcon :size="16"><FileUploadOutlined /></NIcon></template>
            导入
          </NButton>
          <NButton size="small" :disabled="!fileExists" @click="handleExport">
            <template #icon><NIcon :size="16"><FileDownloadOutlined /></NIcon></template>
            导出
          </NButton>
          <NButton size="small" :disabled="saving" @click="handleReload">
            <template #icon><NIcon :size="16"><RefreshOutlined /></NIcon></template>
            重新加载
          </NButton>
          <NButton size="small" type="primary" :loading="saving" :disabled="!isDirty" @click="handleSave">
            <template #icon><NIcon :size="16"><SaveOutlined /></NIcon></template>
            保存
          </NButton>
        </div>
      </div>

      <div class="toolbar-grid">
        <label class="toolbar-field">
          <span class="toolbar-label">译文来源</span>
          <NSelect :value="currentSource" :options="SOURCE_OPTIONS" size="small" @update:value="handleSourceChange" />
        </label>
        <label v-if="currentSource !== 'default'" class="toolbar-field">
          <span class="toolbar-label">语言</span>
          <NSelect
            :value="currentLanguage"
            :options="languageOptions"
            size="small"
            :disabled="languageOptions.length === 0"
            @update:value="handleLanguageChange"
          />
        </label>
      </div>

      <div class="file-info">
        <div class="info-row">
          <div class="info-item">
            <span class="info-label">文件路径</span>
            <code class="info-value file-path">{{ filePath }}</code>
          </div>
        </div>
        <div class="info-row">
          <div class="info-item">
            <span class="info-label">条目数量</span>
            <span class="info-value">{{ entryCount }}</span>
          </div>
          <div class="info-item">
            <span class="info-label">当前语言</span>
            <NTag size="small" :bordered="false">{{ currentLanguage.toUpperCase() }}</NTag>
          </div>
          <div class="info-item">
            <span class="info-label">文件状态</span>
            <NTag size="small" :bordered="false" :type="fileExists ? 'success' : 'warning'">
              {{ fileExists ? '已存在' : '保存时创建' }}
            </NTag>
          </div>
        </div>
      </div>

      <NAlert v-if="currentSource === 'pretranslated'" type="info" style="margin-top: 12px">
        当前正在编辑预翻译文本。切换来源或语言前，请先保存当前修改。
      </NAlert>
      <NAlert
        v-if="currentSource !== 'default' && languageOptions.length === 0"
        type="info"
        style="margin-top: 12px"
      >
        当前还没有可用的预翻译缓存，将使用目标语言路径创建新文件。
      </NAlert>
      <NAlert v-if="currentSource === 'pretranslated-regex'" type="warning" style="margin-top: 12px">
        当前正在编辑预翻译正则。再次执行预翻译时，基础规则与动态规则会被覆盖，自定义规则会保留。
      </NAlert>
      </div>

      <div class="section-card" style="animation-delay: 0.15s">
        <div class="section-header">
          <h2 class="section-title">
            <span class="section-icon">
              <NIcon :size="16"><TranslateOutlined /></NIcon>
            </span>
            {{ isRegexMode ? '正则规则' : '翻译条目' }}
            <NTag size="small" :bordered="false" style="margin-left: 8px">
              {{ entryCount }}
            </NTag>
          </h2>
        </div>

        <template v-if="isRegexMode">
          <RegexRuleEditor v-model:rules="regexRules" />
        </template>

        <template v-else>
          <div class="add-entry-row">
            <NInput
              v-model:value="newOriginal"
              placeholder="原文"
              size="small"
              type="textarea"
              :autosize="{ minRows: 1, maxRows: 3 }"
            />
            <NInput
              v-model:value="newTranslation"
              placeholder="译文（可选）"
              size="small"
              type="textarea"
              :autosize="{ minRows: 1, maxRows: 3 }"
            />
            <NButton size="small" type="primary" @click="handleAddEntry">
              <template #icon><NIcon :size="16"><AddOutlined /></NIcon></template>
              添加
            </NButton>
          </div>

          <div v-if="entries.length > 0" class="table-container">
            <NDataTable
              :columns="tableColumns"
              :data="entries"
              :row-key="(row: TranslationRow) => row._id"
              :max-height="640"
              virtual-scroll
              striped
              size="small"
            />
          </div>
          <NEmpty v-else description="暂无翻译条目" style="padding: 40px 0" />
        </template>
      </div>
    </template>
  </div>
</template>

<style scoped>
.toolbar-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 320px));
  gap: 12px;
  margin-bottom: 16px;
}

.toolbar-field {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.toolbar-label {
  font-size: 12px;
  color: var(--text-3);
}

.file-info {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.info-row {
  display: flex;
  flex-wrap: wrap;
  gap: 16px;
}

.info-item {
  display: flex;
  align-items: center;
  gap: 8px;
}

.info-label {
  font-size: 12px;
  color: var(--text-3);
}

.info-value {
  font-size: 13px;
  color: var(--text-1);
  font-family: var(--font-mono);
}

.file-path {
  background: var(--bg-subtle);
  padding: 2px 8px;
  border-radius: var(--radius-sm);
  word-break: break-all;
}

.add-entry-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr) auto;
  gap: 12px;
  margin-bottom: 16px;
}

.table-container {
  overflow: hidden;
}

@media (max-width: 960px) {
  .add-entry-row {
    grid-template-columns: 1fr;
  }
}
</style>
