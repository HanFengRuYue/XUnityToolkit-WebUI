# Custom Accent Color Picker Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a color picker alongside the existing 8 preset accent color swatches, allowing users to choose any color.

**Architecture:** Use Naive UI's built-in `NColorPicker` with its `#trigger` slot to render a custom swatch trigger button. The same pattern is applied to two UI locations. No backend or store changes needed.

**Tech Stack:** Vue 3, Naive UI (`NColorPicker`), TypeScript

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `XUnityToolkit-Vue/src/views/SettingsView.vue` | Modify | Add custom color swatch + NColorPicker in settings accent color row |
| `XUnityToolkit-Vue/src/components/library/LibraryCustomizer.vue` | Modify | Add custom color swatch + NColorPicker in library customizer panel, with debounced backend save |

---

## Chunk 1: Implementation

### Task 1: Add NColorPicker to SettingsView.vue

**Files:**
- Modify: `XUnityToolkit-Vue/src/views/SettingsView.vue`

**Context:** The accent color row is at lines 221-235. It renders 8 preset swatch buttons. The settings object uses `useAutoSave` with 1000ms debounce, so changes to `settings.accentColor` are automatically persisted. A watcher at line 95 syncs `settings.accentColor` to `themeStore.setAccentColor()`.

- [ ] **Step 1: Add NColorPicker import**

In the `naive-ui` import block (line 3-13), add `NColorPicker`:

```typescript
import {
  NButton,
  NInput,
  NSelect,
  NIcon,
  NProgress,
  NSpin,
  NSwitch,
  NColorPicker,
  useMessage,
  useDialog,
} from 'naive-ui'
```

- [ ] **Step 2: Add isCustomColor computed and showPicker ref**

After `const updateStore = useUpdateStore()` (line 42), add:

```typescript
const showColorPicker = ref(false)
const isCustomAccent = computed(() =>
  !accentPresets.some(p => p.hex === settings.value.accentColor)
)
```

- [ ] **Step 3: Add NColorPicker with #trigger slot after the preset loop**

Replace the `<div class="accent-color-row">` block (lines 221-235) with:

**Important:** Use `#trigger` slot (not `#label`) — `#label` customizes text inside the default trigger rectangle, while `#trigger` replaces the entire trigger element. Do NOT call the slot's provided `onClick` — NColorPicker's internal `handleTriggerClick` only opens (never toggles), so we manage visibility manually via `showColorPicker`.

```html
<div class="accent-color-row">
  <button
    v-for="preset in accentPresets"
    :key="preset.hex"
    class="accent-swatch"
    :class="{ active: settings.accentColor === preset.hex }"
    :style="{ '--swatch-color': preset.hex }"
    :title="preset.name"
    @click="settings.accentColor = preset.hex"
  >
    <svg v-if="settings.accentColor === preset.hex" class="swatch-check" viewBox="0 0 16 16" fill="none">
      <path d="M4 8.5L7 11.5L12 5" stroke="white" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
    </svg>
  </button>
  <NColorPicker
    :show="showColorPicker"
    :value="settings.accentColor"
    :modes="['hex']"
    :show-alpha="false"
    :swatches="accentPresets.map(p => p.hex)"
    :actions="[]"
    @update:show="showColorPicker = $event"
    @update:value="settings.accentColor = $event"
  >
    <template #trigger>
      <button
        class="accent-swatch custom-swatch"
        :class="{ active: isCustomAccent }"
        :style="isCustomAccent ? { '--swatch-color': settings.accentColor } : {}"
        title="自定义颜色"
        @click="showColorPicker = !showColorPicker"
      >
        <svg v-if="isCustomAccent" class="swatch-check" viewBox="0 0 16 16" fill="none">
          <path d="M4 8.5L7 11.5L12 5" stroke="white" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
        </svg>
      </button>
    </template>
  </NColorPicker>
</div>
```

- [ ] **Step 4: Add CSS for custom swatch rainbow gradient**

After the `.swatch-check` rule (line 635-638), add:

```css
.custom-swatch:not(.active) {
  background: conic-gradient(
    #f43f5e, #f97316, #f59e0b, #10b981, #06b6d4, #3b82f6, #8b5cf6, #f43f5e
  );
}
```

- [ ] **Step 5: Verify type-check passes**

Run: `cd XUnityToolkit-Vue && npx vue-tsc --noEmit`
Expected: No errors

- [ ] **Step 6: Commit**

```bash
git add XUnityToolkit-Vue/src/views/SettingsView.vue
git commit -m "feat: 在设置页添加自定义主题色取色器"
```

---

### Task 2: Add NColorPicker to LibraryCustomizer.vue

**Files:**
- Modify: `XUnityToolkit-Vue/src/components/library/LibraryCustomizer.vue`

**Context:** The color grid is at lines 102-116. `setAccent()` (line 38-46) does a full GET+PUT API roundtrip. During color picker drag, `on-update:value` fires rapidly, so we need to debounce the backend save while keeping the CSS preview instant.

- [ ] **Step 1: Add NColorPicker import and refs**

Update the `naive-ui` import (line 2) to include `NColorPicker`:

```typescript
import { NIcon, NSwitch, NPopover, NColorPicker } from 'naive-ui'
```

Add `ref`, `computed`, `onBeforeUnmount` to a new vue import:

```typescript
import { ref, computed, onBeforeUnmount } from 'vue'
```

After `const themeStore = useThemeStore()` (line 8), add:

```typescript
const showColorPicker = ref(false)
const isCustomAccent = computed(() =>
  !accentPresets.some(p => p.hex === themeStore.accentColor)
)

let saveTimer: ReturnType<typeof setTimeout> | null = null

function setAccentFromPicker(hex: string) {
  themeStore.setAccentColor(hex)
  // Debounce backend save during drag
  if (saveTimer) clearTimeout(saveTimer)
  saveTimer = setTimeout(() => {
    import('@/api/games').then(({ settingsApi }) => {
      settingsApi.get().then(s => {
        s.accentColor = hex
        settingsApi.save(s)
      })
    })
  }, 500)
}

onBeforeUnmount(() => {
  if (saveTimer) clearTimeout(saveTimer)
})
```

- [ ] **Step 2: Add NColorPicker with #trigger slot after the preset loop**

Replace the `<div class="color-grid">` block (lines 102-116) with:

**Important:** Same as Task 1 — use `#trigger` slot, manage visibility manually, do NOT call slot's `onClick`.

```html
<div class="color-grid">
  <button
    v-for="preset in accentPresets"
    :key="preset.hex"
    class="color-swatch"
    :class="{ active: themeStore.accentColor === preset.hex }"
    :style="{ '--swatch-color': preset.hex }"
    :title="preset.name"
    @click="setAccent(preset.hex)"
  >
    <svg v-if="themeStore.accentColor === preset.hex" class="check-icon" viewBox="0 0 16 16" fill="none">
      <path d="M4 8.5L7 11.5L12 5" stroke="white" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
    </svg>
  </button>
  <NColorPicker
    :show="showColorPicker"
    :value="themeStore.accentColor"
    :modes="['hex']"
    :show-alpha="false"
    :swatches="accentPresets.map(p => p.hex)"
    :actions="[]"
    placement="bottom-end"
    @update:show="showColorPicker = $event"
    @update:value="setAccentFromPicker($event)"
  >
    <template #trigger>
      <button
        class="color-swatch custom-swatch"
        :class="{ active: isCustomAccent }"
        :style="isCustomAccent ? { '--swatch-color': themeStore.accentColor } : {}"
        title="自定义颜色"
        @click="showColorPicker = !showColorPicker"
      >
        <svg v-if="isCustomAccent" class="check-icon" viewBox="0 0 16 16" fill="none">
          <path d="M4 8.5L7 11.5L12 5" stroke="white" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
        </svg>
      </button>
    </template>
  </NColorPicker>
</div>
```

- [ ] **Step 3: Add CSS for custom swatch rainbow gradient**

After the `.check-icon` rule (lines 251-253), add:

```css
.custom-swatch:not(.active) {
  background: conic-gradient(
    #f43f5e, #f97316, #f59e0b, #10b981, #06b6d4, #3b82f6, #8b5cf6, #f43f5e
  );
}
```

- [ ] **Step 4: Verify type-check passes**

Run: `cd XUnityToolkit-Vue && npx vue-tsc --noEmit`
Expected: No errors

- [ ] **Step 5: Build frontend**

Run: `cd XUnityToolkit-Vue && npm run build`
Expected: Build succeeds with no errors

- [ ] **Step 6: Commit**

```bash
git add XUnityToolkit-Vue/src/components/library/LibraryCustomizer.vue
git commit -m "feat: 在库自定义面板添加自定义主题色取色器"
```
