# AGENTS.history.md

## Scope
- Stores archived AGENTS decisions, replacement trails, and durable context that should not stay in the active root index.
- This file is reference material only; live instructions stay in the current root-level `AGENTS*.md` files.

## Archived Changes
- 2026-04-17: Replaced the previous `AGENTS.build.md`, `AGENTS.ops.md`, and `AGENTS.notes.md` layout with the standard-domain sidecars `AGENTS.infra.md`, `AGENTS.testing.md`, and `AGENTS.history.md`.
- 2026-04-17: Rewrote `AGENTS.md` as a concise index and moved durable implementation detail into `AGENTS.frontend.md`, `AGENTS.backend.md`, `AGENTS.data.md`, `AGENTS.infra.md`, and `AGENTS.testing.md`.
- 2026-04-17: Preserved the verified frontend, backend, and data rules from the prior sidecars while dropping duplicated repo summaries and placeholder text.
- 2026-04-17: Kept AGENTS coverage at the repository root only; no nested `UnityLocalizationToolkit-Vue/AGENTS.md` or `UnityLocalizationToolkit-WebUI/AGENTS.md` files were added.

## Replacement Trail
- `AGENTS.build.md` was retired and its lasting packaging and build rules moved into `AGENTS.infra.md`.
- `AGENTS.ops.md` was retired and its lasting runtime and operational safeguards moved into `AGENTS.infra.md`.
- `AGENTS.notes.md` was retired and its lasting repo facts were redistributed into `AGENTS.md`, `AGENTS.backend.md`, `AGENTS.data.md`, and `AGENTS.infra.md`.

## Archive Notes
- `.agents-maintainer/backups/` contains historical snapshots only and is not part of the live AGENTS navigation.
- Machine-local Git ownership or `safe.directory` issues were intentionally left out of the active AGENTS rules because they are workstation-specific, not repository conventions.
