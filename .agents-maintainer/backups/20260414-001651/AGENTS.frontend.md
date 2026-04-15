# AGENTS.frontend.md

<!-- agents-maintainer:auto:start -->
## Read This When
- Touching web UI, client code, components, styling, or JavaScript package workflows.

## Relevant Paths
- `UnityLocalizationToolkit-WebUI`
- `UnityLocalizationToolkit-WebUI.Tests`

## Commands / Constraints
- Keep notes here focused on durable constraints, not general repo summaries.
- Keep notes specific to this topic and move path-local rules into closer directory AGENTS.md files when possible.

## Verification
- Run the topic-specific checks that match the affected files.
<!-- agents-maintainer:auto:end -->

## Manual Notes
<!-- agents-maintainer:manual:start -->
- 前端主目录是 `UnityLocalizationToolkit-Vue`，技术栈为 `Vue 3 + TypeScript + Naive UI + Pinia + Vite 8`。构建产物会写入 `UnityLocalizationToolkit-WebUI/wwwroot`。
- 常用检查命令：
  - `cd UnityLocalizationToolkit-Vue`
  - `npx vue-tsc --build`
  - `npm run build`
  - 开发联调使用 `npm run dev`
- Vite 开发代理固定走 `http://127.0.0.1:51821`，不要改成 `localhost`。
- 顶层页面依赖 `RouterView + KeepAlive + Pinia`。涉及 `SignalR`、定时器、窗口事件或全局监听时，必须同时处理 `onActivated`、`onDeactivated` 和 `onBeforeUnmount`。
- 不要在页面里直接改 store 内部状态，优先通过 store action 更新。
- 自动保存类页面在重新装载数据时遵循 `disable -> load/assign -> nextTick -> enable`，避免把服务端值误判成用户编辑。
- 视觉变更至少补一次真实 UI 验证；如果改动明显，优先看后端端口 `51821` 的完整界面，开发态静态资源问题则用 Vite dev server 兜底。
- 手动翻译工作台入口是 `/games/:id/manual-translation`，页面文件是 `src/views/ManualTranslationWorkspaceView.vue`。它应复用现有二级页体系：`sub-page`、`section-card`、`page-title`、`workspace-mode-chip`，不要再做独立视觉体系。
- 手动翻译主列表必须保持远程分页和按页渲染。现状是 `NDataTable` + 服务端分页，默认 `pageSize=50`；不要回退到把整份 `asset-index.json` 拉到浏览器后本地过滤。
- 手动翻译筛选使用显式 `valueKind`：`All`、`Text`、`Code`、`Image`、`Font`、`Binary`。顶部统计从 `status.kindCounts` 读取，不要自己重算。
- 手动翻译保存覆盖、删除覆盖后只刷新“当前页 + 当前详情 + 状态统计”，不要整页重载。
<!-- agents-maintainer:manual:end -->
