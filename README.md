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

## 下载安装

| [**前往下载页面**](https://github.com/HanFengRuYue/XUnityToolkit-WebUI/releases/latest) |
|:---:|

---

## 目录

### 用户指南

- [功能概览](#功能概览)
- [系统要求](#系统要求)
- [使用教程](#使用教程)
  - [添加游戏](#1-添加游戏)
  - [安装翻译框架](#2-安装翻译框架)
  - [配置 AI 翻译](#3-配置-ai-翻译)
  - [使用本地大模型](#4-使用本地大模型)
  - [启动翻译](#5-启动翻译)
  - [预翻译](#6-预翻译)
  - [字体替换](#7-字体替换)
  - [术语表优化](#8-术语表优化)
  - [翻译编辑与校对](#9-翻译编辑与校对)
  - [插件包导出与分享](#10-插件包导出与分享)
- [支持的 AI 翻译服务](#支持的-ai-翻译服务)
- [常见问题](#常见问题)
- [详细功能介绍](#详细功能介绍)

### 开发者指南

- [从源码构建](#从源码构建)
- [开发模式](#开发模式)
- [项目结构](#项目结构)
- [技术栈](#技术栈)
- [数据存储](#数据存储)
- [构建命令速查](#构建命令速查)
- [许可证](#许可证)

---

## 功能概览

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
| **本地大模型** | NVIDIA 显卡需要 CUDA 12.4+；AMD/Intel 显卡需支持 Vulkan；也可使用 CPU 推理 |
| **开发环境** | .NET 10 SDK（Preview）、Node.js 20.19+ 或 22.12+ |

## 使用教程

### 1. 添加游戏

启动应用后，在游戏库界面点击 **「添加游戏」** 按钮，选择游戏所在文件夹。

- 工具会自动检测游戏的 Unity 引擎类型（Mono / IL2CPP）和架构（x86 / x64）
- 如果游戏目录中存在 `steam_appid.txt`，会自动从 Steam 获取游戏名称和封面图
- 也可以使用 **「自动检测添加」** 方式，工具会扫描所选文件夹中的所有 Unity 游戏并批量添加

> **提示**：添加游戏后，可以在游戏详情页上传自定义封面图、图标和背景图，或从 SteamGridDB / 网络搜索中选取。

### 2. 安装翻译框架

进入游戏详情页，点击 **「安装」** 按钮即可一键安装翻译框架。

- 安装过程会依次完成：下载框架文件 → 部署 BepInEx → 部署 XUnity.AutoTranslator → 配置翻译端点 → 写入配置文件
- 每个步骤的状态实时显示在安装进度面板中
- 安装完成后，工具会自动创建备份清单，后续可以通过 **「卸载」** 按钮完整还原游戏

> **提示**：如果游戏已经安装了 BepInEx（如安装了其他 Mod），工具会检测到并跳过 BepInEx 安装步骤，只部署翻译相关组件。

### 3. 配置 AI 翻译

进入 **「设置」** 页面，在 AI 翻译部分配置翻译提供商：

1. 选择一个 AI 提供商（如 OpenAI、DeepSeek、Claude 等）
2. 填入 API Key（密钥会使用 DPAPI 加密存储）
3. 选择或填入要使用的模型名称（可点击「获取模型列表」自动拉取可用模型）
4. 配置 API 基础 URL（如使用代理或自部署服务）
5. 点击 **「测试」** 验证配置是否正确

**负载均衡**：可以同时添加多个提供商，系统会自动将翻译请求分散到各个提供商，提高翻译速度。当某个提供商出错时，自动切换到其他可用提供商。

> **提示**：DeepSeek 和通义千问性价比高且对中文翻译效果好。如果追求翻译质量，可以选择 Claude 或 GPT-4o。

### 4. 使用本地大模型

如果不想使用云端 API 或希望完全离线翻译，可以在 **「本地大模型」** 页面配置：

1. 查看 **GPU 信息** — 工具会自动检测你的显卡型号和显存大小
2. 从 **模型目录** 中选择合适的模型（会根据你的显存大小标注适合的模型）
3. 点击 **「下载」**（支持暂停/续传），等待下载完成
4. 调整 **GPU 层数** 和 **上下文长度**（更多 GPU 层 = 更快但占用更多显存）
5. 点击 **「启动」** 运行本地模型服务
6. 启动后可以点击 **「测试」** 验证模型是否正常工作

本地模式启动后，AI 翻译会自动使用本地模型，无需额外配置提供商。

> **提示**：如果你已有 GGUF 格式的模型文件，可以使用 **「添加自有模型」** 直接添加，无需重新下载。

### 5. 启动翻译

安装翻译框架并配置好 AI 翻译后，直接启动游戏即可开始翻译：

- 游戏运行时，界面中出现的文本会自动被 XUnity.AutoTranslator 捕获
- 捕获的文本通过翻译端点（`LLMTranslate.dll`）发送到本工具的 API
- AI 翻译完成后，译文实时回显到游戏界面中
- 翻译过的文本会被缓存，下次遇到相同文本时直接使用缓存结果

在 **「AI 翻译」** 页面可以实时查看翻译统计信息，包括总翻译数、成功率、当前队列等。

### 6. 预翻译

对于文本量大的游戏（如 RPG、视觉小说），可以在游戏启动前进行预翻译：

1. 在游戏详情页点击 **「资产提取」** — 工具会扫描游戏的 `.assets` 和 AssetBundle 文件，提取所有可翻译文本
2. 提取完成后，查看提取到的文本数量和内容
3. 点击 **「预翻译」** — 开始批量翻译所有提取的文本
4. 翻译完成后，结果会自动写入 XUnity.AutoTranslator 的缓存文件

下次启动游戏时，已翻译的文本直接从缓存加载，无需等待 AI 实时翻译。

> **提示**：预翻译可以随时暂停/取消。已翻译的部分会保留，下次继续时自动跳过已完成的条目。

### 7. 字体替换

如果游戏翻译后出现方块字（「口口口」）或中文缺字，说明游戏使用的 TextMesh Pro 字体不包含中文字符，需要进行字体替换：

1. 在游戏详情页进入 **「字体替换」** 功能
2. 点击 **「扫描」** — 工具会扫描游戏中所有的 TMP 字体资产
3. 查看扫描结果，确认需要替换的字体
4. 选择替换方式：
   - **内置 TMP 字体**：使用预置的中文 TMP 字体（自动匹配 Unity 版本）
   - **自定义字体上传**：上传自己的 TMP 字体文件
5. 点击 **「替换」** 完成字体替换

**使用 SDF 字体生成器**（如果需要自定义字体）：

1. 进入 **「字体生成器」** 页面
2. 上传一个 TTF 或 OTF 中文字体文件（最大 50 MB）
3. 选择字符集（GB2312 覆盖常用中文，GBK 覆盖更多，CJK 完整覆盖所有 CJK 字符）
4. 选择渲染模式（SDFAA 兼顾质量和体积）
5. 点击 **「生成」**，等待完成后下载或直接安装到游戏中

> **提示**：替换字体前会自动备份原始文件，可以随时通过 **「还原」** 恢复。

### 8. 术语表优化

利用术语表提高翻译一致性和准确性：

1. 在游戏详情页进入 **「术语表」** 页面
2. **手动添加术语**：输入原文和对应的译文（如 `Dragonborn` → `龙裔`）
3. **AI 自动提取**：在 AI 翻译设置中开启「自动提取术语」，AI 会在翻译过程中自动识别并添加专有名词
4. **编写游戏描述**：在 **「AI 描述」** 中编写游戏的背景介绍，帮助 AI 理解游戏世界观

**设置禁翻列表**：

1. 进入游戏的 **「禁翻列表」** 页面
2. 添加不需要翻译的词汇（如英文技能名 `Fireball`、物品代码 `ITEM_001`）
3. 可以为每个条目单独设置是否大小写敏感

> **提示**：术语表和禁翻列表是按游戏独立维护的，互不影响。

### 9. 翻译编辑与校对

对 AI 翻译结果进行人工校对：

1. 在游戏详情页进入 **「翻译编辑器」**
2. 浏览所有已翻译的条目，找到需要修改的译文
3. 直接编辑译文内容，保存后立即生效
4. 可以将翻译结果 **导出** 为文件，或 **导入** 外部翻译文件

### 10. 插件包导出与分享

将完整的翻译配置打包分享给其他玩家：

1. 在游戏详情页找到 **「插件包」** 功能
2. 点击 **「导出」** — 工具会将翻译框架、翻译端点、术语表、翻译缓存等打包为 ZIP 文件
3. 将 ZIP 文件分享给其他玩家
4. 其他玩家在相同游戏的详情页使用 **「导入」** 即可一键还原所有翻译配置

> **提示**：导入时会自动覆盖已有的翻译配置，建议在导入前先备份。

---

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

---

## 常见问题

**Q：翻译速度很慢怎么办？**

- 配置多个 AI 提供商实现负载均衡，翻译请求会自动分散到各提供商
- 使用预翻译功能提前批量翻译，避免游戏中实时等待
- 如果使用本地大模型，可以增加 GPU 层数提高推理速度

**Q：游戏翻译后出现方块字 / 乱码 / 缺字？**

- 这是因为游戏使用的 TextMesh Pro 字体不包含中文字符
- 使用「字体替换」功能替换为中文 TMP 字体即可解决
- 如果内置字体不满足需求，可以使用「字体生成器」从任意中文字体生成 SDF 字体

**Q：游戏无法启动或闪退？**

- 使用「BepInEx 日志分析」功能，AI 会自动诊断问题原因
- 常见原因包括：BepInEx 版本不兼容、其他 Mod 冲突等
- 如果问题持续，可以通过「卸载」功能完整还原游戏文件

**Q：API Key 安全吗？**

- 所有 API 密钥使用 Windows DPAPI 加密存储在本地，不会上传到任何服务器
- 程序只会将密钥发送到你配置的 AI 提供商 API 地址

**Q：支持哪些 Unity 游戏？**

- 理论上支持所有使用 Unity 引擎的 Windows 游戏（Mono 和 IL2CPP 均支持）
- 部分使用特殊加密或反作弊的游戏可能不兼容

**Q：如何更新工具？**

- MSI 安装版支持在线增量更新，在「设置」页面检查更新即可
- 便携版同样支持在线更新，更新器会自动处理文件替换
- 也可以手动下载最新版本覆盖安装

---

## 详细功能介绍

### 🎮 一键安装翻译框架

XUnityToolkit 会自动检测游戏的 Unity 引擎类型和架构，为你选择正确的框架版本：

- **Mono 游戏**：安装 BepInEx 5 + XUnity.AutoTranslator
- **IL2CPP 游戏**：安装 BepInEx 6 BE + XUnity.AutoTranslator
- **架构检测**：自动识别 x86 / x64，选择对应版本
- **安全备份**：安装前自动创建备份清单，支持完整卸载还原，不留残余文件
- **进度追踪**：安装过程中每个步骤的状态实时显示，包括下载、解压、配置写入等

安装完成后，工具会自动配置好翻译端点（`LLMTranslate.dll`），使游戏中的文本能够自动发送到 AI 进行翻译。

### 🤖 AI 翻译

支持接入多家主流大模型 API，提供灵活的翻译配置：

- **多提供商**：同时配置多个 API 提供商（如 OpenAI + DeepSeek），系统会自动进行负载均衡，提高翻译速度和稳定性
- **实时翻译**：游戏运行时，文本自动发送到 AI 翻译并实时回显到游戏界面
- **批量翻译**：预翻译模式下支持并发批量翻译，通过信号量控制并发数
- **翻译统计**：实时显示翻译进度、成功率、错误信息等统计数据
- **自定义系统提示词**：可自定义翻译提示词，包含 7 条默认规则，支持 `{from}`/`{to}` 语言变量替换
- **API Key 加密**：所有 API 密钥使用 Windows DPAPI 加密存储，保障安全

### 🖥️ 本地大模型

无需云端 API，完全离线运行 AI 翻译：

- **自动 GPU 检测**：通过 DXGI（主要方式）和 WMI（备用方式）检测显卡型号和显存大小，自动匹配合适的模型
- **多后端支持**：根据显卡类型自动选择最优推理后端
  - NVIDIA 显卡：CUDA 后端（需 CUDA 12.4+）
  - AMD / Intel 显卡：Vulkan 后端
  - 无独立显卡：CPU 推理
- **模型管理**：内置模型目录，支持一键下载、暂停、续传；也可手动添加已有的 GGUF 模型文件
- **参数调节**：可配置 GPU 层数、上下文长度等参数，在性能和显存占用之间取得平衡
- **内置 llama.cpp**：预编译的 llama-server 二进制文件作为 ZIP 捆绑，首次使用时自动解压

### 📦 资产提取与预翻译

无需启动游戏即可批量翻译游戏文本，实现「启动即汉化」：

- **资产提取**：使用 AssetsTools.NET 解析 Unity `.assets` 文件和 AssetBundle，提取所有可翻译文本字符串
- **智能过滤**：自动过滤纯数字、路径、代码等非自然语言文本
- **批量预翻译**：将提取的文本批量发送给 AI 翻译，翻译结果直接写入 XUnity.AutoTranslator 的缓存文件
- **进度控制**：支持暂停/取消预翻译任务，进度实时显示
- **缓存复用**：已翻译的文本会被缓存，游戏启动时直接加载，无需重复翻译

> **适用场景**：对于文本量大的游戏（如 RPG、视觉小说），预翻译可以避免游戏中等待翻译的延迟，提供更流畅的体验。

### 🔤 字体替换与生成

解决中文翻译后游戏内「口口口」乱码或缺字问题：

- **字体扫描**：自动扫描游戏的 `.assets` 和 AssetBundle 文件，找出所有 TextMesh Pro（TMP）字体资产
- **一键替换**：将游戏内的 TMP 字体替换为包含中文字符的版本，保留原有 PPtr 引用关系
- **内置 TMP 字体**：根据游戏的 Unity 版本自动匹配预置的中文 TMP 字体
- **自定义字体上传**：支持上传自己的 TMP 字体文件用于替换
- **SDF 字体生成器**：从 TTF/OTF 字体文件生成 TMP 兼容的 SDF 字体
  - 支持多种渲染模式：SDFAA / SDF8 / SDF16 / SDF32
  - 内置字符集：GB2312、GBK、CJK 常用、CJK 完整、日文等
  - 自定义字符集：从 TXT 文件或 XUnity 翻译文件中提取所需字符
  - 多图集支持，自动图集打包
- **安全还原**：替换前自动备份原始字体资产，支持一键还原
- **Addressables 兼容**：自动清除 Addressables CRC 校验，避免替换后加载失败

### 📚 游戏库管理

集中管理所有已添加的 Unity 游戏：

- **自动检测**：选择游戏文件夹后，自动识别 Unity 引擎版本、游戏类型（Mono/IL2CPP）、架构（x86/x64）
- **封面与图标**：自动从游戏可执行文件提取图标；支持从 Steam、SteamGridDB、网络搜索获取封面图、背景图
- **自定义外观**：可上传自定义封面图、图标和背景图
- **快捷操作**：一键打开游戏文件夹、启动游戏
- **游戏重命名**：支持修改游戏显示名称

### 📖 术语表与翻译记忆

提高翻译一致性，让 AI 准确翻译游戏专有名词：

- **自定义术语**：为每个游戏维护独立的术语表，指定角色名、地名、技能名等专有名词的翻译方式
- **AI 自动提取**：开启后，AI 在翻译过程中自动识别和积累新的专有名词
- **正则表达式**：术语表支持正则表达式匹配，处理复杂的翻译模式
- **禁翻列表**：指定不需要翻译的词汇（如英文技能名、物品代码），支持大小写敏感控制。禁翻词在翻译前被替换为占位符 `{{DNT_x}}`，翻译后还原，确保 AI 不会修改这些词
- **AI 游戏描述**：为每个游戏编写背景描述，帮助 AI 理解游戏的世界观和语境，提高翻译质量
- **术语表上限**：单个游戏最多 5000 条术语，禁翻列表最多 10000 条

### ✏️ 翻译编辑器

手动编辑和校对翻译结果：

- **浏览翻译**：查看游戏的所有翻译条目（原文 → 译文）
- **在线编辑**：直接修改译文，保存后立即生效
- **导入/导出**：支持导入外部翻译文件，或将翻译结果导出为标准格式，方便与他人分享

### 📋 BepInEx 日志分析

快速诊断游戏运行中的插件问题：

- **日志读取**：读取游戏目录下的 `BepInEx/LogOutput.log`，支持与运行中的游戏共享文件访问
- **AI 诊断**：将日志发送给 AI 进行智能分析，自动识别插件加载失败、版本不兼容、运行时错误等常见问题，并给出解决建议
- **日志下载**：支持下载完整日志文件

### ⚙️ 更多功能

- **配置可视化编辑**：图形化编辑 XUnity.AutoTranslator 的 INI 配置文件，无需手动修改文本
- **原始配置编辑**：直接编辑 INI 原始文本（上限 512 KB），满足高级用户需求
- **插件包导出/导入**：将游戏的翻译配置（框架 + 翻译端点 + 术语表 + 翻译缓存）打包为 ZIP，方便分享给其他玩家；导入时自动还原
- **在线更新**：基于 GitHub Releases 的差分增量更新，自动下载变更的组件；`Updater.exe`（AOT 编译，无运行时依赖）负责文件替换和重启；支持回滚
- **系统托盘**：最小化到系统托盘运行，翻译状态通知
- **深色/浅色主题**：跟随系统或手动切换
- **镜像加速**：内置 GitHub 镜像加速选项，解决国内下载慢的问题

---

## 从源码构建

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

## 开发模式

需要同时运行后端和前端开发服务器：

```bash
# 终端 1：启动后端
dotnet run --project XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj

# 终端 2：启动前端开发服务器（带热更新）
cd XUnityToolkit-Vue && npm install && npm run dev
```

前端开发服务器会自动将 `/api` 和 `/hubs` 请求代理到后端。

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
