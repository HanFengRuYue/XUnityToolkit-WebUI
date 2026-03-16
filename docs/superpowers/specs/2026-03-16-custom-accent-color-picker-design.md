# Custom Accent Color Picker

**Date:** 2026-03-16
**Status:** Approved

## Problem

The application has 8 preset accent colors, but users cannot pick an arbitrary color. Adding a color picker alongside the presets enables full customization.

## Design

### Overview

Add a "custom color" swatch button at the end of the existing preset color row in both `SettingsView.vue` and `LibraryCustomizer.vue`. Clicking it opens Naive UI's built-in `NColorPicker`. The selected color feeds into the existing `themeStore.setAccentColor(hex)` pipeline — no backend or theme store changes required.

### UI Behavior

**Custom swatch button:**
- Positioned after the 8 preset color swatches
- When no custom color is active (current color matches a preset): displays a conic rainbow gradient to indicate "pick any color"
- When a custom (non-preset) color is active: displays the current custom color with the active border + checkmark, same as preset swatches

**Color picker:**
- Uses `NColorPicker` with the custom swatch button rendered via its `#trigger` slot — no separate `NPopover` needed
- Visibility controlled via `:show` / `@update:show` props
- Restricted to hex-only mode via `:modes="['hex']"` to ensure output is always `#RRGGBB` format (theme store's `hexToRgb()` expects this)
- The 8 preset colors are passed as `swatches` prop for quick access within the picker
- On color change: calls `themeStore.setAccentColor(hex)` for live preview
- Picker closes on click-outside (default behavior)

**LibraryCustomizer save debounce:**
- `LibraryCustomizer.setAccent()` does a GET+PUT API roundtrip on every call
- During color picker drag, `on-update:value` fires on every mouse movement
- Solution: call `themeStore.setAccentColor(hex)` on every update for instant CSS preview, but debounce the backend save (e.g., 500ms) to avoid flooding the API

### Affected Files

| File | Change |
|------|--------|
| `XUnityToolkit-Vue/src/views/SettingsView.vue` | Add custom swatch with NColorPicker after preset loop |
| `XUnityToolkit-Vue/src/components/library/LibraryCustomizer.vue` | Same pattern (28px swatch), debounced backend save |

### No Changes Required

- **`stores/theme.ts`** — `setAccentColor(hex)` already accepts any valid hex string
- **`App.vue`** — `themeOverrides` already derives from `themeStore.accentColor` dynamically
- **Backend `AppSettings.AccentColor`** — already a `string` field, persisted via existing settings save flow
- **`main.css`** — CSS variables already set dynamically by `applyAccentColor()`

### Helper Logic

A computed `isCustomColor` checks whether `themeStore.accentColor` is NOT in the `accentPresets` array. This determines:
- Whether the custom swatch shows rainbow gradient vs current color
- Whether the custom swatch has the `active` class

### Edge Cases

- **Color format:** `:modes="['hex']"` ensures NColorPicker always outputs `#RRGGBB`, compatible with `hexToRgb()` in theme store
- **Same color as preset:** If user picks a color that matches a preset hex exactly, the preset swatch shows active instead of the custom swatch (natural behavior from the existing `===` check)
- **Persistence:** No change — `setAccentColor` already persists to localStorage and syncs to backend via existing settings save
- **Drag performance:** Theme store CSS update is cheap (inline style set); only the API save is debounced
