# Translation Editor Sort & Filter Design

## Overview

Add sorting and filtering capabilities to the Translation Editor (`TranslationEditorView.vue`). Currently the editor only has a keyword search box. This design replaces it with a unified filter panel and adds column-header sorting.

## Current State

- **Data model**: `TranslationEntry { original, translation }` — two fields only
- **UI**: NDataTable with virtual-scroll, inline editing, add/delete/save/import/export
- **Search**: Standalone search box above table, filters by keyword (case-insensitive, matches both original and translation)
- **Sorting**: None
- **Data volume**: Hundreds to tens of thousands of entries, loaded in full

## Design

### 1. Filter Panel

Remove the standalone search box. Add a "Filter" button (funnel icon) next to the "Add Entry" form. Clicking it toggles a collapsible filter panel below.

When filters are active and the panel is collapsed, the button shows a badge indicator.

**Panel layout (top to bottom):**

| Row | Control | Description |
|-----|---------|-------------|
| 1 | **Sort order** (NSelect) | Options: Default order / Original length asc / Original length desc / Untranslated first |
| 2 | **Keyword search** (NInput) | Matches original or translation, case-insensitive. Replaces the old standalone search box. |
| 3 | **Translation status** (NRadioGroup) | All / Translated / Untranslated. "Untranslated" = `translation` is empty string. |
| 4 | **Feature filter** (NCheckboxGroup) | Contains placeholders (`{{...}}`, `{0}`, `<...>`) / Contains newlines (`\n`, `\r`) / Contains special characters |
| 5 | **Regex filter** (NInput + NRadioGroup) | Regex pattern input + match target (Original / Translation / Both). Invalid regex shows red border + error message. |
| 6 | **Reset** button | Clears all filter conditions to defaults |

### 2. Column Header Sorting

Add `sorter` to the Original and Translation columns in NDataTable. Clicking a column header sorts alphabetically using `localeCompare`, with three-state toggle: ascending → descending → none.

### 3. Sort Priority / Mutual Exclusion

Column header sorting and panel sort dropdown are **mutually exclusive**:
- Selecting a panel sort option clears any column header sort
- Clicking a column header resets the panel dropdown to "Default order"

This avoids confusing compound sorts.

### 4. Filter Logic

All filter conditions combine with **AND** (intersection):
1. Keyword → `original` or `translation` includes keyword (case-insensitive)
2. Translation status → checks if `translation` is empty
3. Feature filter → multiple checked features use **OR** (match any checked feature passes this condition)
4. Regex → tests against selected target (original / translation / both)

Processing order: **filter first, then sort**.

### 5. Edit Behavior During Filter/Sort

When a user edits an entry such that it no longer matches the current filter (e.g., filling in a translation while filtering "Untranslated"), the entry **stays visible** in the current view. It only disappears when the user changes filter conditions or toggles the panel. This prevents disorienting disappearances during editing.

Same for sorting: editing content does not trigger re-sort.

## Scope

- **Frontend only** — no backend API changes needed
- **Files modified**: `TranslationEditorView.vue` only
- **No new dependencies**

## Out of Scope

- Pagination (virtual-scroll handles large datasets)
- Backend-side filtering/sorting
- Saved filter presets
- Batch operations
