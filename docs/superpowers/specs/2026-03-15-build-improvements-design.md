# Build Improvements Design

## Overview

Four improvements to the build system: remove ARM64, pin llama.cpp version, add network timeout/retry to CI, and align local/CI builds.

## 1. Remove ARM64 Support

Remove all `win-arm64` references from build scripts, CI workflows, and runtime code.

### Files

| File | Change |
|------|--------|
| `build.ps1` | Remove `win-arm64` from `-Runtime` ValidateSet; default to `win-x64`; remove `$Runtimes` array (single RID); remove WiX ARM64 platform mapping |
| `.github/workflows/build.yml` | Remove `matrix: rid:` strategy (hardcode `win-x64`); remove `if: matrix.rid == 'win-x64'` conditionals; remove `win-arm64.zip` from release assets |
| `.github/workflows/release.yml` | Remove `win-arm64` row from download table |
| `.github/workflows/dep-check.yml` | Remove `win-arm64` row from release notes |
| `XUnityToolkit-WebUI/Services/UpdateService.cs` | Remove `Architecture.Arm64 => "win-arm64"` branch |

## 2. Pin llama.cpp to B8354

Replace dynamic "latest release" API queries with a fixed release tag `b8354`.

### Files

| File | Change |
|------|--------|
| `build.ps1` | Replace GitHub API query (`/releases?per_page=10` + filter) with direct download from `https://github.com/ggml-org/llama.cpp/releases/tag/b8354`; use GitHub API to fetch release assets by tag |
| `.github/workflows/build.yml` | Same change: fixed tag `b8354` instead of latest release query |
| `XUnityToolkit-WebUI/Services/LocalLlmService.cs` | Update `LlamaVersion` constant from `"b8272"` to `"b8354"` |

### Asset patterns (unchanged, all x64)

- `llama-*-bin-win-cuda-12.4-x64.zip`
- `cudart-llama-bin-win-cuda-12.4-x64.zip`
- `llama-*-bin-win-vulkan-x64.zip`
- `llama-*-bin-win-cpu-x64.zip`

## 3. Add Timeout/Retry to CI

`build.ps1` already has `Invoke-WithRetry` (3 attempts, exponential backoff 2/4/8s) and explicit timeouts (API: 30s, downloads: 600s). CI `build.yml` has neither.

### Change

Add to `build.yml`:
- `Invoke-WithRetry` function matching `build.ps1` logic (3 retries, exponential backoff)
- `-TimeoutSec 30` on all API calls
- `-TimeoutSec 600` on all file downloads

## 4. Align Local and CI Builds

Apply changes 1-3 to both `build.ps1` and `build.yml` in parallel. Additionally fix two known divergences:

| Divergence | Fix in CI |
|------------|-----------|
| `Compress-Archive` (broken on PS 7.5.5) | Replace with `[System.IO.Compression.ZipFile]` |
| MSI file lookup without `-Recurse` | Add `-Recurse` to handle `zh-CN/` culture subfolder |

## Out of Scope

- Structural refactoring to share code between `build.ps1` and CI (user chose option C: manual sync)
- `dep-check.yml` does not track llama.cpp and will continue not to
- `build-local.ps1` already deleted in working tree; no action needed
