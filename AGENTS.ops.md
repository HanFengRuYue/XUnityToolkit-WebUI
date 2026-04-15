# AGENTS.ops.md

<!-- agents-maintainer:auto:start -->
## Read This When
- Touching runtime setup, ports, logging, deployment, external services, or operational safeguards.

## Relevant Paths
- Confirm the owning paths from the repository layout before adding new notes here.

## Commands / Constraints
- Keep notes here focused on durable constraints, not general repo summaries.
- Keep notes specific to this topic and move path-local rules into closer directory AGENTS.md files when possible.

## Verification
- Run the topic-specific checks that match the affected files.
<!-- agents-maintainer:auto:end -->

## Manual Notes
<!-- agents-maintainer:manual:start -->
- Development and packaged runtime both bind to `127.0.0.1`; the default port comes from `settings.json` via `aiTranslation.port`.
- Prefer validating the full UI through `http://127.0.0.1:51821` because it serves both the API and the built frontend. Use the Vite dev server only when `wwwroot` assets are intentionally stale or missing.
- Startup logging should keep reporting `CurrentDirectory`, `BaseDirectory`, `ContentRoot`, `WebRoot`, and whether `wwwroot/index.html` exists. Missing entry assets should remain a critical startup signal.
- `ApplicationStarted` performs deferred AI and updater initialization, and `ApplicationStopping` tears down runtime state. Keep those lifecycle hooks intact when restructuring host startup or shutdown.
- External URLs, ZIP extraction, and file imports are high-risk entry points. Reuse the existing `PathSecurity` validation and extraction helpers instead of adding ad hoc path handling.
- Local LLM launch path resolution must continue flowing through `LocalLlmLaunchPathResolver`, including its fallback chain for relative paths, short paths, and launch-cache aliases.
<!-- agents-maintainer:manual:end -->
