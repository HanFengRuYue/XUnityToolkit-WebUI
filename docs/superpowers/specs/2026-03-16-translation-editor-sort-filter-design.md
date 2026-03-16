# Translation Editor — Sort, Filter & Replace

## Overview

Enhance the Translation Editor (`TranslationEditorView.vue`) with sorting, filtering, and find-and-replace capabilities. The existing standalone search bar is removed; all filtering is consolidated into a collapsible filter panel.

## Current State

- NDataTable with virtual scroll, full data loaded at once
- Standalone keyword search bar (case-insensitive, matches original + translation)
- No sorting, no advanced filtering, no replace
- Data model: `{ original: string, translation: string }` per entry

## Design

### 1. Sorting

#### 1a. Column Header Sort (NDataTable native)

Add `sorter` to the **Original** and **Translation** column definitions:

- Sort via `String.localeCompare()` — supports ascending / descending / cancel (three-state toggle)
- Clicking a column header sorts by that column's text content

#### 1b. Panel Dropdown Sort

An `NSelect` inside the filter panel with options:

| Value | Label |
|-------|-------|
| `default` | 默认顺序 |
| `length-asc` | 原文长度（升序） |
| `length-desc` | 原文长度（降序） |
| `untranslated-first` | 未翻译优先 |

#### 1c. Mutual Exclusion

- Selecting a panel dropdown sort clears any active column header sort
- Clicking a column header resets the panel dropdown to "默认顺序"
- This prevents confusing combined sort states

#### 1d. Sort Stability

- Editing a cell does not trigger re-sort; current row position is preserved until the user changes the sort setting

### 2. Filter Panel

#### 2a. Entry Point

- Remove the existing standalone search `NInput`
- Add a **filter button** (funnel icon) in the toolbar area (where the search bar used to be)
- Click toggles the filter panel open/closed
- When any filter is active and the panel is collapsed, show a **badge** on the button indicating active filters

#### 2b. Panel Layout

The panel appears below the toolbar, inside the translation entries card. Contents top to bottom:

1. **Sort dropdown** — `NSelect` (see §1b)
2. **Keyword search** — `NInput`, placeholder "搜索关键词…", matches `original` or `translation`, case-insensitive
3. **Translation status** — `NRadioGroup`: 全部 / 已翻译 / 未翻译
   - "未翻译" = `translation` is empty string `""`
4. **Feature filter** — `NCheckboxGroup` (multiple selection):
   - 包含占位符 (`{{...}}`, `{0}`, `<...>` patterns)
   - 包含换行符 (`\n` or `\r` in text)
   - 包含特殊字符 (non-alphanumeric, non-CJK, non-punctuation)
   - Multiple checked features use **OR** logic (match any)
5. **Regex filter** — `NInput` for pattern + `NRadioGroup` for target: 原文 / 译文 / 两者
   - Invalid regex: red border + error tooltip
   - Empty = disabled
6. **Reset button** — "重置筛选", clears all filter and sort settings to defaults

#### 2c. Filter Logic

- All filter categories combine with **AND** (intersection)
- Within feature filter checkboxes: **OR** (union)
- Pipeline: raw data → filter → sort → display
- Editing a row that no longer matches filters does **not** immediately remove it from view; view updates when the user changes any filter/sort setting

### 3. Find & Replace

#### 3a. UI

A separate collapsible section in the filter panel (or a dedicated button next to the filter button):

- **Find input** — `NInput`, placeholder "查找…"
- **Replace input** — `NInput`, placeholder "替换为…"
- **Mode toggle** — `NSwitch` or segmented button: 普通文本 / 正则表达式
  - In regex mode, replace text supports `$1`, `$2` capture group references
  - Invalid regex: red border + error tooltip

#### 3b. Target

- Replace operates on the **translation** column only

#### 3c. Actions

- **替换当前** — replaces the currently highlighted match, advances to next match
- **全部替换** — shows confirmation dialog: "将替换 N 处匹配，是否继续？"; on confirm, executes and shows result message: "已替换 X 处"

#### 3d. Scope

- Replace operates only on entries visible in the current filtered view
- If filters are active, only filtered entries are affected

### 4. Implementation Notes

- All sorting and filtering is **client-side** (data is already fully loaded)
- Virtual scroll remains enabled — `filteredEntries` computed drives the table
- The `_id` field ensures stable row keys across filter/sort changes
- Dirty detection (`isDirty`) continues comparing full `entries` array against snapshot (not filtered view)
- Save sends the full `entries` array regardless of active filters
- Replace modifies `entries` in-place (by `_id` lookup), triggering dirty state

### 5. Data Flow

```
entries (ref)
  → applyFilters(keyword, status, features, regex)
    → applySort(columnSort | panelSort)
      → filteredAndSortedEntries (computed)
        → NDataTable :data
```

### 6. Out of Scope

- Backend changes (no new API endpoints needed)
- Pagination
- Undo/redo for replace operations
- Export filtered results only
