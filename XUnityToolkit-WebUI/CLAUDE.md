# XUnityToolkit-WebUI Backend

ASP.NET Core backend. See root `CLAUDE.md` for project overview, API endpoints, and build commands.

## Core

- **No migration code:** Project is pre-stable — no backward-compat migrations or old-format converters
- `AppSettingsService`: in-memory cache; `GetAsync()` no disk I/O; must NOT mutate returned object — use `UpdateAsync`/`SaveAsync`
- **Data storage:** `%AppData%\XUnityToolkit\` for all runtime data; `AppDataPaths` centralizes paths; shipped assets (`bundled/`, `wwwroot/`) stay at program root
- **Sensitive data encryption:** `DpapiProtector` (DPAPI CurrentUser) encrypts `ApiEndpointConfig.ApiKey` and `SteamGridDbApiKey`; prefix `ENC:DPAPI:` + Base64; encrypt/decrypt in `AppSettingsService.ReadAsync`/`WriteAsync` boundary; decryption failure preserves ciphertext + creates `.bak` backup
- **Pre-DI paths:** `Program.cs` reads `builder.Configuration["AppData:Root"]` fallback to `%AppData%\XUnityToolkit\` before DI — must stay in sync with `AppDataPaths._root` formula
- **App URL:** `http://127.0.0.1:{port}` default `51821`; **MUST use `127.0.0.1` not `localhost`** (Unity Mono resolves to IPv6 `::1`); port configurable via `settings.json` → `aiTranslation.port` (read pre-DI in `Program.cs`)
- **Static file caching:** `UseStaticFiles` sets `Cache-Control: public, max-age=31536000, immutable` for `/assets/*` (Vite content-hashed); `no-cache` for root files (`index.html`, `favicon.ico`); `MapFallbackToFile` also sets `no-cache` on `index.html` — both must be configured separately
- Named `HttpClient`: `"LLM"` (120s/200conn), `"SteamGridDB"` (30s), `"LocalLlmDownload"` (12h), `"WebImageSearch"` (15s, browser UA), `"GitHubUpdate"` (60s), `"GitHubCdn"` (30s, CDN/web requests)
- **Multipart upload endpoints:** Must chain `.DisableAntiforgery()` on `MapPost` — otherwise ASP.NET rejects `multipart/form-data` requests
- **Updater AOT constraints:** `Updater/` targets `net10.0` (not `-windows`), `PublishAot=true`, `InvariantGlobalization`; no `JsonSerializer` reflection — use manual string formatting for JSON; no WinForms or UI frameworks
- **InformationalVersion gotcha:** .NET SDK appends `+commitHash` suffix; always `Split('+')[0]` before version comparison
- **GitHub API JSON:** `JsonOptions` uses `CamelCase`; GitHub API returns snake_case (`tag_name`, `browser_download_url`) — `GitHubRelease`/`GitHubAsset` models use `[JsonPropertyName]` to override; any new GitHub API model properties must also use explicit `[JsonPropertyName("snake_case")]`
- **GitHub API rate limit:** Unauthenticated ETag/`If-None-Match` 304 responses **still count** against the 60/hour limit — only authenticated 304s are exempt; CDN (`objects.githubusercontent.com`) and web (`github.com`) requests have separate, more generous limits
- **Update check three-layer strategy:** CDN (`update-check.json` asset) → Atom Feed (`/releases.atom`) → GitHub API fallback; when extracting methods that throw `InvalidOperationException` (e.g., rate limit), caller **must** still set `_status` to Error and broadcast via SignalR — otherwise status gets stuck at `Checking`
- **Mirror:** `AppSettings.HfMirrorUrl`; HF host-replacement for model downloads; plugins/llama binaries are bundled (no runtime GitHub downloads)
- **Fire-and-forget:** `CancellationToken.None` in `Task.Run`; `CancellationTokenSource` dicts for user cancellation
- **HTTP Range 416:** Verify completeness via `Content-Range`; size mismatch → delete and restart
- `Console.OutputEncoding = UTF8` before `WebApplication.CreateBuilder()`
- P/Invoke: `[DllImport]` not `[LibraryImport]`; renaming methods → search all call sites
- **Dialog foreground:** `DialogEndpoints.ForceForegroundWindow` uses `AttachThreadInput` — do NOT simplify to bare `SetForegroundWindow` (silently fails from background)
- **File upload security:** Always use `Path.GetFileName(file.FileName)` on uploaded file names — `Path.Combine` does NOT prevent path traversal from malicious filenames
- **Per-game data cleanup:** When adding new per-game data directories, must also add cleanup in `DELETE /api/games/{id}` handler (`GameEndpoints.cs`) + cache eviction if service has `RemoveCache`
- Reading log files: must use `FileShare.ReadWrite` to avoid `IOException`
- **C# `[GeneratedRegex]` with quotes:** Raw string literals (`"""..."""`) fail when regex contains `"` — use regular escaped strings instead

## INI Configuration

- **Never generate XUnity config from scratch** — always `PatchAsync` read-modify-write
- Config filename: `AutoTranslatorConfig.ini` at `BepInEx/config/`
- PatchAsync null semantics: `null` = skip, `""` = clear value
- `ApplyOptimalDefaultsAsync` sets `[General] Language = zh`, `OverrideFont = Microsoft YaHei`, `Endpoint = LLMTranslate` with `FallbackEndpoint = GoogleTranslateV2`
- **Sensitive sections** (sanitize for export): `GoogleLegitimate`, `BingLegitimate`, `Baidu`, `Yandex`, `DeepLLegitimate`, `PapagoTranslate`, `LingoCloud`, `Watson`, `Custom`, `LecPowerTranslator15`, `ezTrans`

## Glossary Extraction

- `GlossaryExtractionService`: buffer → trigger → drain → LLM extract → filter DNT → merge; depends on `DoNotTranslateService` to exclude DNT words from extraction (both via prompt hint and hard filter before merge)
- **GlossaryEntry model:** `Original`, `Translation`, `IsRegex`, `Description` (nullable)
- **Critical:** settings check (async) BEFORE buffer drain — otherwise pairs lost when disabled
- `TryTriggerExtraction` is synchronous (hot-path); async work deferred to `DrainAndExtractAsync`
- DLL must send `gameId` in `POST /api/translate` — requires `[LLMTranslate] GameId` in INI
- **All translation paths** must call `BufferTranslation` + `TryTriggerExtraction`, guarded by `!string.IsNullOrEmpty(gameId)` AND `!isLocalMode`

## Concurrency & Performance

- **Hot-path:** `POST /api/translate` receives 100+ req/s; all I/O must use in-memory caches, never disk per request
- `SemaphoreSlim`: one slot per batch; `EnsureSemaphore` delays Dispose 3 min; 60s timeout → 503; **critical:** semaphore wait and LLM call in separate `try` blocks; **local mode batch splitting:** `TranslateAsync` loops single-text `TranslateBatchAsync` calls so `_translating` shows 1 (not batch size)
- **Hot-path caching:** Never `GameLibraryService.GetByIdAsync` on hot path; use `ConcurrentDictionary` + explicit invalidation
- `BroadcastStats`: CAS throttle 200ms; `force: true` for completion/errors
- **Stats counters unit:** `_queued`, `_translating`, `_totalReceived`, `_totalTranslated` must ALL count individual texts (not batches/HTTP requests)
- **RecordError:** `LlmTranslationService.RecordError` is sole site — endpoint catch must NOT double-count
- `volatile` vs `Volatile.Read`: don't combine; `DateTime?` → `long` ticks + `Interlocked`; async cannot have `ref`/`in`/`out` → wrapper class
- **Plugin concurrency:** DLL 10x10 = 100 texts; Mono >15 connections deadlocks — batch instead
- **XUnity HTTP:** Mono `DefaultConnectionLimit` = 2 → `FindServicePoint(uri).ConnectionLimit`; no `Connection: close` (CLOSE_WAIT bug)
- **Pre-translation:** `Parallel.ForEachAsync` over batches of 10 (local mode: batch=1, parallelism=1); CAS-throttled 200ms progress

## AI Translation Context

- **Batch mode:** entire batch as one LLM call; JSON array I/O
- **Translation memory:** per-game volatile; cloud `ContextSize` (10, max 100), local `LocalContextSize` (0, max 10)
- **Game description:** `Game.AiDescription`; `_descriptionCache` invalidated by `PUT /description`; truncated to 500 chars
- **SystemPrompt order:** template → description → glossary → memory → dntHint → [texts]
- **Adding SystemPrompt sections:** New params must thread through: `TranslateAsync` → `TranslateBatchAsync` → `CallProviderAsync` → all 8 provider switch arms → `Call*Async` → `BuildSystemPrompt`; also update `TestTranslateAsync` (passes `null`)
- **ParseTranslationArray:** strips `<think>...</think>` then extracts JSON array (handles non-fenced)
- **`CallLlmRawAsync`:** public method for arbitrary LLM calls without semaphore; used by `GlossaryExtractionService`, `BepInExLogService`; endpoint selection: `OrderByDescending(e => e.Priority)` (higher value = preferred, consistent with `CalculateScore`)
- **Placeholder bypass:** When ENTIRE input text is a single placeholder (`{{G_x}}`/`{{DNT_x}}`), pre-compute the result directly and skip LLM call — LLMs unreliably preserve placeholders; pre-computed results must also skip `ApplyGlossaryPostProcess` (marked via `preComputed` dictionary)
- **Prompt glossary:** ALL glossary entries (including non-regex) must remain in system prompt even when placeholders are used — do NOT filter `promptGlossary` to regex-only; removing non-regex entries from prompt eliminates the LLM's awareness of terminology AND breaks `ApplyGlossaryPostProcess` fallback
- **Empty translation guard:** LLM may return `""` for untranslatable texts (plugin names, abbreviations); XUnity.AutoTranslator treats empty translations as errors — 5 consecutive errors trigger automatic translator Shutdown; `TranslateAsync` must fall back to original text when translation is empty/whitespace

## Pre-Translation Cache Monitor

- **Lazy loading:** `EnsureCacheAsync(gameId, toLang, ct)` called from `POST /api/translate`; reads `_PreTranslated.txt` once per game; `_loadAttemptedForGameId` prevents repeated attempts (even if file missing); `UnloadCache()` resets attempt tracking
- **Hot-path guard:** After initial load, `EnsureCacheAsync` is a single `volatile` read — no lock contention on steady state
- **Dependencies:** `GameLibraryService` + `AppSettingsService` (both in-memory cached); `GetByIdAsync` only called on game change, not per-request

## Asset Extraction (AssetsTools.NET)

- `MonoCecilTempGenerator`/`Cpp2IlTempGenerator` both in `AssetsTools.NET.Extra`; IL2CPP: fully qualified `AssetsTools.NET.Cpp2IL.Cpp2IlTempGenerator`
- **`classdata.tpk`:** embedded as resource; auto-updated from [AssetRipper/Tpk](https://github.com/AssetRipper/Tpk) CI by `build.ps1` (requires `gh` CLI); committed file serves as fallback
- `LoadAssetsFile()` holds file handles — must `UnloadAssetsFile()` per iteration
- **Bundle files:** `LoadBundleFile(path, true)` → iterate DirectoryInfos (skip `.resource`/`.resS`) → `LoadAssetsFileFromBundle` → `UnloadBundleFile`
- **TypeTree fallback:** bundles usually embed type trees — check `afile.Metadata.TypeTreeEnabled`
- Install flow auto-extracts → detects language → patches `[General] FromLanguage` → caches; failure doesn't block install
- **Language detection:** Proportion-based; Latin >80% → English immediately; non-Latin needs ≥50 chars AND ≥2% of total; Japanese requires kana ≥5% of CJK+kana; default is `"en"` (not `"ja"`); `IsGameText` is heuristic exclusion filter
- XUnity cache format: `encoded_original=encoded_translation`; escapes `\\`, `\n`, `\r`, `\=`; `XUnityTranslationFormat` static class
- **`{Lang}` in OutputFile:** substitute with `config.TargetLanguage`; guard against path traversal

## Font Replacement (AssetsTools.NET)

- **TMP_FontAsset detection:** MonoBehaviour with `m_Version` + `m_GlyphTable` fields = v1.1.0 (supported); `m_fontInfo` + `m_glyphInfoList` = v1.0.0 (unsupported)
- **Font scanning class DB fallback:** When `LoadClassDatabaseFromPackage` fails and no embedded type tree, skip MonoBehaviour scan but still attempt `AssetClassID.Font` scan — TTF Font assets use raw classId matching (`GetAssetsOfType`) which works without class DB in format version >= 16; `GetBaseField` may still fail (caught per-asset); also scan `*.bundle` files in `{GameName}_Data/` top level (not just `StreamingAssets/`)
- **Safe deep copy:** `DeepCopyFieldValues` recursively copies values matching by field name, preserving destination's type tree structure for cross-TMP-version compatibility; `RebuildArrayEntries` creates new array entries from destination's `TemplateField` to avoid serialization mismatch; copies `m_AtlasPopulationMode` and `m_IsMultiAtlasTexturesEnabled` in addition to data fields; DO NOT touch PPtr fields (`material`, `m_SourceFontFile`, `m_FallbackFontAssetTable`); `m_AtlasTextures` is handled separately by multi-atlas logic
- **Bundle backup optimization:** `EnsureBackup` uses `File.Move` (instant on same volume) after file handle released; `EnsureBackupCopy` (File.Copy) for loose files where handle may be open; same-volume detection via `Path.GetPathRoot`
- **Multi-atlas replacement:** `ReplaceSingleFont` reads ALL source atlas pages via `List<SourceAtlasPage>`; replaces existing destination textures; creates new Texture2D assets for extra pages (globally unique PathId via `AssetInfos.Max(PathId) + 1`); updates `m_AtlasTextures` PPtr array
- **Texture replacement:** `AssetsTools.NET.Texture` v3.0.2 (latest); API uses `TextureFile.ReadTextureFile()` → `SetPictureData()`/`FillPictureData()` → `WriteTo()`; clear `m_StreamData` (path/offset/size) after replacing embedded texture
- **New Texture2D pitfall:** `ValueBuilder.DefaultValueFieldFromTemplate` creates ZEROED fields; `TextureFile.ReadTextureFile(zeroedField)` → m_MipCount=0, m_TextureDimension=0 — Unity won't render; MUST `ReadTextureFile(existingTexture)` to inherit metadata, then override format/data/dimensions via `SetPictureData` + `WriteTo`
- **`TextureFile` field coverage:** `WriteTo` writes ALL 31 texture fields; `SetPictureData` only sets 5 (width, height, data, completeImageSize, streamData) — other metadata (m_MipCount, m_TextureSettings, m_IsReadable, m_TextureDimension, m_ImageCount, m_ColorSpace) must come from `ReadTextureFile` source
- **SDF atlas texture metadata:** When generating SDF atlas textures, MUST explicitly set `m_MipCount=1, m_MipMap=false, m_StreamingMipmaps=false, m_IsReadable=true` — template textures may have mipmaps enabled, and inheriting those settings causes black stripe artifacts (SDF distance values don't downscale correctly via mipmap averaging)
- **`AssetFileInfo.Create` (v>=16):** `TypeIdOrIndex` = raw classId (not index); does NOT add TypeTreeType — existing type tree entry for that classId must already exist in metadata
- **Bundle write pattern:** modify assets → `DirectoryInfos[i].SetNewData(afile)` → write uncompressed `.tmp` → re-read → `Pack(writer, LZ4)` → delete tmp → move to original
- **Addressables CRC:** regex zero-out `"Crc"\s*:\s*\d+` in `catalog.json`; delete `catalog.hash`; `catalog.bundle` contains TextAsset with JSON
- **Backup naming:** relative path from game root, separators replaced with `_` (e.g., `XXX_Data_sharedassets0.assets`)
- **External restore detection:** SHA256 hash stored in manifest, compared on `GET .../status`; wrap hash computation in `Task.Run` (files can be hundreds of MB)
- **Custom font auto-resolution:** `ReplaceFontsAsync` checks `data/custom-fonts/{gameId}/` before falling back to bundled font; `GetStatusAsync` returns `CustomFontFileName` for frontend display; `DELETE .../custom-font` clears custom font
- **`FontReplacementService` DI:** depends on `TmpFontService`, `BundledAssetPaths`, `AppDataPaths`; `BundledAssetPaths` used for TTF bundled font path resolution (prefer over `Assembly.GetExecutingAssembly().Location` which fails in single-file publish)
- **TTF replacement:** `ReplaceSingleTtfFont` replaces `m_FontData` byte array in `AssetClassID.Font` assets; preserves all layout metadata (`m_FontSize`, `m_LineSpacing`, etc.)
- **Bundled TTF:** `bundled/fonts/SourceHanSansCN-Regular.ttf` (~10MB) used as default replacement source for TTF fonts
- **Custom font coexistence:** `data/custom-fonts/{gameId}/` can hold both an AssetBundle (for TMP) and a TTF/OTF (for TTF) simultaneously; files distinguished by extension; upload endpoint uses magic bytes (`00 01 00 00` TTF, `4F 54 54 4F` OTF) for format detection
- **TMP source filtering:** `ReplaceFontsAsync` custom font auto-resolve filters by extension (excludes `.ttf`/`.otf` from TMP source, excludes non-TTF from TTF source)
- **Creating new array entries:** `ValueBuilder.DefaultValueFieldFromTemplate(prototype.TemplateField)` creates a new field instance from an existing entry's template; use first array child as prototype, clone per-entry, set values, then assign `array.Children = newList`

## Font Generation (FreeTypeSharp + Felzenszwalb EDT)

- **SDF pipeline (Unity-faithful):** `FT_RENDER_MODE_NORMAL` → AA bitmap → `DistanceFieldGenerator.GenerateSdf()` (Felzenszwalb EDT) → SDF with padding; replicates Unity's FontEngine approach; do NOT use `FT_RENDER_MODE_SDF` (outline-based, produces different results from Unity)
- **`DistanceFieldGenerator`:** static class; **critical: `outside` field MUST init to 0 (not INF)** — padding area is outside the glyph; `inside` field inits to INF; v≥1 pixels must explicitly set `outside=INF`; SDFAA uses edge-centered sub-pixel seeding `(0.5-v)²`/`(v-0.5)²` (edge at v=0.5; do NOT use `v²`/`(1-v)²`); SDF8/16/32 uses binary initialization at threshold 128; Felzenszwalb 1D→2D parabola envelope; bilinear downsample for upsampled modes; **normalization must divide by `padding * upsampling`**; reference: TinySDF (`gridInner.fill(0)`, `gridOuter.fill(INF)`)
- **Render modes:** SDFAA (1x, default), SDF8 (8x), SDF16 (16x), SDF32 (32x upsampling); `AtlasRenderMode` enum: SDFAA=4165, SDF8=4168, SDF16=4169, SDF32=4170; constraint: `samplingSize × upsampling ≤ 16384`
- **Padding:** dynamic calculation — percentage mode `(int)(samplingSize * percent / 100)` or pixel mode; minimum 1; `GradientScale = padding + 1` injected into Material `m_SavedProperties.m_Floats`; `m_AtlasPadding` must match
- **SDF bitmap includes padding:** `BitmapWidth/Height` in GlyphData are padded dimensions; packing uses padded size directly (no double-padding); `GlyphRect` references inner glyph region (`AtlasX + padding`, unpadded width); `UsedGlyphRects` references full padded region
- **Auto-sizing:** `AutoSizeSamplingSize` binary search (15 iterations); samples 200 glyphs to estimate area; initial max = `sqrt(atlasArea / count) × 3`; recalculates padding if percentage mode
- **FreeTypeSharp:** v3.1.0 raw unsafe P/Invoke (bundles FreeType 2.13.2); requires `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in csproj; `FT_LOAD` is enum not int — use `FT_LOAD.FT_LOAD_NO_BITMAP | FT_LOAD.FT_LOAD_NO_HINTING` directly (no cast)
- **Atlas Y-axis:** FreeType bitmaps are top-down (Y=0 at top); Unity Texture2D/TMP GlyphRect are bottom-up (Y=0 at bottom); atlas bytes must be row-flipped and GlyphRect Y converted (`atlasHeight - y - height`) before injection
- **`TmpFontService.ResolveFontFile` callers:** game install passes full version (`"2022.3.62f3"`), font generation passes major-only (`"6000"`); `ParseMajorVersion` must handle both formats (with and without dots)
- **Multi-atlas:** `PackGlyphs` tries single page first; binary search + 90% safety margin for max glyphs per page; `RectanglePacker.Pack` does NOT throw on overflow — must check returned `bounds` against atlas dimensions
- **async/unsafe conflict:** `GenerateCore` is `unsafe` (FreeType pointers) — character resolution via `CharacterSetService.ResolveCharactersAsync` MUST happen in `GenerateAsync` before `Task.Run`, never inside `GenerateCore`
- **`HashSet<int>` not `HashSet<char>`:** CJK Extension B (U+20000+) requires supplementary plane support; `char` is 16-bit; use `int` codepoints throughout pipeline; `StringInfo.GetTextElementEnumerator()` for surrogate pair handling
- **Disk-temp SDF bitmaps:** For large charsets (70K+ chars), SDF bitmaps saved to `data/font-generation/temp/{sessionId}/` during rendering, read back per-page during compositing, cleaned up in `finally`
- **CharacterSetService:** singleton; depends on `AppDataPaths`, `GameLibraryService`; `ResolveCharactersAsync` merges built-in + custom TXT + translation file sources; superset warnings from `BuiltinCharsets.SupersetOf`; translation file path resolved from game INI `[Files] OutputFile` + `[General] Language`
- **Install as TMP font:** `POST /install-tmp-font/{gameId}` copies generated bundle to `{GamePath}/BepInEx/Font/{fontName}` (preserves actual name, strips `.bundle` ext) and patches `[Behaviour] FallbackFontTextMeshPro`; uses `TmpFontService.InstallCustomFont`
- **`TmpFontService` API:** `InstallFont(gamePath, gameInfo)` returns `string?` config path (e.g. `"BepInEx/Font/SourceHanSans_U2022"`), null if unavailable; `InstallCustomFont(gamePath, srcPath, destFileName)` static method for generated fonts; `RemoveFont` deletes all files in `BepInEx/Font/`; no `FontFileName`/`ConfigValue` constants
- **`FallbackFontTextMeshPro` config:** NOT in `ApplyOptimalDefaultsAsync` defaults; set by `InstallOrchestrator` after font install and by `POST /{id}/tmp-font` endpoint

## Local LLM

- GPU detection: `DxgiGpuDetector` (DXGI, 64-bit VRAM, PCI VendorId) primary; WMI fallback (uint32 caps at 4GB)
- Backend selection: NVIDIA→CUDA, AMD/Intel→Vulkan, none→CPU; binaries at `{dataRoot}/llama/{cuda,vulkan,cpu}/`
- **Bundled binaries:** ZIPs in `bundled/llama/`; lazy-extracted on first `POST /start` via `ExtractLlamaZipAsync`
- **Model downloads:** `.downloading` temp files + HTTP Range; `_pauseRequests` differentiates pause from cancel; dual source: HuggingFace (`/resolve/main/`) or ModelScope (`/models/{repo}/resolve/master/` — note `master` not `main`); `_downloadModelScopeState` tracks active source; cleanup checks both sources' temp files
- **ModelScope URL:** `https://modelscope.cn/models/{owner}/{repo}/resolve/master/{file}` — supports Range headers for resume; public models need no auth
- **GPU monitoring:** nvidia-smi polled every 3s when CUDA running
- **Reasoning disabled:** `--reasoning-budget 0` prevents `<think>` blocks
- Endpoint auto-registers as `Custom` provider with Priority=8; stable `EndpointId`
- Settings: `data/local-llm-settings.json`; mirror settings unified in `AppSettings`

## BepInEx Installation (Bundled)

- **Mono games:** BepInEx 5 from `bundled/bepinex5/` (x64 + x86)
- **IL2CPP games:** BepInEx 6 BE from `bundled/bepinex6/` (x64 + x86); supports IL2CPP metadata v31+
- `BepInExInstallerService.InstallAsync` is plain ZIP extraction; version parsed from filename

## Plugin Package (Export/Import)

- `PluginPackageService`: exports BepInEx + XUnity as ZIP (max compression)
- **INI sanitization:** blanks sensitive section values; `[LLMTranslate]` only blanks `GameId`
- **ZIP filename:** `{sanitized game name}_{target language}_{yyyy-MM-dd}.zip`
- **Import with font replacement:** `_font_replacement_manifest.json` sentinel in ZIP; import MUST backup target asset files BEFORE extraction (ZIP overwrites originals); update `OriginalPath` in manifest to match importing game's directory

## DI & Provider Gotchas

- Pre-constructed `ILoggerProvider`: use `AddSingleton<T>(_ => instance)` to avoid double-dispose
- `IHubContext<T>.SendAsync` is extension method — requires `using Microsoft.AspNetCore.SignalR;`
- **`SystemTrayService` DI:** `AddSingleton` + `AddHostedService(sp => sp.GetRequired...)` for injection + hosting

## Web Image Search

- **`WebImageSearchService`:** Scrapes Bing and Google for image results; no API key needed
- **SSRF protection:** `ValidateImageUrl` rejects non-http(s), loopback, private IPs before server-side download
- **Content-Type validation:** Must check `IsAllowedContentType` before saving downloaded images
