# 代码混淆（Obfuz）

## Obfuz 接入与 dnlib 冲突解决

### 背景

项目接入 Obfuz 做代码混淆，并配合 `obfuz4hybridclr` 扩展包支持 HybridCLR 热更工作流。Obfuz 内置的是**定制版 dnlib**（新增 `PolymorphicWriter` 等多态 dll 相关类型），而 HybridCLR 内置的是官方原版 dnlib。两者同时存在时 Unity 可能把原版 dnlib 解析给 `obfuz4hybridclr`，导致 `dnlib.DotNet.PolymorphicWriter` 找不到的编译错误（CS0234/CS0246）。

### 改动摘要

- `com.code-philosophy.hybridclr`、`com.code-philosophy.obfuz` 从 git URL 包转为 `Packages/` 下的**本地包**（manifest 改为 `file:` 引用），包内容提交进版本库。
- 移除本地 HybridCLR 包的 `Plugins/dnlib.dll`，全项目只保留 Obfuz 的定制 dnlib（官方原版的功能超集，HybridCLR 代码可正常编译）。
- 新增一键同步脚本 `Packages/sync-hybridclr-local.sh` / `sync-obfuz-local.sh`（及对应 `.bat` 双击包装），负责拉取指定版本、同步为本地包、删除 HybridCLR 的 dnlib、改写 manifest。
- `com.code-philosophy.obfuz4hybridclr` 仍为 git URL 包（不含 dnlib，无冲突）。

### 使用方式

```bash
# 安装/升级到最新稳定 tag（自动解析，跳过预发布版）
bash Packages/sync-hybridclr-local.sh
bash Packages/sync-obfuz-local.sh

# 指定版本（tag / 分支 / 完整 commit SHA 均可）
bash Packages/sync-hybridclr-local.sh v8.13.0
bash Packages/sync-obfuz-local.sh v3.1.0
```

也可双击对应 `.bat` 运行。默认从 GitHub 拉取，国内网络可用环境变量切 gitee 镜像：

```bash
SYNC_HYBRIDCLR_REPO=https://gitee.com/focus-creative-games/hybridclr_unity.git bash Packages/sync-hybridclr-local.sh
SYNC_OBFUZ_REPO=https://gitee.com/focus-creative-games/obfuz.git bash Packages/sync-obfuz-local.sh
```

混淆功能开关：菜单 `Obfuz/Settings...` → Build Pipeline Settings → `Enable`，关闭后构建流程与未装 Obfuz 一致。

### 注意事项

- **升级 HybridCLR/Obfuz 必须重跑对应脚本**，不要用 Package Manager 直接更新——脚本会重新执行删 dnlib 和 manifest 改写，漏掉则冲突复发。
- 本地包已入库（跟随 `Packages/MCPForUnity` 先例），拉代码即可用，无需先跑脚本；脚本只在升级时需要。
- App 发布后不要修改 Obfuz 的静态密钥；App 与热更包的混淆状态需保持一致。
- Obfuz 与 HybridCLR 都通过 Package Manager 更新会重新引入双 dnlib，属已知禁区。

### 关键文件

- `UnityProject/Packages/sync-hybridclr-local.sh`、`UnityProject/Packages/sync-hybridclr-local.bat`
- `UnityProject/Packages/sync-obfuz-local.sh`、`UnityProject/Packages/sync-obfuz-local.bat`
- `UnityProject/Packages/manifest.json`
- `UnityProject/Packages/com.code-philosophy.hybridclr/`、`UnityProject/Packages/com.code-philosophy.obfuz/`
