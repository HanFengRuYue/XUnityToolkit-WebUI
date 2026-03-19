<script setup lang="ts">
import { ref, computed, h, onMounted, onBeforeUnmount, watch } from 'vue'
import { useRoute, useRouter, onBeforeRouteLeave } from 'vue-router'
import {
  NButton,
  NIcon,
  NInput,
  NDataTable,
  NTag,
  NSpin,
  NEmpty,
  NAlert,
  NSelect,
  NRadioGroup,
  NRadio,
  NCheckboxGroup,
  NCheckbox,
  NSwitch,
  NBadge,
  NTooltip,
  useMessage,
  useDialog,
} from 'naive-ui'
import type { DataTableColumns, DataTableSortState } from 'naive-ui'
import {
  ArrowBackOutlined,
  SaveOutlined,
  FileUploadOutlined,
  FileDownloadOutlined,
  AddOutlined,
  DeleteOutlined,
  DeleteSweepOutlined,
  BookmarkAddOutlined,
  DriveFileRenameOutlineOutlined,
  FolderOutlined,
  FilterAltOutlined,
  FindReplaceOutlined,
  RestartAltOutlined,
} from '@vicons/material'
import { gamesApi, translationEditorApi } from '@/api/games'
import type { Game, TranslationEntry, TermEntry } from '@/api/types'

interface TranslationRow extends TranslationEntry {
  _id: number
}

const route = useRoute()
const router = useRouter()
const message = useMessage()
const dialog = useDialog()

const gameId = route.params['id'] as string
const game = ref<Game | null>(null)
const loading = ref(true)
const saving = ref(false)
const filePath = ref('')
const fileExists = ref(false)

// Editor state
const entries = ref<TranslationRow[]>([])
const savedSnapshot = ref('')
let nextId = 1

// Import
const importFileInput = ref<HTMLInputElement | null>(null)
const importing = ref(false)

// Add entry form
const newOriginal = ref('')
const newTranslation = ref('')

// Filter & sort state
const showFilterPanel = ref(false)
const showReplacePanel = ref(false)
const panelSortMode = ref<string>('default')
const filterKeyword = ref('')
const filterStatus = ref<string>('all')
const filterFeatures = ref<string[]>([])
const filterRegexPattern = ref('')
const filterRegexTarget = ref<string>('both')
const debouncedRegexPattern = ref('')
let regexDebounceTimer: ReturnType<typeof setTimeout> | null = null

// Column sort state (controlled mode)
const columnSortKey = ref<string | null>(null)
const columnSortOrder = ref<'ascend' | 'descend' | false>(false)

// Intl.Collator for performant string sorting
const collator = new Intl.Collator(undefined, { sensitivity: 'base' })

// Replace state
const replaceFindText = ref('')
const replaceWithText = ref('')
const replaceIsRegex = ref(false)
const debouncedReplaceFindText = ref('')
let replaceFindDebounceTimer: ReturnType<typeof setTimeout> | null = null

const isDirty = computed(() => {
  const current = JSON.stringify(entries.value.map(e => ({ o: e.original, t: e.translation })))
  return current !== savedSnapshot.value
})

function takeSnapshot() {
  savedSnapshot.value = JSON.stringify(entries.value.map(e => ({ o: e.original, t: e.translation })))
}

function toRows(items: TranslationEntry[]): TranslationRow[] {
  return items.map(e => ({ ...e, _id: nextId++ }))
}

const FEATURE_PATTERNS: Record<string, RegExp> = {
  placeholder: /\{\{.*?\}\}|\{[0-9]+\}|<[^>]+>/,
  newline: /[\n\r]/,
  special: /[^\w\s\p{P}\p{sc=Han}\p{sc=Katakana}\p{sc=Hiragana}]/u,
}

const SORT_OPTIONS = [
  { label: '默认顺序', value: 'default' },
  { label: '原文长度（升序）', value: 'length-asc' },
  { label: '原文长度（降序）', value: 'length-desc' },
  { label: '未翻译优先', value: 'untranslated-first' },
]

const FEATURE_OPTIONS = [
  { label: '包含占位符', value: 'placeholder' },
  { label: '包含换行符', value: 'newline' },
  { label: '包含特殊字符', value: 'special' },
]

const filteredAndSortedEntries = ref<TranslationRow[]>([])

function recomputeFilteredEntries() {
  let result: TranslationRow[] = entries.value

  const kw = filterKeyword.value.toLowerCase()
  if (kw) {
    result = result.filter(
      e => e.original.toLowerCase().includes(kw) || e.translation.toLowerCase().includes(kw)
    )
  }

  if (filterStatus.value === 'translated') {
    result = result.filter(e => e.translation !== '')
  } else if (filterStatus.value === 'untranslated') {
    result = result.filter(e => e.translation === '')
  }

  if (filterFeatures.value.length > 0) {
    result = result.filter(e => {
      return filterFeatures.value.some(feat => {
        const re = FEATURE_PATTERNS[feat]
        if (!re) return false
        return re.test(e.original) || re.test(e.translation)
      })
    })
  }

  if (debouncedRegexPattern.value) {
    try {
      const re = new RegExp(debouncedRegexPattern.value)
      result = result.filter(e => {
        const target = filterRegexTarget.value
        if (target === 'original') return re.test(e.original)
        if (target === 'translation') return re.test(e.translation)
        return re.test(e.original) || re.test(e.translation)
      })
    } catch {
      // Invalid regex — skip filter
    }
  }

  if (columnSortKey.value && columnSortOrder.value) {
    const key = columnSortKey.value as keyof TranslationRow
    const dir = columnSortOrder.value === 'ascend' ? 1 : -1
    result = [...result].sort((a, b) => dir * collator.compare(String(a[key]), String(b[key])))
  } else if (panelSortMode.value !== 'default') {
    result = [...result]
    switch (panelSortMode.value) {
      case 'length-asc':
        result.sort((a, b) => a.original.length - b.original.length)
        break
      case 'length-desc':
        result.sort((a, b) => b.original.length - a.original.length)
        break
      case 'untranslated-first':
        result.sort((a, b) => {
          const aEmpty = a.translation === '' ? 0 : 1
          const bEmpty = b.translation === '' ? 0 : 1
          return aEmpty - bEmpty
        })
        break
    }
  }

  filteredAndSortedEntries.value = result
}

watch(
  [filterKeyword, filterStatus, filterFeatures, debouncedRegexPattern, filterRegexTarget,
   panelSortMode, columnSortKey, columnSortOrder],
  recomputeFilteredEntries,
  { deep: true }
)

const entriesVersion = ref(0)
watch(entriesVersion, recomputeFilteredEntries)

function bumpEntriesVersion() {
  entriesVersion.value++
}

watch(filterRegexPattern, (val) => {
  if (regexDebounceTimer) clearTimeout(regexDebounceTimer)
  regexDebounceTimer = setTimeout(() => {
    debouncedRegexPattern.value = val
  }, 300)
})

watch(replaceFindText, (val) => {
  if (replaceFindDebounceTimer) clearTimeout(replaceFindDebounceTimer)
  replaceFindDebounceTimer = setTimeout(() => {
    debouncedReplaceFindText.value = val
  }, 300)
})

watch(panelSortMode, (val) => {
  if (val !== 'default') {
    columnSortKey.value = null
    columnSortOrder.value = false
  }
})

const hasActiveFilters = computed(() => {
  return filterKeyword.value !== ''
    || filterStatus.value !== 'all'
    || filterFeatures.value.length > 0
    || filterRegexPattern.value !== ''
    || panelSortMode.value !== 'default'
})

const regexError = computed(() => {
  if (!debouncedRegexPattern.value) return ''
  try {
    new RegExp(debouncedRegexPattern.value)
    return ''
  } catch (e) {
    return (e as Error).message
  }
})

const replaceFindRegexError = computed(() => {
  if (!replaceIsRegex.value || !debouncedReplaceFindText.value) return ''
  try {
    new RegExp(debouncedReplaceFindText.value)
    return ''
  } catch (e) {
    return (e as Error).message
  }
})

const replaceMatchCount = computed(() => {
  const findText = debouncedReplaceFindText.value
  if (!findText) return 0
  if (replaceIsRegex.value) {
    try {
      const re = new RegExp(findText, 'g')
      let count = 0
      for (const entry of filteredAndSortedEntries.value) {
        if (re.test(entry.translation)) count++
        re.lastIndex = 0
      }
      return count
    } catch {
      return 0
    }
  } else {
    const lower = findText.toLowerCase()
    return filteredAndSortedEntries.value.filter(e =>
      e.translation.toLowerCase().includes(lower)
    ).length
  }
})

function resetFilters() {
  panelSortMode.value = 'default'
  filterKeyword.value = ''
  filterStatus.value = 'all'
  filterFeatures.value = []
  filterRegexPattern.value = ''
  debouncedRegexPattern.value = ''
  filterRegexTarget.value = 'both'
  columnSortKey.value = null
  columnSortOrder.value = false
}

function handleReplaceAll() {
  const findText = debouncedReplaceFindText.value
  if (!findText || replaceMatchCount.value === 0) return

  dialog.warning({
    title: '全部替换',
    content: `将替换 ${replaceMatchCount.value} 条匹配，是否继续？`,
    positiveText: '替换',
    negativeText: '取消',
    onPositiveClick: () => {
      let replaced = 0
      const visibleIds = new Set(filteredAndSortedEntries.value.map(e => e._id))

      for (const entry of entries.value) {
        if (!visibleIds.has(entry._id)) continue

        let replacedText: string
        if (replaceIsRegex.value) {
          try {
            const re = new RegExp(findText, 'g')
            replacedText = entry.translation.replace(re, replaceWithText.value)
          } catch {
            continue
          }
        } else {
          const escaped = findText.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
          const re = new RegExp(escaped, 'gi')
          replacedText = entry.translation.replace(re, replaceWithText.value)
        }

        if (replacedText !== entry.translation) {
          entry.translation = replacedText
          replaced++
        }
      }

      bumpEntriesVersion()
      message.success(`已替换 ${replaced} 条`)
    },
  })
}

function handleSortersChange(sorters: DataTableSortState | DataTableSortState[] | null) {
  const sorter = Array.isArray(sorters) ? sorters[0] : sorters
  if (!sorter || sorter.order === false) {
    columnSortKey.value = null
    columnSortOrder.value = false
  } else {
    columnSortKey.value = sorter.columnKey as string
    columnSortOrder.value = sorter.order
    panelSortMode.value = 'default'
  }
}

const addingToGlossary = ref(false)

async function handleAddToGlossary(row: TranslationRow) {
  if (!row.original.trim() || !row.translation.trim()) return
  addingToGlossary.value = true
  try {
    const terms = await gamesApi.getTerms(gameId)
    if (terms.some((e: TermEntry) => e.original === row.original)) {
      message.warning('该原文在术语库中已存在')
      return
    }
    terms.unshift({
      type: 'translate',
      original: row.original,
      translation: row.translation,
      isRegex: false,
      caseSensitive: true,
      exactMatch: false,
      priority: 0,
    })
    await gamesApi.saveTerms(gameId, terms)
    message.success('已添加到术语库')
  } catch (e) {
    message.error(e instanceof Error ? e.message : '添加失败')
  } finally {
    addingToGlossary.value = false
  }
}

function handleClearAll() {
  if (entries.value.length === 0) return
  dialog.warning({
    title: '清空全部',
    content: `确定要清空全部 ${entries.value.length} 条翻译条目吗？此操作不可撤销。`,
    positiveText: '清空',
    negativeText: '取消',
    onPositiveClick: () => {
      entries.value = []
      bumpEntriesVersion()
      message.success('已清空全部条目')
    },
  })
}

const tableColumns = computed<DataTableColumns<TranslationRow>>(() => [
  {
    title: '原文',
    key: 'original',
    resizable: true,
    minWidth: 200,
    sorter: true,
    sortOrder: columnSortKey.value === 'original' ? columnSortOrder.value : false,
    render(row) {
      return h(NInput, {
        value: row.original,
        size: 'small',
        type: 'text',
        'onUpdate:value': (v: string) => { row.original = v },
      })
    },
  },
  {
    title: '译文',
    key: 'translation',
    resizable: true,
    minWidth: 200,
    sorter: true,
    sortOrder: columnSortKey.value === 'translation' ? columnSortOrder.value : false,
    render(row) {
      return h(NInput, {
        value: row.translation,
        size: 'small',
        type: 'text',
        'onUpdate:value': (v: string) => { row.translation = v },
      })
    },
  },
  {
    title: '',
    key: 'actions',
    width: 80,
    render(row) {
      return h('div', { style: 'display: flex; gap: 2px' }, [
        h(NTooltip, null, {
          trigger: () => h(NButton, {
            size: 'tiny',
            quaternary: true,
            type: 'primary',
            disabled: !row.translation.trim() || addingToGlossary.value,
            onClick: () => handleAddToGlossary(row),
          }, {
            icon: () => h(NIcon, { size: 16 }, () => h(BookmarkAddOutlined)),
          }),
          default: () => '添加到术语库',
        }),
        h(NButton, {
          size: 'tiny',
          quaternary: true,
          type: 'error',
          onClick: () => {
            const idx = entries.value.findIndex(e => e._id === row._id)
            if (idx >= 0) {
              entries.value.splice(idx, 1)
              bumpEntriesVersion()
            }
          },
        }, {
          icon: () => h(NIcon, { size: 16 }, () => h(DeleteOutlined)),
        }),
      ])
    },
  },
])

// ── Lifecycle ──

function handleBeforeUnload(e: BeforeUnloadEvent) {
  if (isDirty.value) {
    e.preventDefault()
    e.returnValue = ''
  }
}

onMounted(async () => {
  window.addEventListener('beforeunload', handleBeforeUnload)
  try {
    const [gameData, editorData] = await Promise.all([
      gamesApi.get(gameId),
      translationEditorApi.getEntries(gameId),
    ])
    game.value = gameData
    filePath.value = editorData.filePath
    fileExists.value = editorData.fileExists
    entries.value = toRows(editorData.entries)
    takeSnapshot()
    bumpEntriesVersion()
  } catch {
    message.error('加载失败')
  } finally {
    loading.value = false
  }
})

onBeforeUnmount(() => {
  window.removeEventListener('beforeunload', handleBeforeUnload)
  if (regexDebounceTimer) clearTimeout(regexDebounceTimer)
  if (replaceFindDebounceTimer) clearTimeout(replaceFindDebounceTimer)
})

onBeforeRouteLeave(() => {
  if (!isDirty.value) return true
  return new Promise<boolean>((resolve) => {
    dialog.warning({
      title: '有未保存的更改',
      content: '离开页面将丢失所有未保存的修改，是否继续？',
      positiveText: '离开',
      negativeText: '取消',
      onPositiveClick: () => resolve(true),
      onNegativeClick: () => resolve(false),
      onClose: () => resolve(false),
    })
  })
})

// ── Actions ──

async function handleSave() {
  // Validate
  const seen = new Set<string>()
  for (const row of entries.value) {
    if (!row.original.trim()) {
      message.error('存在空白的原文条目，请填写或删除')
      return
    }
    if (seen.has(row.original)) {
      message.error(`重复的原文: "${row.original.slice(0, 50)}"`)
      return
    }
    seen.add(row.original)
  }

  saving.value = true
  try {
    await translationEditorApi.saveEntries(gameId, entries.value.map(e => ({
      original: e.original,
      translation: e.translation,
    })))
    fileExists.value = true
    takeSnapshot()
    bumpEntriesVersion()
    message.success('保存成功')
  } catch (e) {
    message.error(e instanceof Error ? e.message : '保存失败')
  } finally {
    saving.value = false
  }
}

function handleAddEntry() {
  if (!newOriginal.value.trim()) {
    message.warning('请输入原文')
    return
  }
  // Check duplicate
  if (entries.value.some(e => e.original === newOriginal.value)) {
    message.warning('该原文已存在')
    return
  }
  const newEntry: TranslationRow = {
    _id: nextId++,
    original: newOriginal.value,
    translation: newTranslation.value,
  }
  entries.value.unshift(newEntry)
  bumpEntriesVersion()
  newOriginal.value = ''
  newTranslation.value = ''

  // Check if the new entry is hidden by active filters
  if (hasActiveFilters.value && !filteredAndSortedEntries.value.some(e => e._id === newEntry._id)) {
    message.info('条目已添加（被当前筛选条件隐藏）')
  }
}

function handleImportClick() {
  importFileInput.value?.click()
}

async function handleImportFile(e: Event) {
  const file = (e.target as HTMLInputElement).files?.[0]
  if (!file) return

  importing.value = true
  try {
    const content = await file.text()
    const importedEntries = await translationEditorApi.parseImport(gameId, content)

    // Merge: add only entries whose original text doesn't already exist
    const existingOriginals = new Set(entries.value.map(e => e.original))
    let added = 0
    for (const entry of importedEntries) {
      if (!existingOriginals.has(entry.original)) {
        entries.value.push({ ...entry, _id: nextId++ })
        existingOriginals.add(entry.original)
        added++
      }
    }
    bumpEntriesVersion()
    message.success(`导入完成: 新增 ${added} 条，跳过 ${importedEntries.length - added} 条重复`)
  } catch (e) {
    message.error(e instanceof Error ? e.message : '导入失败')
  } finally {
    importing.value = false
    // Reset file input so same file can be imported again
    if (importFileInput.value) importFileInput.value.value = ''
  }
}

function handleExport() {
  window.open(translationEditorApi.getExportUrl(gameId), '_blank')
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
        <NIcon :size="24"><DriveFileRenameOutlineOutlined /></NIcon>
      </span>
      译文编辑器
      <span v-if="isDirty" class="unsaved-badge">未保存</span>
    </h1>

    <!-- File Info Card -->
    <div class="section-card" style="animation-delay: 0.1s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><FolderOutlined /></NIcon>
          </span>
          文件信息
        </h2>
        <div class="header-actions">
          <NButton
            size="small"
            :loading="importing"
            @click="handleImportClick"
          >
            <template #icon><NIcon :size="16"><FileUploadOutlined /></NIcon></template>
            导入
          </NButton>
          <NButton
            size="small"
            :disabled="!fileExists"
            @click="handleExport"
          >
            <template #icon><NIcon :size="16"><FileDownloadOutlined /></NIcon></template>
            导出
          </NButton>
          <NButton
            size="small"
            type="primary"
            :loading="saving"
            :disabled="!isDirty"
            @click="handleSave"
          >
            <template #icon><NIcon :size="16"><SaveOutlined /></NIcon></template>
            保存
          </NButton>
        </div>
      </div>

      <div class="file-info">
        <div class="info-item">
          <span class="info-label">文件路径</span>
          <code class="info-value file-path">{{ filePath }}</code>
        </div>
        <div class="info-row">
          <div class="info-item">
            <span class="info-label">条目数量</span>
            <span class="info-value">{{ entries.length }}</span>
          </div>
          <div class="info-item">
            <span class="info-label">文件状态</span>
            <NTag :type="fileExists ? 'success' : 'warning'" size="small" :bordered="false">
              {{ fileExists ? '已存在' : '保存时创建' }}
            </NTag>
          </div>
        </div>
      </div>

      <NAlert
        v-if="!fileExists && entries.length === 0"
        type="info"
        style="margin-top: 12px"
      >
        翻译文件尚不存在。添加条目并保存后将自动创建。
      </NAlert>
    </div>

    <!-- Editor Card -->
    <div class="section-card" style="animation-delay: 0.15s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><DriveFileRenameOutlineOutlined /></NIcon>
          </span>
          翻译条目
          <NTag size="small" :bordered="false" style="margin-left: 8px">
            {{ filteredAndSortedEntries.length }} / {{ entries.length }}
          </NTag>
        </h2>
        <div class="header-actions">
          <NButton
            size="small"
            type="error"
            secondary
            :disabled="entries.length === 0"
            @click="handleClearAll"
          >
            <template #icon><NIcon :size="16"><DeleteSweepOutlined /></NIcon></template>
            清空
          </NButton>
          <NBadge :show="hasActiveFilters" dot :offset="[-2, 2]">
            <NButton
              size="small"
              :type="showFilterPanel ? 'primary' : 'default'"
              secondary
              @click="showFilterPanel = !showFilterPanel"
            >
              <template #icon><NIcon :size="16"><FilterAltOutlined /></NIcon></template>
              筛选
            </NButton>
          </NBadge>
          <NButton
            size="small"
            :type="showReplacePanel ? 'primary' : 'default'"
            secondary
            @click="showReplacePanel = !showReplacePanel"
          >
            <template #icon><NIcon :size="16"><FindReplaceOutlined /></NIcon></template>
            替换
          </NButton>
        </div>
      </div>

      <!-- Add Entry Form -->
      <div class="add-entry-row">
        <NInput
          v-model:value="newOriginal"
          placeholder="原文"
          size="small"
          style="flex: 1"
          @keyup.enter="handleAddEntry"
        />
        <NInput
          v-model:value="newTranslation"
          placeholder="译文（可选）"
          size="small"
          style="flex: 1"
          @keyup.enter="handleAddEntry"
        />
        <NButton
          size="small"
          type="primary"
          :disabled="!newOriginal.trim()"
          @click="handleAddEntry"
        >
          <template #icon><NIcon :size="16"><AddOutlined /></NIcon></template>
          添加
        </NButton>
      </div>

      <!-- Filter Panel -->
      <div v-if="showFilterPanel" class="filter-panel">
        <div class="filter-row">
          <span class="filter-label">排序方式</span>
          <NSelect
            v-model:value="panelSortMode"
            :options="SORT_OPTIONS"
            size="small"
            style="width: 200px"
          />
        </div>

        <div class="filter-row">
          <span class="filter-label">关键词</span>
          <NInput
            v-model:value="filterKeyword"
            placeholder="搜索关键词…"
            clearable
            size="small"
            style="flex: 1"
          />
        </div>

        <div class="filter-row">
          <span class="filter-label">翻译状态</span>
          <NRadioGroup v-model:value="filterStatus" size="small">
            <NRadio value="all">全部</NRadio>
            <NRadio value="translated">已翻译</NRadio>
            <NRadio value="untranslated">未翻译</NRadio>
          </NRadioGroup>
        </div>

        <div class="filter-row">
          <span class="filter-label">特征筛选</span>
          <NCheckboxGroup v-model:value="filterFeatures" size="small">
            <NCheckbox v-for="opt in FEATURE_OPTIONS" :key="opt.value" :value="opt.value" :label="opt.label" />
          </NCheckboxGroup>
        </div>

        <div class="filter-row">
          <span class="filter-label">正则表达式</span>
          <div style="display: flex; flex-direction: column; gap: 6px; flex: 1">
            <NInput
              v-model:value="filterRegexPattern"
              placeholder="正则表达式…"
              clearable
              size="small"
              :status="regexError ? 'error' : undefined"
            />
            <NTooltip v-if="regexError" trigger="hover">
              <template #trigger>
                <span class="regex-error">{{ regexError }}</span>
              </template>
              {{ regexError }}
            </NTooltip>
            <NRadioGroup v-model:value="filterRegexTarget" size="small">
              <NRadio value="original">原文</NRadio>
              <NRadio value="translation">译文</NRadio>
              <NRadio value="both">两者</NRadio>
            </NRadioGroup>
          </div>
        </div>

        <div class="filter-row" style="justify-content: flex-end">
          <NButton size="small" @click="resetFilters">
            <template #icon><NIcon :size="14"><RestartAltOutlined /></NIcon></template>
            重置筛选
          </NButton>
        </div>
      </div>

      <!-- Replace Panel -->
      <div v-if="showReplacePanel" class="filter-panel">
        <div class="filter-row">
          <span class="filter-label">查找</span>
          <div style="display: flex; gap: 8px; flex: 1; align-items: center">
            <NInput
              v-model:value="replaceFindText"
              placeholder="查找…"
              clearable
              size="small"
              :status="replaceFindRegexError ? 'error' : undefined"
              style="flex: 1"
            />
            <NTag v-if="debouncedReplaceFindText && !replaceFindRegexError" size="small" :bordered="false">
              {{ replaceMatchCount }} 条匹配
            </NTag>
          </div>
        </div>

        <NTooltip v-if="replaceFindRegexError" trigger="hover">
          <template #trigger>
            <div class="filter-row" style="padding-left: 82px">
              <span class="regex-error">{{ replaceFindRegexError }}</span>
            </div>
          </template>
          {{ replaceFindRegexError }}
        </NTooltip>

        <div class="filter-row">
          <span class="filter-label">替换为</span>
          <NInput
            v-model:value="replaceWithText"
            placeholder="替换为…"
            clearable
            size="small"
            style="flex: 1"
          />
        </div>

        <div class="filter-row">
          <span class="filter-label">模式</span>
          <div style="display: flex; align-items: center; gap: 8px">
            <span style="font-size: 12px; color: var(--text-3)">普通文本</span>
            <NSwitch v-model:value="replaceIsRegex" size="small" />
            <span style="font-size: 12px; color: var(--text-3)">正则表达式</span>
          </div>
        </div>

        <div class="filter-row" style="justify-content: flex-end">
          <NButton
            size="small"
            type="warning"
            :disabled="replaceMatchCount === 0"
            @click="handleReplaceAll"
          >
            全部替换
          </NButton>
        </div>
      </div>

      <!-- Table -->
      <div v-if="entries.length > 0" class="table-container">
        <NDataTable
          :columns="tableColumns"
          :data="filteredAndSortedEntries"
          :max-height="560"
          :item-size="40"
          :row-key="(row: TranslationRow) => row._id"
          virtual-scroll
          size="small"
          striped
          @update:sorters="handleSortersChange"
        />
      </div>
      <NEmpty v-else description="暂无翻译条目" style="padding: 40px 0" />
    </div>

    <!-- Hidden file input for import -->
    <input
      ref="importFileInput"
      type="file"
      accept=".txt"
      style="display: none"
      @change="handleImportFile"
    />
  </div>
</template>

<style scoped>
.filter-panel {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 12px;
  margin-bottom: 12px;
  background: var(--bg-subtle);
  border-radius: var(--radius-md);
  border: 1px solid var(--border);
}

.filter-row {
  display: flex;
  align-items: center;
  gap: 12px;
}

.filter-label {
  font-size: 12px;
  color: var(--text-3);
  white-space: nowrap;
  min-width: 70px;
}

.regex-error {
  font-size: 11px;
  color: var(--error, #e88080);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  max-width: 300px;
}

@media (max-width: 768px) {
  .filter-row {
    flex-direction: column;
    align-items: flex-start;
    gap: 6px;
  }

  .filter-label {
    min-width: unset;
  }
}

.file-info {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.info-row {
  display: flex;
  gap: 24px;
}

.info-item {
  display: flex;
  align-items: center;
  gap: 8px;
}

.info-label {
  font-size: 12px;
  color: var(--text-3);
  white-space: nowrap;
}

.info-value {
  font-size: 13px;
  color: var(--text-1);
  font-family: var(--font-mono);
}

.file-path {
  font-size: 12px;
  color: var(--text-2);
  background: var(--bg-subtle);
  padding: 2px 8px;
  border-radius: var(--radius-sm);
  word-break: break-all;
}

@media (max-width: 768px) {
  .info-row {
    flex-direction: column;
    gap: 8px;
  }
}

@media (max-width: 480px) {
  .filter-panel {
    padding: 10px;
  }

  .regex-error {
    max-width: 200px;
  }
}
</style>
