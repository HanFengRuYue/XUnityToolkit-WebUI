<script setup lang="ts">
import { ref, computed, watch, nextTick, onBeforeUnmount } from 'vue'
import { NModal, NInput, NIcon, NButton, NSpin, NSelect, NAlert, NEllipsis } from 'naive-ui'
import {
  FolderOutlined,
  InsertDriveFileOutlined,
  StorageOutlined,
  ChevronRightRound,
  ArrowUpwardRound,
  DesktopWindowsOutlined,
  FileDownloadOutlined,
  DescriptionOutlined,
  ImageOutlined,
  MusicNoteOutlined,
  VideocamOutlined,
  PushPinOutlined,
} from '@vicons/material'
import { useFileExplorer } from '@/composables/useFileExplorer'
import { filesystemApi } from '@/api/filesystem'
import { formatBytes } from '@/utils/format'
import type { DriveEntry, FileSystemEntry, QuickAccessEntry } from '@/api/types'
import type { FileExplorerFilter } from '@/composables/useFileExplorer'
import type { Component } from 'vue'

const { show, mode, options, confirm, cancel } = useFileExplorer()

// Template refs
const fileListRef = ref<HTMLElement | null>(null)

// Local state
const currentPath = ref('')
const entries = ref<FileSystemEntry[]>([])
const drives = ref<DriveEntry[]>([])
const quickAccessEntries = ref<QuickAccessEntry[]>([])
const loading = ref(false)
const error = ref<string | null>(null)
const selectedFile = ref<FileSystemEntry | null>(null)
const activeFilterIndex = ref(0)
const pathInput = ref('')

const standardFolderIcons: Record<string, Component> = {
  '桌面': DesktopWindowsOutlined,
  '下载': FileDownloadOutlined,
  '文档': DescriptionOutlined,
  '图片': ImageOutlined,
  '音乐': MusicNoteOutlined,
  '视频': VideocamOutlined,
}

function getQuickAccessIcon(entry: QuickAccessEntry): Component {
  if (entry.type === 'standard') {
    return standardFolderIcons[entry.name] ?? FolderOutlined
  }
  return FolderOutlined
}

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

const activeQuickAccess = computed(() => {
  if (!currentPath.value) return null
  const normalized = currentPath.value.replace(/[\\/]+$/, '').toLowerCase()
  return quickAccessEntries.value.find(
    (e) => e.fullPath.replace(/[\\/]+$/, '').toLowerCase() === normalized,
  ) ?? null
})

// Fetch drives and quick access on open
watch(show, async (visible) => {
  if (!visible) {
    currentPath.value = ''
    entries.value = []
    selectedFile.value = null
    error.value = null
    pathInput.value = ''
    activeFilterIndex.value = 0
    return
  }

  // Fetch drives and quick access in parallel if not cached
  const promises: Promise<void>[] = []
  if (drives.value.length === 0) {
    promises.push(
      filesystemApi.getDrives().then((d) => { drives.value = d }).catch(() => { drives.value = [] }),
    )
  }
  if (quickAccessEntries.value.length === 0) {
    promises.push(
      filesystemApi.getQuickAccess().then((e) => { quickAccessEntries.value = e }).catch(() => { quickAccessEntries.value = [] }),
    )
  }
  await Promise.all(promises)

  // Navigate to initial path or first drive
  const initialPath = options.value.initialPath
  if (initialPath) {
    await navigateTo(initialPath)
  } else {
    const firstDrive = drives.value[0]
    if (firstDrive) await navigateTo(firstDrive.rootPath)
  }

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
  } finally {
    loading.value = false
  }

  await nextTick()
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

function handleQuickAccessClick(entry: QuickAccessEntry) {
  navigateTo(entry.fullPath)
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

// Resizable columns
const dateColWidth = ref(145)
const sizeColWidth = ref(80)
const dragging = ref(false)

let dragColumn: 'date' | 'size' | null = null
let dragStartX = 0
let dragStartWidth = 0

function onResizeStart(e: MouseEvent, column: 'date' | 'size') {
  e.preventDefault()
  dragColumn = column
  dragStartX = e.clientX
  dragStartWidth = column === 'date' ? dateColWidth.value : sizeColWidth.value
  dragging.value = true
  document.addEventListener('mousemove', onResizeMove)
  document.addEventListener('mouseup', onResizeEnd)
}

function onResizeMove(e: MouseEvent) {
  if (!dragColumn) return
  // Drag left = increase width (columns are right-aligned)
  const delta = dragStartX - e.clientX
  const newWidth = Math.max(60, Math.min(400, dragStartWidth + delta))
  if (dragColumn === 'date') {
    dateColWidth.value = newWidth
  } else {
    sizeColWidth.value = newWidth
  }
}

function onResizeEnd() {
  dragColumn = null
  dragging.value = false
  document.removeEventListener('mousemove', onResizeMove)
  document.removeEventListener('mouseup', onResizeEnd)
}

onBeforeUnmount(() => {
  document.removeEventListener('mousemove', onResizeMove)
  document.removeEventListener('mouseup', onResizeEnd)
})
</script>

<template>
  <NModal
    :show="show"
    preset="card"
    :title="title"
    style="width: 90vw; max-width: 1200px"
    :mask-closable="true"
    @update:show="(v: boolean) => { if (!v) handleCancel() }"
  >
    <div
      class="file-explorer"
      :class="{ 'is-resizing': dragging }"
      :style="{ '--date-col-w': dateColWidth + 'px', '--size-col-w': sizeColWidth + 'px' }"
    >
      <div class="file-explorer-body">
        <!-- Sidebar: Quick Access + Drives -->
        <div class="sidebar-panel">
          <!-- Quick Access -->
          <template v-if="quickAccessEntries.length > 0">
            <div class="sidebar-title">快速访问</div>
            <div
              v-for="entry in quickAccessEntries"
              :key="entry.fullPath"
              class="sidebar-item"
              :class="{ active: activeQuickAccess?.fullPath === entry.fullPath }"
              @click="handleQuickAccessClick(entry)"
            >
              <NIcon :component="getQuickAccessIcon(entry)" :size="16" />
              <NEllipsis class="sidebar-item-name" :tooltip="{ maxWidth: 300 }">
                {{ entry.name }}
              </NEllipsis>
              <NIcon
                v-if="entry.type === 'pinned'"
                :component="PushPinOutlined"
                :size="12"
                class="pin-icon"
              />
            </div>
            <div class="sidebar-divider" />
          </template>

          <!-- Drives -->
          <div class="sidebar-title">磁盘</div>
          <div
            v-for="drive in drives"
            :key="drive.name"
            class="sidebar-item"
            :class="{ active: activeDrive === drive.name && !activeQuickAccess }"
            @click="handleDriveClick(drive)"
          >
            <NIcon :component="StorageOutlined" :size="16" />
            <div class="drive-info">
              <span class="drive-name">{{ drive.name }}</span>
              <span v-if="drive.label" class="drive-label">{{ drive.label }}</span>
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

          <!-- Column headers -->
          <div class="column-header">
            <span class="col-icon" />
            <span class="col-name">名称</span>
            <span class="col-date col-header-cell">
              <span class="resize-handle" @mousedown.prevent="onResizeStart($event, 'date')" />
              修改日期
            </span>
            <span class="col-size col-header-cell">
              <span class="resize-handle" @mousedown.prevent="onResizeStart($event, 'size')" />
              大小
            </span>
          </div>

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
                <NIcon :component="FolderOutlined" :size="18" class="entry-icon col-icon" />
                <div class="col-name">..</div>
                <span class="col-date" />
                <span class="col-size" />
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
                  class="entry-icon col-icon"
                />
                <div class="col-name">
                  <NEllipsis :tooltip="{ maxWidth: 400 }">
                    {{ entry.name }}
                  </NEllipsis>
                </div>
                <span class="col-date">
                  {{ entry.lastModified ? formatDate(entry.lastModified) : '' }}
                </span>
                <span class="col-size">
                  {{ !entry.isDirectory && entry.size != null ? formatBytes(entry.size) : '' }}
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
          <NEllipsis v-if="selectedPath" :tooltip="{ maxWidth: 500 }">
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
  height: calc(80vh - 120px);
  min-height: 400px;
}

/* Sidebar */
.sidebar-panel {
  width: 180px;
  flex-shrink: 0;
  border-right: 1px solid var(--border);
  background: var(--bg-muted);
  overflow-y: auto;
  padding: 8px 0;
}

.sidebar-title {
  font-size: 11px;
  font-weight: 600;
  color: var(--text-3);
  text-transform: uppercase;
  letter-spacing: 0.5px;
  padding: 4px 12px 6px;
}

.sidebar-divider {
  height: 1px;
  background: var(--border);
  margin: 6px 12px;
}

.sidebar-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 5px 12px;
  cursor: pointer;
  transition: background 0.15s;
  color: var(--text-2);
  font-size: 13px;
}

.sidebar-item:hover {
  background: var(--bg-subtle-hover);
}

.sidebar-item.active {
  background: var(--accent-soft);
  color: var(--text-1);
}

.sidebar-item-name {
  flex: 1;
  min-width: 0;
}

.pin-icon {
  flex-shrink: 0;
  color: var(--text-3);
  opacity: 0.6;
}

.drive-info {
  min-width: 0;
  flex: 1;
  display: flex;
  align-items: baseline;
  gap: 6px;
}

.drive-name {
  font-weight: 500;
  flex-shrink: 0;
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

/* Column header */
.column-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 4px 12px;
  font-size: 12px;
  color: var(--text-3);
  font-weight: 600;
  border-top: 1px solid var(--border);
  border-bottom: 1px solid var(--border);
  background: var(--bg-muted);
  flex-shrink: 0;
}

.col-header-cell {
  position: relative;
}

.resize-handle {
  position: absolute;
  top: 0;
  left: -6px;
  width: 8px;
  height: 100%;
  cursor: col-resize;
  z-index: 1;
}

.resize-handle::after {
  content: '';
  position: absolute;
  top: 20%;
  left: 3px;
  width: 2px;
  height: 60%;
  border-radius: 1px;
  background: transparent;
  transition: background 0.15s;
}

.resize-handle:hover::after,
.is-resizing .resize-handle::after {
  background: var(--accent);
}

.is-resizing {
  user-select: none;
  cursor: col-resize;
}

/* Shared column widths */
.col-icon {
  width: 18px;
  flex-shrink: 0;
}

.col-name {
  flex: 1;
  min-width: 0;
  overflow: hidden;
}

.col-date {
  width: var(--date-col-w, 145px);
  flex-shrink: 0;
  text-align: right;
}

.col-size {
  width: var(--size-col-w, 80px);
  flex-shrink: 0;
  text-align: right;
}

/* File list */
.file-explorer-list {
  flex: 1;
  overflow-y: auto;
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
  color: var(--text-3);
}

.file-row .col-date,
.file-row .col-size {
  color: var(--text-3);
  font-size: 12px;
  font-family: 'Cascadia Mono', Consolas, 'Courier New', monospace;
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

  .sidebar-panel {
    width: 100%;
    border-right: none;
    border-bottom: 1px solid var(--border);
    display: flex;
    flex-wrap: wrap;
    overflow-x: auto;
    overflow-y: hidden;
    padding: 4px;
    max-height: 48px;
  }

  .sidebar-title,
  .sidebar-divider,
  .pin-icon {
    display: none;
  }

  .sidebar-item {
    white-space: nowrap;
    padding: 4px 10px;
  }

  .drive-label {
    display: none;
  }

  .col-date {
    display: none;
  }
}
</style>
