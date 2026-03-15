<div align="center">

<img src="XUnityToolkit-Vue/public/logo.png" alt="XUnityToolkit-WebUI" width="128" height="128">

# XUnityToolkit-WebUI

**Unity 游戏翻译工具箱** — 一键安装翻译框架，集成 AI 翻译、本地大模型、资产提取、字体替换

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Vue](https://img.shields.io/badge/Vue-3-4FC08D?logo=vue.js)](https://vuejs.org/)
[![TypeScript](https://img.shields.io/badge/TypeScript-5-3178C6?logo=typescript&logoColor=white)](https://www.typescriptlang.org/)
[![GitHub Release](https://img.shields.io/github/v/release/HanFengRuYue/XUnityToolkit-WebUI?color=brightgreen&logo=github)](https://github.com/HanFengRuYue/XUnityToolkit-WebUI/releases)
[![GitHub Stars](https://img.shields.io/github/stars/HanFengRuYue/XUnityToolkit-WebUI?style=social)](https://github.com/HanFengRuYue/XUnityToolkit-WebUI/stargazers)

[下载发布版](https://github.com/HanFengRuYue/XUnityToolkit-WebUI/releases) · [报告问题](https://github.com/HanFengRuYue/XUnityToolkit-WebUI/issues) · [功能建议](https://github.com/HanFengRuYue/XUnityToolkit-WebUI/issues)

---

</div>

## 功能特性

<table>
<tr>
<td width="50%">

### 🎮 一键安装
自动检测 Unity 游戏类型（Mono / IL2CPP），一键部署 [BepInEx](https://github.com/BepInEx/BepInEx) + [XUnity.AutoTranslator](https://github.com/bbepis/XUnity.AutoTranslator) 翻译框架

### 🤖 AI 翻译
接入 OpenAI / Claude / Gemini / DeepSeek / 通义千问 / 智谱 GLM / Kimi 等多家大模型 API，支持多提供商负载均衡

### 🖥️ 本地大模型
内置 llama.cpp，自动检测 GPU 并选择最优后端（CUDA / Vulkan / CPU），支持一键下载和运行本地模型

### 📦 资产提取 & 预翻译
从 Unity `.assets` 和 AssetBundle 中提取文本，批量翻译并写入缓存，实现启动即汉化

### 🔤 字体替换 & 生成
扫描并替换游戏内 TextMesh Pro 字体，内置 SDF 字体生成器（支持 GB2312/GBK/CJK 等字符集），解决中文乱码/缺字问题

</td>
<td width="50%">

### 📚 游戏库管理
添加、组织游戏，自动获取封面/图标/背景图（支持 Steam、SteamGridDB、网络搜索）

### 📖 术语表 & 翻译记忆
自定义翻译术语，AI 自动提取专有名词，按游戏维护翻译上下文；支持「禁翻列表」保护特定词汇不被翻译

### ✏️ 翻译编辑器
手动编辑和校对翻译结果，支持导入/导出翻译文件

### 📋 BepInEx 日志分析
读取 BepInEx 运行日志，AI 智能诊断插件错误和兼容性问题

### ⚙️ 更多功能
配置可视化编辑 · 插件包导出/导入 · 在线更新 · 系统托盘 · 深色/浅色主题 · 镜像加速

</td>
</tr>
</table>

## 系统要求

| 项目 | 要求 |
|------|------|
| **操作系统** | Windows 10 / 11（x64） |
| **运行时** | 无需额外安装（发布版为自包含应用） |
| **开发环境** | .NET 10 SDK（Preview）、Node.js 20.19+ 或 22.12+ |

## 快速开始

### 安装方式

**方式一：MSI 安装包（推荐）**

从 [Releases](https://github.com/HanFengRuYue/XUnityToolkit-WebUI/releases) 下载 `.msi` 安装包，双击安装。程序安装到 `%LocalAppData%\Programs\`，数据存储在 `%AppData%\XUnityToolkit\`，支持在线增量更新。

**方式二：便携版**

从 [Releases](https://github.com/HanFengRuYue/XUnityToolkit-WebUI/releases) 下载 ZIP 压缩包，解压到任意目录，运行 `XUnityToolkit-WebUI.exe`。所有数据保存在程序目录下的 `data/` 文件夹中，不写注册表，可随意移动。

> 启动后浏览器自动打开 http://127.0.0.1:51821

### 从源码构建

```bash
# 克隆仓库
git clone https://github.com/HanFengRuYue/XUnityToolkit-WebUI.git
cd XUnityToolkit-WebUI

# 一键发布（下载捆绑资产 + 构建前端 + 发布后端）
.\build.ps1

# 跳过资产下载（使用已缓存的）
.\build.ps1 -SkipDownload

# 构建产物位于 Release/win-x64/
```

### 开发模式

需要同时运行后端和前端开发服务器：

```bash
# 终端 1：启动后端
dotnet run --project XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj

# 终端 2：启动前端开发服务器（带热更新）
cd XUnityToolkit-Vue && npm install && npm run dev
```

前端开发服务器会自动将 `/api` 和 `/hubs` 请求代理到后端。

## 使用指南

### 1. 添加游戏

启动应用后，在游戏库界面点击 **「添加游戏」** 按钮，选择游戏所在文件夹。工具会自动检测游戏类型（Mono / IL2CPP）和架构（x86 / x64）。如果目录中存在 `steam_appid.txt`，会自动从 Steam 获取封面图。

### 2. 安装翻译框架

进入游戏详情页，点击 **「安装」** 按钮即可一键安装 BepInEx + XUnity.AutoTranslator。安装过程中的进度会实时显示在抽屉面板中。

### 3. 配置 AI 翻译

进入 **「设置」** 页面，添加至少一个 AI 翻译提供商（如 OpenAI、DeepSeek 等），填入 API Key 和模型名称。支持同时配置多个提供商实现负载均衡。

### 4. 使用本地大模型（可选）

如果不想使用云端 API，可以在 **「AI 翻译」** 页面切换到本地模式：
- 从模型列表中选择合适的模型（会根据你的 GPU 显存推荐）
- 点击下载，完成后启动即可使用
- 支持 NVIDIA（CUDA）、AMD/Intel（Vulkan）和 CPU 推理

### 5. 启动翻译

安装完成后，直接启动游戏。游戏中出现的文本会自动发送到 AI 进行翻译，翻译结果实时显示在游戏界面中。

### 6. 预翻译（推荐）

如果希望启动游戏时就有完整的翻译，可以在游戏详情页使用 **「资产提取」** 提取游戏内文本，然后点击 **「预翻译」** 进行批量翻译。翻译结果会写入 XUnity 缓存文件，下次启动游戏时直接加载。

### 7. 字体替换（按需）

部分游戏使用 TextMesh Pro 字体，可能不包含中文字符。在游戏详情页的 **「字体替换」** 中扫描游戏字体资产，选择替换为内置或自定义的中文 TMP 字体。也可以使用 **「字体生成器」** 从 TTF/OTF 字体生成包含所需字符集的 SDF 字体。

### 8. 术语表优化

在游戏的 **「术语表」** 页面可以自定义翻译术语（如角色名、专有名词）。开启自动提取后，AI 会在翻译过程中自动识别和积累专有名词。还可以设置 **「禁翻列表」**，保护特定词汇（如技能名、物品名）不被翻译。

## 支持的 AI 翻译服务

| 提供商 | 说明 |
|--------|------|
| **OpenAI** | GPT-4o / GPT-4o mini 等 |
| **Claude** | Anthropic Claude 系列 |
| **Gemini** | Google Gemini 系列 |
| **DeepSeek** | DeepSeek-V3 / DeepSeek-R1 等 |
| **通义千问** | 阿里云 Qwen 系列 |
| **智谱 GLM** | 智谱 AI 大模型 |
| **Kimi** | Moonshot AI 大模型 |
| **本地模型** | 通过内置 llama.cpp 运行（Qwen2.5 等） |
| **自定义** | 任何兼容 OpenAI API 格式的服务 |

## 项目结构

```
XUnityToolkit-WebUI/
├── XUnityToolkit-WebUI/          # ASP.NET Core 后端
│   ├── Endpoints/                #   Minimal API 端点定义
│   ├── Services/                 #   业务逻辑服务（30+ 个服务）
│   ├── Models/                   #   数据模型
│   ├── Hubs/                     #   SignalR Hub（实时通信）
│   └── Resources/                #   嵌入资源（classdata.tpk）
├── XUnityToolkit-Vue/            # Vue 3 前端
│   └── src/
│       ├── api/                  #   API 客户端和类型定义
│       ├── components/           #   UI 组件（按功能分组）
│       ├── composables/          #   组合式函数
│       ├── stores/               #   Pinia 状态管理
│       ├── views/                #   页面视图（12 个页面）
│       └── router/               #   路由配置
├── TranslatorEndpoint/           # XUnity 自定义翻译端点（net35 DLL）
├── Updater/                      # 在线更新器（AOT 编译，无运行时依赖）
├── Installer/                    # MSI 安装包（WiX v5）
├── bundled/                      # 捆绑资产（构建时下载）
│   ├── bepinex5/                 #   BepInEx 5（Mono 游戏）
│   ├── bepinex6/                 #   BepInEx 6 BE（IL2CPP 游戏）
│   ├── xunity/                   #   XUnity.AutoTranslator
│   ├── llama/                    #   llama.cpp 预编译二进制（CUDA / Vulkan / CPU）
│   └── fonts/                    #   TextMesh Pro 预置字体
└── build.ps1                     # 一键构建脚本
```

## 技术栈

| 层级 | 技术 |
|------|------|
| **后端** | ASP.NET Core Minimal API (.NET 10.0) |
| **前端** | Vue 3 + TypeScript + Naive UI + Pinia |
| **实时通信** | SignalR |
| **构建工具** | Vite 7 |
| **资产解析** | AssetsTools.NET 3.0.4 |
| **字体生成** | FreeType（渲染）+ Felzenszwalb EDT（SDF 生成） |
| **本地推理** | llama.cpp（CUDA / Vulkan / CPU） |
| **翻译端点** | .NET Framework 3.5 DLL（LLMTranslate.dll） |
| **安装包** | WiX Toolset v5（MSI） |
| **在线更新** | 差分下载 + 原子替换（Updater.exe, AOT） |
| **GPU 检测** | DXGI（主）+ WMI（备） |
| **数据加密** | DPAPI（Windows 数据保护 API） |

## 数据存储

应用支持两种运行模式：
- **安装模式**（MSI 安装）：数据存储在 `%AppData%\XUnityToolkit\`
- **便携模式**（ZIP 解压）：数据存储在程序目录下的 `data/`

| 路径 | 说明 |
|------|------|
| `data/library.json` | 游戏库数据 |
| `data/settings.json` | 应用设置（API 密钥经 DPAPI 加密） |
| `data/local-llm-settings.json` | 本地大模型设置 |
| `data/backups/` | 游戏备份清单（用于卸载还原） |
| `data/cache/` | 封面、图标、背景图缓存 |
| `data/do-not-translate/` | 按游戏维护的禁翻列表 |
| `data/font-backups/` | 字体替换备份 |
| `data/custom-fonts/` | 用户上传的自定义字体 |
| `data/generated-fonts/` | SDF 字体生成器输出 |
| `data/logs/` | 应用日志 |
| `data/llama/` | llama.cpp 解压后的可执行文件 |
| `data/models/` | 下载的本地模型文件 |
| `data/update-staging/` | 在线更新暂存区 |

## 构建命令速查

```bash
# 构建后端（自动构建前端）
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj

# 跳过前端构建
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true

# 构建 TranslatorEndpoint DLL
dotnet build TranslatorEndpoint/TranslatorEndpoint.csproj -c Release

# 前端类型检查
cd XUnityToolkit-Vue && npx vue-tsc --noEmit

# 一键发布（win-x64）
.\build.ps1

# 跳过资产下载
.\build.ps1 -SkipDownload
```

## Star History

<div align="center">

<a href="https://star-history.com/#HanFengRuYue/XUnityToolkit-WebUI&Date">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/svg?repos=HanFengRuYue/XUnityToolkit-WebUI&type=Date&theme=dark" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/svg?repos=HanFengRuYue/XUnityToolkit-WebUI&type=Date" />
   <img alt="Star History Chart" src="https://api.star-history.com/svg?repos=HanFengRuYue/XUnityToolkit-WebUI&type=Date" />
 </picture>
</a>

</div>

## 许可证

本项目基于 [MIT License](LICENSE) 开源。

---

<div align="center">

**如果这个项目对你有帮助，欢迎给一个 Star ⭐**

</div>
