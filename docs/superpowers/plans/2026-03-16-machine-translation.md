# Machine Translation Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add free machine translation using reverse-engineered web APIs (Baidu, Youdao, Papago) as a third translation mode alongside cloud AI and local LLM.

**Architecture:** New `MachineTranslationService` parallel to `LlmTranslationService`. Shared DNT/glossary logic extracted into `TranslationHelper`. Three provider implementations behind `IMachineTranslationProvider` interface. Frontend gets a new dedicated page at `/machine-translation`. `POST /api/translate` routes based on `ActiveMode`.

**Tech Stack:** ASP.NET Core (.NET 10), Vue 3 + TypeScript + Naive UI + Pinia, SignalR

**Spec:** `docs/superpowers/specs/2026-03-16-machine-translation-design.md`

---

## Chunk 1: Backend Models & Shared Utilities

### Task 1: Create Machine Translation Settings Model

**Files:**
- Create: `XUnityToolkit-WebUI/Models/MachineTranslationSettings.cs`

- [ ] **Step 1: Create the model file**

```csharp
namespace XUnityToolkit_WebUI.Models;

public enum MachineTranslationProvider
{
    Baidu,
    Youdao,
    Papago
}

public class MachineProviderConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public MachineTranslationProvider Provider { get; set; }
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } // Lower = higher priority
}

public class MachineTranslationSettings
{
    public List<MachineProviderConfig> Providers { get; set; } =
    [
        new() { Provider = MachineTranslationProvider.Baidu, Priority = 1 },
        new() { Provider = MachineTranslationProvider.Youdao, Priority = 2 },
        new() { Provider = MachineTranslationProvider.Papago, Priority = 3 }
    ];
}

public class ProviderStatus
{
    public MachineTranslationProvider Provider { get; set; }
    public string Name { get; set; } = "";
    public bool Healthy { get; set; } = true;
    public bool InCooldown { get; set; }
    public string? LastError { get; set; } // Safe static messages only
    public DateTime? LastErrorTime { get; set; }
}
```

- [ ] **Step 2: Add MachineTranslation to AppSettings**

Modify: `XUnityToolkit-WebUI/Models/AppSettings.cs` — add after `ReceivePreReleaseUpdates` (line 18):

```csharp
public MachineTranslationSettings MachineTranslation { get; set; } = new();
```

- [ ] **Step 3: Extend ActiveMode in AiTranslationSettings**

Modify: `XUnityToolkit-WebUI/Models/AiTranslationSettings.cs` — update the `ActiveMode` doc comment (line 9) to include `"machine"`:

```csharp
/// <summary>cloud, local, or machine</summary>
public string ActiveMode { get; set; } = "cloud";
```

- [ ] **Step 4: Build verification**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add XUnityToolkit-WebUI/Models/MachineTranslationSettings.cs XUnityToolkit-WebUI/Models/AppSettings.cs XUnityToolkit-WebUI/Models/AiTranslationSettings.cs
git commit -m "feat(机器翻译): 添加机器翻译设置模型和提供商配置"
```

---

### Task 2: Extract TranslationHelper from LlmTranslationService

**Files:**
- Create: `XUnityToolkit-WebUI/Services/TranslationHelper.cs`
- Modify: `XUnityToolkit-WebUI/Services/LlmTranslationService.cs`

This task extracts shared DNT and glossary post-processing logic into a static helper class, then updates `LlmTranslationService` to call the helper. No behavior change.

- [ ] **Step 1: Create TranslationHelper.cs**

```csharp
using System.Text.RegularExpressions;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

/// <summary>
/// Shared translation utility methods used by both LlmTranslationService and MachineTranslationService.
/// Handles DNT placeholder substitution/restoration and glossary post-processing.
/// </summary>
public static partial class TranslationHelper
{
    // Lenient pattern: 1-2 braces with optional whitespace, captures index
    public static readonly Regex DntRestoreRegex = DntRestoreRegexGen();
    // Full-match variant for pre-computation check
    public static readonly Regex FullDntPlaceholderRegex = FullDntPlaceholderRegexGen();

    [GeneratedRegex(@"\{{1,2}\s*DNT_(\d+)\s*\}{1,2}", RegexOptions.Compiled)]
    private static partial Regex DntRestoreRegexGen();

    [GeneratedRegex(@"^\{{2}DNT_(\d+)\}{2}$", RegexOptions.Compiled)]
    private static partial Regex FullDntPlaceholderRegexGen();

    /// <summary>
    /// Replace DNT words in texts with {{DNT_x}} placeholders.
    /// Returns the processed texts and a mapping from placeholder to original word.
    /// </summary>
    public static (List<string> replacedTexts, Dictionary<string, string> mapping)
        ApplyDntReplacements(IList<string> texts, List<DoNotTranslateEntry> entries)
    {
        // Implementation extracted from LlmTranslationService.ApplyDoNotTranslateReplacements (line 783)
        // Copy the exact logic from the existing method
    }

    /// <summary>
    /// Restore {{DNT_x}} placeholders back to original words in translated texts.
    /// </summary>
    public static List<string> RestoreDntPlaceholders(
        IList<string> translations, Dictionary<string, string> mapping)
    {
        // Implementation extracted from LlmTranslationService.RestoreDoNotTranslatePlaceholders (line 845)
        // Copy the exact logic from the existing method
    }

    /// <summary>
    /// Apply glossary post-processing: replace glossary original words found in translated text
    /// with their glossary translations.
    /// </summary>
    public static string ApplyGlossaryPostProcess(string translated, List<GlossaryEntry> glossary)
    {
        // Implementation extracted from LlmTranslationService.ApplyGlossaryPostProcess (line 972)
        // Copy the exact logic from the existing method
    }

    /// <summary>Load DNT entries for a game. Returns empty list if gameId is null/empty.</summary>
    public static async Task<List<DoNotTranslateEntry>> LoadDntEntries(
        string? gameId, DoNotTranslateService dntService, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(gameId)) return [];
        return await dntService.GetAsync(gameId, ct);
    }

    /// <summary>Load glossary entries for a game. Returns null if gameId is null/empty.</summary>
    public static async Task<List<GlossaryEntry>?> LoadGlossary(
        string? gameId, GlossaryService glossaryService, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(gameId)) return null;
        return await glossaryService.GetAsync(gameId, ct);
    }
}
```

**IMPORTANT:** Copy the exact method bodies from `LlmTranslationService.cs`:
- `ApplyDoNotTranslateReplacements` (line 783) → `TranslationHelper.ApplyDntReplacements`
- `RestoreDoNotTranslatePlaceholders` (line 845) → `TranslationHelper.RestoreDntPlaceholders`
- `ApplyGlossaryPostProcess` (line 972) → `TranslationHelper.ApplyGlossaryPostProcess`

- [ ] **Step 2: Update LlmTranslationService to use TranslationHelper**

Modify: `XUnityToolkit-WebUI/Services/LlmTranslationService.cs`

Replace the private static methods with calls to `TranslationHelper`:
- Replace body of `ApplyDoNotTranslateReplacements` (line 783) to delegate: `return TranslationHelper.ApplyDntReplacements(texts, entries);`
- Replace body of `RestoreDoNotTranslatePlaceholders` (line 845) to delegate: `return TranslationHelper.RestoreDntPlaceholders(translations, mapping);`
- Replace body of `ApplyGlossaryPostProcess` (line 972) to delegate: `return TranslationHelper.ApplyGlossaryPostProcess(translated, glossary);`
- Update `DntRestoreRegex` references (line 766) to use `TranslationHelper.DntRestoreRegex`
- Update `FullDntPlaceholderRegex` references (line 775) to use `TranslationHelper.FullDntPlaceholderRegex`
- Remove the private regex fields that are now in TranslationHelper (keep `GlossaryRestoreRegex` and `FullGlossaryPlaceholderRegex` as they are LLM-specific)

- [ ] **Step 3: Build verification**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeded (no behavior change, just extraction)

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-WebUI/Services/TranslationHelper.cs XUnityToolkit-WebUI/Services/LlmTranslationService.cs
git commit -m "refactor(翻译): 提取共享翻译工具方法到 TranslationHelper"
```

---

## Chunk 2: Backend Provider Implementations

### Task 3: Create IMachineTranslationProvider Interface

**Files:**
- Create: `XUnityToolkit-WebUI/Services/MachineTranslation/IMachineTranslationProvider.cs`

- [ ] **Step 1: Create the interface**

```csharp
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services.MachineTranslation;

public interface IMachineTranslationProvider
{
    /// <summary>Display name (e.g., "百度翻译")</summary>
    string Name { get; }

    MachineTranslationProvider Provider { get; }

    /// <summary>Whether the provider is healthy (not in cooldown)</summary>
    bool IsHealthy { get; }

    /// <summary>Last error message (safe static message, not raw exception)</summary>
    string? LastError { get; }

    /// <summary>Last error time</summary>
    DateTime? LastErrorTime { get; }

    /// <summary>Whether the provider is in cooldown period</summary>
    bool InCooldown { get; }

    /// <summary>
    /// Initialize the provider (acquire tokens, cookies, etc.).
    /// Called lazily on first use and on re-initialization after failures.
    /// Thread-safe: concurrent calls wait for initialization to complete.
    /// </summary>
    Task InitializeAsync(HttpClient httpClient, ILogger logger, CancellationToken ct = default);

    /// <summary>
    /// Translate a single text. Providers handle rate limiting internally.
    /// </summary>
    Task<string> TranslateAsync(HttpClient httpClient, string text, string from, string to,
        ILogger logger, CancellationToken ct = default);

    /// <summary>Mark provider as unhealthy with cooldown</summary>
    void MarkUnhealthy(string error);

    /// <summary>Check if cooldown has expired and reset if so</summary>
    void CheckCooldown();
}
```

- [ ] **Step 2: Create base class with shared logic**

Create: `XUnityToolkit-WebUI/Services/MachineTranslation/MachineTranslationProviderBase.cs`

```csharp
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services.MachineTranslation;

public abstract class MachineTranslationProviderBase : IMachineTranslationProvider
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile bool _initialized;
    private DateTime _lastInitTime;
    private DateTime _cooldownUntil;
    private int _consecutiveFailures;

    public abstract string Name { get; }
    public abstract MachineTranslationProvider Provider { get; }
    protected abstract TimeSpan TokenTtl { get; } // e.g., 1 hour for Baidu

    public bool IsHealthy => !InCooldown;
    public string? LastError { get; private set; }
    public DateTime? LastErrorTime { get; private set; }
    public bool InCooldown => DateTime.UtcNow < _cooldownUntil;

    public async Task InitializeAsync(HttpClient httpClient, ILogger logger, CancellationToken ct)
    {
        if (_initialized && (DateTime.UtcNow - _lastInitTime) < TokenTtl)
            return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized && (DateTime.UtcNow - _lastInitTime) < TokenTtl)
                return;

            logger.LogInformation("[{Provider}] 正在初始化...", Name);
            await InitializeCoreAsync(httpClient, logger, ct);
            _initialized = true;
            _lastInitTime = DateTime.UtcNow;
            _consecutiveFailures = 0;
            logger.LogInformation("[{Provider}] 初始化成功", Name);
        }
        catch (Exception ex)
        {
            _initialized = false;
            logger.LogError(ex, "[{Provider}] 初始化失败", Name);
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<string> TranslateAsync(HttpClient httpClient, string text, string from, string to,
        ILogger logger, CancellationToken ct)
    {
        try
        {
            await InitializeAsync(httpClient, logger, ct);
            var result = await TranslateCoreAsync(httpClient, text, from, to, logger, ct);
            _consecutiveFailures = 0;
            return result;
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            logger.LogWarning(ex, "[{Provider}] 翻译失败 (连续失败: {Count})", Name, _consecutiveFailures);

            // 3 consecutive failures trigger re-initialization
            if (_consecutiveFailures >= 3)
            {
                _initialized = false;
                logger.LogInformation("[{Provider}] 连续失败 {Count} 次，将在下次请求时重新初始化", Name, _consecutiveFailures);
            }

            throw;
        }
    }

    public void MarkUnhealthy(string error)
    {
        LastError = error;
        LastErrorTime = DateTime.UtcNow;
        _cooldownUntil = DateTime.UtcNow.AddSeconds(60);
    }

    public void CheckCooldown()
    {
        // No action needed — InCooldown property checks time automatically
    }

    protected abstract Task InitializeCoreAsync(HttpClient httpClient, ILogger logger, CancellationToken ct);
    protected abstract Task<string> TranslateCoreAsync(HttpClient httpClient, string text, string from, string to,
        ILogger logger, CancellationToken ct);

    /// <summary>Random delay between requests (200-800ms) to avoid detection</summary>
    protected static async Task RateLimitDelay(CancellationToken ct)
    {
        var delay = Random.Shared.Next(200, 800);
        await Task.Delay(delay + 1000, ct); // 1s base + 200-800ms jitter
    }
}
```

- [ ] **Step 3: Build verification**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-WebUI/Services/MachineTranslation/
git commit -m "feat(机器翻译): 添加机器翻译提供商接口和基类"
```

---

### Task 4: Implement Baidu Translate Provider

**Files:**
- Create: `XUnityToolkit-WebUI/Services/MachineTranslation/BaiduTranslateProvider.cs`

- [ ] **Step 1: Create BaiduTranslateProvider**

Implement the provider:
- `InitializeCoreAsync`: GET `https://fanyi.baidu.com` to extract `token` (from `window.common.token` or `token: '...'` in page JS) and `gtk` (from `window.gtk = '...'`) and cookies
- `TranslateCoreAsync`: POST to `https://fanyi.baidu.com/v2transapi` with form data: `from`, `to`, `query`, `token`, `sign` (computed from gtk + text)
- Sign algorithm: Port Baidu's JS `sign()` function — takes `gtk` string (format: `"320305.131321201"`) and input text, produces a numeric signature. This is a well-documented algorithm, search for "baidu translate sign algorithm" for reference implementations
- Language mapping: `from="ja"` → `"jp"`, `to="zh"` → `"zh"`
- Parse JSON response: `result.trans_result[0].dst`
- Log all HTTP requests/responses at Debug level, sign computation at Debug level
- Rate limit: call `RateLimitDelay()` before each request

- [ ] **Step 2: Build verification**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add XUnityToolkit-WebUI/Services/MachineTranslation/BaiduTranslateProvider.cs
git commit -m "feat(机器翻译): 实现百度翻译提供商"
```

---

### Task 5: Implement Youdao Translate Provider

**Files:**
- Create: `XUnityToolkit-WebUI/Services/MachineTranslation/YoudaoTranslateProvider.cs`

- [ ] **Step 1: Create YoudaoTranslateProvider**

Implement the provider:
- `InitializeCoreAsync`: GET Youdao's JS bundle to extract `secretKey`, `aesKey`, `aesIv` for response decryption, and `client`/`product`/`key` for signing
- `TranslateCoreAsync`: POST to `https://dict.youdao.com/webtranslate` with form data including `sign = MD5(client + text + salt + key)`, `salt` (timestamp + random), `mysticTime` (timestamp)
- Response is AES-128-CBC encrypted; decrypt with extracted `aesKey`/`aesIv`, then parse JSON
- Language mapping: `from="ja"` → `"ja"`, `to="zh"` → `"zh-CHS"`
- Parse decrypted JSON: `translateResult[0][0].tgt`
- Log all HTTP requests/responses at Debug level, sign/decrypt at Debug level
- Rate limit: call `RateLimitDelay()` before each request

- [ ] **Step 2: Build verification**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add XUnityToolkit-WebUI/Services/MachineTranslation/YoudaoTranslateProvider.cs
git commit -m "feat(机器翻译): 实现有道翻译提供商"
```

---

### Task 6: Implement Papago Translate Provider

**Files:**
- Create: `XUnityToolkit-WebUI/Services/MachineTranslation/PapagoTranslateProvider.cs`

- [ ] **Step 1: Create PapagoTranslateProvider**

Implement the provider:
- `InitializeCoreAsync`: GET `https://papago.naver.com` to extract version/auth info; generate device ID (UUID v4); obtain HMAC key from page JS
- `TranslateCoreAsync`: POST to `https://papago.naver.com/apis/n2mt/translate` with JSON body containing `source`, `target`, `text`, `deviceId`, `locale`; set `Authorization` header with HMAC-MD5 signature (device-id + URL + timestamp)
- Language mapping: `from="ja"` → `"ja"`, `to="zh"` → `"zh-CN"`
- Parse JSON response: `translatedText`
- Papago supports longer text, but for consistency use single-text mode same as other providers
- Log all HTTP requests/responses at Debug level
- Rate limit: call `RateLimitDelay()` before each request

- [ ] **Step 2: Build verification**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add XUnityToolkit-WebUI/Services/MachineTranslation/PapagoTranslateProvider.cs
git commit -m "feat(机器翻译): 实现 Papago 翻译提供商"
```

---

## Chunk 3: Backend Service & Endpoints

### Task 7: Create MachineTranslationService

**Files:**
- Create: `XUnityToolkit-WebUI/Services/MachineTranslationService.cs`

- [ ] **Step 1: Create the service**

Constructor DI parameters (register as singleton, same pattern as `LlmTranslationService`):
```csharp
public sealed class MachineTranslationService(
    IHttpClientFactory httpClientFactory,
    AppSettingsService settingsService,
    GlossaryService glossaryService,
    DoNotTranslateService doNotTranslateService,
    IHubContext<InstallProgressHub> hubContext,
    ILogger<MachineTranslationService> logger)
```

Key fields:
```csharp
private bool _enabled;
public bool Enabled { get => Volatile.Read(ref _enabled); set => Volatile.Write(ref _enabled, value); }

private readonly SemaphoreSlim _semaphore = new(1, 1);
private readonly Dictionary<MachineTranslationProvider, IMachineTranslationProvider> _providers = new()
{
    [MachineTranslationProvider.Baidu] = new BaiduTranslateProvider(),
    [MachineTranslationProvider.Youdao] = new YoudaoTranslateProvider(),
    [MachineTranslationProvider.Papago] = new PapagoTranslateProvider(),
};

// Stats counters (same pattern as LlmTranslationService lines 22-62)
private long _totalReceived, _totalTranslated, _totalErrors, _translating, _queued;
private long _totalResponseTimeMs;
private readonly ConcurrentQueue<RecentTranslation> _recentTranslations = new();
private readonly ConcurrentQueue<TranslationError> _recentErrors = new();
private long _lastBroadcastTicks;
```

Key methods:

**`TranslateAsync`** — same signature as `LlmTranslationService`:
```csharp
public async Task<IList<string>> TranslateAsync(
    IList<string> texts, string from, string to,
    string? gameId = null, CancellationToken ct = default)
```

Logic:
1. Check `_enabled`, throw `InvalidOperationException("机器翻译功能已停用")` if not
2. Increment `_totalReceived`, broadcast stats
3. Load DNT entries via `TranslationHelper.LoadDntEntries(gameId, doNotTranslateService, ct)`
4. Load glossary via `TranslationHelper.LoadGlossary(gameId, glossaryService, ct)`
5. Apply DNT replacements via `TranslationHelper.ApplyDntReplacements`
6. Check for pre-computed results (texts that are entirely a single DNT placeholder via `TranslationHelper.FullDntPlaceholderRegex`)
7. Acquire semaphore (120s timeout)
8. Translate texts one-by-one through selected provider (with rate limiting); use `httpClientFactory.CreateClient("MT-{provider}")` for each provider
9. Release semaphore
10. Apply glossary post-process via `TranslationHelper.ApplyGlossaryPostProcess` on each translation
11. Restore DNT placeholders via `TranslationHelper.RestoreDntPlaceholders`
12. Record recent translations (with `TokensUsed = 0`), broadcast stats
13. For large batches (>20 texts), process in chunks of 20: acquire → translate chunk → release → re-acquire
14. **No translation memory** — machine translation results are NOT written to translation memory (per spec: avoids cross-mode quality contamination)

**`RecordError`** — 3 parameters, same signature as `LlmTranslationService` (line 131):
```csharp
public void RecordError(string message, string? endpointName = null, string? gameId = null)
```
Note: `endpointName` stores provider display name (e.g., "百度翻译") for machine translation errors.

**`GetStats`** — returns `TranslationStats` (same structure as `LlmTranslationService.GetStats`):
```csharp
public TranslationStats GetStats()
```

**`GetProviderStatuses`** — returns list of `ProviderStatus` for all providers:
```csharp
public List<ProviderStatus> GetProviderStatuses()
```

**`BroadcastStats`** — same throttled pattern as `LlmTranslationService` (200ms CAS):
```csharp
private async Task BroadcastStats(bool force = false)
```
Broadcasts to `"ai-translation"` group, event `"statsUpdate"`.

**`SelectProvider`** — picks highest-priority healthy enabled provider:
```csharp
private IMachineTranslationProvider? SelectProvider(AppSettingsService settingsService)
```

- [ ] **Step 2: Build verification**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add XUnityToolkit-WebUI/Services/MachineTranslationService.cs
git commit -m "feat(机器翻译): 创建 MachineTranslationService 核心服务"
```

---

### Task 8: Create Machine Translation Endpoints

**Files:**
- Create: `XUnityToolkit-WebUI/Endpoints/MachineTranslateEndpoints.cs`

- [ ] **Step 1: Create the endpoints file**

Follow the pattern from `TranslateEndpoints.cs`. Register as extension method `MapMachineTranslateEndpoints`.

Endpoints:
1. `GET /api/machine-translate/stats` — calls `machineTranslationService.GetStats()`; returns `ApiResult<TranslationStats>`
2. `POST /api/machine-translate/test` — accepts `MachineTranslateTestRequest { Text, From, To, Provider? }`; translates via the specified provider (or highest-priority); returns `ApiResult<MachineTranslateTestResponse { Translation, Provider, ResponseTimeMs }>`
3. `GET /api/machine-translate/providers/status` — calls `machineTranslationService.GetProviderStatuses()`; returns `ApiResult<List<ProviderStatus>>`

Request/response records at the bottom of the file (same pattern as `TranslateEndpoints.cs` lines 147-161).

- [ ] **Step 2: Build verification**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add XUnityToolkit-WebUI/Endpoints/MachineTranslateEndpoints.cs
git commit -m "feat(机器翻译): 添加机器翻译 API 端点"
```

---

### Task 9: Modify TranslateEndpoints for Mode Routing

**Files:**
- Modify: `XUnityToolkit-WebUI/Endpoints/TranslateEndpoints.cs`

- [ ] **Step 1: Update POST /api/translate to route by mode**

At `TranslateEndpoints.cs`, modify the `POST /api/translate` handler (line 11):

1. Add `MachineTranslationService machineTranslationService` parameter to the handler
2. After validation, read `activeMode` from settings:
   ```csharp
   var appSettings = await settingsService.GetAsync();
   var isMachineMode = string.Equals(appSettings.AiTranslation.ActiveMode, "machine", StringComparison.OrdinalIgnoreCase);
   ```
3. Route to the appropriate service:
   ```csharp
   IList<string> translations;
   if (isMachineMode)
       translations = await machineTranslationService.TranslateAsync(request.Texts, request.From ?? "ja", request.To ?? "zh", request.GameId, ct);
   else
       translations = await translationService.TranslateAsync(request.Texts, request.From ?? "ja", request.To ?? "zh", request.GameId, ct);
   ```
4. Update error handling: use the correct service's `RecordError` based on mode
5. Update glossary extraction gating: add `isMachineMode` check alongside `isLocalMode` (line 34):
   ```csharp
   var isLocalMode = string.Equals(appSettings.AiTranslation.ActiveMode, "local", StringComparison.OrdinalIgnoreCase);
   if (!isLocalMode && !isMachineMode)
   {
       // glossary extraction...
   }
   ```
6. Update log messages to be mode-aware

- [ ] **Step 2: Update POST /api/ai/toggle to route by mode**

At `TranslateEndpoints.cs`, modify the `POST /api/ai/toggle` handler (line 86):

1. Add `MachineTranslationService machineTranslationService` parameter
2. Read `activeMode` from settings:
   ```csharp
   var settings = await settingsService.GetAsync();
   var isMachineMode = string.Equals(settings.AiTranslation.ActiveMode, "machine", StringComparison.OrdinalIgnoreCase);
   ```
3. Route toggle — set correct service AND disable the other:
   ```csharp
   if (isMachineMode)
   {
       machineTranslationService.Enabled = request.Enabled;
       translationService.Enabled = false;
   }
   else
   {
       translationService.Enabled = request.Enabled;
       machineTranslationService.Enabled = false;
   }
   ```
4. Persist enabled state (existing logic unchanged)

- [ ] **Step 3: Build verification**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-WebUI/Endpoints/TranslateEndpoints.cs
git commit -m "feat(机器翻译): POST /api/translate 和 toggle 按模式路由"
```

---

### Task 9.5: Add Mode-Switching Logic to SettingsEndpoints

**Files:**
- Modify: `XUnityToolkit-WebUI/Endpoints/SettingsEndpoints.cs`

When `PUT /api/settings` saves new settings, the `ActiveMode` may have changed. The service `Enabled` flags must be updated accordingly.

- [ ] **Step 1: Update PUT /api/settings handler**

At `SettingsEndpoints.cs`, modify the `PUT /api/settings` handler (line 20). Add `LlmTranslationService` and `MachineTranslationService` parameters, then after `settingsService.SaveAsync(settings)`:

```csharp
// Sync service Enabled flags based on active mode
var isMachineMode = string.Equals(settings.AiTranslation.ActiveMode, "machine", StringComparison.OrdinalIgnoreCase);
if (isMachineMode)
{
    machineTranslationService.Enabled = settings.AiTranslation.Enabled;
    translationService.Enabled = false;
}
else
{
    translationService.Enabled = settings.AiTranslation.Enabled;
    machineTranslationService.Enabled = false;
}
```

- [ ] **Step 2: Build verification**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add XUnityToolkit-WebUI/Endpoints/SettingsEndpoints.cs
git commit -m "feat(机器翻译): PUT /api/settings 同步模式切换的 Enabled 状态"
```

---

### Task 10: Register Services and Endpoints in Program.cs

**Files:**
- Modify: `XUnityToolkit-WebUI/Program.cs`

- [ ] **Step 1: Register named HttpClients for each provider**

Each provider needs its own named HttpClient with its own `CookieContainer` to avoid cross-provider cookie contamination. After existing named HttpClient registrations (around line 121), add:

```csharp
// Machine translation providers — each needs its own CookieContainer
foreach (var name in new[] { "MT-Baidu", "MT-Youdao", "MT-Papago" })
{
    builder.Services.AddHttpClient(name, client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
    }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        UseCookies = true
    });
}
```

Each provider uses `httpClientFactory.CreateClient("MT-Baidu")` etc. to get its own client with isolated cookies.

- [ ] **Step 2: Register MachineTranslationService**

After `LlmTranslationService` registration (line 142):

```csharp
builder.Services.AddSingleton<MachineTranslationService>();
```

- [ ] **Step 3: Map endpoints**

After `app.MapTranslateEndpoints()` (around line 243):

```csharp
app.MapMachineTranslateEndpoints();
```

- [ ] **Step 4: Set initial Enabled state**

After existing `LlmTranslationService` Enabled initialization (line 277):

```csharp
var machineTranslationService = app.Services.GetRequiredService<MachineTranslationService>();
if (string.Equals(settings.AiTranslation.ActiveMode, "machine", StringComparison.OrdinalIgnoreCase))
{
    machineTranslationService.Enabled = settings.AiTranslation.Enabled;
    app.Services.GetRequiredService<LlmTranslationService>().Enabled = false;
}
```

- [ ] **Step 5: Build verification**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add XUnityToolkit-WebUI/Program.cs
git commit -m "feat(机器翻译): 注册服务、HttpClient 和端点"
```

---

## Chunk 3.5: Backend Verification

### Task 11: Full Backend Build & Manual Test

- [ ] **Step 1: Full build (including frontend)**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj`
Expected: Build succeeded

- [ ] **Step 2: Manual test (if backend is runnable)**

Start backend, then verify:
1. `GET /api/machine-translate/stats` returns empty stats
2. `GET /api/machine-translate/providers/status` returns 3 providers
3. `PUT /api/settings` with `aiTranslation.activeMode = "machine"` persists correctly
4. `POST /api/ai/toggle` with `enabled: true` when in machine mode toggles `MachineTranslationService`

---

## Chunk 4: Frontend Types, API, Store

### Task 12: Add Frontend Types

**Files:**
- Modify: `XUnityToolkit-Vue/src/api/types.ts`

- [ ] **Step 1: Add machine translation types**

After existing `DoNotTranslateEntry` (line 307), add:

```typescript
// Machine Translation
export type MachineTranslationProvider = 'Baidu' | 'Youdao' | 'Papago'

export interface MachineProviderConfig {
  id: string
  provider: MachineTranslationProvider
  enabled: boolean
  priority: number
}

export interface MachineTranslationSettings {
  providers: MachineProviderConfig[]
}

export interface ProviderStatus {
  provider: MachineTranslationProvider
  name: string
  healthy: boolean
  inCooldown: boolean
  lastError?: string
  lastErrorTime?: string
}

export interface MachineTranslateTestRequest {
  text: string
  from: string
  to: string
  provider?: MachineTranslationProvider
}

export interface MachineTranslateTestResponse {
  translation: string
  provider: string
  responseTimeMs: number
}
```

- [ ] **Step 2: Extend AppSettings interface**

At `types.ts` line 174, add `machineTranslation` to `AppSettings`:

```typescript
export interface AppSettings {
  // ... existing fields ...
  machineTranslation: MachineTranslationSettings
}
```

- [ ] **Step 3: Extend activeMode type**

At `types.ts` line 160, change:
```typescript
// Before:
activeMode: 'cloud' | 'local'
// After:
activeMode: 'cloud' | 'local' | 'machine'
```

- [ ] **Step 4: Type-check**

Run: `cd XUnityToolkit-Vue && npx vue-tsc --noEmit`
Expected: Type errors in SettingsView.vue and AiTranslationView.vue (missing `machineTranslation` in defaults) — expected and will be fixed in later tasks.

- [ ] **Step 5: Commit**

```bash
git add XUnityToolkit-Vue/src/api/types.ts
git commit -m "feat(机器翻译): 添加前端类型定义"
```

---

### Task 13: Add API Client

**Files:**
- Modify: `XUnityToolkit-Vue/src/api/games.ts`

- [ ] **Step 1: Add machineTranslateApi**

After the existing `translateApi` object (around line 201), add:

```typescript
export const machineTranslateApi = {
  getStats: () => api.get<TranslationStats>('/api/machine-translate/stats'),
  testTranslate: (request: MachineTranslateTestRequest) =>
    api.post<MachineTranslateTestResponse>('/api/machine-translate/test', request),
  getProviderStatus: () => api.get<ProviderStatus[]>('/api/machine-translate/providers/status'),
}
```

Add the necessary imports at the top of the file:
```typescript
import type { MachineTranslateTestRequest, MachineTranslateTestResponse, ProviderStatus } from './types'
```

- [ ] **Step 2: Commit**

```bash
git add XUnityToolkit-Vue/src/api/games.ts
git commit -m "feat(机器翻译): 添加机器翻译 API 客户端"
```

---

### Task 14: Create Machine Translation Pinia Store

**Files:**
- Create: `XUnityToolkit-Vue/src/stores/machineTranslation.ts`

- [ ] **Step 1: Create the store**

Follow the exact pattern from `src/stores/aiTranslation.ts`:

```typescript
import { ref } from 'vue'
import { defineStore } from 'pinia'
import * as signalR from '@microsoft/signalr'
import type { TranslationStats, ProviderStatus } from '@/api/types'
import { machineTranslateApi, translateApi } from '@/api/games'

export const useMachineTranslationStore = defineStore('machineTranslation', () => {
  const stats = ref<TranslationStats | null>(null)
  const providerStatus = ref<ProviderStatus[]>([])
  let connection: signalR.HubConnection | null = null

  async function connect() {
    if (connection) return
    connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/install')
      .withAutomaticReconnect()
      .build()

    connection.on('statsUpdate', (data: TranslationStats) => {
      stats.value = data
    })

    await connection.start()
    await connection.invoke('JoinAiTranslationGroup')
  }

  async function disconnect() {
    if (!connection) return
    try {
      await connection.invoke('LeaveAiTranslationGroup')
      await connection.stop()
    } catch { /* ignore */ }
    connection = null
  }

  async function fetchStats() {
    const result = await machineTranslateApi.getStats()
    if (result.data) stats.value = result.data
  }

  async function fetchProviderStatus() {
    const result = await machineTranslateApi.getProviderStatus()
    if (result.data) providerStatus.value = result.data
  }

  async function toggleEnabled(enabled: boolean) {
    await translateApi.toggle(enabled)
    if (stats.value) {
      stats.value = { ...stats.value, enabled }
    }
  }

  return { stats, providerStatus, connect, disconnect, fetchStats, fetchProviderStatus, toggleEnabled }
})
```

- [ ] **Step 2: Commit**

```bash
git add XUnityToolkit-Vue/src/stores/machineTranslation.ts
git commit -m "feat(机器翻译): 创建 Pinia store"
```

---

## Chunk 5: Frontend View & Navigation

### Task 15: Create MachineTranslationView

**Files:**
- Create: `XUnityToolkit-Vue/src/views/MachineTranslationView.vue`

- [ ] **Step 1: Create the view component**

Follow the structure pattern from `AiTranslationView.vue`. Key sections:

```vue
<script setup lang="ts">
defineOptions({ name: 'MachineTranslationView' })
// Imports, store setup, settings loading (same pattern as AiTranslationView)
</script>
```

**Script setup requirements:**
- `defineOptions({ name: 'MachineTranslationView' })` — required for KeepAlive
- Use `useMachineTranslationStore()` for stats and provider status
- Use `useGamesStore()` for game name resolution (same as AI translation)
- Load `AppSettings` via `settingsApi.get()` with `useAutoSave` (1s debounce)
- Default `machineTranslation` settings inline (same pattern as `DEFAULT_AI_TRANSLATION` in AiTranslationView)
- `handleToggle`: set `aiSettings.activeMode = 'machine'` + `aiSettings.enabled = enabled`, call `store.toggleEnabled(enabled)`
- Provider priority reorder: up/down functions that swap priorities and trigger auto-save
- `onMounted`: connect store, fetch stats, fetch provider status, load settings
- `onUnmounted`: disconnect store

**Template sections (top to bottom):**
1. Title row: "机器翻译" + NSwitch toggle + experimental badge
2. Live metrics strip: 4 pills (total received, success rate, avg speed, active provider) — reuse CSS classes from AiTranslationView
3. Translation status card: pipeline visualization (queued → translating → translated) — simplified version of AiTranslationView's status card
4. Provider cards: `.provider-cards` grid with one card per provider showing:
   - Provider name + health indicator (NTag: green/red/yellow)
   - Priority arrows (NButton up/down)
   - NSwitch enable/disable
   - Last error (if any)
5. Test translation card: NInput for text, NSelect for provider, NButton to translate, result display
6. Recent translations: same card layout as AiTranslationView (NDataTable or card list)
7. Error log: same card layout as AiTranslationView

**Style:** Follow existing `.page-container`, `.section-card` patterns from AiTranslationView. Use scoped styles.

- [ ] **Step 2: Commit**

```bash
git add XUnityToolkit-Vue/src/views/MachineTranslationView.vue
git commit -m "feat(机器翻译): 创建机器翻译页面视图"
```

---

### Task 16: Update Navigation, Routing, and KeepAlive

**Files:**
- Modify: `XUnityToolkit-Vue/src/router/index.ts`
- Modify: `XUnityToolkit-Vue/src/components/layout/AppShell.vue`

- [ ] **Step 1: Add route**

In `router/index.ts`, after the `/ai-translation` route (around line 52), add:

```typescript
{
  path: '/machine-translation',
  name: 'machine-translation',
  component: () => import('@/views/MachineTranslationView.vue'),
  meta: { depth: 1 },
},
```

- [ ] **Step 2: Add nav item in AppShell.vue**

In `AppShell.vue`, update `navItems` array (line 35-41). Insert after "AI 翻译" entry:

```typescript
{ label: '机器翻译', key: '/machine-translation', icon: GTranslateOutlined },
```

Add import at the top (line 5):
```typescript
import { GamepadFilled, SettingsOutlined, SmartToyOutlined, ArticleOutlined, FontDownloadOutlined, GTranslateOutlined } from '@vicons/material'
```

If `GTranslateOutlined` is not available in `@vicons/material`, use `TranslateOutlined` instead and add it to the import.

- [ ] **Step 3: Update cachedPages**

In `AppShell.vue` line 33, add `'MachineTranslationView'`:

```typescript
const cachedPages = ['LibraryView', 'AiTranslationView', 'MachineTranslationView', 'FontGeneratorView', 'LogView', 'SettingsView']
```

- [ ] **Step 4: Add CSS animation delay for new nav item**

In `AppShell.vue` styles (around line 259-262), add delays for the new 6th item. Since machine translation is the 3rd item now, update all delays:

Now 6 nav items (was 5). Update existing 4 rules and add 2 more:

```css
.nav-item:nth-child(1) { animation-delay: 0.12s; }
.nav-item:nth-child(2) { animation-delay: 0.18s; }
.nav-item:nth-child(3) { animation-delay: 0.24s; }
.nav-item:nth-child(4) { animation-delay: 0.30s; }
.nav-item:nth-child(5) { animation-delay: 0.36s; }
.nav-item:nth-child(6) { animation-delay: 0.42s; }
```

- [ ] **Step 5: Commit**

```bash
git add XUnityToolkit-Vue/src/router/index.ts XUnityToolkit-Vue/src/components/layout/AppShell.vue
git commit -m "feat(机器翻译): 添加路由和导航栏项"
```

---

### Task 17: Update AiTranslationView Mode Indicator

**Files:**
- Modify: `XUnityToolkit-Vue/src/views/AiTranslationView.vue`

- [ ] **Step 1: Add machine mode indicator**

When `activeMode === 'machine'`, show a notice banner at the top of the page (after the title row) indicating the user is in machine translation mode:

```vue
<div v-if="activeMode === 'machine'" class="mode-notice">
  <n-alert type="info" :bordered="false">
    当前已切换至机器翻译模式。
    <router-link to="/machine-translation">前往机器翻译页面</router-link>
  </n-alert>
</div>
```

- [ ] **Step 2: Update isLocalMode to also handle machine mode**

At line 130, the glossary extraction section uses `isLocalMode`. Add a new computed:

```typescript
const isMachineMode = computed(() => activeMode.value === 'machine')
```

Update glossary extraction visibility (line 479) to also hide when machine mode:
```vue
v-if="settings && !isLocalMode && !isMachineMode"
```

- [ ] **Step 3: Commit**

```bash
git add XUnityToolkit-Vue/src/views/AiTranslationView.vue
git commit -m "feat(机器翻译): AI翻译页面添加机器翻译模式指示器"
```

---

### Task 18: Update SettingsView Defaults

**Files:**
- Modify: `XUnityToolkit-Vue/src/views/SettingsView.vue`

- [ ] **Step 1: Add machineTranslation to settings defaults**

In `SettingsView.vue`, at the `settings` ref initialization (around line 56-81), add the `machineTranslation` field with defaults:

```typescript
machineTranslation: {
  providers: [
    { id: crypto.randomUUID(), provider: 'Baidu' as const, enabled: true, priority: 1 },
    { id: crypto.randomUUID(), provider: 'Youdao' as const, enabled: true, priority: 2 },
    { id: crypto.randomUUID(), provider: 'Papago' as const, enabled: true, priority: 3 },
  ],
},
```

This ensures settings reset creates proper defaults for the new field.

- [ ] **Step 2: Type-check**

Run: `cd XUnityToolkit-Vue && npx vue-tsc --noEmit`
Expected: No type errors

- [ ] **Step 3: Commit**

```bash
git add XUnityToolkit-Vue/src/views/SettingsView.vue
git commit -m "feat(机器翻译): SettingsView 添加机器翻译默认设置"
```

---

## Chunk 6: Integration & CLAUDE.md Updates

### Task 19: Update CLAUDE.md Sync Points

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Add sync points**

In the Sync Points section, add:

```markdown
- **Adding MachineTranslationSettings fields:** Sync 4 places: `Models/MachineTranslationSettings.cs`, `src/api/types.ts`, `MachineTranslationView.vue`, `SettingsView.vue`
- **Adding MachineTranslationProvider enum values:** Sync 2 places: `Models/MachineTranslationSettings.cs`, `src/api/types.ts`
- **ActiveMode values (`cloud`/`local`/`machine`):** Sync 5 places: `AiTranslationSettings.cs`, `types.ts`, `AiTranslationView.vue`, `MachineTranslationView.vue`, `TranslateEndpoints.cs`
- **ProviderStatus model:** Sync 3 places: `MachineTranslationService.cs`, `types.ts`, `MachineTranslationView.vue`
```

- [ ] **Step 2: Add Machine Translation to Architecture section**

Add to the Architecture bullet list:

```markdown
- **Machine Translation:** `MachineTranslationService` dispatches to reverse-engineered web translation providers (Baidu/Youdao/Papago); `SemaphoreSlim(1,1)` serializes requests due to provider rate limits; priority-based provider selection with automatic failover (60s cooldown); `TranslationHelper` shares DNT/glossary logic with `LlmTranslationService`; experimental feature — provider APIs may break
```

- [ ] **Step 3: Add Machine Translation API endpoints**

Add to API Endpoints section:

```markdown
- **Machine Translation:** `GET /api/machine-translate/stats`, `POST .../test`, `GET .../providers/status`
```

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: 更新 CLAUDE.md 添加机器翻译同步点和架构说明"
```

---

### Task 20: Full Build & Integration Verification

- [ ] **Step 1: Full build**

```bash
cd XUnityToolkit-Vue && npm run build
cd .. && dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj
```

- [ ] **Step 2: Type-check frontend**

```bash
cd XUnityToolkit-Vue && npx vue-tsc --noEmit
```

- [ ] **Step 3: Manual integration test**

Start backend (`dotnet run --project XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj`), verify:
1. Machine translation page loads at `/machine-translation`
2. Navigation sidebar shows the new item below AI Translation
3. Enable toggle sets mode to `machine` and enables translation
4. Provider cards show 3 providers with health status
5. Test translation works with at least one provider
6. Stats update in real-time via SignalR
7. Switching back to AI Translation page shows mode indicator
8. Settings persist across app restart

- [ ] **Step 4: Final commit (if any fixes needed)**

Stage only the specific files that were fixed (do not use `git add -A`), then commit:

```bash
git commit -m "fix(机器翻译): 修复集成测试发现的问题"
```
