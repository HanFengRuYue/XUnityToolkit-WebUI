# AGENTS.frontend.md

## Scope
- Covers the Vue client in `UnityLocalizationToolkit-Vue` and the generated frontend assets in `UnityLocalizationToolkit-WebUI/wwwroot`.
- Covers views, components, stores, routes, composables, styling, Vite configuration, and frontend-facing API usage.
- Does not own backend endpoint contracts or app-data persistence rules except where shared shapes must stay synchronized.

## Continue Reading When
- Editing `UnityLocalizationToolkit-Vue/src`, `public`, `scripts`, `vite.config.ts`, or generated `wwwroot/assets`.
- Changing routes such as the game detail pages, manual translation pages, settings UI, or shared layout patterns.
- Touching client-side polling, SignalR subscriptions, autosave flows, or state management in Pinia.

## Current Structure
- Entry points: `UnityLocalizationToolkit-Vue/src/main.ts`, `src/App.vue`, `src/router/index.ts`
- Shared client modules: `src/api`, `src/components`, `src/composables`, `src/stores`, `src/utils`, `src/constants`
- Route-heavy views: `src/views`
- Static assets: `public`, `src/assets`
- Production output: `UnityLocalizationToolkit-WebUI/wwwroot`

## Rules And Conventions
- `[frontend:stack]` Keep the frontend on the current Vue stack.
  - Scope: frontend
  - Last confirmed: 2026-04-17
  - Notes: The active stack is `Vue 3 + TypeScript + Naive UI + Pinia + Vite 8`.
- `[frontend:runtime-url]` Keep frontend runtime traffic on `127.0.0.1`.
  - Scope: frontend
  - Last confirmed: 2026-04-17
  - Notes: `vite.config.ts` proxies `/api` and `/hubs` to `http://127.0.0.1:51821`; do not switch frontend runtime URLs to `localhost`.
- `[frontend:build-output]` Treat `UnityLocalizationToolkit-WebUI/wwwroot` as generated frontend output.
  - Scope: frontend
  - Last confirmed: 2026-04-17
  - Notes: `npm run build` writes directly into `../UnityLocalizationToolkit-WebUI/wwwroot`, so hashed asset updates there are expected after frontend builds.
- `[frontend:state-lifecycle]` Keep long-lived view effects aligned with `KeepAlive`.
  - Scope: frontend
  - Last confirmed: 2026-04-17
  - Notes: Top-level state flows through `RouterView + KeepAlive + Pinia`; timers, SignalR listeners, window listeners, and similar subscriptions should handle `onActivated`, `onDeactivated`, and `onBeforeUnmount` together.
- `[frontend:store-mutations]` Prefer store actions over ad hoc nested state mutation from views.
  - Scope: frontend
  - Last confirmed: 2026-04-17
  - Notes: Keep business logic in the owning Pinia store whenever possible.
- `[frontend:autosave-reload]` Use the autosave reload guard sequence on auto-save pages.
  - Scope: frontend
  - Last confirmed: 2026-04-17
  - Notes: Follow `disable -> load or assign -> nextTick -> enable` so incoming server values are not mistaken for user edits.
- `[frontend:manual-translation-patterns]` Reuse the shared manual translation UI patterns.
  - Scope: frontend
  - Last confirmed: 2026-04-17
  - Notes: Manual translation routes are `/manual-translation/:id` and `/manual-translation/:id/assets`; reuse the shared `sub-page`, `section-card`, hero, and `WorkspaceModeSwitch` patterns instead of inventing a separate page system.
- `[frontend:manual-translation-pagination]` Keep manual translation asset browsing server-paginated.
  - Scope: frontend
  - Last confirmed: 2026-04-17
  - Notes: Use `page`, `pageSize`, `valueKind`, `search`, `editableOnly`, and `overriddenOnly`, and consume `status.kindCounts`; do not load the full asset index into the browser for local filtering.
- `[frontend:manual-translation-refresh]` Refresh the active slice after manual translation edits.
  - Scope: frontend
  - Last confirmed: 2026-04-17
  - Notes: After saving or deleting an override, refresh the current page, selected asset detail, and workspace status instead of reloading the entire page.

## Commands And Checks
- `cd UnityLocalizationToolkit-Vue && npm run dev`
- `cd UnityLocalizationToolkit-Vue && npx vue-tsc --build`
- `cd UnityLocalizationToolkit-Vue && npm run build`
- Validate visible UI changes against the backend-served app on `http://127.0.0.1:51821` when possible; use the Vite dev server only when `wwwroot` is intentionally stale or missing.

## Common Issues And Handling
- If import resolution is odd, keep TypeScript files authoritative; `vite.config.ts` already prefers `.ts` and `.tsx` ahead of stale `.js` mirrors.
- Generated asset filename churn under `UnityLocalizationToolkit-WebUI/wwwroot/assets` is usually expected output, not accidental manual edits.

## Recent Relevant Changes
- 2026-04-17: Frontend guidance was rewritten into the standard domain template while preserving the existing runtime URL, autosave, and manual translation workflow rules.
