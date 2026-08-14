<div align="center">

<img src="XUnityToolkit-Vue/public/logo.png" alt="XUnityToolkit-WebUI" width="128" height="128">

# XUnityToolkit-WebUI

**面向 Unity 游戏汉化 / 翻译工作流的 Windows 桌面工具**

一键安装 BepInEx 与 XUnity.AutoTranslator，集成云端大模型、本地 llama.cpp、实时 AI 翻译、字体处理，并提供插件智能诊断 / 自动修复和可直接执行工具箱操作的对话智能体。

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Vue](https://img.shields.io/badge/Vue-3-4FC08D?logo=vue.js)](https://vuejs.org/)
[![TypeScript](https://img.shields.io/badge/TypeScript-7-3178C6?logo=typescript&logoColor=white)](https://www.typescriptlang.org/)
[![GitHub Release](https://img.shields.io/github/v/release/HanFengRuYue/XUnityToolkit-WebUI?color=brightgreen&logo=github)](https://github.com/HanFengRuYue/XUnityToolkit-WebUI/releases)
[![GitHub Stars](https://img.shields.io/github/stars/HanFengRuYue/XUnityToolkit-WebUI?style=social)](https://github.com/HanFengRuYue/XUnityToolkit-WebUI/stargazers)

[下载发布版](https://github.com/HanFengRuYue/XUnityToolkit-WebUI/releases) · [报告问题](https://github.com/HanFengRuYue/XUnityToolkit-WebUI/issues) · [功能建议](https://github.com/HanFengRuYue/XUnityToolkit-WebUI/issues)

</div>

## 项目简介

XUnityToolkit-WebUI 适合需要给 Unity 游戏做实时机翻增强、术语约束、翻译记忆、字体修复和插件分发的用户。发布版面向 Windows 10 / 11 x64，开箱即可运行；当你需要从源码开发时，也保留了完整的前后端项目结构。

## 下载与版本选择

<!-- DOWNLOAD_LINKS_START -->
| 版本 | ZIP 便携版 | MSI 安装包 |
|------|-----------|-----------|
| **Full（完整版）** | [下载](https://github.com/HanFengRuYue/XUnityToolkit-WebUI/releases/download/v5.0/XUnityToolkit-WebUI-v5.0-win-x64.zip) | [下载](https://github.com/HanFengRuYue/XUnityToolkit-WebUI/releases/download/v5.0/XUnityToolkit-WebUI-v5.0-win-x64.msi) |
| **No-LLAMA** | [下载](https://github.com/HanFengRuYue/XUnityToolkit-WebUI/releases/download/v5.0/XUnityToolkit-WebUI-v5.0-win-x64-no-llama.zip) | [下载](https://github.com/HanFengRuYue/XUnityToolkit-WebUI/releases/download/v5.0/XUnityToolkit-WebUI-v5.0-win-x64-no-llama.msi) |
| **Lite（精简版）** | [下载](https://github.com/HanFengRuYue/XUnityToolkit-WebUI/releases/download/v5.0/XUnityToolkit-WebUI-v5.0-win-x64-lite.zip) | [下载](https://github.com/HanFengRuYue/XUnityToolkit-WebUI/releases/download/v5.0/XUnityToolkit-WebUI-v5.0-win-x64-lite.msi) |
<!-- DOWNLOAD_LINKS_END -->

- **Full**：自包含，附带本地 AI 运行时与常用资源，适合大多数用户。
- **No-LLAMA**：自包含，只使用云端 API，不附带本地模型运行时。
- **Lite**：体积最小，需要先安装 [.NET 10 运行时](https://dotnet.microsoft.com/download/dotnet/10.0)。
- **本地 AI 适用环境**：NVIDIA 推荐 CUDA，AMD / Intel 推荐 Vulkan；没有独显也可走 CPU。

## 三分钟快速上手

1. 在 **游戏库** 中添加游戏目录，工具会检测 Unity 版本、Mono / IL2CPP、架构与可执行文件。
2. 打开 **游戏详情页**，使用 **一键安装** 自动部署 BepInEx、XUnity.AutoTranslator、AI 端点与推荐配置。
3. 进入 **AI 翻译** 页面，配置云端端点，或切换到 **本地 AI** 使用 llama.cpp 和本地 GGUF 模型。
4. 直接启动游戏开始实时翻译；出现缺字时可使用 **字体替换 / 字体生成**，已有译文可在 **译文编辑器** 中校对。
5. 在提供商列表下方统一选择 **工具箱智能体使用的提供商**，再从左侧打开 **工具箱智能体**、按需选择游戏并描述目标；智能体会展示实际调用的工具和结果，高影响操作、外部文件读取与脚本执行会等待你确认。

## 核心能力

- **一键接入翻译框架**：自动检测 Unity 游戏，安装 BepInEx 与 XUnity.AutoTranslator，并回写 AI 端点配置。
- **本机连接自愈**：首选端口被占用时自动回退，游戏插件通过运行时发现文件重新连接；工具箱重启或实际端口变化后无需手工改 INI。
- **Windows 原生窗口**：桌面端使用 WinUI 3 + WebView2 原生壳，以 32 epx 空白原生拖动区隐藏重复的窗口标题与图标，同时保留系统窗口按钮、右键系统菜单、`Win+Z`、Windows 11 分屏布局与贴靠组；业务网页和浏览器访问方式保持一致。
- **云端 AI 翻译**：支持 OpenAI、Claude、Gemini、DeepSeek、Qwen、GLM、Kimi 与自定义 OpenAI 兼容接口；OpenAI、DeepSeek、Qwen 及兼容服务可选 Responses API，并可按端点关闭或调整思考强度。
- **本地 AI 模式**：内置 llama.cpp，支持 HuggingFace / ModelScope 下载模型，也支持导入自有 GGUF。
- **统一术语与翻译记忆**：支持术语约束、精确/模糊翻译记忆与分阶段术语审查。
- **字体与插件工具链**：支持 TMP + Legacy `Font` 替换、SDF 字体生成、AI 驱动的 BepInEx 插件智能诊断、插件包导入导出与在线更新。
- **可操作的工具箱智能体（测试版）**：在不离开当前页面的悬浮对话窗中，用自然语言检查或操作游戏库、安装、配置、术语、译文、字体、插件、日志、更新与本地模型；也可读取环境信息、经确认运行诊断脚本，并在可信目录内管理文件。提供商在 AI 设置中统一配置。
- **插件全自动修复**：诊断后不再只给建议；工具箱会让云端 AI 生成受限修复计划，先备份目标文件，再修复工具箱组件/路由、补丁式修改 INI/CFG 或禁用有明确证据的问题插件，最后重新诊断。

## 智能体功能

项目现在统一使用一个 **工具箱智能体**。左侧悬浮窗提供通用对话与实际操作；游戏详情页和 BepInEx 日志页保留“智能诊断 / 自动修复”快捷入口，但它们复用同一个提供商设置、端点解析、证据诊断与工具执行体系，不再是另一套独立智能体。

### 工具箱智能体（测试版）

工具箱智能体不是单纯的问答窗口。可拖动、最小化的悬浮窗会先理解目标，再通过白名单内的本机工具和现有 API 读取状态、执行操作，并把每次工具调用的成功、失败、跳过或待确认状态显示在对话中。操作完成后，游戏库与界面偏好会主动刷新。

| 场景 | 当前可执行的操作 |
|------|------------------|
| 游戏与安装 | 列出和检查游戏、重新检测 Unity 信息、安装 / 卸载翻译组件、启动游戏、打开目录 |
| 翻译与配置 | 调整安全设置、修改 XUnity 配置、管理术语 / 脚本标签 / 译文、查看或清理翻译记忆，以及查看翻译统计与日志 |
| 插件与诊断 | 检查插件健康状态、调用云端智能诊断与自动修复、启停或卸载第三方插件、导入插件或插件包 |
| 字体与图片 | 导入 TTF / OTF / TMP 字体源、生成 TMP SDF 字体、替换支持的 TMP 与 Legacy 字体、设置 fallback，以及设置游戏图标 / 封面 / 背景 |
| 本地模型与更新 | 查看或管理 llama.cpp、模型与下载任务，检查和下载工具箱更新；更新应用仍需到更新页面手动执行 |
| 文件与环境 | 直接读取已添加游戏目录和工具箱数据目录中的原文或被动二进制元数据；外部电脑路径逐次确认后只读；可信目录内可批量创建、覆盖、复制、移动、重命名或删除任意文件 |
| 诊断脚本 | 在展示用途、完整 PowerShell / CMD 脚本与风险后逐次请求确认，以当前用户权限运行并读取输出；脚本只允许用于诊断，系统或外部环境修复由用户自行执行 |
| 数据清理 | 用户明确确认后，可退出程序、清空完整工具箱数据目录并自动重启；不会删除游戏目录 |

使用时先在 **AI 翻译** 页面配置并启用至少一个有效的云端端点，然后：

1. 在 **AI 翻译 → AI 提供商** 下方，把智能体提供商设为“自动”（选择最高优先级的有效云端端点），或固定到某个端点；固定端点失效时会停止并要求重新配置，不会回退。
2. 从左侧导航打开 **工具箱智能体**。悬浮窗不再单独选择提供商。
3. 按需选择当前游戏，输入希望得到的最终结果；字体、插件包、配置或图片等任务可先上传附件。
4. 查看智能体实际执行的工具记录。高影响操作会暂停；同一目的的可信目录文件变更会合并为一个批次确认，外部目录 / 文件 / 分块读取和每个脚本则逐次确认。
5. 后续可从历史抽屉继续原对话，或删除单条 / 全部历史。

可以直接尝试这些指令：

- `检查当前游戏的插件状态，有明确问题就自动修复。`
- `把 Captain 加到当前游戏的术语表，固定翻译为“船长”。`
- 上传 TTF / OTF 后说：`用这个字体生成并应用中文 TMP 字体。`
- 上传 ZIP 后说：`把这个插件包导入当前游戏。`
- `检查本地模型服务；如果没有启动就帮我启动。`
- `读取显卡、系统代理和工具箱日志，分析为什么插件无法连接；不要修改系统。`

### 插件智能诊断与自动修复

- 打开游戏详情或 BepInEx 日志页只会刷新本地客观检查，不会自动请求模型，也不会产生隐藏的 AI 费用。
- 手动点击 **智能诊断** 后，第一阶段先让工具箱智能体从候选资料清单中选择必要证据，第二阶段才分析可信游戏目录中选定的带行号原始日志、配置与元数据；报告会区分客观检查和 AI 判断，证据变化后旧报告会标记为过期。
- **启动并智能诊断** 会先短暂启动游戏，等待新的 BepInEx 日志与工具箱 ping，再进入同一诊断流程。
- **AI 全自动修复** 只有在后端检测到确定性修复项，或本次 AI 报告存在中 / 高置信度且有有效证据的警告 / 错误时才显示；它只执行受限计划，写入前创建备份，完成后重新检查。这里的专用备份规则不受通用文件工具“默认不备份”影响。
- 若云端 AI 不可用或诊断失败，界面只保留可验证的本地事实，不会用旧规则或预制建议冒充 AI 结论。

### 云端、数据与安全边界

- 工具箱对话、插件诊断和修复规划统一使用设置页选定的云端端点，且独立于实时翻译总开关和本地 / 云端模式；因此普通翻译使用本地 llama.cpp 或暂时关闭时仍可显式运行智能体。智能体不会改用本地模型，多阶段调用可能产生 API 费用。
- 工具箱智能体最多保留最近 100 个本地对话，每个对话最多展示最近 200 条消息；历史记录可恢复和删除，但待确认操作不会跨重启保留。
- 附件支持字体、TMP Bundle、DLL / ZIP、常见文本配置和 PNG / JPEG / WebP 图片；一次最多 8 个，单个不超过 50 MB，每个会话合计不超过 150 MB。临时附件约保留 6 小时，历史中只长期保存附件信息，过期后需要重新上传。
- 附件二进制不会直接发送给模型，图片附件用于本地图标 / 封面 / 背景工具。二进制读取只提供哈希、签名、PE / 程序集引用、ZIP 清单等被动元数据或有限十六进制块，不会在工具箱进程中加载用户程序集。
- 所有已添加游戏目录和完整工具箱数据目录属于可信根：模型可直接取得其中的原始内容与绝对路径；通用文件变更必须经批次确认，可操作任意扩展名且默认不备份。可信根以外只能读取，每次目录、文件或分块读取都要确认，确认后原文和路径会发送到所选提供商。
- PowerShell / CMD 脚本必须展示具体用途、完整命令、超时与风险并逐次确认，只以当前用户权限运行、不提权。脚本“只能读取和诊断”由系统提示词约束，后端无法技术性保证脚本绝对只读；因此不要确认不理解的命令。智能体只能给出系统 / 外部环境修复方案，最终由用户自行执行。
- API Key / Token 与完整设置仍不交给模型。清空全部工具箱数据使用专用退出后助手，不创建备份并自动重启；它只删除工具箱数据根，不删除已添加游戏目录。

<details>
<summary><strong>完整教程（点击展开）</strong></summary>

### 1. 添加游戏与安装翻译插件

- 游戏库支持单个添加，也支持批量扫描目录中的 Unity 游戏。
- 游戏详情页会展示 Unity 版本、脚本后端、架构、插件状态和快捷操作。
- **一键安装** 会按顺序处理翻译框架部署、AI 端点写入、推荐配置应用与本地验证；安装流程只检查文件、新启动日志和工具箱 ping，不会调用模型或产生隐藏的 AI 费用。

<img src="docs/readme/library-overview.png" alt="游戏库首页" width="100%">

<img src="docs/readme/game-detail-install.png" alt="游戏详情与安装流程" width="100%">

### 2. 配置 AI 翻译

- **云端模式**：在 **AI 翻译** 页面添加端点，支持 API 格式、思考模式/强度、优先级、启停、模型名和连通性测试。
- **Responses API**：OpenAI、DeepSeek 与 Qwen 新端点默认使用 Responses；旧端点继续保留 Chat Completions，避免升级后静默改变请求格式。
- **关闭思考**：DeepSeek V4、OpenAI GPT-5.6、Qwen、GLM、Claude Sonnet 5 与 Kimi K2.6 会发送各自真实的关闭参数。Gemini 3 目前只能降到 `minimal`，Kimi K3 属于强制思考模型。
- **本地模式**：切换到本地 AI 后，可根据显卡情况选择模型、下载运行时、启动 llama.cpp 服务。
- 如果你只想用自建兼容接口，可以直接使用 **Custom（OpenAI 兼容）**。

<img src="docs/readme/ai-translation.png" alt="AI 翻译配置" width="100%">

### 3. 字体替换与字体生成

- 字体替换支持 TMP 与 Legacy `Font` 两类资源。
- 可直接使用内置替换源，也可以上传自定义 TMP / TTF / OTF 资源。
- 字体生成页可基于 TTF / OTF 生成 TMP SDF 字体，并按字符集输出结果。

<img src="docs/readme/font-replacement.png" alt="字体替换流程" width="100%">

### 4. 术语、翻译编辑与插件包

- **术语编辑器**：管理翻译术语、禁翻词、分类、优先级与正则匹配。
- **翻译编辑器**：对 AI 输出做人工校对，并支持导入 / 导出。
- **插件包导入导出**：打包当前游戏的翻译插件、缓存与配置，便于分发给其他玩家。
- **智能体辅助**：可直接让工具箱智能体管理术语、译文或插件包，也可从游戏详情 / 日志页启动专项诊断；完整能力与安全边界见上方的 [智能体功能](#智能体功能)。

</details>

## 常见问题

<details>
<summary><strong>我应该下载哪个版本？</strong></summary>

- 想省心，优先用 **Full**。
- 只打算使用云端 API，用 **No-LLAMA**。
- 追求最小体积且已经安装 .NET 10 运行时，用 **Lite**。

</details>

<details>
<summary><strong>云端 AI 和本地 AI 怎么选？</strong></summary>

- 云端模式配置简单、更新快，适合大多数用户。
- 本地模式更适合离线环境、长时间批量翻译，或不希望把文本发送到第三方接口的场景。
- 本地模式对显卡、显存和磁盘空间要求更高。
- 工具箱对话、插件智能诊断和全自动修复统一使用 **AI 提供商** 下方选定的智能体端点，不依赖普通翻译的云端 / 本地模式或总开关；普通翻译使用本地模型时仍可运行，但智能体不会改用 llama-server。

</details>

<details>
<summary><strong>翻译后出现方块字或缺字怎么办？</strong></summary>

- 先进入 **字体替换** 页面扫描当前字体资源。
- 如果游戏依赖的 TMP / Legacy `Font` 不包含中文字符，可直接替换为内置字体或上传自定义字体。
- 需要完全自定义时，再进入 **字体生成** 生成 TMP SDF 字体。

</details>

<details>
<summary><strong>提示端口占用，或开代理/加速器后游戏连不上怎么办？</strong></summary>

- `51821` 现在是“首选端口”。若它被其他程序占用或被系统保留，工具箱会自动选择新的本机端口，并通过 `%AppData%\\XUnityToolkit\\runtime\\toolbox-endpoint-v1.json` 告知新版 `LLMTranslate.dll`。
- 游戏与工具箱之间始终使用 `127.0.0.1` 且强制直连，不读取系统代理或 PAC；这不会改变工具箱访问云端 AI 时使用的代理设置。
- 普通系统代理通常不会再影响连接。若启用 TUN/全局加速后仍超时，请在健康检查中查看最后心跳与诊断原因，并尝试开启“绕过局域网/环回地址”。内核级过滤驱动仍可能拦截环回流量，代码无法保证绕过任意此类驱动。
- 工具箱设置页会同时显示首选端口和本次实际端口。修改首选端口会在下次启动生效，当前游戏配置继续使用实际地址。

</details>

<details>
<summary><strong>配置和缓存存在哪里？</strong></summary>

- 默认目录是 `%AppData%\\XUnityToolkit`。
- `runtime\\toolbox-endpoint-v1.json` 是当前运行实例的本机端点发现文件，会在正常退出时按实例归属清理。
- 工具箱智能体历史位于 `toolbox-agent\\conversations`，临时附件位于 `cache\\toolbox-agent-uploads`；两者都不会进入设置导出包，附件过期后会自动清理。
- 可通过应用内的 **导出配置 / 导入配置** 做迁移或备份。
- 开发与维护层面的完整数据布局，请查看 [AGENTS.md](AGENTS.md)。

</details>

<details>
<summary><strong>可以把翻译成果发给别人吗？</strong></summary>

- 可以，优先使用 **插件包导出**。
- 它会把当前游戏目录中的翻译插件、译文文件和相关配置打包出来，其他人可直接导入。

</details>

<details>
<summary><strong>开发者说明（点击展开）</strong></summary>

### 环境要求

- Windows 10 / 11 x64
- .NET 10 SDK
- Node.js 20.19+ 或 22.12+

### 常用命令

```bash
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true
dotnet run --project XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj

cd XUnityToolkit-Vue
npm run dev
npm run build
npx vue-tsc --build

cd ..
.\build.ps1
.\build.ps1 -SkipDownload
```

### 开发模式

- 后端优先监听 `http://127.0.0.1:51821`；端口冲突时自动绑定其他本机端口，实际地址以运行时发现文件或“工具箱连接”卡片为准。
- 前端开发时也应代理到 `127.0.0.1`，不要改成 `localhost`。
- `XUnityToolkit-WebUI.csproj` 默认会在构建前自动执行前端安装与构建。

### 关键子项目

- `XUnityToolkit-WebUI/`：ASP.NET Core Minimal API + WinUI 3 / WebView2 原生宿主（WinForms 仅用于托盘图标）
- `XUnityToolkit-Vue/`：Vue 3 + TypeScript + Naive UI 前端
- `TranslatorEndpoint/`：提供给 XUnity.AutoTranslator 调用的 `LLMTranslate.dll`
- `Updater/`：AOT 更新器
- `Installer/`：WiX 安装器

### 维护说明

- README 现在主要面向用户。
- 仓库维护、运行时数据布局、同步点和不变量统一记录在 [AGENTS.md](AGENTS.md)。
- 构建与发版流程调整时，需要同时检查 `build.ps1` 和 `.github/workflows/build.yml`。

</details>

## 许可证

本项目基于 [MIT License](LICENSE) 开源。
