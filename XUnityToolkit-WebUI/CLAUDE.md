# XUnityToolkit-WebUI Backend

ASP.NET Core backend. See root `CLAUDE.md` for project overview, API endpoints, and build commands.

## Core

- **No migration code:** Project is pre-stable — no backward-compat migrations or old-format converters
- **Key infrastructure services:** `GameLibraryService` (in-memory game library CRUD), `AppSettingsService` (settings cache), `ConfigurationService` (INI read/write/patch), `UnityDetectionService` (game detection), `GameImageService` (icon/cover/background management)
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
- **Fire-and-forget SignalR invariant:** When fire-and-forget `Task.Run` wraps an async service call and the frontend resets state via a SignalR completion event, the endpoint handler MUST broadcast completion on failure/cancel paths too — services may only broadcast on success; otherwise frontend state gets permanently stuck; always wrap the entire body in `try/catch` with error broadcast in `catch`
- **SignalR error broadcast:** In fire-and-forget `catch (Exception ex)` blocks that broadcast errors via SignalR, use static error messages (e.g., `"字体生成失败，请检查字体文件格式"`), NOT `ex.Message` — log the exception server-side via `ILogger` instead
- **Endpoint return pattern:** Always wrap return values with `Results.Ok(ApiResult<T>.Ok(...))` — returning `ApiResult<T>` directly (without `Results.Ok()`) bypasses configured `JsonSerializerOptions` and may produce different enum casing
- **Validation error HTTP codes:** Always use `Results.BadRequest(ApiResult.Fail(...))` for input validation failures — never `Results.Ok(ApiResult.Fail(...))` (returns 200 on error, confusing clients)
- **Update endpoint error codes:** `/api/update/apply` pre-condition failures (active operations) → 409 Conflict; download/apply service errors → 500; `InvalidOperationException` from check → 400
- **HTTP Range 416:** Verify completeness via `Content-Range`; size mismatch → delete and restart
- `Console.OutputEncoding = UTF8` before `WebApplication.CreateBuilder()`
- P/Invoke: `[DllImport]` not `[LibraryImport]`; renaming methods → search all call sites
- **Dialog foreground:** `DialogEndpoints.ForceForegroundWindow` uses `AttachThreadInput` — do NOT simplify to bare `SetForegroundWindow` (silently fails from background)
- **File upload security:** Always use `Path.GetFileName(file.FileName)` on uploaded file names — `Path.Combine` does NOT prevent path traversal from malicious filenames
- **Per-game data cleanup:** When adding new per-game data directories, must also add cleanup in `DELETE /api/games/{id}` handler (`GameEndpoints.cs`) + cache eviction if service has `RemoveCache` (e.g., `termService.RemoveCache`, `scriptTagService.RemoveCache`)
- **Service cache clearing:** `TermService.ClearAllCache()`, `ScriptTagService.ClearAllCache()`, `TranslationMemoryService.ClearAllCache()`, `DynamicPatternService.ClearAllCache()`, `TermExtractionService.ClearAllCache()` clear all in-memory caches; used by settings reset (`POST /api/settings/reset`) and settings import (`POST /api/settings/import`); `RemoveCache(gameId)` clears single game
- **`ClearAllCache` dispose invariant:** Services with per-game `ConcurrentDictionary<string, SemaphoreSlim>` locks MUST dispose all semaphores in `ClearAllCache()` (iterate `.Values` → `.Dispose()` → `.Clear()`); matches `RemoveCache` pattern
- **Settings reset/import cache invalidation:** Both `/reset` and `/import` must call all 7 invalidations: `settingsService.InvalidateCache()`, `termService.ClearAllCache()`, `scriptTagService.ClearAllCache()`, `tmService.ClearAllCache()`, `dynamicPatternService.ClearAllCache()`, `extractionService.ClearAllCache()`, `cacheMonitor.UnloadCache()` — missing any one leaves stale in-memory state
- **Settings reset log suspension:** `POST /api/settings/reset` calls `FileLoggerProvider.SuspendFileLog()` before deleting data directory (releases log file handle), then `ResumeFileLog()` in `finally`; do NOT replace whole-directory deletion with per-subdirectory deletion (previously caused incomplete cleanup)
- **Bundled file build copy:** New files in `bundled/` require `<Content CopyToOutputDirectory="PreserveNewest" Link="bundled\...">` in `.csproj` — `build.ps1` only runs on publish, `dotnet run` uses build output
- **`ScriptTagService` DI:** `AddSingleton`; follows `TermService` pattern (SemaphoreSlim + ConcurrentDictionary cache + atomic file writes); preset auto-update in `GetAsync`; compiled regex cache invalidated on save/auto-update
- **Log level config:** `Program.cs` has two-layer filtering: `AddFilter("XUnityToolkit_WebUI", LogLevel.Debug)` (ASP.NET pipeline) + `FileLoggerProvider(logsDirectory, LogLevel.Debug)` (provider); both must be ≤ desired level or logs are silently dropped
- **Static method logging pattern:** Static methods can't access `ILogger` — either pass `ILogger? log = null` parameter and use `log?.LogDebug(...)`, or add logging at the instance-method call site where `logger` is available; prefer call-site logging when only aggregate info (counts, before/after) is needed
- Reading log files: must use `FileShare.ReadWrite` to avoid `IOException`
- **C# `[GeneratedRegex]` with quotes:** Raw string literals (`"""..."""`) fail when regex contains `"` — use regular escaped strings instead
- **`Lock` type API (.NET 9+):** Use `_lock.Enter()`/`_lock.Exit()` — do NOT use `Monitor.Enter(_lock)`/`Monitor.Exit(_lock)` (CS9216 warning; `Lock` is not `object`-based)
- **Per-game locking pattern:** Services with per-game data files (`TranslationMemoryService`, `DynamicPatternService`) use `ConcurrentDictionary<string, SemaphoreSlim>` for per-game locks — do NOT use a single global `SemaphoreSlim` for multi-game services

## INI Configuration

- **Never generate XUnity config from scratch** — always `PatchAsync` read-modify-write
- Config filename: `AutoTranslatorConfig.ini` at `BepInEx/config/`
- PatchAsync null semantics: `null` = skip, `""` = clear value
- `ApplyOptimalDefaultsAsync` sets `[General] Language = zh`, `OverrideFont = Microsoft YaHei`, `Endpoint = LLMTranslate` with `FallbackEndpoint = GoogleTranslateV2`
- **Sensitive sections** (sanitize for export): `GoogleLegitimate`, `BingLegitimate`, `Baidu`, `Yandex`, `DeepLLegitimate`, `PapagoTranslate`, `LingoCloud`, `Watson`, `Custom`, `LecPowerTranslator15`, `ezTrans`

## Glossary Extraction

- `GlossaryExtractionService`: buffer → trigger → drain → LLM extract → filter DNT terms → merge; depends on `TermService` to exclude DoNotTranslate-type terms from extraction (both via prompt hint and hard filter before merge)
- **TermEntry model:** `Type` (Translate/DoNotTranslate), `Original`, `Translation`, `Category`, `Description`, `IsRegex`, `CaseSensitive`, `ExactMatch`, `Priority`
- **Critical:** settings check (async) BEFORE buffer drain — otherwise pairs lost when disabled
- `TryTriggerExtraction` is synchronous (hot-path); async work deferred to `DrainAndExtractAsync`
- DLL must send `gameId` in `POST /api/translate` — requires `[LLMTranslate] GameId` in INI
- **All translation paths** must call `BufferTranslation` + `TryTriggerExtraction`, guarded by `!string.IsNullOrEmpty(gameId)` AND `!isLocalMode`

## Concurrency & Performance

- **Hot-path:** `POST /api/translate` receives 100+ req/s; all I/O must use in-memory caches, never disk per request
- `SemaphoreSlim`: one slot per batch; `EnsureSemaphore` delays Dispose 3 min; 60s timeout → 503; **critical:** semaphore wait and LLM call in separate `try` blocks; **local mode batch splitting:** `TranslateAsync` loops single-text `TranslateBatchAsync` calls so `_translating` shows 1 (not batch size)
- **Semaphore release guard:** `finally` blocks that release a semaphore MUST check `if (semaphoreAcquired)` — unconditional `Release()` after timeout/cancellation corrupts the semaphore count; same applies to stat counters like `_translating`
- **Hot-path caching:** Never `GameLibraryService.GetByIdAsync` on hot path; use `ConcurrentDictionary` + explicit invalidation
- `BroadcastStats`: CAS throttle 200ms; `force: true` for completion/errors AND all `TranslateBatchAsync` state transitions (`_queued++/--`, `_translating++/--`) — non-forced broadcasts are silently dropped because they fire within the throttle window of the caller's broadcast, making intermediate pipeline states invisible to the frontend
- **Stats counters unit:** `_queued` and `_translating` count API requests (±1 per `TranslateBatchAsync` call); `_totalReceived` and `_totalTranslated` count individual texts; `MaxConcurrency` in `TranslationStats` reflects `_currentMaxConcurrency`
- **`TranslationStats.Queued` is computed:** `max(0, TotalReceived - TotalTranslated - _totalFailedTexts)` — NOT read from `_queued`; `_queued` only used internally for semaphore-level tracking/logging; `_totalFailedTexts` tracks texts from failed `TranslateAsync` calls to keep the queue balanced; `TranslateAsync` uses `bool completed` flag + `finally` block to ensure `_totalFailedTexts` is incremented on all failure paths
- **RecordError:** `LlmTranslationService.RecordError` is sole site — endpoint catch must NOT double-count
- `volatile` vs `Volatile.Read`: don't combine; `DateTime?` → `long` ticks + `Interlocked`; async cannot have `ref`/`in`/`out` → wrapper class
- **`Volatile.Read` consistency:** When a field uses `Volatile.Read`/`Volatile.Write`, ALL access sites must use them — a single direct read (e.g., in stats snapshots) bypasses memory barriers and can return stale values
- **ConcurrentDictionary iteration safety:** Iterating `.Keys` while concurrent modifications happen (e.g., `ClearAllCache` while `Add` inserts) can miss entries; use `.Keys.ToList()` to snapshot before mutating
- **CTS atomic swap:** When guarding "only one operation at a time" with a nullable `CancellationTokenSource` field, use `Interlocked.CompareExchange(ref _cts, newCts, null)` — not `if (_cts is not null) throw; _cts = new...` (TOCTOU race)
- **CTS replacement in debounce:** When replacing a CTS in a `ConcurrentDictionary`, write the new CTS first, THEN cancel/dispose the old — avoids a window where concurrent code finds neither entry
- **CTS debounce atomic swap:** `_dict.GetValueOrDefault(key)` + `_dict[key] = newCts` is NOT thread-safe — concurrent callers can orphan a CTS (leaked background task) or double-dispose old CTS; use `GetOrAdd`/`TryUpdate` CAS loop pattern instead
- **CTS atomic cleanup in finally:** Use `Interlocked.Exchange(ref _cts, null)?.Dispose()` — NOT `var cts = _cts; _cts = null; cts?.Dispose()` (non-atomic read-null-dispose allows concurrent `.Cancel()` on already-disposed CTS)
- **`await using` early close:** When a stream must be closed before subsequent code reads the file, use a block scope `{ await using var s = ...; }` instead of calling `s.Close()` (redundant with `await using` implicit dispose)
- **Plugin concurrency:** DLL 10x10 = 100 texts; Mono >15 connections deadlocks — batch instead
- **XUnity HTTP:** Mono `DefaultConnectionLimit` = 2 → `FindServicePoint(uri).ConnectionLimit`; no `Connection: close` (CLOSE_WAIT bug)
- **Pre-translation:** `Parallel.ForEachAsync` over batches of 10 (local mode: batch=1, parallelism=1); CAS-throttled 200ms progress; `catch (OperationCanceledException) when (ct.IsCancellationRequested)` guards prevent HTTP `TaskCanceledException` from aborting entire operation — **always use `when` guard** in `Parallel.ForEachAsync` bodies
- **`when` guard in sequential LLM loops:** `DynamicPatternService`/`TermExtractionService` batch loops also require `catch (OperationCanceledException) when (ct.IsCancellationRequested)` — bare `catch (OperationCanceledException) { throw; }` catches HTTP timeout `TaskCanceledException` and aborts all remaining batches on a single timeout
- **LLM retry:** `TranslateBatchAsync` retries transient errors (`TaskCanceledException`, `HttpRequestException`, `TimeoutException`) up to 2 times with exponential backoff (2s, 4s); retry loop is inside semaphore-acquired block; `IsTransientError` helper determines retryability
- **TM debounced persistence:** `TranslationMemoryService.Add()` is synchronous (in-memory); persistence debounced 5s via `ScheduleDebouncedPersist`; `FlushAsync` for immediate persist after batch completion; per-game `SemaphoreSlim` locks (not global)

## AI Translation Context

- **Batch mode:** entire batch as one LLM call; JSON array I/O
- **Translation memory:** per-game volatile; cloud `ContextSize` (10, max 100), local `LocalContextSize` (0, max 10)
- **Game description:** `Game.AiDescription`; `_descriptionCache` invalidated by `PUT /description`; truncated to 500 chars
- **Multi-phase pipeline:** Phase 0 (TM lookup) → Phase 1 (natural, cloud only) → Phase 2 (placeholder substitution) → Phase 3 (force correction); `TermAuditService` verifies compliance between phases; `NaturalTranslationMode` and `TermAuditEnabled` control phases
- **Translation Memory Phase 0:** Inserted before Phase 1 in `TranslateAsync`; `phase0Resolved` HashSet tracks TM-resolved indices; Phase 1/2/3 filter loops must skip these indices; `perTextTranslationSource` array tracks `"tmExact"`/`"tmFuzzy"`/`"tmPattern"` per text; TM hits undergo `TermAuditService` validation before acceptance
- **SystemPrompt order:** template → description → terms → memory → [texts]
- **Adding SystemPrompt sections:** New params must thread through: `TranslateAsync` → `TranslateBatchAsync` → `CallProviderAsync` → all 8 provider switch arms → `Call*Async` → `BuildSystemPrompt`; also update `TestTranslateAsync` (passes `null`)
- **ParseTranslationArray:** strips `<think>...</think>` then extracts JSON array (handles non-fenced)
- **`CallLlmRawAsync`:** `Task<(string content, long tokens)> CallLlmRawAsync(ApiEndpointConfig endpoint, string systemPrompt, string userContent, double temperature, CancellationToken ct)` — returns tuple, must destructure `var (content, _) = await ...`; public method for arbitrary LLM calls without semaphore; used by `GlossaryExtractionService`, `BepInExLogService`, `DynamicPatternService`, `TermExtractionService`; endpoint selection: `OrderByDescending(e => e.Priority)` (higher value = preferred, consistent with `CalculateScore`)
- **Test vs Translate endpoint divergence:** `TestTranslateAsync` uses endpoints directly from the request body; `TranslateAsync` reads from stored settings via `settingsService.GetAsync()`. Test can pass while translation fails if settings aren't persisted — always check both paths when debugging "没有可用的AI提供商" errors
- **Gemini auth:** Use `x-goog-api-key` HTTP header, NOT URL query parameter `?key=` — URL params leak in exception messages, logs, and HTTP traces
- **Placeholder bypass:** When ENTIRE input text is a single placeholder (`{{G_x}}`/`{{DNT_x}}`), pre-compute the result directly and skip LLM call — LLMs unreliably preserve placeholders; pre-computed results skip `ApplyGlossaryPostProcess` but still go through term audit (counted as phase2Pass); `preComputed` dictionary tracks these indices
- **Prompt terms:** In cloud mode, ALL translate-type term entries (including non-regex) remain in system prompt even when placeholders are used — do NOT filter to regex-only. In local mode, `promptTerms` is set to `null` to save context tokens; term enforcement relies solely on placeholder substitution (non-regex) and `ApplyGlossaryPostProcess` (regex + fallback)
- **Term prompt annotation:** `AppendTermAnnotation` formats Category (`GetCategoryLabel` → 角色/地点/物品/技能/组织/通用) + Description; Phase 1 uses full-width `（）`, Phase 2 uses half-width `()`; both phases include category and description in term listings sent to LLM
- **`AppendTermAnnotation` bracket format:** Full-width directly appends `（`; half-width directly appends `(` — do NOT insert a leading space before `(`; both close with matching bracket
- **Term substring protection:** `ApplyGlossaryPostProcess`, Phase 3 force correction, and `TermAuditService` all use protected-span tracking to prevent shorter terms from corrupting substrings inside longer terms' translations (e.g., `Settings`→`设置` must NOT replace the `Settings` inside `SaveSettings.es3`→`SaveSettings.es3`); `TermAuditService` sorts terms longest-first and skips audit for short terms subsumed by a longer passed term; any new code doing `string.Replace` with term originals/translations MUST respect this pattern
- **Empty translation guard:** LLM may return `""` for untranslatable texts (plugin names, abbreviations); XUnity.AutoTranslator treats empty translations as errors — 5 consecutive errors trigger automatic translator Shutdown; `TranslateAsync` must fall back to original text when translation is empty/whitespace

## Pre-Translation Cache Monitor

- **Lazy loading:** `EnsureCacheAsync(gameId, toLang, ct)` called from `POST /api/translate`; reads `_PreTranslated.txt` once per game; `_loadAttemptedForGameId` prevents repeated attempts (even if file missing); `UnloadCache()` resets attempt tracking
- **Hot-path guard:** After initial load, `EnsureCacheAsync` is a single `volatile` read — no lock contention on steady state
- **Dependencies:** `GameLibraryService` + `AppSettingsService` (both in-memory cached); `GetByIdAsync` only called on game change, not per-request
- **IDisposable:** `PreTranslationCacheMonitor` implements `IDisposable` — disposes `_summaryTimer` and `_loadLock` on shutdown

## Asset Extraction (AssetsTools.NET)

- `MonoCecilTempGenerator`/`Cpp2IlTempGenerator` both in `AssetsTools.NET.Extra`; IL2CPP: fully qualified `AssetsTools.NET.Cpp2IL.Cpp2IlTempGenerator`
- **`classdata.tpk`:** embedded as resource; auto-updated from [AssetRipper/Tpk](https://github.com/AssetRipper/Tpk) CI by `build.ps1` (requires `gh` CLI); committed file serves as fallback
- `LoadAssetsFile()` holds file handles — must `UnloadAssetsFile()` per iteration
- **Bundle files:** `LoadBundleFile(path, true)` → iterate DirectoryInfos (skip `.resource`/`.resS`) → `LoadAssetsFileFromBundle` → `UnloadBundleFile`
- **TypeTree fallback:** bundles usually embed type trees — check `afile.Metadata.TypeTreeEnabled`
- Install flow auto-extracts → detects language → patches `[General] FromLanguage` → caches; failure doesn't block install
- **Language detection:** Proportion-based; Latin >80% → English immediately; non-Latin needs ≥50 chars AND ≥2% of total; Japanese requires kana ≥5% of CJK+kana; default is `"en"` (not `"ja"`); `IsGameText` is heuristic exclusion filter
- **`IsGameText` path filter:** Path detection requires no-space heuristic — `text.Contains('/') && text.Contains('.') && !text.Contains(' ')` for Unix paths; `text.Contains(":\\")` for Windows paths; natural text with `/` and `.` (rich text closing tags like `</b>`, dialogue with periods) preserved; backslash paths also require no spaces
- **`CollectStrings` depth:** Recursion limit is 20 (supports deeply nested frameworks like GameCreator 2)
- **TextAsset JSON detection:** For games storing structured data as JSON TextAssets (e.g., VIDE dialogues), add detection in the TextAsset extraction loop (`TryExtract*` returning `List<ExtractedText>?`) BEFORE the generic `ExtractTextLines` fallback; use `System.Text.Json.JsonDocument` for parsing; return `null` to fall back to generic extraction
- **GameCreator 2 support:** `DetectGameCreatorDialogueType` identifies Dialogue/Quest/Actor/Stat types; type-specific extraction with `GameCreator:{Type}:{Name}` source tags; `DetectAndLogTemplateVariables` scans for `{Variable}` patterns (e.g., `{PC}`, `{M}`) and logs DNT term suggestions
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
- **Always-included charsets:** `ResolveCharactersAsync` unconditionally unions `Ascii()` + `CommonPunctuation()` into every generated font; `CommonPunctuation()` covers CJK Symbols & Punctuation (U+3000–U+303F), CJK Compatibility Forms (U+FE30–U+FE4F), Fullwidth ASCII (U+FF01–U+FF60), and select General Punctuation (dashes, quotes, ellipsis); adding new always-included ranges: add to `CommonPunctuation()` in `BuiltinCharsets.cs` + mirror in fallback path of `TmpFontGeneratorService.GenerateAsync`
- **Zero-size glyph GlyphRect:** Glyphs with `BitmapWidth==0 || BitmapHeight==0` (space, control chars) MUST have GlyphRect `{0,0,0,0}` — `BitmapWidth - 2*padding` produces negative values that TMP rejects; zero-size glyphs skip atlas packing but are included in GlyphTable/CharacterTable for valid metrics
- **FT_Load/Render failure tracking:** `FT_Load_Glyph`/`FT_Render_Glyph` failures MUST add the character to `missingChars` — bare `continue` silently drops characters from both the font and the report
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
- **Endpoint lifecycle:** `RegisterEndpointAsync` sets `Enabled=true` on `StartAsync` (after health check); `UnregisterEndpointAsync` sets `Enabled=false` on `StopAsync`; **startup cleanup** in `Program.cs` forces `Enabled=false` for `ApiKey=="local"` endpoints (local LLM never runs on fresh start; guards against crash-orphaned state)
- **Translation gate:** `POST /api/translate` injects `LocalLlmService` and blocks requests when `ActiveMode=="local" && !IsRunning` (503); this prevents XUnity error accumulation (5 consecutive errors → translator shutdown)
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
