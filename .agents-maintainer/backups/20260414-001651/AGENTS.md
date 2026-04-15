# AGENTS.md

<!-- agents-maintainer:auto:start -->
## Scope
- Applies to the repository root unless a closer AGENTS.md file overrides it.
- Use this file for the minimum repo-wide context every new session should read first.

## Project Snapshot
- Primary toolchain: dotnet (aspnet, winforms, xunit).
- Install dependencies with `dotnet restore UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj` when relevant.

## Repo Shape
- High-signal paths for common work: `UnityLocalizationToolkit-WebUI`, `UnityLocalizationToolkit-WebUI.Tests`, `TranslatorEndpoint`, `Updater`, `.github`.

## Quick Rules
- Start locally: `dotnet run --project UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj`
- Build: `dotnet build UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj` or `dotnet publish UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj`
- Test: `dotnet test UnityLocalizationToolkit-WebUI.Tests/UnityLocalizationToolkit-WebUI.Tests.csproj`
- Keep root concise: store repo-wide essentials here, and move detailed topic guidance into AGENTS.<topic>.md sidecars.

## Instruction Map
- Read `AGENTS.frontend.md` when touching web ui, client code, components, styling, or javascript package workflows.
- Read `AGENTS.backend.md` when touching apis, services, desktop host code, runtime orchestration, or server-side logic.
- Read `AGENTS.build.md` when touching build scripts, packaging, ci, code generation, or dependency installation workflow.
- Read `AGENTS.data.md` when touching persisted data layout, config files, caches, schemas, models, or migration-sensitive paths.
- Read `AGENTS.ops.md` when touching runtime setup, ports, logging, deployment, external services, or operational safeguards.
- Read `AGENTS.notes.md` when needing extra durable project notes that do not fit a more specific topic.

## Verification
- Run `dotnet test UnityLocalizationToolkit-WebUI.Tests/UnityLocalizationToolkit-WebUI.Tests.csproj` before considering the task done when relevant.
- Run `dotnet build UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj` before considering the task done when relevant.
- Run `dotnet publish UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj` before considering the task done when relevant.
<!-- agents-maintainer:auto:end -->

## Manual Notes
<!-- agents-maintainer:manual:start -->
- 根文件只保留索引级说明，不再恢复成大而全手册。
- 详细规则按主题拆到 `AGENTS.<topic>.md` sidecar，避免新会话一次性加载过多上下文。
- 触达前端时读 `AGENTS.frontend.md`。
- 触达后端时读 `AGENTS.backend.md`。
- 触达构建、发布或 CI 时读 `AGENTS.build.md`。
- 触达运行时数据、模型或配置时读 `AGENTS.data.md`。
- 触达运行、端口、日志或安全兜底时读 `AGENTS.ops.md`。
- 其他长期有效但不适合放进上面主题的说明，放在 `AGENTS.notes.md`。
<!-- agents-maintainer:manual:end -->
