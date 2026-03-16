# Machine Translation Feature Design

## Overview

Add a free machine translation feature that uses reverse-engineered web translation APIs (Baidu, Youdao, Papago) as a third translation mode alongside cloud AI and local LLM. The feature targets free users who cannot afford API keys, providing zero-configuration translation for Japanese games.

## Motivation

Most built-in machine translation endpoints in XUnity.AutoTranslator require API keys, which creates a barrier for free users. Major translation providers (Baidu, Youdao, Papago) offer free web-based translation. By reverse-engineering their web APIs, we can provide free machine translation without any user configuration.

## Architecture

### Three-Mode Coexistence

`AiTranslation.ActiveMode` extends from `"cloud" | "local"` to `"cloud" | "local" | "machine"`. The DLL (`LLMTranslate.dll`) continues calling `POST /api/translate` unchanged. The backend routes based on active mode:

```
POST /api/translate
  → read settings.AiTranslation.ActiveMode
    → "cloud"   → LlmTranslationService.TranslateAsync()
    → "local"   → LlmTranslationService.TranslateAsync()
    → "machine" → MachineTranslationService.TranslateAsync()
```

Only one mode is active at a time. The enable/disable toggle (`POST /api/ai/toggle`) controls whichever mode is currently active.

### Backend Service: `MachineTranslationService`

Independent service parallel to `LlmTranslationService`. Core responsibilities:

- `TranslateAsync(texts, from, to, gameId)` — main translation method (same signature as LLM service)
- Provider dispatch with priority-based selection and automatic failover
- DNT placeholder substitution/restoration (shared utility methods)
- Glossary post-processing (shared utility methods)
- Statistics tracking using existing `TranslationStats` structure
- SignalR broadcast via `IHubContext<InstallProgressHub>` to `"ai-translation"` group

### Provider Failover Logic

```
1. Sort enabled providers by Priority (ascending)
2. Try highest-priority provider
3. On failure → mark provider "unhealthy" (60s cooldown)
4. Auto-switch to next available provider
5. All providers unavailable → return error
6. After cooldown expires, automatically retry previously failed provider
```

### Shared Utility Methods

Extract from `LlmTranslationService` into `TranslationHelper` static class:

- `ApplyDntPlaceholders(texts, dntEntries)` → `(processedTexts, restoreMap)`
- `RestoreDntPlaceholders(translations, restoreMap)`
- `ApplyGlossaryPostProcess(translations, glossary)`
- `LoadGameContext(gameId, services)` → `(glossary, dntEntries)`

Both `LlmTranslationService` and `MachineTranslationService` call these shared methods.

## Provider Implementations

### Interface

```csharp
interface IMachineTranslationProvider
{
    string Name { get; }                    // Display name: "百度翻译"
    MachineTranslationProvider Provider { get; }
    bool IsHealthy { get; }
    Task InitializeAsync();                 // Acquire token/cookie
    Task<List<string>> TranslateAsync(List<string> texts, string from, string to);
}
```

Each provider is an independent class implementing this interface.

### Baidu Translate (`BaiduTranslateProvider`)

- **Endpoint:** `https://fanyi.baidu.com/v2transapi`
- **Initialization:** Request `https://fanyi.baidu.com` to obtain cookie (`BAIDUID`) and page token (from embedded JS `window.gtk` and `token` variables)
- **Signature:** Custom algorithm based on `gtk` + input text (port from Baidu's JS `sign()` function)
- **Rate limit:** 1 request/second with random jitter (50-200ms)
- **Batch handling:** Single text per request; serialize for batches
- **Language codes:** `jp` → `zh`

### Youdao Translate (`YoudaoTranslateProvider`)

- **Endpoint:** `https://dict.youdao.com/webtranslate`
- **Initialization:** Extract `secretKey` and `aesKey`/`aesIv` from Youdao's JS bundle for response decryption
- **Signature:** `sign = MD5(client + text + salt + key)` with parameters derived from page JS
- **Rate limit:** 1 request/second with random jitter
- **Batch handling:** Single text per request
- **Response:** AES-encrypted JSON, decrypt with extracted keys
- **Language codes:** `ja` → `zh-CHS`

### Papago Translate (`PapagoTranslateProvider`)

- **Endpoint:** `https://papago.naver.com/apis/n2mt/translate`
- **Initialization:** Generate device ID (UUID), obtain auth token from Papago page
- **Signature:** HMAC-based authorization header with device ID and timestamp
- **Rate limit:** 1 request/second with random jitter
- **Batch handling:** Supports longer text; concatenate multiple texts with newline, split results after translation
- **Language codes:** `ja` → `zh-CN`

### Debug Logging

Each provider logs extensively for troubleshooting when APIs break:

- **Debug level:** Every HTTP request/response body, signature computation inputs/outputs, token values
- **Information level:** Provider initialization success, token refresh, failover switches
- **Warning level:** Single request failures, entering cooldown period
- **Error level:** All providers unavailable, initialization failure

## Data Models

### Backend Models

**`Models/MachineTranslationSettings.cs`:**

```csharp
enum MachineTranslationProvider { Baidu, Youdao, Papago }

class MachineProviderConfig
{
    string Id { get; set; }          // GUID
    MachineTranslationProvider Provider { get; set; }
    bool Enabled { get; set; }
    int Priority { get; set; }       // Lower = higher priority
}

class MachineTranslationSettings
{
    List<MachineProviderConfig> Providers { get; set; }
    // Default: all three enabled, priority Baidu=1, Youdao=2, Papago=3
}
```

**Statistics:** Reuse existing `TranslationStats`, `RecentTranslation`, `TranslationError` from `Models/TranslationStats.cs`. `RecentTranslation.EndpointName` stores provider display name (e.g., "百度翻译"). `TokensUsed` = 0 for machine translation.

**Provider health status (runtime only, not persisted):**

```csharp
class ProviderStatus
{
    MachineTranslationProvider Provider { get; set; }
    string Name { get; set; }        // Display name
    bool Healthy { get; set; }
    bool InCooldown { get; set; }
    string? LastError { get; set; }
    DateTime? LastErrorTime { get; set; }
}
```

### Frontend Types

```typescript
type MachineTranslationProvider = 'Baidu' | 'Youdao' | 'Papago'

interface MachineProviderConfig {
  id: string
  provider: MachineTranslationProvider
  enabled: boolean
  priority: number
}

interface MachineTranslationSettings {
  providers: MachineProviderConfig[]
}

interface ProviderStatus {
  provider: MachineTranslationProvider
  name: string
  healthy: boolean
  inCooldown: boolean
  lastError?: string
  lastErrorTime?: string
}
```

`AppSettings.aiTranslation.activeMode` type extends to `'cloud' | 'local' | 'machine'`.

`AppSettings.machineTranslation: MachineTranslationSettings` added as new top-level field.

## API Endpoints

### New Endpoints

| Endpoint | Method | Purpose |
|---|---|---|
| `/api/machine-translate/stats` | GET | Get machine translation statistics |
| `/api/machine-translate/test` | POST | Test translation with specific provider |
| `/api/machine-translate/providers/status` | GET | Get all provider health statuses |

### Modified Endpoints

| Endpoint | Change |
|---|---|
| `POST /api/translate` | Route to `MachineTranslationService` when `activeMode = "machine"` |
| `PUT /api/settings` | Persist `machineTranslation` settings |

### Test Translation Request/Response

```
POST /api/machine-translate/test
Request:  { text: string, from: string, to: string, provider?: MachineTranslationProvider }
Response: { translation: string, provider: string, responseTimeMs: number }
```

If `provider` is omitted, uses the highest-priority enabled provider.

## Frontend

### Navigation

Add to `AppShell.vue` `navItems` array, after AI Translation:

```typescript
{ label: '机器翻译', key: '/machine-translation', icon: TranslateOutlined }
```

Add `'MachineTranslationView'` to `cachedPages`. Add route at depth 1.

### View: `MachineTranslationView.vue`

Page layout (top to bottom):

1. **Title row** — "机器翻译" heading + enable/disable toggle switch
   - Enabling sets `activeMode = 'machine'` and `enabled = true`
   - Disabling sets `enabled = false`
2. **Live metrics strip** — 4 pills: total received, success rate, avg speed, current provider
3. **Translation status card** — pipeline visualization (queued → translating → translated), current game indicator, error bar
4. **Provider cards** — one card per provider showing:
   - Name + icon
   - Health indicator (green=healthy, red=unhealthy, yellow=cooldown)
   - Priority (up/down arrows to reorder)
   - Enable/disable switch
   - Last error message (if any)
5. **Test translation card** — input text, provider selector, translate button, result display with response time
6. **Recent translations** — reuse AI translation page's display style
7. **Error log** — reuse AI translation page's display style

### Store: `stores/machineTranslation.ts`

```typescript
// State
stats: ref<TranslationStats | null>
providerStatus: ref<ProviderStatus[]>

// Actions
connect()              // Join "ai-translation" SignalR group, listen for statsUpdate
disconnect()           // Leave group
fetchStats()           // GET /api/machine-translate/stats
fetchProviderStatus()  // GET /api/machine-translate/providers/status
toggleEnabled(enabled) // POST /api/ai/toggle
```

### Settings Integration

Machine translation settings saved via `AppSettings` auto-save (same pattern as AI translation). Provider list and priorities persist in `settings.json`.

## Integration with Existing Features

### DNT (Do-Not-Translate)

- **Enabled** in machine translation mode
- DNT words replaced with `{{DNT_x}}` placeholders before sending to provider
- Restored after translation result received
- Processing order same as AI translation: glossary restore → glossary post-process → DNT restore

### Glossary

- **Placeholder substitution (`{{G_x}}`) — NOT used** (machine translation cannot follow placeholder instructions)
- **Post-processing — Enabled** (`ApplyGlossaryPostProcess` runs on results)
- **Extraction — NOT used** (machine translation has no extraction capability)

### Translation Memory

- Machine translation results **are written** to translation memory
- Machine translation **does not use** translation memory as context input

### Mode Switching UI Linkage

- AI Translation page and Machine Translation page both read `AppSettings.aiTranslation.activeMode`
- When machine translation is enabled, AI Translation page shows "已切换至机器翻译模式" indicator
- When AI Translation switches to cloud/local, Machine Translation page toggle reflects disabled state
- Both pages use the same `enabled` field in `AiTranslationSettings`

## SignalR

Reuse existing `"ai-translation"` group and `statsUpdate` event. Machine translation and AI translation are never active simultaneously, so there is no conflict. No new hub groups needed.

The `InstallProgressHub` requires no changes — `MachineTranslationService` broadcasts through `IHubContext<InstallProgressHub>` directly (same pattern as `LlmTranslationService`).

## Sync Points (to add to CLAUDE.md)

| Sync Item | Files |
|---|---|
| `ActiveMode` values | `AiTranslationSettings.cs`, `types.ts`, `AiTranslationView.vue`, `MachineTranslationView.vue`, `TranslateEndpoints.cs` |
| `MachineTranslationSettings` model | `Models/MachineTranslationSettings.cs`, `types.ts`, `MachineTranslationView.vue` |
| `MachineTranslationProvider` enum | `Models/MachineTranslationSettings.cs`, `types.ts` |
| `ProviderStatus` model | `MachineTranslationService.cs`, `types.ts`, `MachineTranslationView.vue` |
| Provider display names | `MachineTranslationService.cs` (backend), `MachineTranslationView.vue` (frontend mapping) |

## Files to Create

| File | Purpose |
|---|---|
| `Models/MachineTranslationSettings.cs` | Settings and provider config models |
| `Services/MachineTranslationService.cs` | Core service with stats, failover, SignalR |
| `Services/TranslationHelper.cs` | Shared DNT/glossary utility methods |
| `Services/MachineTranslation/IMachineTranslationProvider.cs` | Provider interface |
| `Services/MachineTranslation/BaiduTranslateProvider.cs` | Baidu implementation |
| `Services/MachineTranslation/YoudaoTranslateProvider.cs` | Youdao implementation |
| `Services/MachineTranslation/PapagoTranslateProvider.cs` | Papago implementation |
| `Endpoints/MachineTranslateEndpoints.cs` | New API endpoints |
| `XUnityToolkit-Vue/src/views/MachineTranslationView.vue` | Frontend page |
| `XUnityToolkit-Vue/src/stores/machineTranslation.ts` | Pinia store |

## Files to Modify

| File | Change |
|---|---|
| `Endpoints/TranslateEndpoints.cs` | Route `POST /api/translate` based on activeMode |
| `Services/LlmTranslationService.cs` | Extract DNT/glossary logic to `TranslationHelper` |
| `Models/AppSettings.cs` | Add `MachineTranslation` property |
| `Program.cs` | Register new services and endpoints |
| `XUnityToolkit-Vue/src/api/types.ts` | Add new types, extend ActiveMode |
| `XUnityToolkit-Vue/src/api/games.ts` | Add machine translation API client |
| `XUnityToolkit-Vue/src/router/index.ts` | Add route |
| `XUnityToolkit-Vue/src/components/layout/AppShell.vue` | Add nav item |
| `XUnityToolkit-Vue/src/views/AiTranslationView.vue` | Show mode indicator when machine mode active |
