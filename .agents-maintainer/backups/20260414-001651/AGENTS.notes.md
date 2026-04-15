# AGENTS.notes.md

<!-- agents-maintainer:auto:start -->
## Read This When
- Needing extra durable project notes that do not fit a more specific topic.

## Relevant Paths
- Confirm the owning paths from the repository layout before adding new notes here.

## Commands / Constraints
- Relevant command: `dotnet run --project UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj`
- Relevant command: `dotnet build UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj`
- Relevant command: `dotnet publish UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj`
- Relevant command: `dotnet test UnityLocalizationToolkit-WebUI.Tests/UnityLocalizationToolkit-WebUI.Tests.csproj`
- Keep notes specific to this topic and move path-local rules into closer directory AGENTS.md files when possible.

## Verification
- Run `dotnet run --project UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj` when it is part of the touched workflow.
- Run `dotnet build UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj` when it is part of the touched workflow.
- Run `dotnet publish UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj` when it is part of the touched workflow.
<!-- agents-maintainer:auto:end -->

## Manual Notes
<!-- agents-maintainer:manual:start -->
- 这是一个面向 Unity 游戏汉化 / 翻译工作流的 Windows 桌面工具，核心能力包括：XUnity 自动翻译接入、云端与本地 LLM、资产提取与预翻译、字体替换 / 生成、插件与日志工具。
- 主要子项目：
  - `UnityLocalizationToolkit-WebUI/`：ASP.NET Core Minimal API + WinForms/WebView2 宿主
  - `UnityLocalizationToolkit-Vue/`：Vue 3 前端
  - `UnityLocalizationToolkit-WebUI.Tests/`：xUnit 后端测试
  - `TranslatorEndpoint/`：`LLMTranslate.dll`
  - `Updater/`：AOT 更新器
  - `Installer/`：WiX 安装器
- 仓库当前只维护根 `AGENTS.md` 和根级 topic sidecar，不再维护历史 `CLAUDE.md` 入口。
- Git 协作文案统一用中文。提交标题默认格式为 `type: 中文摘要`，`type` 只用仓库现有的小写英文类型：`feat`、`fix`、`docs`、`refactor`、`perf`、`ci`、`chore`、`style`、`test`。
- 游戏详情页里 `XUnity` 和 `手动翻译` 是并列工作流。任何新增状态、汇总或删除逻辑都要确认这两套数据不会互相覆盖。
<!-- agents-maintainer:manual:end -->
