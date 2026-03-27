# XUnityToolkit-Vue

XUnityToolkit-WebUI 的 Vue 3 前端。项目概览、API 端点和构建命令请参见根目录 `CLAUDE.md`。

## 代码规范

- 使用 Vue 3 Composition API 和 `<script setup lang="ts">`
- 使用 `<style scoped>` 作用域样式；使用 `main.css` 中的 CSS 变量
- 图标：`@vicons/material` 和 `@vicons/ionicons5`，通过 Naive UI `NIcon` 包裹使用
- API 客户端：`api.get`、`api.post`、`api.put`、`api.del`（不要用 `.delete` —— 这是 JS 保留字）；从 `@/api/client` 导入（不要用 `@/api` —— 它是一个目录，Vite 会报 EISDIR 错误）
- API 层：`src/api/client.ts`（axios 封装）、`src/api/types.ts`（共享类型）、`src/api/games.ts`（游戏 API 方法）

## 设计系统

- **主题：** 通过 `data-theme` 实现深色/浅色/跟随系统切换；`useThemeStore`（localStorage + 操作系统检测）为权威数据源，不是后端的 `AppSettings.Theme`；`ThemeMode = 'dark' | 'light' | 'system'`；渲染决策使用 `resolvedTheme`（始终为 `'dark'|'light'`），不要用 `mode`（可能为 `'system'`）；默认值为 `'system'`；`matchMedia` 监听器在操作系统主题变化时自动更新；浅色模式自动将强调色加深 15%
- **CSS 变量：** 在 `main.css` 中定义（`--bg-root`、`--accent`、`--text-1` 等）；支持主题感知：使用 `--bg-subtle`/`--bg-muted` —— 绝不硬编码 `rgba(255,255,255,...)`
- **布局：** 侧边栏（默认 230px，可折叠至 64px，通过拖拽可调整 180-400px）+ 可滚动内容区；`useSidebarStore` 将状态持久化到 localStorage（`sidebarCollapsed`、`sidebarWidth`）；768px 响应式断点（平板使用抽屉模式，禁用折叠/调整大小），480px 断点（手机单列布局）
- **卡片：** `.section-card` 搭配 `.section-icon`/`.section-title`（基础色为 `--accent`；仅在语义场景使用 `.danger`/`.warning`）；装饰色使用 `--accent`；用 `color-mix()` 实现半透明背景
- **共享 CSS（main.css）：** `.page-title`、`.page-title-icon`、`.section-card`、`.section-header`、`.section-title`、`.section-icon`、`.header-actions`、`.section-desc`、`.loading-state`、`.table-container`、`.add-entry-row`、`.unsaved-badge`、`.auto-save-badge`、`.empty-hint`、`.back-button` 均为全局类 —— 不要在作用域样式中重新定义
- **子页面布局：** 游戏子页面使用 `.sub-page`（24px 标题）+ `.sub-page-header` + `.back-button`；顶层页面直接使用 `.page-title`（26px）
- **头部操作区：** 使用 `.header-actions`（不要用 `.header-btn-group`）来布局区块头部的按钮组
- **字体排版：** 不要对 UI 文本（版本号、标签等）使用 `font-family: monospace` —— 这与系统字体冲突；仅在代码块、面向开发者的输出、或表格数据列（如文件浏览器的日期/大小列）中使用等宽字体以确保数字对齐
- **内容内边距：** `24px 28px`（桌面端）、`20px 20px`（平板端）、`16px 12px`（手机端）—— hero 背景的负外边距必须与之匹配；不要在页面级容器上设置 padding（`.main-content` 已提供）；不要在页面容器上使用 `max-width`，除非内容确实需要约束宽度；页面级 flex 列使用 `gap: 16px`
- **CSS 变量命名：** 绝不使用 `--error-color`、`--success-color`、`--border-color`、`--text-secondary`、`--text-color-3` —— 这些不存在；应使用 `--danger`、`--success`、`--border`、`--text-3`；用 `color-mix(in srgb, var(--danger) N%, transparent)` 实现半透明语义色
- **组件命名大小写：** Naive UI 组件始终使用 PascalCase（`NSwitch`、`NTag`、`NAlert`、`NProgress`、`NCollapse`），不要用小写短横线形式（`n-switch`、`n-tag`）
- **内联样式：** 避免使用 `style="..."` 做布局/颜色；应创建使用设计系统变量的作用域 CSS 类；仅在一次性的 `animation-delay` 值时可接受内联样式
- **按钮渐变背景：** 自定义样式的按钮需要 `appearance: none`；带 `border: Npx solid transparent` 的渐变背景（`conic-gradient` 等）需用 `background: <gradient> border-box` —— 默认 `padding-box` origin 会导致渐变在圆角边缘错位

## 添加新页面

- **顶层页面：** 在 `src/views/` 中添加视图，在 `router/index.ts` 中添加懒加载路由，在 `AppShell.vue` 的 `mainNavItems` 中添加导航项（设置页独立为 `settingsNavItem`）
- **游戏子页面：** 懒加载路由位于 `/games/:id/{name}`，在 `GameDetailView.vue` 中添加按钮 —— 不要添加到 `navItems`；子页面包括 `TermEditorView`、`TranslationEditorView`、`ConfigEditorView`、`AssetExtractionView`、`FontReplacementView`、`BepInExLogView`、`PluginManagerView`
- **TermEditorView：** 统一的术语编辑器（替代旧的 GlossaryEditorView）；提供类型（translate/doNotTranslate）和分类的筛选标签栏；NDataTable 虚拟滚动、行内编辑、列显示控制；单个 `useAutoSave` 实例；JSON/CSV 导入导出；通过弹窗实现跨游戏导入
- **页面过渡：** 路由的 `meta.depth`（1=顶层页面，2=游戏详情，3=游戏子页面）；添加新路由必须包含 `meta: { depth: N }`
- SignalR store：在 `connect()` 前检查 `state !== Disconnected`，在 `onreconnected` 中重新加入组
- **KeepAlive 视图中的 SignalR：** 绝不在模块/脚本顶层创建 `HubConnection` —— 始终在 `onMounted` 中创建，并在 `onDeactivated` 和 `onBeforeUnmount` 中都进行清理（KeepAlive 视图被停用而非卸载 —— 单独使用 `onBeforeUnmount` 不会触发）；将清理逻辑提取为共享函数以避免重复；声明 `let connection: HubConnection | null = null` 并在 `onMounted` 中赋值
- **KeepAlive 视图中的 Window 监听器：** `onMounted` 中的 `window.addEventListener` 必须在 `onDeactivated` 和 `onBeforeUnmount` 中都配对 `removeEventListener`；在 `onActivated` 中重新添加 —— 否则监听器在视图停用时仍保持活跃，或在重新激活时累积
- **`onBeforeUnmount` 而非 `onUnmounted`：** 始终使用 `onBeforeUnmount`（不要用 `onUnmounted`）进行清理 —— `onUnmounted` 在组件已销毁后才触发，来不及安全地拆卸资源；KeepAlive 视图在正常导航期间 `onUnmounted` 也永远不会触发
- **KeepAlive 子组件：** 在 KeepAlive 缓存视图内渲染的子组件（如 `AiTranslationView` 中的 `LocalAiPanel`）也会接收 `onActivated`/`onDeactivated` —— 如果它们管理 SignalR 或其他资源，必须使用这些钩子（而不仅是 `onMounted`/`onBeforeUnmount`）
- **FontGeneratorView 的 KeepAlive SignalR：** `FontGeneratorView` 被 `<KeepAlive>` 缓存 —— 必须在 `onActivated`（而非仅 `onMounted`）中创建新的 `HubConnection`，并在 `onDeactivated` 中拆卸；与其他 KeepAlive 视图模式相同

## 模式与注意事项

- **Pinia store 状态变更：** 绝不从视图或组合式函数中直接修改 store 的 `ref` 状态（`store.x = y`、`store.arr.push()`）；始终使用 store actions —— 直接修改会绕过 devtools 追踪并产生竞态条件
- **Games store 设置器：** `setViewMode`、`setSortBy`、`setCardSize`、`setGap`、`setShowLabels` —— 使用这些 actions 而非直接赋值；`launchGame(id)` 同时处理 API 调用和 `lastPlayedAt` 更新
- **GameCard 启动：** 必须使用 `gamesStore.launchGame(id)` —— 绝不直接修改 `props.game`（Vue 禁止 prop 修改；会绕过 store 响应式）
- **主题默认值：** CSS `:root` = 深色主题；`loadInitialTheme()` 默认为 `'system'`；通过 `matchMedia` 检测操作系统主题；`resolveTheme('system')` 在操作系统检测不可用时回退到 `'dark'`
- **作用域样式 → 全局 CSS：** 作用域样式（`.class[data-v-xxx]`）的优先级高于全局样式（`.class`）；将共享样式提取到 `main.css` 时，必须移除作用域中的重复定义，否则它们会覆盖全局样式；页面特有的覆盖保留在作用域中，仅包含有差异的属性
- **折叠动画：** 使用 `display: grid; grid-template-rows: 1fr/0fr` 模式（与 `main.css` 中全局 `.section-body` 一致），不要用 `max-height` —— `max-height` 会导致缓动迟滞，因为过渡应用于完整的 0→Npx 范围，而非实际内容高度；**`-body-inner` 元素不得有 `padding`** —— padding 不会随 `0fr` grid 轨道折叠；要么使用嵌套包裹元素来设置 padding，要么在折叠时将 `padding-top/padding-bottom` 过渡为 `0`（参见 `AiTranslationView.vue` 中的 `.settings-group-body-inner` 模式）
- **Flex `gap` 与隐藏子元素：** 通过 `max-width: 0; opacity: 0`（用于平滑折叠过渡）隐藏 flex 子元素时，flex `gap` 仍然生效 —— 会将可见项推离中心。必须在折叠状态下将父元素的 `gap` 设为 `0`。影响侧边栏 logo、导航项，以及任何包含过渡隐藏子元素的 flex 容器。
- **Flex 列子元素拉伸：** `flex-direction: column` 容器中的子项默认拉伸至全宽。对于折叠侧边栏的方形按钮，需设置明确的 `width`/`height` + `margin: 0 auto` 以防止矩形拉伸。
- **嵌套可折叠区域 `@click.stop`：** 当带有可折叠头部的 `.section-card` 包含内层可折叠分组时（如设置分组），内层分组头部需要 `@click.stop` 以防止事件冒泡到外层折叠切换。
- **`defineOptions` 位置：** 必须放在 `<script setup>` 中所有 `import` 语句之后，绝不放在之前 —— 否则后续导入会报 TS1232 错误
- **KeepAlive：** 顶层视图（Library、AiTranslation、FontGenerator、Log、Settings）通过 AppShell 中的 `<KeepAlive :include>` 缓存；每个视图必须在导入语句之后有 `defineOptions({ name: 'XxxView' })`
- **LogView 日志级别同步：** `selectedLevels`（默认选中项）和 `levelDefs`（筛选标签定义）都必须包含某个级别，该级别才会显示并处于激活状态；`levelClass()` 样式也必须有对应的 CSS 类（如 `.level-dbg`）
- **安装状态恢复：** `startInstall`/`startUninstall` 必须将后端 `GET /api/games/{id}/status` 作为回退方案查询 —— 页面刷新时 Pinia store 状态会丢失，而后端安装仍在继续运行
- 对于复杂的多步骤 UI 流程使用组合式函数（`src/composables/`）；`useAddGameFlow`（添加游戏向导）、`useAutoSave`（防抖自动保存）、`useFileExplorer`（文件/文件夹选择）、`useWindowControls`（WebView2 窗口控制）
- **`useWindowControls` 单例模式：** 模块级 `isWebView2`/`isMaximized` ref；通过 `window.chrome?.webview` 检测 WebView2 环境；`postMessage` 发送 `minimize`/`maximize`/`close` 命令；监听 host 的 `maximized`/`normal` 状态通知；仅在 WebView2 模式渲染标题栏和窗口控制按钮；`env.d.ts` 中声明 `ChromeWebView` 接口（在 `declare global` 块内，因文件含 `import` 为模块）
- **`useFileExplorer` 单例模式：** 模块级 reactive 状态 + `resolveCallback`；`selectFile(opts)`/`selectFolder(opts)` 返回 `Promise<string | null>`；`FileExplorerModal.vue` 在 `App.vue` 中通过 `defineAsyncComponent` 全局挂载一次（在 `NDialogProvider` 内）；返回服务端文件路径
- **自动保存：** `useAutoSave(source, saveFn, { debounceMs, deep })`；`disable()` → 加载数据 → `nextTick()` → `enable()`；`disable()` 必须清除待处理的定时器；`onBeforeUnmount` 自动刷新；手动保存必须在数据重新赋值前 `disable()`，在 `finally` 中 `enable()`
- **KeepAlive 设置覆写风险：** `AiTranslationView` 和 `SettingsView` 都通过 `PUT /api/settings` 自动保存完整的 `AppSettings`。两者都必须在 `onActivated` 中重新加载设置（`loadSettings()`）—— 否则过期的 KeepAlive 副本会覆盖另一个页面的更改（如新添加的端点会丢失）。模式：`disableAutoSave()` → 获取 → 赋值 → `nextTick()` → `enableAutoSave()`
- **ConfigPanel** 内部自动保存（2 秒），不发出 `save` 事件
- Naive UI：浅色主题传 `null`；`NDrawer` 宽度只接受数字；`NForm` 的 label-placement 通过 computed 设置（不用 CSS）；`NInput` 的 `string?` 使用 `:value` + `@update:value`；`NInput` 的 blur+enter 双触发 —— 用标志位防护；`NDialogOptions.onPositiveClick`：返回 `Promise` 会保持对话框打开直到 resolve —— 对耗时异步操作使用即发即忘方式（如 `() => { doWork() }`）以立即关闭
- `NInputNumber`：`@update:value` 发出 `number | null` —— 用户清空字段时值为 `null`；保存到 API 的处理函数必须检查 `if (val === null) return`
- `NDataTable`：`virtual-scroll` 和 `pagination` 互斥；用 `filteredEntries.length > 0` 做空状态保护；`row-key` 必须全局唯一 —— 如果 ID 可能跨分类冲突，使用组合键如 `` `${category}:${id}` ``；没有明确 `width`/`minWidth` 的列在固定宽度列总和超过容器时会被压缩到 0px —— 弹性列必须设置 `minWidth`；**行内编辑的排序稳定性**：`computed` 因深层响应式在每次单元格修改时重新触发 —— 使用 `ref` 缓存 + 版本计数器（`entriesVersion`）并显式 `watch` 筛选/排序设置来解耦；**受控排序模式**：使用 `sorter: true` + `@update:sorters` + 响应式 `sortOrder` 在列上，手动在数据管道中应用排序（不要用 NDataTable 的内置排序）；类型为 `DataTableSortState`（不是 `SortState`）
- `NBadge`：使用 `:show` 属性控制圆点可见性（不要用 `:value` 配合 `dot: true` —— `show` 默认为 `true`，所以仅设 `dot` 时圆点会始终显示）
- `NPopselect`：用于下拉选择按钮（如排序模式选择器）—— 包裹一个 `NButton` 触发器，显示带选中状态的可选选项；当控件应呈现为按钮而非表单字段时，优于 `NSelect`
- `NColorPicker`：`#trigger` 插槽替换整个触发元素；`#label` 仅自定义默认矩形触发器内的文本 —— 自定义触发按钮使用 `#trigger`；消费 hex 值时始终设置 `:modes="['hex']"`（默认允许 rgb/hsl/hsv 切换，会导致 `hexToRgb()` 失败）；通过 `:show`/`@update:show` 手动管理可见性 —— 不要同时调用插槽提供的 `onClick`（它只能打开，不能切换）
- `v-show` + `loading="lazy"` 死锁：使用 `opacity: 0` + `position: absolute` 替代
- `onBeforeRouteLeave` 配合 async：必须 `return new Promise<boolean>()` —— 不要用 `next()` 回调
- **RouterView key：** `:key="route.path"` 确保相同组件不同路由导航时触发过渡动画
- **RouteMeta 扩展：** `env.d.ts` 为 TypeScript 在 `RouteMeta` 上声明了 `depth?: number`
- **流水线流程 CSS 类：** `.pipeline-flow`（position: relative）> `.pipeline-svg`（SVG 叠加层，z-index: 1）+ `.pipeline-hbox`（水平 flex，z-index: 2）> `.pipeline-root` + `.pipeline-branches` > `.pipeline-branch` > `.branch-header` + `.branch-nodes`；节点使用 `flex: 1 1 0` 填满宽度；根节点使用 `flex: 0 0 auto`；SVG 连线使用直角路径（`L` 线段，不是贝塞尔曲线）；通过 CSS `offset-path` 实现动画粒子；术语提取内联于 LLM `.branch-nodes` 中（不是单独的 `.branch-sub`）；TM 分支使用单个 `.tm-node` 配合 `.tm-chips-inline`（不是单独的 `.tm-chip` 卡片）；TM 颜色使用基于强调色的 `color-mix()` 变体（不是 `--secondary`）；所有流水线节点都用 `NPopover` 包裹以显示悬浮提示；`.pipeline-flow` 上的 `ResizeObserver` 重新计算 SVG 路径；在 `onDeactivated` 和 `onBeforeUnmount` 中都进行清理（KeepAlive）
- **GameDetailView 动画：** 每项间隔 0.05s；插入一张卡片会移动所有后续卡片的延迟
- **Blob 下载：** `fetch` → `blob()` → `createObjectURL` → `a.click()` → `setTimeout(revokeObjectURL, 1000)`
- 修改后：使用 `npx vue-tsc --build` 和 `npm run build` 验证
- **视觉验证：** 始终使用 Playwright MCP（`browser_navigate` + `browser_take_screenshot`）在报告完成前验证 UI 更改 —— 类型检查和构建成功不能保证正确的视觉输出
- 验证图标：`node -e "const m = require('@vicons/material'); console.log(m['IconName'] ? 'YES' : 'NO')"`
- **`embedded` 属性模式：** 根据独立使用或嵌套使用来条件渲染卡片包裹
- **`LocalAiPanel.vue`：** 通过 `v-model` 接收设置；共享设置通过父组件的 `useAutoSave` 流转；本地专属设置通过 `PUT /api/local-llm/settings` 保存
- TypeScript：对类型化对象使用 `Object.assign({}, obj, patch)` 而非展开运算符；懒加载弹窗：`defineAsyncComponent`
- **类型化对象的动态字段访问：** `(obj as Record<string, unknown>)[field]` 在严格 TS 下失败；使用 `(obj as unknown as Record<string, unknown>)[field]`
- **Markdown 渲染：** `marked` 包（自带类型定义，不需要 `@types/marked`）；使用 `marked.parse(md, { async: false }) as string` —— 需要 `as string` 类型断言（重载返回 `string | Promise<string>`）
- **正则匹配分组：** 在严格 TS 中 `match[1]` 为 `string | undefined` —— 始终检查 `match && match[1]`
- **作用域 `:deep()` 嵌套：** 绝不链式使用 `:deep()` —— `.x :deep(a) :deep(b)` 会静默失败；在单个 `:deep()` 调用中使用后代选择器 `:deep(a b)`
- **`NTabs` 等宽分段：** `:deep(.n-tabs-tab) { flex: 1; justify-content: center; }`
- **`NTabs type="segment"` 深色主题：** 分段标签会融入背景；通过 `:deep(.n-tabs-tab--active)` 覆写，使用 `color: var(--accent)`、`background: color-mix(in srgb, var(--accent) 12%, var(--bg-card))`、`border: 1px solid var(--accent-border)`
- **`NUpload` 在 flex 容器中：** NUpload 用额外 div 包裹触发器，会破坏 flex 对齐；通过 `:deep(.n-upload), :deep(.n-upload-trigger) { display: flex; align-items: center; }` 修复
- **`NEllipsis` 在 flex 容器中：** NEllipsis 内部使用 `display: inline-block`，直接对其添加 `flex: 1` 的 class 不生效——必须用 `<div class="col-name"><NEllipsis>...</NEllipsis></div>` 包裹，让外层 div 承担 flex 布局
- **跨页面术语访问：** 其他视图（如 TranslationEditorView）可通过 `gamesApi.getTerms` → 检查重复 → `unshift` → `gamesApi.saveTerms` 添加条目；没有共享的 Pinia store —— 每个页面独立获取/保存
- **批量清除模式：** "全部清除" = 将响应式数组设为 `[]`；对于自动保存视图（GlossaryEditor），这会触发空数组的自动保存；对于手动保存视图（TranslationEditor），这会标记脏状态；始终使用 `dialog.warning` 确认

## 游戏详情背景图片

- **Hero 背景：** `GameDetailView.vue` —— 绝对定位于内容后方；图片不要加模糊（仅使用 `filter: brightness(0.65) saturate(1.1)`）；渐变 + 暗角叠加层淡出至 `--bg-root`
- **亚克力卡片：** `.section-card` 使用 `backdrop-filter: blur(20px)` 配合半透明 `--bg-card` —— 是卡片模糊背景，而非图片本身模糊
- **视差效果：** 滚动监听器在 `.main-content`（父元素）上；hero 图片使用 `translateY(scrollTop * 0.3)`；`passive: true`
- **bgTimestamp：** 独立的 `ref(Date.now())` 用于独立于 `game.updatedAt` 对背景 URL 进行缓存失效

## 网页图片搜索（前端）

- **`WebImageSearchTab` 模式：** `'cover'` | `'icon'` | `'background'` —— 各自有不同的搜索后缀、尺寸筛选和网格宽高比
- **GameDetailView 标题图标：** 左键点击 → IconPickerModal；右键点击 → 上下文菜单（更换封面 / 更换背景 / 搜索图标 / 上传图标 / 删除图标 / 删除背景）
