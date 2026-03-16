# Translation Editor Sort, Filter & Replace — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add sorting, advanced filtering, and find-and-replace to the Translation Editor view.

**Architecture:** Pure frontend change — all logic is client-side in `TranslationEditorView.vue`. Filter and sort use a cached `ref` (not a `computed`) to decouple from real-time cell edits — this ensures rows don't jump position while the user is typing (sort stability). The cache re-computes only when filter/sort settings change, not when `entries` mutate. NDataTable column header sorting uses controlled mode. Filter panel and replace panel are collapsible UI sections toggled by toolbar buttons.

**Tech Stack:** Vue 3 Composition API, Naive UI (NDataTable, NSelect, NRadioGroup, NRadio, NCheckboxGroup, NCheckbox, NSwitch, NBadge), @vicons/material

**Spec:** `docs/superpowers/specs/2026-03-16-translation-editor-sort-filter-design.md`

---

## Chunk 1: Filter/Sort State + Computed Pipeline + Column Sorting

### Task 1: Add reactive state and computed pipeline

**Files:**
- Modify: `XUnityToolkit-Vue/src/views/TranslationEditorView.vue`

- [ ] **Step 1: Update Vue imports**

Merge `watch` and `triggerRef` into the existing import on line 2:

```typescript
import { ref, computed, h, onMounted, onUnmounted, watch, triggerRef } from 'vue'
```

- [ ] **Step 2: Remove old search state**

Remove `const searchKeyword = ref('')` (line 50) — it is replaced by `filterKeyword`.

- [ ] **Step 3: Add filter/sort reactive state**

Add these refs after the existing editor state section (after the `newTranslation` ref):

```typescript
// Filter & sort state
const showFilterPanel = ref(false)
const showReplacePanel = ref(false)
const panelSortMode = ref<string>('default')
const filterKeyword = ref('')
const filterStatus = ref<string>('all') // 'all' | 'translated' | 'untranslated'
const filterFeatures = ref<string[]>([])
const filterRegexPattern = ref('')
const filterRegexTarget = ref<string>('both') // 'original' | 'translation' | 'both'
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
```

- [ ] **Step 4: Add regex debounce watchers**

```typescript
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
```

- [ ] **Step 5: Add feature filter regex constants**

```typescript
const FEATURE_PATTERNS: Record<string, RegExp> = {
  placeholder: /\{\{.*?\}\}|\{[0-9]+\}|<[^>]+>/,
  newline: /[\n\r]/,
  special: /[^\w\s\p{P}\p{sc=Han}\p{sc=Katakana}\p{sc=Hiragana}]/u,
}
```

- [ ] **Step 6: Replace `filteredEntries` with `filteredAndSortedEntries` (cached ref)**

Remove the existing `filteredEntries` computed (lines 74–80). Replace with a cached `ref` + `recomputeFilteredEntries()` function + watchers. This decouples from real-time cell edits to satisfy sort stability (spec §1d):

```typescript
// Cached filtered+sorted result — only recomputes when filter/sort settings change,
// NOT when entry content is edited (sort stability: rows don't jump while typing)
const filteredAndSortedEntries = ref<TranslationRow[]>([])

function recomputeFilteredEntries() {
  let result: TranslationRow[] = entries.value

  // 1. Keyword filter
  const kw = filterKeyword.value.toLowerCase()
  if (kw) {
    result = result.filter(
      e => e.original.toLowerCase().includes(kw) || e.translation.toLowerCase().includes(kw)
    )
  }

  // 2. Translation status filter
  if (filterStatus.value === 'translated') {
    result = result.filter(e => e.translation !== '')
  } else if (filterStatus.value === 'untranslated') {
    result = result.filter(e => e.translation === '')
  }

  // 3. Feature filter (OR within)
  if (filterFeatures.value.length > 0) {
    result = result.filter(e => {
      return filterFeatures.value.some(feat => {
        const re = FEATURE_PATTERNS[feat]
        if (!re) return false
        return re.test(e.original) || re.test(e.translation)
      })
    })
  }

  // 4. Regex filter
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

  // 5. Sort
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

// Recompute when filter/sort settings change (but NOT on entry content edits)
watch(
  [filterKeyword, filterStatus, filterFeatures, debouncedRegexPattern, filterRegexTarget,
   panelSortMode, columnSortKey, columnSortOrder],
  recomputeFilteredEntries,
  { deep: true }
)

// Also recompute when entries array itself changes (add/delete/import, NOT cell edits)
// Use a version counter to manually trigger
const entriesVersion = ref(0)
watch(entriesVersion, recomputeFilteredEntries)
```

- [ ] **Step 7: Add helper to bump entries version**

Add a helper function that should be called after structural changes to `entries` (add, delete, import, load):

```typescript
function bumpEntriesVersion() {
  entriesVersion.value++
}
```

Update these existing functions to call `bumpEntriesVersion()`:
- After `entries.value = toRows(editorData.entries)` in `onMounted`
- After `entries.value.unshift(...)` in `handleAddEntry`
- After `entries.value.splice(idx, 1)` in the delete button handler (in `tableColumns`)
- After the import loop in `handleImportFile`
- After `takeSnapshot()` in `handleSave` (to refresh view in case entries were re-ordered)

- [ ] **Step 8: Add filter active indicator, regex error, and reset**

```typescript
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
```

- [ ] **Step 9: Update template references from `filteredEntries` to `filteredAndSortedEntries`**

In the template:
- `{{ filteredEntries.length }}` → `{{ filteredAndSortedEntries.length }}`
- `:data="filteredEntries"` → `:data="filteredAndSortedEntries"`

- [ ] **Step 10: Clean up debounce timers on unmount**

Update the existing `onUnmounted`:

```typescript
onUnmounted(() => {
  window.removeEventListener('beforeunload', handleBeforeUnload)
  if (regexDebounceTimer) clearTimeout(regexDebounceTimer)
  if (replaceFindDebounceTimer) clearTimeout(replaceFindDebounceTimer)
})
```

- [ ] **Step 11: Commit**

```bash
git add XUnityToolkit-Vue/src/views/TranslationEditorView.vue
git commit -m "feat(译文编辑器): 添加筛选/排序响应式状态和计算管道"
```

### Task 2: Column header sorting (controlled mode)

**Files:**
- Modify: `XUnityToolkit-Vue/src/views/TranslationEditorView.vue`

- [ ] **Step 1: Add sort handler**

Update the type import to include `DataTableSortState`:

```typescript
import type { DataTableColumns, DataTableSortState } from 'naive-ui'
```

Add the handler:

```typescript
function handleSortersChange(sorters: DataTableSortState | DataTableSortState[] | null) {
  const sorter = Array.isArray(sorters) ? sorters[0] : sorters
  if (!sorter || sorter.order === false) {
    columnSortKey.value = null
    columnSortOrder.value = false
  } else {
    columnSortKey.value = sorter.columnKey as string
    columnSortOrder.value = sorter.order
    // Mutual exclusion: clear panel sort
    panelSortMode.value = 'default'
  }
}
```

- [ ] **Step 2: Add `sorter` and `sortOrder` to column definitions**

Update the `tableColumns` computed — add `sorter: true` and `sortOrder` to original and translation columns:

```typescript
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
    width: 50,
    render(row) {
      return h(NButton, {
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
      })
    },
  },
])
```

- [ ] **Step 3: Add `@update:sorters` to NDataTable in template**

```html
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
```

- [ ] **Step 4: Add panel sort mutual exclusion watcher**

```typescript
watch(panelSortMode, (val) => {
  if (val !== 'default') {
    // Mutual exclusion: clear column sort
    columnSortKey.value = null
    columnSortOrder.value = false
  }
})
```

- [ ] **Step 5: Commit**

```bash
git add XUnityToolkit-Vue/src/views/TranslationEditorView.vue
git commit -m "feat(译文编辑器): 添加列头排序（受控模式）"
```

### Task 3: Verify chunk 1 compiles

- [ ] **Step 1: Type-check**

```bash
cd XUnityToolkit-Vue && npx vue-tsc --noEmit
```

- [ ] **Step 2: Build**

```bash
cd XUnityToolkit-Vue && npm run build
```

- [ ] **Step 3: Fix any errors and re-commit if needed**

---

## Chunk 2: Filter Panel UI

### Task 4: Filter panel template and imports

**Files:**
- Modify: `XUnityToolkit-Vue/src/views/TranslationEditorView.vue`

- [ ] **Step 1: Update imports**

Update the naive-ui import to:

```typescript
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
```

Update the icons import — remove `SearchOutlined`, add new icons:

```typescript
import {
  ArrowBackOutlined,
  SaveOutlined,
  FileUploadOutlined,
  FileDownloadOutlined,
  AddOutlined,
  DeleteOutlined,
  DriveFileRenameOutlineOutlined,
  FolderOutlined,
  FilterAltOutlined,
  FindReplaceOutlined,
  RestartAltOutlined,
} from '@vicons/material'
```

- [ ] **Step 2: Add sort/feature options constants**

```typescript
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
```

- [ ] **Step 3: Replace the standalone search input with filter/replace toolbar buttons**

Remove the existing search `NInput` block (the `<!-- Search -->` section) and replace with:

```html
<!-- Toolbar -->
<div class="editor-toolbar">
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
```

- [ ] **Step 4: Add filter panel template**

Insert after the toolbar div, before the table:

```html
<!-- Filter Panel -->
<div v-if="showFilterPanel" class="filter-panel">
  <!-- Sort -->
  <div class="filter-row">
    <span class="filter-label">排序方式</span>
    <NSelect
      v-model:value="panelSortMode"
      :options="SORT_OPTIONS"
      size="small"
      style="width: 200px"
    />
  </div>

  <!-- Keyword -->
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

  <!-- Translation status -->
  <div class="filter-row">
    <span class="filter-label">翻译状态</span>
    <NRadioGroup v-model:value="filterStatus" size="small">
      <NRadio value="all">全部</NRadio>
      <NRadio value="translated">已翻译</NRadio>
      <NRadio value="untranslated">未翻译</NRadio>
    </NRadioGroup>
  </div>

  <!-- Feature filter -->
  <div class="filter-row">
    <span class="filter-label">特征筛选</span>
    <NCheckboxGroup v-model:value="filterFeatures" size="small">
      <NCheckbox v-for="opt in FEATURE_OPTIONS" :key="opt.value" :value="opt.value" :label="opt.label" />
    </NCheckboxGroup>
  </div>

  <!-- Regex filter -->
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

  <!-- Reset -->
  <div class="filter-row" style="justify-content: flex-end">
    <NButton size="small" @click="resetFilters">
      <template #icon><NIcon :size="14"><RestartAltOutlined /></NIcon></template>
      重置筛选
    </NButton>
  </div>
</div>
```

- [ ] **Step 5: Add filter panel and toolbar styles**

Add to `<style scoped>`:

```css
.editor-toolbar {
  display: flex;
  gap: 8px;
  margin-bottom: 12px;
}

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
```

- [ ] **Step 6: Update `handleAddEntry` with filter-hidden toast**

Replace the existing `handleAddEntry` function (lines 211–228) with:

```typescript
function handleAddEntry() {
  if (!newOriginal.value.trim()) {
    message.warning('请输入原文')
    return
  }
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
```

- [ ] **Step 7: Commit**

```bash
git add XUnityToolkit-Vue/src/views/TranslationEditorView.vue
git commit -m "feat(译文编辑器): 添加筛选面板 UI"
```

### Task 5: Verify chunk 2 compiles

- [ ] **Step 1: Type-check**

```bash
cd XUnityToolkit-Vue && npx vue-tsc --noEmit
```

- [ ] **Step 2: Build**

```bash
cd XUnityToolkit-Vue && npm run build
```

- [ ] **Step 3: Fix any errors and re-commit if needed**

---

## Chunk 3: Find & Replace

### Task 6: Find & Replace logic and UI

**Files:**
- Modify: `XUnityToolkit-Vue/src/views/TranslationEditorView.vue`

- [ ] **Step 1: Add replace match count computed and error**

```typescript
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
```

Note: `replaceMatchCount` counts **entries** with at least one match, not total occurrence count. The UI label uses "条匹配" (entries matched) to match this semantic.

- [ ] **Step 2: Add replaceAll handler**

```typescript
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

        let newTranslation: string
        if (replaceIsRegex.value) {
          try {
            const re = new RegExp(findText, 'g')
            newTranslation = entry.translation.replace(re, replaceWithText.value)
          } catch {
            continue
          }
        } else {
          // Case-insensitive plain text replace (all occurrences)
          const escaped = findText.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
          const re = new RegExp(escaped, 'gi')
          newTranslation = entry.translation.replace(re, replaceWithText.value)
        }

        if (newTranslation !== entry.translation) {
          entry.translation = newTranslation
          replaced++
        }
      }

      message.success(`已替换 ${replaced} 条`)
    },
  })
}
```

- [ ] **Step 3: Add replace panel template**

Insert after the filter panel div, before the table:

```html
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
```

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-Vue/src/views/TranslationEditorView.vue
git commit -m "feat(译文编辑器): 添加查找替换功能"
```

### Task 7: Final verification

- [ ] **Step 1: Type-check**

```bash
cd XUnityToolkit-Vue && npx vue-tsc --noEmit
```

- [ ] **Step 2: Build**

```bash
cd XUnityToolkit-Vue && npm run build
```

- [ ] **Step 3: Fix any errors and re-commit if needed**

- [ ] **Step 4: Manual smoke test**

Run the backend and verify in browser:

```bash
dotnet run --project XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj
```

Open `http://127.0.0.1:51821`, navigate to a game → Translation Editor. Verify:

1. Filter button with funnel icon appears, clicking toggles panel
2. Keyword search works (type text, entries filter)
3. Translation status radio works (all/translated/untranslated)
4. Feature checkboxes work (placeholder/newline/special)
5. Regex filter works (valid pattern filters, invalid shows error)
6. Sort dropdown works (length asc/desc, untranslated first)
7. Column header click sorts alphabetically (ascending/descending/cancel)
8. Column sort and panel sort are mutually exclusive
9. Badge dot appears on filter button when filters active
10. **Sort stability**: editing a cell does NOT cause rows to jump position
11. Replace button opens replace panel
12. Find shows match count (N 条匹配)
13. Replace All shows confirmation and replaces
14. Replace scoped to filtered entries only
15. Reset clears all filters
16. Adding entry while filtered shows toast if hidden
17. Dirty state and save still work correctly

- [ ] **Step 5: Final commit if any fixes needed**
