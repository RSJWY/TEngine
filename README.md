# TEngine

<div align="center">

![TEngine Logo](Books/src/TEngine.png)

**Unity 框架解决方案**

[![Unity Version](https://img.shields.io/badge/Unity-2021.3.20%2B-blue.svg?style=flat-square)](https://unity3d.com/)
[![License](https://img.shields.io/github/license/ALEXTANGXIAO/TEngine?style=flat-square)](LICENSE)
[![Last Commit](https://img.shields.io/github/last-commit/ALEXTANGXIAO/TEngine?style=flat-square)](https://github.com/ALEXTANGXIAO/TEngine)
[![Issues](https://img.shields.io/github/issues/ALEXTANGXIAO/TEngine?style=flat-square)](https://github.com/ALEXTANGXIAO/TEngine/issues)
[![Top Language](https://img.shields.io/github/languages/top/ALEXTANGXIAO/TEngine?style=flat-square)](https://github.com/ALEXTANGXIAO/TEngine)
[![DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/Alex-Rachel/TEngine)

</div>

---

## 🛠️ 本 Fork 的定制改动

> 本仓库 fork 自上游 [ALEXTANGXIAO/TEngine](https://github.com/ALEXTANGXIAO/TEngine)，在其基础上围绕**热更新、资源打包、运行时配置、场景加载、数据绑定、存档与数据中心、日志工具、窗口控制、代码混淆、运行时工具、计时器模块、UI 组件扩展**做了定制改造。

| 主题 | 主要改动 |
| --- | --- |
| 日志系统 | TouchSocket 日志桥接、Unity 日志落盘、LogViewer 工具 |
| 事件系统 | `GameEvent.RemoveAllListeners` 按事件 ID 批量移除监听 |
| 数据绑定 | 纯数据 DataBinding 运行时、生成器和 Odin 面板 |
| 运行时配置 | `RuntimeConfigModule`、`DeployConfig`、TOML/JSON 轻量配置 |
| 热更新 | 独立 `CodePackage`、XXTEA、AOT 元数据清单、版本确认流程 |
| 资源打包 | 按包构建管线、发布整理、Odin 化打包窗口 |
| 场景系统 | DynamicSpawn 通用化、加载进度下沉到 `GameSceneModule` |
| 窗口管理 | Windows Standalone 多显示器窗口布局控制 `ScreenModule` |
| 代码混淆 | Obfuz 接入、dnlib 冲突解决、本地包同步脚本 |
| 运行时工具 | `GameTickWatcher` 逻辑计时器（独立 `RuntimeTools` 程序集） |
| 计时器模块 | `TimerModule` 链表化、坏帧安全、限定循环次数 |
| 存档与数据中心 | `ClientSaveDataMgr` 存档框架、`DataCenterSys` 玩家数据中枢 |
| UI 组件扩展 | `UIButton`/`UIImage`/`UIText`/`RichTextItem` + `ListPool` 公共化 |

详细设计、使用方式和关键文件见 [Fork 定制改动总览](Books/Fork/README.md)。按时间查看改动见 [Fork 改动时间线](Books/Fork/CHANGELOG.md)。

---

## 📖 简介

**TEngine** 是一个简单（新手友好、开箱即用）且强大的 Unity 框架全平台解决方案。对于需要一套上手快、文档清晰、高性能且可拓展性极强的商业级解决方案的开发者或团队来说，TEngine 是一个很好的选择。

### ✨ 核心特性

- 🚀 **开箱即用** - 5 分钟即可上手整套开发流程，代码整洁，思路清晰
- 🔥 **高性能** - 基于 UniTask 的异步系统，零 GC 事件分发，严格的内存管理
- 🧩 **高内聚低耦合** - 模块化设计，可轻松移除或替换不需要的模块
- 🔄 **热更新支持** - 集成 HybridCLR，全平台热更新流程已跑通
- 📦 **资源管理** - 集成 YooAsset，支持 LRU、ARC 缓存策略，自动资源释放
- 📊 **配置表系统** - 集成 Luban，支持懒加载、异步加载、同步加载
- 🎨 **UI 框架** - 商业化 UI 开发流程，支持代码自动生成
- 🌍 **全平台支持** - Windows、Android、iOS、WebGL、微信小游戏等

---

## 🚀 快速开始

### 环境要求

- **Unity 版本**: 2021.3.20f1c1（推荐）或更高
- **Odin Inspector**: 仓库不内置该插件，请自行从 Odin 官方渠道下载并导入项目后再打开工程
- **开发环境**: .NET 4.x
- **支持平台**: Windows、OSX、Android、iOS、WebGL

### 快速上手

1. **克隆项目**
   ```bash
   git clone https://github.com/ALEXTANGXIAO/TEngine.git
   ```

2. **打开项目** - 使用 Unity 2021.3.20f1c1 打开项目

3. **编辑器模式运行**
   - 选择顶部栏目 `EditorMode` 编辑器下的模拟模式
   - 点击 `Launcher` 开始运行

4. **打包运行**（热更新流程） - 运行菜单 `HybridCLR/Install...` 安装 HybridCLR，随后 `HybridCLR/Generate/All` 生成代码、`YooAsset/AssetBundle Builder` 构建 AB，最后 Build And Run

> 💡 遇到问题请查看 [HybridCLR 常见错误](https://hybridclr.doc.code-philosophy.com/docs/help/commonerrors)

详细教程请参考：[快速开始指南](Books/1-快速开始.md)

---

## 🤖 AI 开发工作流

TEngine 深度集成了一套面向 Claude Code 的 AI 辅助开发工作流，通过 **tengine-dev skill 按需查询架构**、**任务等级分级触发**和**会话内缓存机制**，实现规范驱动、高效的 AI 开发体验。

详细规范请参考：[CLAUDE.md](UnityProject/CLAUDE.md) | [AI 开发工作流指南](Books/AI-Development-Workflow.md)

---

## 📚 文档导航

### 基础文档

| 文档 | 描述 |
|------|------|
| [📖 介绍](Books/0-介绍.md) | TEngine 框架介绍与核心特性 |
| [🏗️ 框架概览](Books/2-框架概览.md) | 框架架构与设计理念 |
| [🚀 快速开始](Books/1-快速开始.md) | 5 分钟快速上手教程 |
| [🌍 全平台运行](Books/99-各平台运行RunAble.md) | 各平台运行截图展示 |
| [🤖 AI 开发工作流](Books/AI-Development-Workflow.md) | openspec + tengine-dev AI 开发指南 |

### 核心模块文档

| 模块 | 文档 | 描述 |
|------|------|------|
| 📦 **资源模块** | [资源模块](Books/3-1-资源模块.md) | YooAsset 资源管理，支持 LRU/ARC 缓存 |
| 🎯 **事件模块** | [事件模块](Books/3-2-事件模块.md) | 零 GC 事件系统，支持 MVE 架构 |
| 💾 **内存池模块** | [内存池模块](Books/3-3-内存池模块.md) | 轻量级内存池管理 |
| 🎮 **对象池模块** | [对象池模块](Books/3-4-对象池模块.md) | 游戏对象池管理 |
| 🎨 **UI 模块** | [UI模块](Books/3-5-UI模块.md) | 商业化 UI 框架，支持代码生成 |
| 📊 **配置表模块** | [配置表模块](Books/3-6-配置表模块.md) | Luban 配置表系统 |
| 🔄 **流程模块** | [流程模块](Books/3-7-流程模块.md) | 商业化启动流程 |
| 🌐 **网络模块** | [网络模块](Books/3-8-网络模块.md) | 网络通信模块 |

---

## 📁 项目结构

```
Assets/
├── AssetArt/              # 美术资源目录
│   └── Atlas/            # 自动生成图集目录
├── AssetRaw/             # 热更资源目录
│   ├── UIRaw/            # UI 图片目录
│   │   ├── Atlas/        # 需要自动生成图集的 UI 素材目录
│   │   └── Raw/          # 不需要自动生成图集的 UI 素材目录
│   ├── Audios/           # 音频资源
│   ├── Effects/          # 特效资源
│   └── Scenes/           # 场景资源
├── Editor/               # 编辑器脚本目录
├── HybridCLRData/        # HybridCLR 相关目录
├── Scenes/               # 主场景目录
├── TEngine/              # 框架核心目录
│   ├── Editor/           # TEngine 编辑器核心代码
│   ├── Runtime/          # TEngine 运行时核心代码
│   └── AssetSetting/     # YooAsset 资源设置
└── GameScripts/          # 程序集目录
    ├── Main/             # 主程序程序集（启动器与流程）
    └── HotFix/           # 游戏热更程序集目录
        ├── GameBase/     # 游戏基础框架程序集 [Dll]
        ├── GameProto/    # 游戏配置协议程序集 [Dll]
        └── GameLogic/    # 游戏业务逻辑程序集 [Dll]
            ├── GameApp.cs                  # 热更主入口
            └── GameApp_RegisterSystem.cs   # 热更主入口注册系统
```

---

## 💻 系统要求

- **推荐版本**: Unity 2021.3.20f1c1
- **支持版本**: Unity 2019.4 / 2020.3 / 2021.3 / 2022.3
- **平台支持**: Windows、macOS、Android、iOS、WebGL、微信小游戏
- **开发环境**: .NET 4.x、Visual Studio 2019+ 或 Rider

---

## 🌐 服务器支持

TEngine 本身为**纯净的客户端框架**，不强绑定任何服务器。推荐使用 **C# 服务器**：

- **[GameNetty](https://github.com/ALEXTANGXIAO/GameNetty)** - 源于 ETServer，首次拆分最新的 ET8.1 的前后端解决方案
- **[Fantasy](https://github.com/qq362946/Fantasy)** - 源于 ETServer 但极为简洁，更好上手的商业级服务器框架（Fantasy 分支已集成）

---

## 🌟 开源项目推荐

| 项目 | 描述 | 链接 |
|------|------|------|
| **YooAsset** | 商业级经历百万 DAU 游戏验证的资源管理系统 | [GitHub](https://github.com/tuyoogame/YooAsset) |
| **HybridCLR** | 特性完整、零成本、高性能、低内存的近乎完美的 Unity 全平台原生 C# 热更方案 | [GitHub](https://github.com/focus-creative-games/hybridclr) |
| **Luban** | 最佳游戏配置解决方案 | [GitHub](https://github.com/focus-creative-games/luban) |
| **Fantasy** | 源于 ETServer 但极为简洁，更好上手的商业级服务器框架 | [GitHub](https://github.com/qq362946/Fantasy) |
| **GameNetty** | 源于 ETServer，首次拆分最新的 ET8.1 的前后端解决方案 | [GitHub](https://github.com/ALEXTANGXIAO/GameNetty) |
| **JEngine** | 使 Unity 开发的游戏支持热更新的解决方案 | [GitHub](https://github.com/JasonXuDeveloper/JEngine) |
| **DGame** | 根据 TEngine 框架，结合工作经验，修改和增加一些实用性拓展修改的框架 | [GitHub](https://github.com/AmaniDawn/DGame) |
| **AlicizaXTemplate** | AlicizaX 是一套面向 Unity 项目的框架模板 | [GitHub](https://github.com/AlicizaX/AlicizaXTemplate) |

### 社区 Demo

- **[TowerDefense-TEngine-Demo](https://github.com/daydayasobi/TowerDefense-TEngine-Demo)** - 群友大佬的塔防 Demo

---

## 🎮 Demo 项目

最新的 Demo 飞机大战位于 **demo 分支**，欢迎体验！

```bash
git checkout demo
```

---

## 🤝 贡献与支持

欢迎提交 Issue 和 Pull Request！如需支持上游 TEngine 项目，请参考 [上游赞助页](https://github.com/Alex-Rachel/TEngine#-%E8%B4%A1%E7%8C%AE%E4%B8%8E%E6%94%AF%E6%8C%81)。

### 致谢

本 Fork 部分功能参考并迁移自 [DGame](https://github.com/AmaniDawn/DGame) 项目，感谢 DGame 作者 [@AmaniDawn](https://github.com/AmaniDawn) 在 TEngine 基础上结合商业项目经验沉淀的实用模块与工具，为本仓库的定制改造提供了重要参考。

---

<div align="center">

**Made with ❤️ by TEngine Team**

[⭐ Star](https://github.com/ALEXTANGXIAO/TEngine) | [🐛 Issues](https://github.com/ALEXTANGXIAO/TEngine/issues) | [📖 Wiki](https://deepwiki.com/Alex-Rachel/TEngine)

</div>
