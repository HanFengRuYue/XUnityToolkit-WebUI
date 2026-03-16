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
  NTabs,
  NTabPane,
  useMessage,
  useDialog,
} from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
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
} from '@vicons/material'
import { gamesApi } from '@/api/games'
import type { Game, GlossaryEntry, DoNotTranslateEntry } from '@/api/types'
import { useAutoSave } from '@/composables/useAutoSave'

interface GlossaryRow extends GlossaryEntry {
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
const entries = ref<GlossaryRow[]>([])
let nextId = 1

// Import
const importFileInput = ref<HTMLInputElement | null>(null)

// Add entry form
const newOriginal = ref('')
const newTranslation = ref('')

// Search
const searchKeyword = ref('')

// ── Do-Not-Translate state ──
interface DntRow extends DoNotTranslateEntry {
  _id: number
}

let dntNextId = 1
const dntEntries = ref<DntRow[]>([])
const dntNewOriginal = ref('')
const dntNewCaseSensitive = ref(true)
const dntSearchKeyword = ref('')
const dntManualSaving = ref(false)
const dntImportFileInput = ref<HTMLInputElement | null>(null)

function toDntRows(items: DoNotTranslateEntry[]): DntRow[] {
  return items.map(e => ({ ...e, _id: dntNextId++ }))
}

function getValidDntEntries(): DoNotTranslateEntry[] {
  return dntEntries.value
    .filter(e => e.original.trim())
    .map(({ original, caseSensitive }) => ({ original, caseSensitive }))
}

const { saving: dntAutoSaving, enable: enableDntAutoSave, disable: disableDntAutoSave } = useAutoSave(
  () => dntEntries.value,
  async () => {
    try {
      const valid = getValidDntEntries()
      await gamesApi.saveDoNotTranslate(gameId, valid)
    } catch {
      message.error('自动保存禁翻表失败')
    }
  },
  { debounceMs: 2000, deep: true },
)

const filteredDntEntries = computed(() => {
  const kw = dntSearchKeyword.value.toLowerCase()
  if (!kw) return dntEntries.value
  return dntEntries.value.filter(e => e.original.toLowerCase().includes(kw))
})

const dntTableColumns = computed<DataTableColumns<DntRow>>(() => [
  {
    title: '原文',
    key: 'original',
    resizable: true,
    minWidth: 200,
    render(row) {
      return h(NInput, {
        value: row.original,
        size: 'small',
        type: 'text',
        placeholder: '不翻译的文本',
        'onUpdate:value': (v: string) => { row.original = v },
      })
    },
  },
  {
    title: '大小写敏感',
    key: 'caseSensitive',
    width: 100,
    align: 'center',
    render(row) {
      return h(NSwitch, {
        value: row.caseSensitive,
        size: 'small',
        'onUpdate:value': (v: boolean) => { row.caseSensitive = v },
      })
    },
  },
  {
    title: '',
    key: 'actions',
    width: 50,
    render(row) {
      return h(NButton, {
        size: 'tiny',
        quaternary: true,
        type: 'error',
        onClick: () => {
          const idx = dntEntries.value.findIndex(e => e._id === row._id)
          if (idx >= 0) dntEntries.value.splice(idx, 1)
        },
      }, {
        icon: () => h(NIcon, { size: 16 }, () => h(DeleteOutlined)),
      })
    },
  },
])

function toRows(items: GlossaryEntry[]): GlossaryRow[] {
  return items.map(e => ({ ...e, _id: nextId++ }))
}

function getValidEntries(): GlossaryEntry[] {
  return entries.value
    .filter(e => e.original.trim())
    .map(({ original, translation, isRegex, description }) => ({
      original,
      translation,
      isRegex,
      description: description?.trim() || undefined,
    }))
}

// Auto-save (2s debounce, deep watch)
const { saving: autoSaving, enable: enableAutoSave, disable: disableAutoSave } = useAutoSave(
  () => entries.value,
  async () => {
    try {
      const valid = getValidEntries()
      await gamesApi.saveGlossary(gameId, valid)
    } catch {
      message.error('自动保存术语库失败')
    }
  },
  { debounceMs: 2000, deep: true },
)

const filteredEntries = computed(() => {
  const kw = searchKeyword.value.toLowerCase()
  if (!kw) return entries.value
  return entries.value.filter(
    e =>
      e.original.toLowerCase().includes(kw) ||
      e.translation.toLowerCase().includes(kw) ||
      (e.description ?? '').toLowerCase().includes(kw),
  )
})

const tableColumns = computed<DataTableColumns<GlossaryRow>>(() => [
  {
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
  },
  {
    title: '译文',
    key: 'translation',
    resizable: true,
    minWidth: 140,
    render(row) {
      return h(NInput, {
        value: row.translation,
        size: 'small',
        type: 'text',
        placeholder: '译文',
        'onUpdate:value': (v: string) => { row.translation = v },
      })
    },
  },
  {
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
  },
  {
    title: '正则',
    key: 'isRegex',
    width: 60,
    align: 'center',
    render(row) {
      return h(NSwitch, {
        value: row.isRegex,
        size: 'small',
        'onUpdate:value': (v: boolean) => { row.isRegex = v },
      })
    },
  },
  {
    title: '',
    key: 'actions',
    width: 50,
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
  },
])

// ── Lifecycle ──

onMounted(async () => {
  disableAutoSave()
  disableDntAutoSave()
  try {
    const [gameData, glossaryData, dntData] = await Promise.all([
      gamesApi.get(gameId),
      gamesApi.getGlossary(gameId),
      gamesApi.getDoNotTranslate(gameId),
    ])
    game.value = gameData
    entries.value = toRows(glossaryData)
    dntEntries.value = toDntRows(dntData)
  } catch {
    message.error('加载失败')
  } finally {
    loading.value = false
  }
  await nextTick()
  enableAutoSave()
  enableDntAutoSave()
})

// ── Actions ──

async function handleManualSave() {
  manualSaving.value = true
  disableAutoSave()
  try {
    const valid = getValidEntries()
    await gamesApi.saveGlossary(gameId, valid)
    // Refresh entries to remove blank rows
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
    original: newOriginal.value,
    translation: newTranslation.value,
    isRegex: false,
    description: undefined,
  })
  newOriginal.value = ''
  newTranslation.value = ''
}

// ── Import / Export ──

function handleImportClick() {
  importFileInput.value?.click()
}

async function handleImportFile(e: Event) {
  const file = (e.target as HTMLInputElement).files?.[0]
  if (!file) return

  try {
    const text = await file.text()
    let imported: GlossaryEntry[]
    try {
      imported = JSON.parse(text)
    } catch {
      message.error('文件格式错误：不是有效的 JSON')
      return
    }

    if (!Array.isArray(imported)) {
      message.error('文件格式错误：应为 JSON 数组')
      return
    }

    // Validate and normalize entries
    const valid: GlossaryEntry[] = []
    for (const item of imported) {
      if (typeof item.original === 'string' && item.original.trim()) {
        valid.push({
          original: item.original,
          translation: typeof item.translation === 'string' ? item.translation : '',
          isRegex: typeof item.isRegex === 'boolean' ? item.isRegex : false,
          description: typeof item.description === 'string' && item.description.trim()
            ? item.description
            : undefined,
        })
      }
    }

    // Merge: skip duplicates
    const existingOriginals = new Set(entries.value.map(e => e.original))
    let added = 0
    for (const entry of valid) {
      if (!existingOriginals.has(entry.original)) {
        entries.value.push({ ...entry, _id: nextId++ })
        existingOriginals.add(entry.original)
        added++
      }
    }
    message.success(`导入完成: 新增 ${added} 条，跳过 ${valid.length - added} 条重复`)
  } catch (e) {
    message.error(e instanceof Error ? e.message : '导入失败')
  } finally {
    if (importFileInput.value) importFileInput.value.value = ''
  }
}

function handleExport() {
  const data = getValidEntries()
  if (data.length === 0) {
    message.warning('术语库为空，无法导出')
    return
  }
  const json = JSON.stringify(data, null, 2)
  const blob = new Blob([json], { type: 'application/json' })
  const a = document.createElement('a')
  a.href = URL.createObjectURL(blob)
  const gameName = game.value?.name?.replace(/[\\/:*?"<>|]/g, '_') ?? 'glossary'
  a.download = `${gameName}_术语库.json`
  a.click()
  setTimeout(() => URL.revokeObjectURL(a.href), 1000)
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

// ── Do-Not-Translate Actions ──

function handleAddDntEntry() {
  if (!dntNewOriginal.value.trim()) {
    message.warning('请输入禁翻文本')
    return
  }
  if (dntEntries.value.some(e => e.original === dntNewOriginal.value)) {
    message.warning('该文本已存在')
    return
  }
  dntEntries.value.unshift({
    _id: dntNextId++,
    original: dntNewOriginal.value,
    caseSensitive: dntNewCaseSensitive.value,
  })
  dntNewOriginal.value = ''
  dntNewCaseSensitive.value = true
}

async function handleDntManualSave() {
  dntManualSaving.value = true
  disableDntAutoSave()
  try {
    const valid = getValidDntEntries()
    await gamesApi.saveDoNotTranslate(gameId, valid)
    dntEntries.value = toDntRows(valid)
    await nextTick()
    message.success('保存成功')
  } catch (e) {
    message.error(e instanceof Error ? e.message : '保存失败')
  } finally {
    dntManualSaving.value = false
    enableDntAutoSave()
  }
}

function handleDntImportClick() {
  dntImportFileInput.value?.click()
}

async function handleDntImportFile(e: Event) {
  const file = (e.target as HTMLInputElement).files?.[0]
  if (!file) return

  try {
    const text = await file.text()
    let imported: DoNotTranslateEntry[]
    try {
      imported = JSON.parse(text)
    } catch {
      message.error('文件格式错误：不是有效的 JSON')
      return
    }

    if (!Array.isArray(imported)) {
      message.error('文件格式错误：应为 JSON 数组')
      return
    }

    const valid: DoNotTranslateEntry[] = []
    for (const item of imported) {
      if (typeof item.original === 'string' && item.original.trim()) {
        valid.push({
          original: item.original,
          caseSensitive: typeof item.caseSensitive === 'boolean' ? item.caseSensitive : true,
        })
      }
    }

    const existingOriginals = new Set(dntEntries.value.map(e => e.original))
    let added = 0
    for (const entry of valid) {
      if (!existingOriginals.has(entry.original)) {
        dntEntries.value.push({ ...entry, _id: dntNextId++ })
        existingOriginals.add(entry.original)
        added++
      }
    }
    message.success(`导入完成: 新增 ${added} 条，跳过 ${valid.length - added} 条重复`)
  } catch (e) {
    message.error(e instanceof Error ? e.message : '导入失败')
  } finally {
    if (dntImportFileInput.value) dntImportFileInput.value.value = ''
  }
}

function handleDntExport() {
  const data = getValidDntEntries()
  if (data.length === 0) {
    message.warning('禁翻表为空，无法导出')
    return
  }
  const json = JSON.stringify(data, null, 2)
  const blob = new Blob([json], { type: 'application/json' })
  const a = document.createElement('a')
  a.href = URL.createObjectURL(blob)
  const gameName = game.value?.name?.replace(/[\\/:*?"<>|]/g, '_') ?? 'dnt'
  a.download = `${gameName}_禁翻表.json`
  a.click()
  setTimeout(() => URL.revokeObjectURL(a.href), 1000)
}

function handleClearAllDnt() {
  if (dntEntries.value.length === 0) return
  dialog.warning({
    title: '清空禁翻表',
    content: `确定要清空全部 ${dntEntries.value.length} 条禁翻条目吗？此操作不可撤销。`,
    positiveText: '清空',
    negativeText: '取消',
    onPositiveClick: () => {
      dntEntries.value = []
      message.success('已清空禁翻表')
    },
  })
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
      AI 翻译词库
      <span v-if="autoSaving || dntAutoSaving" class="auto-save-badge">保存中...</span>
    </h1>

    <!-- Tabbed content -->
    <NTabs type="segment" animated class="editor-tabs" style="animation-delay: 0.1s">
      <!-- Tab 1: Glossary -->
      <NTabPane name="glossary" tab="术语表">
        <div class="section-card">
          <div class="section-header">
            <h2 class="section-title">
              <span class="section-icon">
                <NIcon :size="16"><MenuBookOutlined /></NIcon>
              </span>
              术语管理
              <NTag size="small" :bordered="false" style="margin-left: 8px">
                {{ entries.length }} 条
              </NTag>
            </h2>
            <div class="header-actions">
              <NButton size="small" type="error" secondary :disabled="entries.length === 0" @click="handleClearAll">
                <template #icon><NIcon :size="16"><DeleteSweepOutlined /></NIcon></template>
                清空
              </NButton>
              <NButton size="small" @click="handleImportClick">
                <template #icon><NIcon :size="16"><FileUploadOutlined /></NIcon></template>
                导入
              </NButton>
              <NButton size="small" :disabled="entries.length === 0" @click="handleExport">
                <template #icon><NIcon :size="16"><FileDownloadOutlined /></NIcon></template>
                导出
              </NButton>
              <NButton size="small" type="primary" :loading="manualSaving" @click="handleManualSave">
                <template #icon><NIcon :size="16"><SaveOutlined /></NIcon></template>
                保存
              </NButton>
            </div>
          </div>

          <div class="add-entry-row">
            <NInput v-model:value="newOriginal" placeholder="原文" size="small" style="flex: 1" @keyup.enter="handleAddEntry" />
            <NInput v-model:value="newTranslation" placeholder="译文" size="small" style="flex: 1" @keyup.enter="handleAddEntry" />
            <NButton size="small" type="primary" :disabled="!newOriginal.trim()" @click="handleAddEntry">
              <template #icon><NIcon :size="16"><AddOutlined /></NIcon></template>
              添加
            </NButton>
          </div>

          <NInput v-model:value="searchKeyword" placeholder="搜索原文、译文或描述..." clearable size="small" style="margin-bottom: 12px">
            <template #prefix><NIcon :size="16"><SearchOutlined /></NIcon></template>
          </NInput>

          <div v-if="filteredEntries.length > 0" class="table-container">
            <NDataTable :columns="tableColumns" :data="filteredEntries" :max-height="560" :item-size="40" :row-key="(row: GlossaryRow) => row._id" virtual-scroll size="small" striped />
          </div>
          <NEmpty v-else-if="entries.length > 0" description="没有匹配的术语" style="padding: 40px 0" />
          <NEmpty v-else description="暂无术语条目，点击添加或导入" style="padding: 40px 0" />
        </div>
      </NTabPane>

      <!-- Tab 2: Do-Not-Translate -->
      <NTabPane name="do-not-translate" tab="禁翻表">
        <div class="section-card">
          <div class="section-header">
            <h2 class="section-title">
              <span class="section-icon">
                <NIcon :size="16"><MenuBookOutlined /></NIcon>
              </span>
              禁翻管理
              <NTag size="small" :bordered="false" style="margin-left: 8px">
                {{ dntEntries.length }} 条
              </NTag>
            </h2>
            <div class="header-actions">
              <NButton size="small" type="error" secondary :disabled="dntEntries.length === 0" @click="handleClearAllDnt">
                <template #icon><NIcon :size="16"><DeleteSweepOutlined /></NIcon></template>
                清空
              </NButton>
              <NButton size="small" @click="handleDntImportClick">
                <template #icon><NIcon :size="16"><FileUploadOutlined /></NIcon></template>
                导入
              </NButton>
              <NButton size="small" :disabled="dntEntries.length === 0" @click="handleDntExport">
                <template #icon><NIcon :size="16"><FileDownloadOutlined /></NIcon></template>
                导出
              </NButton>
              <NButton size="small" type="primary" :loading="dntManualSaving" @click="handleDntManualSave">
                <template #icon><NIcon :size="16"><SaveOutlined /></NIcon></template>
                保存
              </NButton>
            </div>
          </div>

          <div class="add-entry-row">
            <NInput v-model:value="dntNewOriginal" placeholder="不翻译的文本" size="small" style="flex: 1" @keyup.enter="handleAddDntEntry" />
            <div class="case-sensitive-toggle">
              <span class="toggle-label">大小写敏感</span>
              <NSwitch v-model:value="dntNewCaseSensitive" size="small" />
            </div>
            <NButton size="small" type="primary" :disabled="!dntNewOriginal.trim()" @click="handleAddDntEntry">
              <template #icon><NIcon :size="16"><AddOutlined /></NIcon></template>
              添加
            </NButton>
          </div>

          <NInput v-model:value="dntSearchKeyword" placeholder="搜索禁翻文本..." clearable size="small" style="margin-bottom: 12px">
            <template #prefix><NIcon :size="16"><SearchOutlined /></NIcon></template>
          </NInput>

          <div v-if="filteredDntEntries.length > 0" class="table-container">
            <NDataTable :columns="dntTableColumns" :data="filteredDntEntries" :max-height="560" :item-size="40" :row-key="(row: DntRow) => row._id" virtual-scroll size="small" striped />
          </div>
          <NEmpty v-else-if="dntEntries.length > 0" description="没有匹配的禁翻条目" style="padding: 40px 0" />
          <NEmpty v-else description="暂无禁翻条目，点击添加或导入" style="padding: 40px 0" />
        </div>
      </NTabPane>
    </NTabs>

    <!-- Hidden file inputs for import -->
    <input ref="importFileInput" type="file" accept=".json" style="display: none" @change="handleImportFile" />
    <input ref="dntImportFileInput" type="file" accept=".json" style="display: none" @change="handleDntImportFile" />
  </div>
</template>

<style scoped>
.case-sensitive-toggle {
  display: flex;
  align-items: center;
  gap: 6px;
  white-space: nowrap;
}
.toggle-label {
  font-size: 12px;
  color: var(--text-3);
}
.editor-tabs {
  animation: slideUp 0.5s var(--ease-out) backwards;
}

/* ── Segment tab dark-mode contrast overrides ── */
.editor-tabs :deep(.n-tabs-rail) {
  background: var(--bg-subtle);
  border: 1px solid var(--border);
}

.editor-tabs :deep(.n-tabs-tab) {
  color: var(--text-3);
  transition: color 0.2s ease, background 0.2s ease;
}

.editor-tabs :deep(.n-tabs-tab--active) {
  color: var(--accent) !important;
  background: color-mix(in srgb, var(--accent) 12%, var(--bg-card));
  border: 1px solid var(--accent-border);
  border-radius: var(--n-tab-border-radius, 3px);
}

.editor-tabs :deep(.n-tabs-tab:not(.n-tabs-tab--active)) {
  color: var(--text-3);
}
</style>
