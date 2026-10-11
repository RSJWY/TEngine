# 本地化第三方包与一键同步脚本

> 本文档记录 fork 将若干第三方 UPM 包从 Git URL 依赖转为 `UnityProject/Packages/` 下的**本地（embedded）包**，以及配套的一键同步脚本。
> Obfuz / HybridCLR / Obfuz4HybridCLR 的本地化还牵涉 dnlib 冲突解决，细节见 [obfuscation.md](obfuscation.md)。

## 背景

上游以 Git URL 引入的第三方包存在两个问题：

- **不可复现 / CI 不友好**：浮动 Git URL 指向默认分支，不同时间解析到的 commit 不同；离线或内网环境也需要能访问 GitHub。
- **dnlib 冲突（仅 HybridCLR/Obfuz/Obfuz4HybridCLR）**：HybridCLR 内置官方原版 `dnlib.dll`，Obfuz 内置定制版 dnlib（含 `PolymorphicWriter` 等多态类型）。两者共存时 Unity 可能把原版 dnlib 解析给 `obfuz4hybridclr`，触发 `CS0234`/`CS0246`。

因此把这 4 个包全部落地为 `Packages/` 下的本地包（内容随版本库提交），并提供一键脚本负责「拉取指定版本 → 覆盖本地包目录 → 改写 `manifest.json` →（HybridCLR）删除多余 dnlib」，升级时重跑脚本即可。

## 本地化包清单

| 包 | 上游仓库 | 本地包目录 | 同步脚本 | 特殊处理 |
| --- | --- | --- | --- | --- |
| `com.code-philosophy.hybridclr` | `focus-creative-games/hybridclr_unity.git` | `Packages/com.code-philosophy.hybridclr` | `sync-hybridclr-local.sh` | 删除 `Plugins/dnlib.dll`，全项目共用 Obfuz 定制 dnlib |
| `com.code-philosophy.obfuz` | `focus-creative-games/obfuz.git` | `Packages/com.code-philosophy.obfuz` | `sync-obfuz-local.sh` | 保留其自带定制 dnlib |
| `com.code-philosophy.obfuz4hybridclr` | `focus-creative-games/obfuz4hybridclr.git` | `Packages/com.code-philosophy.obfuz4hybridclr` | `sync-obfuz4hybridclr-local.sh` | 不含 dnlib；包根即仓库根 |
| `com.cysharp.unitask` | `Cysharp/UniTask.git` | `Packages/com.cysharp.unitask` | `sync-unitask-local.sh` | 包根是仓库子目录 `src/UniTask/Assets/Plugins/UniTask` |

其余 Git URL 依赖（如 `com.coplaydev.unity-mcp`）未本地化，维持原有引用。

当前仓库锁定版本：HybridCLR `8.15.0`、Obfuz `3.1.0`、Obfuz4HybridCLR `3.0.1`、UniTask `2.5.11`。其中 UniTask 对应上游 `main` 的 commit `ceac8d6946b1125fe782cd171fbcb245b567dbf9`（迁移时 Package Manager 的解析结果，与该版本的 `2.5.11` tag 内容并不完全一致，故按 commit 固化以保证零行为变化）。

## 通用约定

- 脚本位于 `UnityProject/Packages/sync-*-local.sh`，配套 `.bat` 双击包装（自动探测 Git Bash）。
- 每个脚本幂等：每次运行都是一次干净的重同步（`rm -rf` 目标目录后重新拷贝）。
- **不传版本参数**时自动解析上游**最新稳定 tag**（优先纯数字版本，退回最新 tag）；也可显式传 tag / 分支 / 完整 commit SHA。
- 默认从 GitHub 拉取，可用环境变量切镜像（见下）。
- 拉取后自动把 `manifest.json` 中对应条目改写为 `file:<包名>`，本地包内容随之提交进版本库。
- 依赖关系：`obfuz4hybridclr` 编译依赖 Obfuz 的定制 dnlib，因此需先保证 `sync-obfuz-local.sh`（保留 dnlib）与 `sync-hybridclr-local.sh`（删除 dnlib）已按序执行过。

## 使用方式

```bash
# 安装/升级到最新稳定 tag（自动解析，跳过预发布版）
bash Packages/sync-hybridclr-local.sh
bash Packages/sync-obfuz-local.sh
bash Packages/sync-obfuz4hybridclr-local.sh
bash Packages/sync-unitask-local.sh

# 指定版本（tag / 分支 / 完整 commit SHA 均可）
bash Packages/sync-hybridclr-local.sh v8.13.0
bash Packages/sync-obfuz-local.sh v3.1.0
bash Packages/sync-obfuz4hybridclr-local.sh v3.0.1
bash Packages/sync-unitask-local.sh 2.5.11
```

也可双击对应 `.bat` 运行。国内网络可用环境变量切 gitee 镜像：

```bash
SYNC_HYBRIDCLR_REPO=https://gitee.com/focus-creative-games/hybridclr_unity.git bash Packages/sync-hybridclr-local.sh
SYNC_OBFUZ_REPO=https://gitee.com/focus-creative-games/obfuz.git bash Packages/sync-obfuz-local.sh
SYNC_OBFUZ4HYBRIDCLR_REPO=https://gitee.com/focus-creative-games/obfuz4hybridclr.git bash Packages/sync-obfuz4hybridclr-local.sh
SYNC_UNITASK_REPO=https://gitee.com/cysharp/UniTask.git bash Packages/sync-unitask-local.sh
```

## 注意事项

- **升级第三方包必须重跑对应脚本**，不要用 Package Manager 直接更新 Git URL——那会重新引入浮动依赖（HybridCLR/Obfuz 还会复发双 dnlib 冲突）。
- 本地包已随版本库提交，拉代码即可用，无需先跑脚本；脚本只在**升级版本**时需要。
- 本地包自带 `.meta`（GUID 随包提交），因此从 Git URL 切换到本地包后，资产引用不会失效。
- UniTask 的包根是仓库子目录，脚本内以 `SUBDIR` 标记；若上游目录结构调整，脚本会因找不到 `package.json` 而显式报错，需同步更新 `SUBDIR`。
- 本地包目录由脚本整体覆盖，不要在其中手工改源码——改动会被下一次同步冲掉（需要定制应改框架/业务侧，或另建补丁）。

## 关键文件

- `UnityProject/Packages/sync-hybridclr-local.sh`、`UnityProject/Packages/sync-hybridclr-local.bat`
- `UnityProject/Packages/sync-obfuz-local.sh`、`UnityProject/Packages/sync-obfuz-local.bat`
- `UnityProject/Packages/sync-obfuz4hybridclr-local.sh`、`UnityProject/Packages/sync-obfuz4hybridclr-local.bat`
- `UnityProject/Packages/sync-unitask-local.sh`、`UnityProject/Packages/sync-unitask-local.bat`
- `UnityProject/Packages/manifest.json`、`UnityProject/Packages/packages-lock.json`
- `UnityProject/Packages/com.code-philosophy.hybridclr/`、`com.code-philosophy.obfuz/`、`com.code-philosophy.obfuz4hybridclr/`、`com.cysharp.unitask/`

## 相关记录

- [obfuscation.md](obfuscation.md)（dnlib 冲突与 Obfuz 接入）
