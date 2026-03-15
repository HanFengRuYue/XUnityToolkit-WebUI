<script setup lang="ts">
import { ref, computed, h, onMounted, onUnmounted } from 'vue'
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
  DriveFileRenameOutlineOutlined,
  FolderOutlined,
} from '@vicons/material'
import { gamesApi, translationEditorApi } from '@/api/games'
import type { Game, TranslationEntry } from '@/api/types'

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
const searchKeyword = ref('')
let nextId = 1

// Import
const importFileInput = ref<HTMLInputElement | null>(null)
const importing = ref(false)

// Add entry form
const newOriginal = ref('')
const newTranslation = ref('')

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

const filteredEntries = computed(() => {
  const kw = searchKeyword.value.toLowerCase()
  if (!kw) return entries.value
  return entries.value.filter(
    e => e.original.toLowerCase().includes(kw) || e.translation.toLowerCase().includes(kw)
  )
})

const tableColumns = computed<DataTableColumns<TranslationRow>>(() => [
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
        'onUpdate:value': (v: string) => { row.original = v },
      })
    },
  },
  {
    title: '译文',
    key: 'translation',
    resizable: true,
    minWidth: 200,
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
  } catch {
    message.error('加载失败')
  } finally {
    loading.value = false
  }
})

onUnmounted(() => {
  window.removeEventListener('beforeunload', handleBeforeUnload)
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
  entries.value.unshift({
    _id: nextId++,
    original: newOriginal.value,
    translation: newTranslation.value,
  })
  newOriginal.value = ''
  newTranslation.value = ''
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
            {{ filteredEntries.length }} / {{ entries.length }}
          </NTag>
        </h2>
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

      <!-- Search -->
      <NInput
        v-model:value="searchKeyword"
        placeholder="搜索原文或译文..."
        clearable
        size="small"
        style="margin-bottom: 12px"
      >
        <template #prefix>
          <NIcon :size="16"><SearchOutlined /></NIcon>
        </template>
      </NInput>

      <!-- Table -->
      <div v-if="entries.length > 0" class="table-container">
        <NDataTable
          :columns="tableColumns"
          :data="filteredEntries"
          :max-height="560"
          :item-size="40"
          :row-key="(row: TranslationRow) => row._id"
          virtual-scroll
          size="small"
          striped
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
</style>
