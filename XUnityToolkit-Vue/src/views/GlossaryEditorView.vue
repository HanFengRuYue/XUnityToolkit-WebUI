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
  useMessage,
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
  MenuBookOutlined,
} from '@vicons/material'
import { gamesApi } from '@/api/games'
import type { Game, GlossaryEntry } from '@/api/types'
import { useAutoSave } from '@/composables/useAutoSave'

interface GlossaryRow extends GlossaryEntry {
  _id: number
}

const route = useRoute()
const router = useRouter()
const message = useMessage()

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
  try {
    const [gameData, glossaryData] = await Promise.all([
      gamesApi.get(gameId),
      gamesApi.getGlossary(gameId),
    ])
    game.value = gameData
    entries.value = toRows(glossaryData)
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
</script>

<template>
  <div v-if="loading" class="loading-state">
    <NSpin size="large" />
  </div>

  <div v-else-if="game" class="editor-page">
    <!-- Header -->
    <div class="page-header" style="animation-delay: 0s">
      <button class="back-button" @click="router.push(`/games/${gameId}`)">
        <NIcon :size="20"><ArrowBackOutlined /></NIcon>
        <span>{{ game.name }}</span>
      </button>
    </div>

    <h1 class="page-title" style="animation-delay: 0.05s">
      <span class="page-title-icon">
        <NIcon :size="24"><MenuBookOutlined /></NIcon>
      </span>
      AI 翻译术语库
      <span v-if="autoSaving" class="auto-save-badge">保存中...</span>
    </h1>

    <!-- Stats & Actions Card -->
    <div class="section-card" style="animation-delay: 0.1s">
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
        <div class="header-btn-group">
          <NButton
            size="small"
            @click="handleImportClick"
          >
            <template #icon><NIcon :size="16"><FileUploadOutlined /></NIcon></template>
            导入
          </NButton>
          <NButton
            size="small"
            :disabled="entries.length === 0"
            @click="handleExport"
          >
            <template #icon><NIcon :size="16"><FileDownloadOutlined /></NIcon></template>
            导出
          </NButton>
          <NButton
            size="small"
            type="primary"
            :loading="manualSaving"
            @click="handleManualSave"
          >
            <template #icon><NIcon :size="16"><SaveOutlined /></NIcon></template>
            保存
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
          placeholder="译文"
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

      <!-- Search -->
      <NInput
        v-model:value="searchKeyword"
        placeholder="搜索原文、译文或描述..."
        clearable
        size="small"
        style="margin-bottom: 12px"
      >
        <template #prefix>
          <NIcon :size="16"><SearchOutlined /></NIcon>
        </template>
      </NInput>

      <!-- Table -->
      <div v-if="filteredEntries.length > 0" class="table-container">
        <NDataTable
          :columns="tableColumns"
          :data="filteredEntries"
          :max-height="560"
          :item-size="40"
          :row-key="(row: GlossaryRow) => row._id"
          virtual-scroll
          size="small"
          striped
        />
      </div>
      <NEmpty
        v-else-if="entries.length > 0"
        description="没有匹配的术语"
        style="padding: 40px 0"
      />
      <NEmpty v-else description="暂无术语条目，点击添加或导入" style="padding: 40px 0" />
    </div>

    <!-- Hidden file input for import -->
    <input
      ref="importFileInput"
      type="file"
      accept=".json"
      style="display: none"
      @change="handleImportFile"
    />
  </div>
</template>

<style scoped>
.loading-state {
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: 300px;
}

.editor-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.page-header {
  display: flex;
  align-items: center;
  margin-bottom: -8px;
  animation: slideUp 0.5s var(--ease-out) backwards;
}

.back-button {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  background: none;
  border: none;
  color: var(--text-3);
  font-size: 13px;
  cursor: pointer;
  padding: 4px 8px;
  border-radius: var(--radius-sm);
  transition: color 0.2s, background 0.2s;
}
.back-button:hover {
  color: var(--text-1);
  background: var(--bg-subtle);
}

.page-title {
  display: flex;
  align-items: center;
  gap: 12px;
  font-family: var(--font-display);
  font-size: 24px;
  font-weight: 600;
  color: var(--text-1);
  margin: 0;
  letter-spacing: -0.03em;
  animation: slideUp 0.5s var(--ease-out) backwards;
}

.page-title-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 38px;
  height: 38px;
  border-radius: 10px;
  background: var(--accent-soft);
  color: var(--accent);
  flex-shrink: 0;
}

.auto-save-badge {
  font-size: 12px;
  font-weight: 500;
  color: var(--accent);
  background: var(--accent-soft);
  padding: 2px 10px;
  border-radius: var(--radius-sm);
}

.section-card {
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  padding: 24px;
  animation: slideUp 0.5s var(--ease-out) backwards;
  transition: border-color 0.3s ease, background 0.3s ease, box-shadow 0.3s ease;
  box-shadow: var(--shadow-card-rest);
}
.section-card:hover {
  border-color: var(--border-hover);
}

.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 16px;
}

.section-title {
  display: flex;
  align-items: center;
  gap: 10px;
  font-family: var(--font-display);
  font-size: 16px;
  font-weight: 600;
  color: var(--text-1);
  margin: 0;
  letter-spacing: -0.01em;
}

.section-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 30px;
  height: 30px;
  border-radius: 8px;
  background: var(--accent-soft);
  color: var(--accent);
  flex-shrink: 0;
}

.header-btn-group {
  display: flex;
  gap: 8px;
}

.add-entry-row {
  display: flex;
  gap: 8px;
  margin-bottom: 12px;
  padding: 12px;
  background: var(--bg-subtle);
  border-radius: var(--radius-md);
  border: 1px dashed var(--border);
}

.table-container {
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  overflow: hidden;
}

@keyframes slideUp {
  from {
    opacity: 0;
    transform: translateY(12px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

@media (max-width: 768px) {
  .section-card {
    padding: 16px;
  }
  .header-btn-group {
    flex-wrap: wrap;
  }
  .add-entry-row {
    flex-direction: column;
  }
}

@media (max-width: 480px) {
  .section-card {
    padding: 14px;
    border-radius: var(--radius-md);
  }
  .page-title {
    font-size: 20px;
    gap: 8px;
  }
  .page-title-icon {
    width: 32px;
    height: 32px;
    border-radius: 8px;
  }
}
</style>
