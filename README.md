# XUnityToolkit-WebUI

Unity 游戏翻译工具箱，提供一键安装 [BepInEx](https://github.com/BepInEx/BepInEx) + [XUnity.AutoTranslator](https://github.com/bbepis/XUnity.AutoTranslator) 翻译框架，并集成 AI 翻译、本地大模型、资产提取等功能。使用本地 WebUI 作为操作界面。

## 功能特性

* **一键安装** — 自动检测 Unity 游戏类型（Mono / IL2CPP），一键部署 BepInEx 和 XUnity.AutoTranslator
* **游戏库管理** — 添加、组织游戏，自动获取封面/图标/背景图（支持 Steam、SteamGridDB、网络搜索）
* **AI 翻译** — 接入 OpenAI / Claude / Gemini / DeepSeek / 通义千问 / 智谱 GLM / Kimi 等多家大模型 API，支持多提供商负载均衡
* **本地大模型** — 内置 llama.cpp，自动检测 GPU 并选择最优后端（CUDA / Vulkan / CPU），支持一键下载和运行本地模型
* **资产提取** — 使用 AssetsTools.NET 从 Unity `.assets` 和 AssetBundle 中提取文本字符串
* **预翻译** — 批量翻译提取的文本并写入 XUnity 缓存，实现启动即汉化
* **术语表** — 自定义翻译术语，支持 AI 自动提取专有名词
* **翻译记忆** — 按游戏维护翻译上下文，提升翻译一致性
* **翻译编辑器** — 手动编辑和校对翻译结果
* **配置管理** — 通过 Web UI 可视化编辑 XUnity 翻译设置
* **插件包导出/导入** — 将完整的 BepInEx + XUnity 环境打包为 ZIP，便于分享
* **系统托盘** — 最小化到系统托盘，支持通知提示
* **深色/浅色主题** — 支持主题切换和自定义主题色

## 系统要求

* **操作系统：** Windows 10 / 11（x64 或 ARM64）
* **运行时：** 无需额外安装（发布版为自包含单文件）
* **开发环境：** .NET 10 SDK、Node.js 20.19+ 或 22.12+

## 快速开始

### 使用发布版

1. 从 Releases 下载对应架构的压缩包（`win-x64` 或 `win-arm64`）
2. 解压到任意目录
3. 运行 `XUnityToolkit-WebUI.exe`
4. 浏览器自动打开 `http://127.0.0.1:51821`

### 从源码构建

```bash
# 克隆仓库
git clone https://github.com/your-repo/XUnityToolkit-WebUI.git
cd XUnityToolkit-WebUI

# 一键发布（下载捆绑资产 + 构建前端 + 发布后端）
.\\build.ps1                    # 同时构建 win-x64 和 win-arm64
.\\build.ps1 -Runtime win-x64  # 仅构建 x64

# 构建产物位于 Release/win-x64/ 或 Release/win-arm64/
```

### 开发模式

需要同时运行后端和前端开发服务器：

```bash
# 终端 1：启动后端
dotnet run --project XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj

# 终端 2：启动前端开发服务器（带热更新）
cd XUnityToolkit-Vue
npm install
npm run dev
```

前端开发服务器会自动将 `/api` 和 `/hubs` 请求代理到后端。

## 项目结构

```
XUnityToolkit-WebUI/
├── XUnityToolkit-WebUI/          # ASP.NET Core 后端
│   ├── Endpoints/                # Minimal API 端点定义
│   ├── Services/                 # 业务逻辑服务
│   ├── Models/                   # 数据模型
│   ├── Hubs/                     # SignalR Hub（实时通信）
│   └── Resources/                # 嵌入资源（classdata.tpk）
├── XUnityToolkit-Vue/            # Vue 3 前端
│   └── src/
│       ├── api/                  # API 客户端和类型定义
│       ├── components/           # UI 组件（按功能分组）
│       ├── composables/          # 组合式函数
│       ├── stores/               # Pinia 状态管理
│       ├── views/                # 页面视图
│       └── router/               # 路由配置
├── TranslatorEndpoint/           # XUnity 自定义翻译端点（net35 DLL）
├── bundled/                      # 捆绑资产（构建时下载）
│   ├── bepinex5/                 # BepInEx 5（Mono 游戏）
│   ├── bepinex6/                 # BepInEx 6 BE（IL2CPP 游戏）
│   ├── xunity/                   # XUnity.AutoTranslator
│   ├── llama/                    # llama.cpp 预编译二进制
│   └── fonts/                    # TextMesh Pro 字体
└── build.ps1                     # 一键构建脚本
```

## 技术栈

|层级|技术|
|-|-|
|后端|ASP.NET Core Minimal API (.NET 10.0)|
|前端|Vue 3 + TypeScript + Naive UI + Pinia|
|实时通信|SignalR|
|构建工具|Vite 7|
|资产解析|AssetsTools.NET 3.0.4|
|本地推理|llama.cpp（CUDA / Vulkan / CPU）|
|翻译端点|.NET Framework 3.5 DLL（LLMTranslate.dll）|
|GPU 检测|DXGI（主）+ WMI（备）|
|数据加密|DPAPI（Windows 数据保护 API）|

## 构建命令

```bash
# 构建后端（自动构建前端）
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj

# 跳过前端构建
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true

# 构建 TranslatorEndpoint DLL
dotnet build TranslatorEndpoint/TranslatorEndpoint.csproj -c Release

# 前端类型检查
cd XUnityToolkit-Vue \&\& npx vue-tsc --noEmit

# 一键发布
.\\build.ps1                        # 完整构建
.\\build.ps1 -Runtime win-x64      # 仅 x64
.\\build.ps1 -SkipDownload         # 跳过资产下载（使用已缓存的）
```

## 数据存储

应用采用便携式设计，所有运行时数据存储在程序目录下的 `data/` 文件夹中：

|路径|说明|
|-|-|
|`data/library.json`|游戏库数据|
|`data/settings.json`|应用设置（API 密钥经 DPAPI 加密）|
|`data/local-llm-settings.json`|本地大模型设置|
|`data/backups/`|游戏备份清单（用于卸载还原）|
|`data/cache/`|封面、图标、背景图缓存|
|`data/logs/`|应用日志|
|`data/llama/`|llama.cpp 解压后的可执行文件|
|`data/models/`|下载的本地模型文件|

## 支持的 AI 翻译服务

|提供商|说明|
|-|-|
|OpenAI|GPT 系列模型|
|Claude|Anthropic Claude 系列|
|Gemini|Google Gemini 系列|
|DeepSeek|DeepSeek 系列|
|通义千问|阿里云大模型|
|智谱 GLM|智谱 AI 大模型|
|Kimi|Moonshot AI 大模型|
|本地模型|通过内置 llama.cpp 运行|
|自定义|任何兼容 OpenAI API 格式的服务|

## 许可证

本项目基于 [MIT License](LICENSE) 开源。

