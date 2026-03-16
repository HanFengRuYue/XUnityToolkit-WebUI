# XUnityToolkit-Vue

Vue 3 frontend for XUnityToolkit-WebUI. See root `CLAUDE.md` for project overview, API endpoints, and build commands.

## Code Conventions

- Vue 3 Composition API with `<script setup lang="ts">`
- Scoped `<style scoped>`; use CSS variables from `main.css`
- Icons: `@vicons/material` and `@vicons/ionicons5` wrapped in Naive UI `NIcon`
- API client: `api.get`, `api.post`, `api.put`, `api.del` (NOT `.delete` — JS reserved word); import from `@/api/client` (NOT `@/api` — it's a directory, Vite fails with EISDIR)

## Design System

- **Theme:** Dark/light/system via `data-theme`; `useThemeStore` (localStorage + OS) is authoritative, not backend `AppSettings.Theme`; `ThemeMode = 'dark' | 'light' | 'system'`; use `resolvedTheme` (always `'dark'|'light'`) for rendering decisions, NOT `mode` (which can be `'system'`); default is `'system'`; `matchMedia` listener auto-updates when OS theme changes; light mode auto-darkens accent 15%
- **CSS Variables:** Defined in `main.css` (`--bg-root`, `--accent`, `--text-1`, etc.); theme-aware: use `--bg-subtle`/`--bg-muted` — never hardcode `rgba(255,255,255,...)`
- **Layout:** Sidebar (230px) + scrollable content; responsive at 768px (tablet drawer) and 480px (phone single-column)
- **Cards:** `.section-card` with `.section-icon`/`.section-title` (base `--accent`; semantic `.danger`/`.warning` only); decorative colors → `--accent`; `color-mix()` for translucent backgrounds
- **Shared CSS (main.css):** `.page-title`, `.page-title-icon`, `.section-card`, `.section-header`, `.section-title`, `.section-icon`, `.header-actions`, `.section-desc`, `.loading-state`, `.table-container`, `.add-entry-row`, `.unsaved-badge`, `.auto-save-badge`, `.empty-hint`, `.back-button` are global classes — do NOT redefine in scoped styles
- **Sub-page layout:** Game sub-pages use `.sub-page` (24px title) + `.sub-page-header` + `.back-button`; top-level pages use `.page-title` directly (26px)
- **Header actions:** Use `.header-actions` (NOT `.header-btn-group`) for button groups in section headers
- **Typography:** Do NOT use `font-family: monospace` for UI text (version numbers, labels, etc.) — it clashes with the system font; only use monospace inside code blocks or developer-facing output
- **Content padding:** `24px 28px` (desktop), `20px 20px` (tablet), `16px 12px` (phone) — hero backdrop negative margins must match; do NOT set padding on page-level containers (`.main-content` already provides it); do NOT use `max-width` on page containers unless content truly needs constraining; use `gap: 16px` for page-level flex column

## Adding a New Page

- **Top-level page:** Add view in `src/views/`, lazy route in `router/index.ts`, nav item in `AppShell.vue` `navItems`
- **Game sub-page:** lazy route at `/games/:id/{name}`, button in `GameDetailView.vue` — do NOT add to `navItems`
- **GlossaryEditorView:** NTabs with "术语表" and "禁翻表" tabs; each tab has independent auto-save, import/export, search, and clear-all; adding a new per-game text management feature → add as a new NTabPane here
- **Page transitions:** `meta.depth` on routes (1=top-level, 2=game detail, 3=game sub-pages); adding a new route requires `meta: { depth: N }`
- SignalR store: guard `connect()` with `state !== Disconnected`, re-join group in `onreconnected`

## Patterns & Gotchas

- **Theme default:** CSS `:root` = dark theme; `loadInitialTheme()` defaults to `'system'`; OS detection via `matchMedia`; `resolveTheme('system')` falls back to `'dark'` when OS detection unavailable
- **Scoped → global CSS:** Scoped styles (`.class[data-v-xxx]`) have higher specificity than global (`.class`); when extracting shared styles to `main.css`, must REMOVE scoped duplicates or they'll override; page-specific overrides stay in scoped with just the differing properties
- **`defineOptions` placement:** Must go AFTER all `import` statements in `<script setup>`, never before — otherwise subsequent imports fail with TS1232
- **KeepAlive:** Top-level views (Library, AiTranslation, FontGenerator, Log, Settings) are cached via `<KeepAlive :include>` in AppShell; each MUST have `defineOptions({ name: 'XxxView' })` after imports
- **Install state recovery:** `startInstall`/`startUninstall` must query backend `GET /api/games/{id}/status` as fallback — Pinia store state is lost on page reload while backend install continues running
- Use composables (`src/composables/`) for complex multi-step UI flows
- **Auto-save:** `useAutoSave(source, saveFn, { debounceMs, deep })`; `disable()` → load → `nextTick()` → `enable()`; `disable()` MUST clear pending timer; `onBeforeUnmount` auto-flushes; manual save MUST `disable()` before data reassign, `enable()` in `finally`
- **ConfigPanel** auto-saves internally (2s), no `save` event
- Naive UI: light theme pass `null`; `NDrawer` width numbers only; `NForm` label-placement via computed (not CSS); `NInput` `string?` use `:value` + `@update:value`; `NInput` blur+enter double-fire → flag guard; `NDialogOptions.onPositiveClick`: returning a `Promise` keeps dialog open until resolved — fire-and-forget long async work (e.g., `() => { doWork() }`) to close immediately
- `NDataTable`: `virtual-scroll` and `pagination` mutually exclusive; empty state guard with `filteredEntries.length > 0`; `row-key` must be globally unique — if ID can collide across categories, use composite key like `` `${category}:${id}` ``; columns without explicit `width`/`minWidth` get squeezed to 0px when fixed-width columns sum exceeds container — always set `minWidth` on flexible columns; **sort stability with in-cell editing**: `computed` re-triggers on every cell mutation via deep reactivity — use a `ref` cache + version counter (`entriesVersion`) and explicit `watch` on filter/sort settings to decouple; **controlled sort mode**: use `sorter: true` + `@update:sorters` + reactive `sortOrder` on columns, apply sorting manually in data pipeline (not NDataTable's built-in sort); type is `DataTableSortState` (NOT `SortState`)
- `NBadge`: use `:show` prop to control dot visibility (NOT `:value` with `dot: true` — `show` defaults to `true` so dot always appears if only `dot` is set)
- `NColorPicker`: `#trigger` slot replaces entire trigger element; `#label` only customizes text inside default rectangular trigger — use `#trigger` for custom trigger buttons; always set `:modes="['hex']"` when consuming hex values (default allows rgb/hsl/hsv switching, which breaks `hexToRgb()`); manage visibility manually via `:show`/`@update:show` — do NOT also call slot's provided `onClick` (it only opens, never toggles)
- `v-show` + `loading="lazy"` deadlock: use `opacity: 0` + `position: absolute`
- `onBeforeRouteLeave` with async: must `return new Promise<boolean>()` — NOT `next()` callback
- **RouterView key:** `:key="route.path"` ensures transitions fire for same-component different-route navigations
- **RouteMeta extension:** `env.d.ts` declares `depth?: number` on `RouteMeta` for TypeScript
- **GameDetailView animation:** 0.05s increments; inserting a card shifts ALL subsequent delays
- **Blob download:** `fetch` → `blob()` → `createObjectURL` → `a.click()` → `setTimeout(revokeObjectURL, 1000)`
- After changes: verify with `npx vue-tsc --noEmit` and `npm run build`
- Verify icon: `node -e "const m = require('@vicons/material'); console.log(m['IconName'] ? 'YES' : 'NO')"`
- **`embedded` prop pattern:** conditionally render card wrapper based on standalone vs nested usage
- **`LocalAiPanel.vue`:** receives settings via `v-model`; shared settings flow through parent's `useAutoSave`; local-only settings saved via `PUT /api/local-llm/settings`
- TypeScript: `Object.assign({}, obj, patch)` not spread for typed objects; lazy modals: `defineAsyncComponent`
- **Markdown rendering:** `marked` package (ships own types, no `@types/marked`); use `marked.parse(md, { async: false })` for synchronous string return
- **Regex match groups:** `match[1]` is `string | undefined` in strict TS — always check `match && match[1]`
- **`NTabs` equal-width segments:** `:deep(.n-tabs-tab) { flex: 1; justify-content: center; }`
- **`NTabs type="segment"` dark theme:** segment tabs blend into background; override with `:deep(.n-tabs-tab--active)` using `color: var(--accent)`, `background: color-mix(in srgb, var(--accent) 12%, var(--bg-card))`, `border: 1px solid var(--accent-border)`
- **`NUpload` in flex containers:** NUpload wraps trigger in extra divs that break flex alignment; fix with `:deep(.n-upload), :deep(.n-upload-trigger) { display: flex; align-items: center; }`
- **Cross-page glossary access:** Other views (e.g., TranslationEditorView) can add entries to glossary via `gamesApi.getGlossary` → check duplicate → `unshift` → `gamesApi.saveGlossary`; no shared Pinia store — each page fetches/saves independently
- **Bulk clear pattern:** "Clear all" = set reactive array to `[]`; for auto-save views (GlossaryEditor) this triggers auto-save of empty array; for manual-save views (TranslationEditor) it marks dirty state; always use `dialog.warning` confirmation

## Game Detail Background Image

- **Hero backdrop:** `GameDetailView.vue` — absolute-positioned behind content; NO blur on image (`filter: brightness(0.65) saturate(1.1)` only); gradient + vignette overlays fade to `--bg-root`
- **Acrylic cards:** `.section-card` uses `backdrop-filter: blur(20px)` with semi-transparent `--bg-card` — cards blur the background, not the image itself
- **Parallax:** scroll listener on `.main-content` (parent); `translateY(scrollTop * 0.3)` on hero img; `passive: true`
- **bgTimestamp:** separate `ref(Date.now())` for cache-busting background URL independently of `game.updatedAt`

## Web Image Search (Frontend)

- **`WebImageSearchTab` modes:** `'cover'` | `'icon'` | `'background'` — each with distinct search suffix, size filter, and grid aspect ratio
- **GameDetailView title-icon:** left-click → IconPickerModal; right-click → context menu (Change Cover / Change Background / Search Icon / Upload Icon / Delete Icon / Delete Background)
