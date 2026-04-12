<script setup lang="ts">
import { ref, computed, h, onMounted, nextTick } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NButton,
  NIcon,
  NInput,
  NDataTable,
  NSwitch,
  NTag,
  NSpin,
  NEmpty,
  NSelect,
  NInputNumber,
  NPopover,
  NCheckbox,
  NModal,
  useMessage,
  useDialog,
} from 'naive-ui'
import type { DataTableColumns, SelectOption } from 'naive-ui'
import {
  ArrowBackOutlined,
  SearchOutlined,
  SaveOutlined,
  FileUploadOutlined,
  FileDownloadOutlined,
  AddOutlined,
  DeleteOutlined,
  DeleteSweepOutlined,
  MenuBookOutlined,
  SettingsOutlined,
  ContentCopyOutlined,
} from '@vicons/material'
import { gamesApi } from '@/api/games'
import { filesystemApi } from '@/api/filesystem'
import { useFileExplorer } from '@/composables/useFileExplorer'
import type { Game, TermEntry, TermType, TermCategory } from '@/api/types'
import { useAutoSave } from '@/composables/useAutoSave'

interface TermRow extends TermEntry {
  _id: number
}

const route = useRoute()
const router = useRouter()
const message = useMessage()
const dialog = useDialog()

const gameId = route.params['id'] as string
const game = ref<Game | null>(null)
const loading = ref(true)
const manualSaving = ref(false)

// Editor state
const entries = ref<TermRow[]>([])
let nextId = 1

// Import
const { selectFile } = useFileExplorer()

// Add entry form
const newType = ref<TermType>('translate')
const newOriginal = ref('')
const newTranslation = ref('')
const newCategory = ref<TermCategory>('general')

// Search
const searchKeyword = ref('')

// Filters
const filterType = ref<TermType | 'all'>('all')
const filterCategory = ref<TermCategory | null>(null)

// Column visibility
const COLUMN_VISIBILITY_KEY = 'term-editor-columns'
const defaultVisibleColumns = ['type', 'original', 'translation', 'category', 'isRegex', 'exactMatch', 'actions']
const visibleColumns = ref<string[]>(loadColumnVisibility())

function loadColumnVisibility(): string[] {
  try {
    const stored = localStorage.getItem(COLUMN_VISIBILITY_KEY)
    if (stored) return JSON.parse(stored)
  } catch { /* ignore */ }
  return [...defaultVisibleColumns]
}

function saveColumnVisibility() {
  localStorage.setItem(COLUMN_VISIBILITY_KEY, JSON.stringify(visibleColumns.value))
}

// Import from game modal
const showImportGameModal = ref(false)
const gameList = ref<Game[]>([])
const loadingGames = ref(false)
const importingFromGame = ref(false)

// Category options
const categoryOptions: SelectOption[] = [
  { label: '角色名', value: 'character' },
  { label: '地名', value: 'location' },
  { label: '物品名', value: 'item' },
  { label: '技能名', value: 'skill' },
  { label: '组织名', value: 'organization' },
  { label: '通用', value: 'general' },
]

const categoryLabelMap: Record<string, string> = {
  character: '角色名',
  location: '地名',
  item: '物品名',
  skill: '技能名',
  organization: '组织名',
  general: '通用',
}

const typeOptions: SelectOption[] = [
  { label: '翻译', value: 'translate' },
  { label: '禁翻', value: 'doNotTranslate' },
]

// All optional column definitions for visibility control
const optionalColumns = [
  { key: 'description', label: '描述' },
  { key: 'caseSensitive', label: '大小写敏感' },
  { key: 'priority', label: '优先级' },
]

function toRows(items: TermEntry[]): TermRow[] {
  return items.map(e => ({ ...e, _id: nextId++ }))
}

function getValidEntries(): TermEntry[] {
  return entries.value
    .filter(e => e.original.trim())
    .map(({ type, original, translation, category, description, isRegex, caseSensitive, exactMatch, priority }) => ({
      type,
      original,
      translation: type === 'doNotTranslate' ? undefined : translation,
      category: category || undefined,
      description: description?.trim() || undefined,
      isRegex,
      caseSensitive,
      exactMatch,
      priority,
    }))
}

// Auto-save (2s debounce, deep watch)
const { saving: autoSaving, enable: enableAutoSave, disable: disableAutoSave } = useAutoSave(
  () => entries.value,
  async () => {
    try {
      const valid = getValidEntries()
      await gamesApi.saveTerms(gameId, valid)
    } catch {
      message.error('自动保存术语库失败')
    }
  },
  { debounceMs: 2000, deep: true },
)

const aggregateCounts = computed(() => {
  const type = { all: entries.value.length, translate: 0, dnt: 0 }
  const category: Record<string, number> = {
    character: 0,
    location: 0,
    item: 0,
    skill: 0,
    organization: 0,
    general: 0,
  }

  for (const entry of entries.value) {
    if (entry.type === 'translate') type.translate++
    else if (entry.type === 'doNotTranslate') type.dnt++

    const key = entry.category || 'general'
    category[key] = (category[key] ?? 0) + 1
  }

  return { type, category }
})

const typeCounts = computed(() => aggregateCounts.value.type)
const categoryCounts = computed(() => aggregateCounts.value.category)

const filteredEntries = computed(() => {
  let result = entries.value

  // Type filter
  if (filterType.value !== 'all') {
    result = result.filter(e => e.type === filterType.value)
  }

  // Category filter
  if (filterCategory.value) {
    result = result.filter(e => (e.category || 'general') === filterCategory.value)
  }

  // Search
  const kw = searchKeyword.value.toLowerCase()
  if (kw) {
    result = result.filter(
      e =>
        e.original.toLowerCase().includes(kw) ||
        (e.translation ?? '').toLowerCase().includes(kw) ||
        (e.description ?? '').toLowerCase().includes(kw),
    )
  }

  return result
})

// Table columns
const tableColumns = computed<DataTableColumns<TermRow>>(() => {
  const cols: DataTableColumns<TermRow> = []

  if (visibleColumns.value.includes('type')) {
    cols.push({
      title: '类型',
      key: 'type',
      width: 70,
      align: 'center',
      render(row) {
        return h(NTag, {
          size: 'small',
          type: row.type === 'translate' ? 'info' : 'warning',
          bordered: false,
          style: 'cursor: pointer',
          onClick: () => {
            if (row.type === 'translate') {
              row.type = 'doNotTranslate'
              row.translation = undefined
            } else {
              row.type = 'translate'
            }
          },
        }, () => row.type === 'translate' ? '翻译' : '禁翻')
      },
    })
  }

  if (visibleColumns.value.includes('original')) {
    cols.push({
      title: '原文',
      key: 'original',
      resizable: true,
      minWidth: 140,
      render(row) {
        return h(NInput, {
          value: row.original,
          size: 'small',
          type: 'text',
          placeholder: '原文 / 正则表达式',
          'onUpdate:value': (v: string) => { row.original = v },
        })
      },
    })
  }

  if (visibleColumns.value.includes('translation')) {
    cols.push({
      title: '译文',
      key: 'translation',
      resizable: true,
      minWidth: 140,
      render(row) {
        if (row.type === 'doNotTranslate') {
          return h('span', { style: 'color: var(--text-3); font-size: 13px;' }, '\u2014')
        }
        return h(NInput, {
          value: row.translation ?? '',
          size: 'small',
          type: 'text',
          placeholder: '译文',
          'onUpdate:value': (v: string) => { row.translation = v },
        })
      },
    })
  }

  if (visibleColumns.value.includes('category')) {
    cols.push({
      title: '分类',
      key: 'category',
      width: 100,
      render(row) {
        return h(NSelect, {
          value: row.category || 'general',
          size: 'small',
          options: categoryOptions,
          'onUpdate:value': (v: TermCategory) => { row.category = v },
        })
      },
    })
  }

  if (visibleColumns.value.includes('description')) {
    cols.push({
      title: '描述',
      key: 'description',
      resizable: true,
      minWidth: 120,
      render(row) {
        return h(NInput, {
          value: row.description ?? '',
          size: 'small',
          type: 'text',
          placeholder: '术语说明',
          'onUpdate:value': (v: string) => { row.description = v || undefined },
        })
      },
    })
  }

  if (visibleColumns.value.includes('isRegex')) {
    cols.push({
      title() {
        return h(NPopover, { trigger: 'hover', placement: 'top' }, {
          trigger: () => h('span', { style: 'cursor: help; border-bottom: 1px dashed var(--text-3)' }, '正则'),
          default: () => h('div', { style: 'max-width: 280px; font-size: 13px' }, [
            h('div', { style: 'font-weight: 600; margin-bottom: 4px' }, '正则表达式匹配'),
            h('div', '开启后，原文字段将作为正则表达式来匹配游戏文本。适用于需要模糊匹配的场景。'),
            h('div', { style: 'margin-top: 4px; color: var(--text-3)' }, '注意：正则术语不参与术语审查验证。'),
          ]),
        })
      },
      key: 'isRegex',
      width: 55,
      align: 'center',
      render(row) {
        return h(NSwitch, {
          value: row.isRegex,
          size: 'small',
          'onUpdate:value': (v: boolean) => { row.isRegex = v },
        })
      },
    })
  }

  if (visibleColumns.value.includes('exactMatch')) {
    cols.push({
      title() {
        return h(NPopover, { trigger: 'hover', placement: 'top' }, {
          trigger: () => h('span', { style: 'cursor: help; border-bottom: 1px dashed var(--text-3)' }, '精确'),
          default: () => h('div', { style: 'max-width: 280px; font-size: 13px' }, [
            h('div', { style: 'font-weight: 600; margin-bottom: 4px' }, '精确匹配（词边界）'),
            h('div', '开启后，仅匹配完整的词，不匹配包含该词的更长文本。'),
            h('div', { style: 'margin-top: 4px; color: var(--text-3)' }, '例如：开启精确匹配后，术语 "fire" 不会匹配 "firefox"。'),
          ]),
        })
      },
      key: 'exactMatch',
      width: 55,
      align: 'center',
      render(row) {
        return h(NSwitch, {
          value: row.exactMatch,
          size: 'small',
          'onUpdate:value': (v: boolean) => { row.exactMatch = v },
        })
      },
    })
  }

  if (visibleColumns.value.includes('caseSensitive')) {
    cols.push({
      title() {
        return h(NPopover, { trigger: 'hover', placement: 'top' }, {
          trigger: () => h('span', { style: 'cursor: help; border-bottom: 1px dashed var(--text-3)' }, '大小写'),
          default: () => h('div', { style: 'max-width: 280px; font-size: 13px' }, [
            h('div', { style: 'font-weight: 600; margin-bottom: 4px' }, '大小写敏感'),
            h('div', '开启后，匹配时区分大小写。'),
            h('div', { style: 'margin-top: 4px; color: var(--text-3)' }, '例如：开启后，术语 "Light" 不会匹配 "light" 或 "LIGHT"。'),
          ]),
        })
      },
      key: 'caseSensitive',
      width: 60,
      align: 'center',
      render(row) {
        return h(NSwitch, {
          value: row.caseSensitive,
          size: 'small',
          'onUpdate:value': (v: boolean) => { row.caseSensitive = v },
        })
      },
    })
  }

  if (visibleColumns.value.includes('priority')) {
    cols.push({
      title: '优先级',
      key: 'priority',
      width: 80,
      render(row) {
        return h(NInputNumber, {
          value: row.priority,
          size: 'small',
          min: 0,
          max: 9999,
          showButton: false,
          placeholder: '0',
          'onUpdate:value': (v: number | null) => { row.priority = v ?? 0 },
        })
      },
    })
  }

  if (visibleColumns.value.includes('actions')) {
    cols.push({
      title: '',
      key: 'actions',
      width: 40,
      render(row) {
        return h(NButton, {
          size: 'tiny',
          quaternary: true,
          type: 'error',
          onClick: () => {
            const idx = entries.value.findIndex(e => e._id === row._id)
            if (idx >= 0) entries.value.splice(idx, 1)
          },
        }, {
          icon: () => h(NIcon, { size: 16 }, () => h(DeleteOutlined)),
        })
      },
    })
  }

  return cols
})

// ── Lifecycle ──

onMounted(async () => {
  disableAutoSave()
  try {
    const [gameData, termsData] = await Promise.all([
      gamesApi.get(gameId),
      gamesApi.getTerms(gameId),
    ])
    game.value = gameData
    entries.value = toRows(termsData)
  } catch {
    message.error('加载失败')
  } finally {
    loading.value = false
  }
  await nextTick()
  enableAutoSave()
})

// ── Actions ──

async function handleManualSave() {
  manualSaving.value = true
  disableAutoSave()
  try {
    const valid = getValidEntries()
    await gamesApi.saveTerms(gameId, valid)
    entries.value = toRows(valid)
    await nextTick()
    message.success('保存成功')
  } catch (e) {
    message.error(e instanceof Error ? e.message : '保存失败')
  } finally {
    manualSaving.value = false
    enableAutoSave()
  }
}

function handleAddEntry() {
  if (!newOriginal.value.trim()) {
    message.warning('请输入原文')
    return
  }
  if (entries.value.some(e => e.original === newOriginal.value)) {
    message.warning('该原文已存在')
    return
  }
  entries.value.unshift({
    _id: nextId++,
    type: newType.value,
    original: newOriginal.value,
    translation: newType.value === 'doNotTranslate' ? undefined : newTranslation.value,
    category: newCategory.value,
    isRegex: false,
    caseSensitive: true,
    exactMatch: false,
    priority: 0,
  })
  newOriginal.value = ''
  newTranslation.value = ''
}

function handleClearAll() {
  if (entries.value.length === 0) return
  dialog.warning({
    title: '清空术语库',
    content: `确定要清空全部 ${entries.value.length} 条术语吗？此操作不可撤销。`,
    positiveText: '清空',
    negativeText: '取消',
    onPositiveClick: () => {
      entries.value = []
      message.success('已清空术语库')
    },
  })
}

// ── Import / Export ──

async function handleImportClick() {
  const path = await selectFile({
    title: '导入术语文件',
    filters: [{ label: '术语文件', extensions: ['.json', '.csv', '.tsv'] }],
  })
  if (!path) return

  try {
    const { content: text, fileName } = await filesystemApi.readText(path)
    const ext = fileName.toLowerCase().split('.').pop() ?? ''

    let imported: TermEntry[]

    if (ext === 'json') {
      imported = parseJsonImport(text)
    } else if (ext === 'csv' || ext === 'tsv') {
      imported = parseCsvImport(text, ext === 'tsv' ? '\t' : ',')
    } else {
      // Try JSON first, then CSV
      try {
        imported = parseJsonImport(text)
      } catch {
        imported = parseCsvImport(text, text.includes('\t') ? '\t' : ',')
      }
    }

    // Merge: skip duplicates
    const existingOriginals = new Set(entries.value.map(e => e.original))
    let added = 0
    for (const entry of imported) {
      if (!existingOriginals.has(entry.original)) {
        entries.value.push({ ...entry, _id: nextId++ })
        existingOriginals.add(entry.original)
        added++
      }
    }
    message.success(`导入完成: 新增 ${added} 条，跳过 ${imported.length - added} 条重复`)
  } catch (e) {
    message.error(e instanceof Error ? e.message : '导入失败')
  }
}

function parseJsonImport(text: string): TermEntry[] {
  let parsed: unknown[]
  try {
    parsed = JSON.parse(text)
  } catch {
    throw new Error('文件格式错误：不是有效的 JSON')
  }

  if (!Array.isArray(parsed)) {
    throw new Error('文件格式错误：应为 JSON 数组')
  }

  const result: TermEntry[] = []
  for (const item of parsed as Record<string, unknown>[]) {
    if (typeof item.original !== 'string' || !item.original.trim()) continue

    // Detect format
    if ('type' in item) {
      // New TermEntry format
      result.push({
        type: item.type === 'doNotTranslate' ? 'doNotTranslate' : 'translate',
        original: item.original,
        translation: typeof item.translation === 'string' ? item.translation : undefined,
        category: typeof item.category === 'string' ? item.category as TermCategory : undefined,
        description: typeof item.description === 'string' && item.description.trim() ? item.description : undefined,
        isRegex: typeof item.isRegex === 'boolean' ? item.isRegex : false,
        caseSensitive: typeof item.caseSensitive === 'boolean' ? item.caseSensitive : true,
        exactMatch: typeof item.exactMatch === 'boolean' ? item.exactMatch : false,
        priority: typeof item.priority === 'number' ? item.priority : 0,
      })
    } else if ('isRegex' in item) {
      // Old GlossaryEntry format
      result.push({
        type: 'translate',
        original: item.original,
        translation: typeof item.translation === 'string' ? item.translation : '',
        description: typeof item.description === 'string' && item.description.trim() ? item.description : undefined,
        isRegex: typeof item.isRegex === 'boolean' ? item.isRegex : false,
        caseSensitive: true,
        exactMatch: false,
        priority: 0,
      })
    } else if ('caseSensitive' in item) {
      // Old DoNotTranslateEntry format
      result.push({
        type: 'doNotTranslate',
        original: item.original,
        isRegex: false,
        caseSensitive: typeof item.caseSensitive === 'boolean' ? item.caseSensitive : true,
        exactMatch: false,
        priority: 0,
      })
    } else {
      // Unknown format, default to translate
      result.push({
        type: 'translate',
        original: item.original,
        translation: typeof item.translation === 'string' ? item.translation : '',
        isRegex: false,
        caseSensitive: true,
        exactMatch: false,
        priority: 0,
      })
    }
  }
  return result
}

function parseCsvImport(text: string, delimiter: string): TermEntry[] {
  const lines = text.trim().split('\n').map(l => l.trim()).filter(Boolean)
  if (lines.length < 2) throw new Error('CSV 文件至少需要表头和一行数据')

  const headers = lines[0]!.split(delimiter).map(h => h.trim().toLowerCase().replace(/^"(.*)"$/, '$1'))
  const result: TermEntry[] = []

  for (let i = 1; i < lines.length; i++) {
    const values = parseCsvLine(lines[i]!, delimiter)
    const row: Record<string, string> = {}
    headers.forEach((h, idx) => { row[h] = values[idx] ?? '' })

    const original = (row['original'] || row['原文'] || '').trim()
    if (!original) continue

    const typeStr = (row['type'] || row['类型'] || '').trim().toLowerCase()
    const type: TermType = typeStr === 'donotranslate' || typeStr === 'donottranslate' || typeStr === '禁翻'
      ? 'doNotTranslate' : 'translate'

    result.push({
      type,
      original,
      translation: row['translation'] || row['译文'] || undefined,
      category: normalizeCategoryInput(row['category'] || row['分类'] || '') || undefined,
      description: (row['description'] || row['描述'] || '').trim() || undefined,
      isRegex: (row['isregex'] || row['正则'] || '').toLowerCase() === 'true',
      caseSensitive: (row['casesensitive'] || row['大小写敏感'] || 'true').toLowerCase() !== 'false',
      exactMatch: (row['exactmatch'] || row['精确匹配'] || '').toLowerCase() === 'true',
      priority: parseInt(row['priority'] || row['优先级'] || '0') || 0,
    })
  }

  return result
}

function parseCsvLine(line: string, delimiter: string): string[] {
  const result: string[] = []
  let current = ''
  let inQuotes = false

  for (let i = 0; i < line.length; i++) {
    const ch = line[i]
    if (inQuotes) {
      if (ch === '"') {
        if (i + 1 < line.length && line[i + 1] === '"') {
          current += '"'
          i++
        } else {
          inQuotes = false
        }
      } else {
        current += ch
      }
    } else {
      if (ch === '"') {
        inQuotes = true
      } else if (ch === delimiter) {
        result.push(current)
        current = ''
      } else {
        current += ch
      }
    }
  }
  result.push(current)
  return result
}

function normalizeCategoryInput(val: string): TermCategory | undefined {
  const v = val.trim().toLowerCase()
  if (!v) return undefined
  const map: Record<string, TermCategory> = {
    character: 'character', '角色名': 'character', '角色': 'character',
    location: 'location', '地名': 'location', '地点': 'location',
    item: 'item', '物品名': 'item', '物品': 'item',
    skill: 'skill', '技能名': 'skill', '技能': 'skill',
    organization: 'organization', '组织名': 'organization', '组织': 'organization',
    general: 'general', '通用': 'general',
  }
  return map[v]
}

function handleExportJson() {
  const data = getValidEntries()
  if (data.length === 0) {
    message.warning('术语库为空，无法导出')
    return
  }
  const json = JSON.stringify(data, null, 2)
  const blob = new Blob([json], { type: 'application/json' })
  downloadBlob(blob, `${safeGameName()}_术语库.json`)
}

function handleExportCsv() {
  const data = getValidEntries()
  if (data.length === 0) {
    message.warning('术语库为空，无法导出')
    return
  }
  const headers = ['type', 'original', 'translation', 'category', 'description', 'isRegex', 'caseSensitive', 'exactMatch', 'priority']
  const lines = [headers.join(',')]
  for (const entry of data) {
    lines.push(headers.map(h => {
      const val = String((entry as unknown as Record<string, unknown>)[h] ?? '')
      return val.includes(',') || val.includes('\n') || val.includes('"')
        ? `"${val.replace(/"/g, '""')}"`
        : val
    }).join(','))
  }
  const csv = lines.join('\n')
  const blob = new Blob(['\ufeff' + csv], { type: 'text/csv;charset=utf-8' })
  downloadBlob(blob, `${safeGameName()}_术语库.csv`)
}

function safeGameName(): string {
  return game.value?.name?.replace(/[\\/:*?"<>|]/g, '_') ?? 'terms'
}

function downloadBlob(blob: Blob, filename: string) {
  const a = document.createElement('a')
  a.href = URL.createObjectURL(blob)
  a.download = filename
  a.click()
  setTimeout(() => URL.revokeObjectURL(a.href), 1000)
}

// ── Import from Game ──

async function handleOpenImportGameModal() {
  showImportGameModal.value = true
  loadingGames.value = true
  try {
    gameList.value = (await gamesApi.list()).filter(g => g.id !== gameId)
  } catch {
    message.error('获取游戏列表失败')
  } finally {
    loadingGames.value = false
  }
}

async function handleImportFromGame(sourceId: string) {
  importingFromGame.value = true
  try {
    const result = await gamesApi.importTermsFromGame(gameId, sourceId)
    // Reload terms after import
    const termsData = await gamesApi.getTerms(gameId)
    disableAutoSave()
    entries.value = toRows(termsData)
    await nextTick()
    enableAutoSave()
    message.success(`导入完成: 新增 ${result.added} 条，跳过 ${result.skipped} 条重复`)
    showImportGameModal.value = false
  } catch (e) {
    message.error(e instanceof Error ? e.message : '导入失败')
  } finally {
    importingFromGame.value = false
  }
}

// ── Filter helpers ──

function toggleTypeFilter(type: TermType | 'all') {
  filterType.value = filterType.value === type ? 'all' : type
}

function toggleCategoryFilter(cat: TermCategory) {
  filterCategory.value = filterCategory.value === cat ? null : cat
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
        <NIcon :size="24"><MenuBookOutlined /></NIcon>
      </span>
      术语管理
      <span v-if="autoSaving" class="auto-save-badge">保存中...</span>
    </h1>

    <!-- Toolbar -->
    <div class="toolbar" style="animation-delay: 0.1s">
      <div class="toolbar-left">
        <NButton size="small" type="error" secondary :disabled="entries.length === 0" @click="handleClearAll">
          <template #icon><NIcon :size="16"><DeleteSweepOutlined /></NIcon></template>
          清空
        </NButton>
        <NButton size="small" @click="handleImportClick">
          <template #icon><NIcon :size="16"><FileUploadOutlined /></NIcon></template>
          导入
        </NButton>
        <NPopover trigger="click" placement="bottom-start">
          <template #trigger>
            <NButton size="small" :disabled="entries.length === 0">
              <template #icon><NIcon :size="16"><FileDownloadOutlined /></NIcon></template>
              导出
            </NButton>
          </template>
          <div class="export-options">
            <NButton size="small" text @click="handleExportJson">导出 JSON</NButton>
            <NButton size="small" text @click="handleExportCsv">导出 CSV</NButton>
          </div>
        </NPopover>
        <NButton size="small" @click="handleOpenImportGameModal">
          <template #icon><NIcon :size="16"><ContentCopyOutlined /></NIcon></template>
          从其他游戏导入
        </NButton>
      </div>
      <div class="toolbar-right">
        <NPopover trigger="click" placement="bottom-end">
          <template #trigger>
            <NButton size="small" quaternary>
              <template #icon><NIcon :size="16"><SettingsOutlined /></NIcon></template>
            </NButton>
          </template>
          <div class="column-visibility">
            <div class="column-visibility-title">显示列</div>
            <NCheckbox
              v-for="col in optionalColumns"
              :key="col.key"
              :checked="visibleColumns.includes(col.key)"
              @update:checked="(v: boolean) => {
                if (v) visibleColumns.push(col.key)
                else visibleColumns = visibleColumns.filter(c => c !== col.key)
                saveColumnVisibility()
              }"
            >
              {{ col.label }}
            </NCheckbox>
          </div>
        </NPopover>
        <NButton size="small" type="primary" :loading="manualSaving" @click="handleManualSave">
          <template #icon><NIcon :size="16"><SaveOutlined /></NIcon></template>
          保存
        </NButton>
      </div>
    </div>

    <!-- Filter chip bar -->
    <div class="filter-bar" style="animation-delay: 0.15s">
      <div class="chip-group">
        <span class="chip" :class="{ active: filterType === 'all' }" @click="filterType = 'all'">
          全部({{ typeCounts.all }})
        </span>
        <span class="chip" :class="{ active: filterType === 'translate' }" @click="toggleTypeFilter('translate')">
          翻译术语({{ typeCounts.translate }})
        </span>
        <span class="chip" :class="{ active: filterType === 'doNotTranslate' }" @click="toggleTypeFilter('doNotTranslate')">
          禁止翻译({{ typeCounts.dnt }})
        </span>
      </div>
      <span class="filter-separator" />
      <div class="chip-group">
        <span
          v-for="cat in (['character', 'location', 'item', 'skill', 'organization', 'general'] as TermCategory[])"
          :key="cat"
          class="chip"
          :class="{ active: filterCategory === cat }"
          @click="toggleCategoryFilter(cat)"
        >
          {{ categoryLabelMap[cat] }}({{ categoryCounts[cat] }})
        </span>
      </div>
    </div>

    <!-- Main card -->
    <div class="section-card" style="animation-delay: 0.2s">
      <!-- Search -->
      <NInput v-model:value="searchKeyword" placeholder="搜索原文、译文或描述..." clearable size="small" style="margin-bottom: 12px">
        <template #prefix><NIcon :size="16"><SearchOutlined /></NIcon></template>
      </NInput>

      <!-- Add entry row -->
      <div class="add-entry-row">
        <NSelect v-model:value="newType" :options="typeOptions" size="small" style="width: 90px" />
        <NInput v-model:value="newOriginal" placeholder="原文" size="small" style="flex: 1" @keyup.enter="handleAddEntry" />
        <NInput
          v-model:value="newTranslation"
          :placeholder="newType === 'doNotTranslate' ? '—' : '译文'"
          :disabled="newType === 'doNotTranslate'"
          size="small"
          style="flex: 1"
          @keyup.enter="handleAddEntry"
        />
        <NSelect v-model:value="newCategory" :options="categoryOptions" size="small" style="width: 100px" />
        <NButton size="small" type="primary" :disabled="!newOriginal.trim()" @click="handleAddEntry">
          <template #icon><NIcon :size="16"><AddOutlined /></NIcon></template>
          添加
        </NButton>
      </div>

      <!-- Table -->
      <div v-if="filteredEntries.length > 0" class="table-container">
        <NDataTable
          :columns="tableColumns"
          :data="filteredEntries"
          :max-height="560"
          :item-size="40"
          :row-key="(row: TermRow) => row._id"
          virtual-scroll
          size="small"
          striped
        />
      </div>
      <NEmpty v-else-if="entries.length > 0" description="没有匹配的术语" style="padding: 40px 0" />
      <NEmpty v-else description="暂无术语条目，点击添加或导入" style="padding: 40px 0" />
    </div>

    <!-- Import from Game modal -->
    <NModal v-model:show="showImportGameModal" preset="card" title="从其他游戏导入术语" style="max-width: 480px">
      <div v-if="loadingGames" class="loading-state" style="min-height: 120px">
        <NSpin size="medium" />
      </div>
      <div v-else-if="gameList.length === 0" style="text-align: center; padding: 24px; color: var(--text-3);">
        没有其他游戏
      </div>
      <div v-else class="game-import-list">
        <div
          v-for="g in gameList"
          :key="g.id"
          class="game-import-item"
          @click="handleImportFromGame(g.id)"
        >
          <span class="game-import-name">{{ g.name }}</span>
          <NButton size="tiny" :loading="importingFromGame" :disabled="importingFromGame">
            导入
          </NButton>
        </div>
      </div>
    </NModal>
  </div>
</template>

<style scoped>
.toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  animation: slideUp 0.5s var(--ease-out) backwards;
}

.toolbar-left,
.toolbar-right {
  display: flex;
  align-items: center;
  gap: 8px;
}

.export-options {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.column-visibility {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 120px;
}

.column-visibility-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--text-2);
  margin-bottom: 2px;
}

.filter-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  animation: slideUp 0.5s var(--ease-out) backwards;
}

.chip-group {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-wrap: wrap;
}

.chip {
  display: inline-flex;
  align-items: center;
  padding: 3px 10px;
  border-radius: 12px;
  font-size: 12px;
  color: var(--text-3);
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  cursor: pointer;
  transition: all 0.2s ease;
  user-select: none;
  white-space: nowrap;
}

.chip:hover {
  color: var(--text-2);
  border-color: var(--border-hover);
  background: var(--bg-subtle-hover);
}

.chip.active {
  color: var(--accent);
  background: var(--accent-soft);
  border-color: var(--accent-border);
}

.filter-separator {
  width: 1px;
  height: 20px;
  background: var(--border);
  flex-shrink: 0;
}

.game-import-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
  max-height: 360px;
  overflow-y: auto;
}

.game-import-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 12px;
  border-radius: var(--radius-sm);
  cursor: pointer;
  transition: background 0.2s ease;
}

.game-import-item:hover {
  background: var(--bg-subtle);
}

.game-import-name {
  font-size: 14px;
  color: var(--text-1);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

@media (max-width: 768px) {
  .toolbar {
    flex-direction: column;
    align-items: stretch;
  }

  .toolbar-left,
  .toolbar-right {
    flex-wrap: wrap;
  }

  .toolbar-right {
    justify-content: flex-end;
  }

  .filter-bar {
    flex-direction: column;
    align-items: flex-start;
  }

  .filter-separator {
    width: 100%;
    height: 1px;
  }
}

@media (max-width: 480px) {
  .toolbar-left,
  .toolbar-right {
    width: 100%;
  }

  .chip {
    font-size: 11px;
    padding: 2px 8px;
  }

  .game-import-item {
    padding: 8px 10px;
  }
}
</style>
