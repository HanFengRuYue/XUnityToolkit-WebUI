<div align="center">

<img src="UnityLocalizationToolkit-Vue/public/logo.png" alt="UnityLocalizationToolkit-WebUI" width="128" height="128">

# UnityLocalizationToolkit-WebUI

**面向 Unity 游戏汉化 / 翻译工作流的 Windows 桌面工具**

一键接入 BepInEx 与 XUnity.AutoTranslator，整合云端大模型、本地 `llama.cpp`、资产提取、预翻译、手动校对、字体处理与插件包导入导出。

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Vue](https://img.shields.io/badge/Vue-3-4FC08D?logo=vue.js)](https://vuejs.org/)
[![TypeScript](https://img.shields.io/badge/TypeScript-5-3178C6?logo=typescript&logoColor=white)](https://www.typescriptlang.org/)
[![GitHub Release](https://img.shields.io/github/v/release/HanFengRuYue/UnityLocalizationToolkit-WebUI?color=brightgreen&logo=github)](https://github.com/HanFengRuYue/UnityLocalizationToolkit-WebUI/releases)
[![GitHub Stars](https://img.shields.io/github/stars/HanFengRuYue/UnityLocalizationToolkit-WebUI?style=social)](https://github.com/HanFengRuYue/UnityLocalizationToolkit-WebUI/stargazers)

[下载发布版](https://github.com/HanFengRuYue/UnityLocalizationToolkit-WebUI/releases) · [报告问题](https://github.com/HanFengRuYue/UnityLocalizationToolkit-WebUI/issues) · [功能建议](https://github.com/HanFengRuYue/UnityLocalizationToolkit-WebUI/issues)

</div>

## 项目简介

`UnityLocalizationToolkit-WebUI` 面向需要为 Unity 游戏做实时翻译、预翻译、术语约束、字体修复和插件分发的用户。发布版聚焦 Windows 10 / 11 x64，开箱即用；源码仓库则保留了完整的 `.NET 10 + Vue 3 + TypeScript` 双栈工程结构，方便继续开发和集成。

## 下载与版本选择

<!-- DOWNLOAD_LINKS_START -->
| 版本 | ZIP 便携版 | MSI 安装包 |
|------|------------|------------|
| **Full（完整版）** | [下载](https://github.com/HanFengRuYue/UnityLocalizationToolkit-WebUI/releases/download/v5.0/UnityLocalizationToolkit-WebUI-v5.0-win-x64.zip) | [下载](https://github.com/HanFengRuYue/UnityLocalizationToolkit-WebUI/releases/download/v5.0/UnityLocalizationToolkit-WebUI-v5.0-win-x64.msi) |
| **No-LLAMA** | [下载](https://github.com/HanFengRuYue/UnityLocalizationToolkit-WebUI/releases/download/v5.0/UnityLocalizationToolkit-WebUI-v5.0-win-x64-no-llama.zip) | [下载](https://github.com/HanFengRuYue/UnityLocalizationToolkit-WebUI/releases/download/v5.0/UnityLocalizationToolkit-WebUI-v5.0-win-x64-no-llama.msi) |
| **Lite（精简版）** | [下载](https://github.com/HanFengRuYue/UnityLocalizationToolkit-WebUI/releases/download/v5.0/UnityLocalizationToolkit-WebUI-v5.0-win-x64-lite.zip) | [下载](https://github.com/HanFengRuYue/UnityLocalizationToolkit-WebUI/releases/download/v5.0/UnityLocalizationToolkit-WebUI-v5.0-win-x64-lite.msi) |
<!-- DOWNLOAD_LINKS_END -->

- **Full**：自带应用、本地 `llama.cpp` 运行时和常用资源，推荐大多数用户。
- **No-LLAMA**：自带应用和翻译链路所需依赖，但不包含本地大模型运行时，适合只使用云端 API。
- **Lite**：体积最小，需要先安装 [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)。
- **本地 AI 适用环境**：NVIDIA 推荐 CUDA，AMD / Intel 推荐 Vulkan；没有独显时也可以退回 CPU。

## 三分钟快速上手

1. 在 **游戏库** 中添加游戏目录，工具会识别 Unity 版本、Mono / IL2CPP、架构和可执行文件。
2. 打开 **游戏详情**，使用 **一键安装** 自动部署 BepInEx、XUnity.AutoTranslator、AI 端点与推荐配置。
3. 进入 **AI 翻译** 页面，配置云端模型端点，或切换到 **本地 AI** 使用 `llama.cpp` 和本地 GGUF 模型。
4. 直接启动游戏开始实时翻译；如果是文本量很大的游戏，建议先做 **资产提取 / 预翻译**，再按需处理 **字体替换 / 字体生成**。

## 核心能力

- **一键部署翻译链路**：自动检测 Unity 游戏，安装 BepInEx 与 XUnity.AutoTranslator，并写入 AI 端点配置。
- **云端 AI 翻译**：支持 OpenAI、Claude、Gemini、DeepSeek、Qwen、GLM、Kimi 以及自定义 OpenAI 兼容接口。
- **本地 AI 模式**：内置 `llama.cpp` 管理能力，可下载运行时、导入 GGUF 模型并本地翻译。
- **资产提取与预翻译**：提取 `.assets` / AssetBundle 文本，批量写入翻译缓存，适合 JRPG、视觉小说等大文本游戏。
- **术语、翻译记忆与人工校对**：支持术语约束、翻译记忆、手动翻译工作台与译文编辑器。
- **字体与插件工具链**：支持 TMP / Legacy `Font` 替换、SDF 字体生成、插件健康检查、BepInEx 日志分析、插件包导入导出。

<details>
<summary><strong>完整流程（点击展开）</strong></summary>

### 1. 添加游戏并初始化翻译环境

- 游戏库支持单个添加，也支持批量扫描目录中的 Unity 游戏。
- 游戏详情页会展示 Unity 版本、脚本后端、架构、插件状态和常用操作。
- **一键安装** 会按顺序处理翻译链路部署、AI 端点写入、推荐配置应用，以及可选的资产提取与健康检查。

<img src="docs/readme/library-overview.png" alt="游戏库总览" width="100%">

<img src="docs/readme/game-detail-install.png" alt="游戏详情与安装流程" width="100%">

### 2. 配置 AI 翻译

- **云端模式**：在 **AI 翻译** 页面添加端点，支持优先级、启停、模型名和连接测试。
- **本地模式**：切换到 **本地 AI** 后，可按显卡情况选择模型、下载运行时、启动 `llama.cpp` 服务。
- 如果你只使用自建兼容接口，也可以直接选择 **Custom（OpenAI 兼容）**。

<img src="docs/readme/ai-translation.png" alt="AI 翻译配置" width="100%">

### 3. 资产提取、预翻译与手动校对

- 工具会缓存提取结果，方便反复进入页面继续处理。
- 预翻译支持语言检测、缓存优化、LLM 动态模式分析、多轮翻译和自动术语提取。
- 手动翻译工作台适合对重点资产做逐条校对、覆写和导出。

<img src="docs/readme/asset-pretranslation.png" alt="资产提取与预翻译流程" width="100%">

### 4. 字体替换与字体生成

- 字体替换支持 TMP 与 Legacy `Font` 两类资源。
- 可直接使用内置替换资源，也可以上传自定义 TMP / TTF / OTF 字体资源。
- 字体生成页可基于 TTF / OTF 生成 TMP SDF 字体，并按字符集输出结果。

<img src="docs/readme/font-replacement.png" alt="字体替换流程" width="100%">

### 5. 术语、译文编辑与插件包

- **术语编辑器**：管理翻译术语、禁翻词、分类、优先级、正则匹配和自动提取候选。
- **译文编辑器**：对 AI 输出做人工校对，并支持导入 / 导出。
- **插件包导入导出**：打包当前游戏的翻译插件、缓存与相关配置，方便分发给其他玩家。
- **日志与健康检查**：排查 BepInEx 插件冲突、缺失依赖、异常堆栈和兼容性问题。

</details>

## 常见问题

<details>
<summary><strong>我应该下载哪个版本？</strong></summary>

- 想省心，优先选 **Full**。
- 只打算使用云端 API，选 **No-LLAMA**。
- 已经安装 .NET 10 Runtime，且希望体积最小，选 **Lite**。

</details>

<details>
<summary><strong>云端 AI 和本地 AI 怎么选？</strong></summary>

- 云端模式配置更简单、更新更快，适合大多数用户。
- 本地模式更适合离线环境、长时间批量翻译，或不希望把文本发给第三方接口的场景。
- 本地模式对显卡、显存和磁盘空间要求更高。

</details>

<details>
<summary><strong>翻译后出现方块字或缺字怎么办？</strong></summary>

- 先进入 **字体替换** 页面扫描当前字体资源。
- 如果游戏依赖的 TMP / Legacy `Font` 不包含中文字符，可直接替换为内置字体或上传自定义字体。
- 需要完全自定义时，再进入 **字体生成** 页面生成 TMP SDF 字体。

</details>

<details>
<summary><strong>什么时候应该使用预翻译？</strong></summary>

- 文本量大、重复文本多、首进游戏时不想等待实时翻译时，优先使用预翻译。
- 视觉小说、JRPG、带大量剧情文本的游戏尤其适合。
- 短流程游戏或快速试用时，可以先直接用实时翻译。

</details>

<details>
<summary><strong>配置和缓存存在哪里？</strong></summary>

- 默认目录是 `%AppData%\\UnityLocalizationToolkit`。
- 可以通过应用内的 **导出配置 / 导入配置** 做迁移或备份。
- 开发与维护层面的目录约定和不变量请查看 [AGENTS.md](AGENTS.md)。

</details>

<details>
<summary><strong>可以把翻译成果发给别人吗？</strong></summary>

- 可以，优先使用 **插件包导出**。
- 它会把当前游戏的翻译插件、术语、缓存和部分相关配置打包出来，其他人可以直接导入。

</details>

<details>
<summary><strong>开发者说明（点击展开）</strong></summary>

### 环境要求

- Windows 10 / 11 x64
- .NET 10 SDK
- Node.js 20.19+ 或 22.12+

### 常用命令

```bash
dotnet build UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj
dotnet build UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj -p:SkipFrontendBuild=true
dotnet run --project UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj

cd UnityLocalizationToolkit-Vue
npm run dev
npm run build
npx vue-tsc --build

cd ..
.\build.ps1
.\build.ps1 -SkipDownload
```

### 开发模式说明

- 后端默认监听 `http://127.0.0.1:51821`。
- 前端开发代理也应保持 `127.0.0.1`，不要切换到 `localhost`。
- `UnityLocalizationToolkit-WebUI.csproj` 默认会在构建前自动执行前端依赖安装和生产构建。

### 关键子项目

- `UnityLocalizationToolkit-WebUI/`：ASP.NET Core Minimal API + WinForms / WebView2 宿主
- `UnityLocalizationToolkit-Vue/`：Vue 3 + TypeScript + Naive UI 前端
- `UnityLocalizationToolkit-WebUI.Tests/`：后端服务与基础设施测试
- `TranslatorEndpoint/`：提供给 XUnity.AutoTranslator 调用的 `LLMTranslate.dll`
- `Updater/`：AOT 更新器
- `Installer/`：WiX 安装工程

### 维护说明

- README 面向最终用户。
- 仓库约定、运行时数据布局、验证命令和长期约束统一记录在 [AGENTS.md](AGENTS.md) 及其侧边文件中。
- 调整构建或发布流程时，请同时检查 `build.ps1` 与 `.github/workflows/build.yml`。

</details>

## 许可证

本项目基于 [MIT License](LICENSE) 开源。
