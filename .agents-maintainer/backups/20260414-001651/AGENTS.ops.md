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
- 开发和运行时监听地址固定为 `127.0.0.1`，默认端口 `51821`。端口来自 `settings.json` 的 `aiTranslation.port`。
- 完整 UI 预览优先访问后端端口 `http://127.0.0.1:51821`，因为它同时承载静态前端和 API；如果开发态 `wwwroot` 缺失导致首页不可用，就用 Vite dev server 做界面验证。
- 启动日志应记录 `CurrentDirectory`、`BaseDirectory`、`ContentRoot`、`WebRoot` 和 `wwwroot/index.html` 是否存在；入口文件缺失时应打 `Critical`。
- `ApplicationStarted` 会异步初始化 AI 翻译状态并自动检查更新；`ApplicationStopping` 会隐藏 UI 并刷新翻译记忆。改启动或关闭流程时不要破坏这些收尾动作。
- 外部 URL、ZIP 解包和文件导入都属于高风险入口。涉及这些路径时优先复用现有安全辅助方法，不要手写绕过逻辑。
- 本地 LLM 进程启动路径必须走 `LocalLlmLaunchPathResolver`，按“相对路径 -> 8.3 短路径 -> `llama/launch-cache/` 别名”兜底，不要直接绕过这条链路。
<!-- agents-maintainer:manual:end -->
