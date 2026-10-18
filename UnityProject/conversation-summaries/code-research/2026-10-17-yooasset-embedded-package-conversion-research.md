# YooAsset registry 包转本地 embedded 包研究

> 日期：2026-10-17
> 任务：将 `com.tuyoogame.yooasset` 从 openupm registry 依赖转为 `Packages/` 本地（embedded）包，并配套一键升级脚本。

## 上游包结构

- 仓库：`https://github.com/tuyoogame/YooAsset.git`，tag 命名为**纯数字**（如 `3.0.6`，无 `v` 前缀）。
- **包根是仓库子目录 `Assets/YooAsset/`**（标准 Unity 工程布局，仓库根还有演示工程 `Assets/` 与 `Packages/`（仅 manifest/lock）），与 UniTask（`src/UniTask/Assets/Plugins/UniTask`）同类，与 Obfuz/HybridCLR（包根即仓库根）不同。同步脚本需以 `SUBDIR` 拷贝。
- tag `3.0.6` 指向 commit `3b4cfb36cc2e81b9558ab74d65fee2cf2eac2804`，与包内 `package.json` 的 `repository.revision` 一致，即 registry tarball 的构建来源 → 以同名 tag 固化即可保证与原 registry 包**内容零差异**。

## GUID 稳定性（关键风险点）

工程侧 5 个 asmdef 按 **GUID** 引用 YooAsset 程序集（非按名称）：

| asmdef | GUID | 引用方 |
| --- | --- | --- |
| `Runtime/YooAsset.asmdef` | `e34a5702dd353724aa315fb8011f08c3` | `TEngine.Runtime`、`GameLogic`、`RuntimeTools` |
| `Editor/YooAsset.Editor.asmdef` | `4d1926c9df5b052469a1c63448b7609a` | `TEngine.Editor`、`YooAsset.Custom.Editor` |

验证结论：**上游仓库自带全套 `.meta`，上述 asmdef GUID 与本机 PackageCache 解析结果完全一致**。因此从 registry 切换到 embedded 包、以及日后用脚本重同步，GUID 不变，五个引用程序集不会断（Unity Editor 重解析后全量编译 0 个 `error CS`）。

补充事实：

- registry 包的 PackageCache `.meta` 与上游 `.meta` 仅行尾符差异（CRLF vs LF），GUID/内容一致；`CHANGELOG.md` 同理。
- Unity 刷新后自动改写 `packages-lock.json`：`"version": "file:com.tuyoogame.yooasset", "source": "embedded"`，无需手工维护。
- 本地包整体约 33MB，含 `Samples~`（Unity 不导入 `~` 后缀目录，仅为随包分发内容）。

## 同步脚本设计

`Packages/sync-yooasset-local.sh` + `.bat`（Windows 双击包装器），完全沿用既有四个本地包脚本约定：

- `SUBDIR="Assets/YooAsset"` 拷贝（找不到 `package.json` 显式报错，提示上游布局变更）。
- 不传参自动解析最新稳定 tag（正则 `^v?[0-9]+(\.[0-9]+)*$` 兼容 `v` 前缀），可显式传 tag/分支/commit。
- `SYNC_YOOASSET_REPO` 环境变量切镜像（本次未验证 gitee 镜像可达性，文档中留占位）。
- 幂等：`rm -rf` 目标目录后干净重拷贝；最后 sed 改写 `manifest.json` 为 `file:` 引用。

## 执行结果（2026-10-17）

- `bash Packages/sync-yooasset-local.sh 3.0.6` 一次成功：`Packages/com.tuyoogame.yooasset` 落地、manifest 改 `file:`、Unity 自动重解析为 embedded、`packages-lock.json` 自动更新、Editor 全量编译 0 错误。
- 锁定版本刻意取 `3.0.6`（原 registry 版本）而非最新 tag，保证转换本身零行为变化；升级到新版本属于后续独立决策，届时重跑脚本即可。

## 关键文件

- `UnityProject/Packages/sync-yooasset-local.sh`、`.bat`
- `UnityProject/Packages/com.tuyoogame.yooasset/`
- `UnityProject/Packages/manifest.json`、`packages-lock.json`
- 文档：`Books/Fork/local-packages.md`、`Books/Fork/CHANGELOG.md`、`repowiki/zh/content/项目概述/当前Fork定制功能.md`
