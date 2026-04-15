# AGENTS.frontend.md

<!-- agents-maintainer:auto:start -->
## Read This When
- Touching web UI, client code, components, styling, or JavaScript package workflows.

## Relevant Paths
- `UnityLocalizationToolkit-WebUI`
- `UnityLocalizationToolkit-WebUI.Tests`

## Commands / Constraints
- Keep notes here focused on durable constraints, not general repo summaries.
- Keep notes specific to this topic and move path-local rules into closer directory AGENTS.md files when possible.

## Verification
- Run the topic-specific checks that match the affected files.
<!-- agents-maintainer:auto:end -->

## Manual Notes
<!-- agents-maintainer:manual:start -->
- Frontend source lives in `UnityLocalizationToolkit-Vue`; the stack is `Vue 3 + TypeScript + Naive UI + Pinia + Vite 8`. Production builds write into `UnityLocalizationToolkit-WebUI/wwwroot`.
- Common frontend loop:
  - `cd UnityLocalizationToolkit-Vue`
  - `npm run dev`
  - `npx vue-tsc --build`
  - `npm run build`
- Keep the Vite proxy target on `http://127.0.0.1:51821`; do not switch frontend runtime URLs to `localhost`.
- Top-level app state flows through `RouterView + KeepAlive + Pinia`. For timers, SignalR, window listeners, or long-lived subscriptions, handle `onActivated`, `onDeactivated`, and `onBeforeUnmount` together.
- Prefer store actions over mutating nested Pinia state directly from views.
- For auto-save pages, follow `disable -> load/assign -> nextTick -> enable` when reloading server data so incoming values are not mistaken for user edits.
- Validate visible UI changes in the real app when possible; use the backend-served app on port `51821` for end-to-end checks and use the Vite dev server only when `wwwroot` assets are intentionally stale or missing.
- Manual translation routes are `/manual-translation/:id` and `/manual-translation/:id/assets`. Reuse the shared `sub-page`, `section-card`, hero, and `WorkspaceModeSwitch` patterns instead of inventing a separate page system.
- Keep manual translation asset browsing server-side paginated via `page`, `pageSize`, `valueKind`, `search`, `editableOnly`, and `overriddenOnly`. Use `status.kindCounts`; do not load the whole asset index into the browser for local filtering.
- After saving or deleting a manual translation override, refresh the current page, selected asset detail, and workspace status instead of reloading the whole page.
<!-- agents-maintainer:manual:end -->
