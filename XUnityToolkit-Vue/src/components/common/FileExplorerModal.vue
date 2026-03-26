<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import { NModal, NInput, NIcon, NButton, NSpin, NSelect, NAlert, NEllipsis } from 'naive-ui'
import {
  FolderOutlined,
  InsertDriveFileOutlined,
  StorageOutlined,
  ChevronRightRound,
  ArrowUpwardRound,
} from '@vicons/material'
import { useFileExplorer } from '@/composables/useFileExplorer'
import { filesystemApi } from '@/api/filesystem'
import { formatBytes } from '@/utils/format'
import type { DriveEntry, FileSystemEntry } from '@/api/types'
import type { FileExplorerFilter } from '@/composables/useFileExplorer'

const { show, mode, options, confirm, cancel } = useFileExplorer()

// Template refs
const fileListRef = ref<HTMLElement | null>(null)

// Local state
const currentPath = ref('')
const entries = ref<FileSystemEntry[]>([])
const drives = ref<DriveEntry[]>([])
const loading = ref(false)
const error = ref<string | null>(null)
const selectedFile = ref<FileSystemEntry | null>(null)
const activeFilterIndex = ref(0)
const pathInput = ref('')

const title = computed(() => {
  if (options.value.title) return options.value.title
  return mode.value === 'folder' ? '选择文件夹' : '选择文件'
})

const breadcrumbs = computed(() => {
  if (!currentPath.value) return []
  const parts = currentPath.value.split(/[\\/]/).filter(Boolean)
  const result: { label: string; path: string }[] = []
  let accumulated = ''
  for (const part of parts) {
    accumulated = accumulated ? `${accumulated}\\${part}` : `${part}\\`
    result.push({ label: part, path: accumulated })
  }
  return result
})

const filterOptions = computed(() => {
  const filters = options.value.filters
  if (!filters || filters.length === 0) return []
  const opts = filters.map((f: FileExplorerFilter, i: number) => ({
    label: f.extensions.length > 0
      ? `${f.label} (${f.extensions.join(', ')})`
      : f.label,
    value: i,
  }))
  if (!filters.some((f: FileExplorerFilter) => f.extensions.length === 0)) {
    opts.push({ label: '所有文件', value: -1 })
  }
  return opts
})

const filteredEntries = computed(() => {
  if (mode.value === 'folder') {
    // In folder mode, show directories only
    return entries.value.filter((e) => e.isDirectory)
  }

  const filters = options.value.filters
  if (!filters || filters.length === 0 || activeFilterIndex.value === -1) {
    return entries.value
  }

  const activeFilter = filters[activeFilterIndex.value]
  if (!activeFilter || activeFilter.extensions.length === 0) {
    return entries.value
  }

  const exts = new Set(activeFilter.extensions.map((e: string) => e.toLowerCase()))
  return entries.value.filter(
    (e) => e.isDirectory || (e.extension && exts.has(e.extension)),
  )
})

const parentPath = computed(() => {
  if (!currentPath.value) return null
  const parent = currentPath.value.replace(/[\\/]$/, '')
  const lastSep = Math.max(parent.lastIndexOf('\\'), parent.lastIndexOf('/'))
  if (lastSep < 0) return null
  return parent.substring(0, lastSep + 1)
})

const selectedPath = computed(() => {
  if (mode.value === 'folder') return currentPath.value
  return selectedFile.value?.fullPath ?? ''
})

const canConfirm = computed(() => {
  if (mode.value === 'folder') return !!currentPath.value
  return !!selectedFile.value
})

const activeDrive = computed(() => {
  if (!currentPath.value) return null
  return currentPath.value.split('\\')[0]
})

// Fetch drives on open
watch(show, async (visible) => {
  if (!visible) {
    // Reset state
    currentPath.value = ''
    entries.value = []
    selectedFile.value = null
    error.value = null
    pathInput.value = ''
    activeFilterIndex.value = 0
    return
  }

  // Fetch drives if not cached
  if (drives.value.length === 0) {
    try {
      drives.value = await filesystemApi.getDrives()
    } catch {
      drives.value = []
    }
  }

  // Navigate to initial path or first drive
  const initialPath = options.value.initialPath
  if (initialPath) {
    await navigateTo(initialPath)
  } else {
    const firstDrive = drives.value[0]
    if (firstDrive) await navigateTo(firstDrive.rootPath)
  }

  // Set the first filter with extensions as active, or -1 for "all"
  const filters = options.value.filters
  if (filters && filters.length > 0) {
    activeFilterIndex.value = 0
  } else {
    activeFilterIndex.value = -1
  }
})

async function navigateTo(path: string) {
  loading.value = true
  error.value = null
  selectedFile.value = null

  try {
    const result = await filesystemApi.listDirectory(path)
    currentPath.value = result.currentPath
    entries.value = result.entries
    pathInput.value = result.currentPath
  } catch (e) {
    error.value = e instanceof Error ? e.message : '无法访问该目录'
    // Keep current directory if navigation fails
  } finally {
    loading.value = false
  }

  await nextTick()
  // Scroll file list to top on navigation
  if (fileListRef.value) fileListRef.value.scrollTop = 0
}

function handleEntryClick(entry: FileSystemEntry) {
  if (entry.isDirectory) {
    navigateTo(entry.fullPath)
  } else if (mode.value === 'file') {
    selectedFile.value = entry
  }
}

function handleEntryDblClick(entry: FileSystemEntry) {
  if (!entry.isDirectory && mode.value === 'file') {
    confirm(entry.fullPath)
  }
}

function handleDriveClick(drive: DriveEntry) {
  navigateTo(drive.rootPath)
}

function handleBreadcrumbClick(path: string) {
  navigateTo(path)
}

function handleGoUp() {
  if (parentPath.value) {
    navigateTo(parentPath.value)
  }
}

function handlePathSubmit() {
  const trimmed = pathInput.value.trim()
  if (trimmed) {
    navigateTo(trimmed)
  }
}

function handleConfirm() {
  if (!canConfirm.value) return
  confirm(selectedPath.value)
}

function handleCancel() {
  cancel()
}

function formatDate(dateStr: string | null): string {
  if (!dateStr) return ''
  const d = new Date(dateStr)
  return d.toLocaleDateString('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}
</script>

<template>
  <NModal
    :show="show"
    preset="card"
    :title="title"
    style="width: 720px; max-width: 95vw"
    :mask-closable="true"
    @update:show="(v: boolean) => { if (!v) handleCancel() }"
  >
    <div class="file-explorer">
      <div class="file-explorer-body">
        <!-- Drives sidebar -->
        <div class="drives-panel">
          <div class="drives-title">磁盘</div>
          <div
            v-for="drive in drives"
            :key="drive.name"
            class="drive-item"
            :class="{ active: activeDrive === drive.name }"
            @click="handleDriveClick(drive)"
          >
            <NIcon :component="StorageOutlined" :size="18" />
            <div class="drive-info">
              <div class="drive-name">{{ drive.name }}</div>
              <div v-if="drive.label" class="drive-label">{{ drive.label }}</div>
            </div>
          </div>
        </div>

        <!-- Content area -->
        <div class="content-area">
          <!-- Path input bar -->
          <div class="path-bar">
            <NButton
              quaternary
              size="small"
              :disabled="!parentPath"
              class="up-btn"
              @click="handleGoUp"
            >
              <template #icon>
                <NIcon :component="ArrowUpwardRound" />
              </template>
            </NButton>
            <NInput
              v-model:value="pathInput"
              size="small"
              placeholder="输入路径..."
              @keyup.enter="handlePathSubmit"
            />
          </div>

          <!-- Breadcrumbs -->
          <div v-if="breadcrumbs.length > 0" class="breadcrumbs">
            <template v-for="(crumb, i) in breadcrumbs" :key="crumb.path">
              <span
                class="breadcrumb-item"
                :class="{ current: i === breadcrumbs.length - 1 }"
                @click="handleBreadcrumbClick(crumb.path)"
              >
                {{ crumb.label }}
              </span>
              <NIcon
                v-if="i < breadcrumbs.length - 1"
                :component="ChevronRightRound"
                :size="16"
                class="breadcrumb-sep"
              />
            </template>
          </div>

          <!-- Error alert -->
          <NAlert v-if="error" type="warning" style="margin-bottom: 8px" :closable="true" @close="error = null">
            {{ error }}
          </NAlert>

          <!-- File list -->
          <div ref="fileListRef" class="file-explorer-list">
            <div v-if="loading" class="list-state">
              <NSpin size="small" />
            </div>
            <template v-else>
              <!-- Parent directory -->
              <div
                v-if="parentPath"
                class="file-row dir-row"
                @click="handleGoUp"
              >
                <NIcon :component="FolderOutlined" :size="18" class="entry-icon" />
                <span class="entry-name">..</span>
              </div>

              <!-- Empty state -->
              <div v-if="filteredEntries.length === 0 && !loading" class="list-state empty">
                空文件夹
              </div>

              <!-- Entries -->
              <div
                v-for="entry in filteredEntries"
                :key="entry.fullPath"
                class="file-row"
                :class="{
                  'dir-row': entry.isDirectory,
                  selected: !entry.isDirectory && selectedFile?.fullPath === entry.fullPath,
                }"
                @click="handleEntryClick(entry)"
                @dblclick="handleEntryDblClick(entry)"
              >
                <NIcon
                  :component="entry.isDirectory ? FolderOutlined : InsertDriveFileOutlined"
                  :size="18"
                  class="entry-icon"
                />
                <NEllipsis class="entry-name" :tooltip="{ maxWidth: 400 }">
                  {{ entry.name }}
                </NEllipsis>
                <span v-if="!entry.isDirectory && entry.size != null" class="entry-size">
                  {{ formatBytes(entry.size) }}
                </span>
                <span v-if="entry.lastModified" class="entry-date">
                  {{ formatDate(entry.lastModified) }}
                </span>
              </div>
            </template>
          </div>

          <!-- Filter dropdown -->
          <div v-if="mode === 'file' && filterOptions.length > 0" class="filter-bar">
            <NSelect
              v-model:value="activeFilterIndex"
              :options="filterOptions"
              size="small"
              style="width: 100%"
            />
          </div>
        </div>
      </div>

      <!-- Footer -->
      <div class="file-explorer-footer">
        <div class="selected-display">
          <NEllipsis v-if="selectedPath" style="max-width: 400px" :tooltip="{ maxWidth: 500 }">
            {{ selectedPath }}
          </NEllipsis>
          <span v-else class="no-selection">
            {{ mode === 'folder' ? '当前目录即为选中' : '请选择文件' }}
          </span>
        </div>
        <div class="footer-actions">
          <NButton @click="handleCancel">取消</NButton>
          <NButton type="primary" :disabled="!canConfirm" @click="handleConfirm">
            确认选择
          </NButton>
        </div>
      </div>
    </div>
  </NModal>
</template>

<style scoped>
.file-explorer {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.file-explorer-body {
  display: flex;
  gap: 0;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  overflow: hidden;
  height: 420px;
}

/* Drives sidebar */
.drives-panel {
  width: 140px;
  flex-shrink: 0;
  border-right: 1px solid var(--border);
  background: var(--bg-muted);
  overflow-y: auto;
  padding: 8px 0;
}

.drives-title {
  font-size: 11px;
  font-weight: 600;
  color: var(--text-3);
  text-transform: uppercase;
  letter-spacing: 0.5px;
  padding: 4px 12px 8px;
}

.drive-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 12px;
  cursor: pointer;
  transition: background 0.15s;
  color: var(--text-2);
}

.drive-item:hover {
  background: var(--bg-subtle-hover);
}

.drive-item.active {
  background: var(--accent-soft);
  color: var(--text-1);
}

.drive-info {
  min-width: 0;
  flex: 1;
}

.drive-name {
  font-size: 13px;
  font-weight: 500;
}

.drive-label {
  font-size: 11px;
  color: var(--text-3);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

/* Content area */
.content-area {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-width: 0;
  overflow: hidden;
}

.path-bar {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 8px 8px 4px;
}

.up-btn {
  flex-shrink: 0;
}

.breadcrumbs {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 2px;
  padding: 0 12px 6px;
  font-size: 12px;
  min-height: 20px;
}

.breadcrumb-item {
  color: var(--text-2);
  cursor: pointer;
  padding: 1px 4px;
  border-radius: 4px;
  transition: color 0.15s, background 0.15s;
}

.breadcrumb-item:hover {
  color: var(--text-1);
  background: var(--bg-subtle-hover);
}

.breadcrumb-item.current {
  color: var(--text-1);
  font-weight: 500;
}

.breadcrumb-sep {
  color: var(--text-3);
  flex-shrink: 0;
}

/* File list */
.file-explorer-list {
  flex: 1;
  overflow-y: auto;
  border-top: 1px solid var(--border);
}

.list-state {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
  color: var(--text-3);
  font-size: 13px;
}

.list-state.empty {
  padding: 40px 0;
}

.file-row {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 5px 12px;
  cursor: pointer;
  transition: background 0.1s;
  font-size: 13px;
  color: var(--text-1);
}

.file-row:hover {
  background: var(--bg-subtle-hover);
}

.file-row.selected {
  background: var(--accent-soft);
}

.dir-row .entry-icon {
  color: var(--accent);
}

.entry-icon {
  flex-shrink: 0;
  color: var(--text-3);
}

.entry-name {
  flex: 1;
  min-width: 0;
}

.entry-size {
  flex-shrink: 0;
  color: var(--text-3);
  font-size: 12px;
  width: 70px;
  text-align: right;
}

.entry-date {
  flex-shrink: 0;
  color: var(--text-3);
  font-size: 12px;
  width: 130px;
  text-align: right;
}

/* Filter bar */
.filter-bar {
  padding: 6px 8px;
  border-top: 1px solid var(--border);
}

/* Footer */
.file-explorer-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.selected-display {
  min-width: 0;
  flex: 1;
  font-size: 13px;
  color: var(--text-1);
}

.no-selection {
  color: var(--text-3);
  font-style: italic;
}

.footer-actions {
  display: flex;
  gap: 8px;
  flex-shrink: 0;
}

/* Responsive */
@media (max-width: 600px) {
  .file-explorer-body {
    flex-direction: column;
    height: 500px;
  }

  .drives-panel {
    width: 100%;
    border-right: none;
    border-bottom: 1px solid var(--border);
    display: flex;
    overflow-x: auto;
    overflow-y: hidden;
    padding: 4px;
    max-height: 48px;
  }

  .drives-title {
    display: none;
  }

  .drive-item {
    white-space: nowrap;
    padding: 4px 10px;
  }

  .drive-label {
    display: none;
  }

  .entry-date {
    display: none;
  }
}
</style>
