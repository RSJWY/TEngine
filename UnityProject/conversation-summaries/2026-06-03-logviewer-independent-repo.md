# LogViewer 独立仓库拆分与自动构建配置

**日期**：2026-06-03  
**任务**：将 LogViewer 工具拆分到独立 GitHub 仓库，配置自动构建和发布

---

## 📋 任务目标

用户希望：
1. LogViewer 工具独立管理，在 GitHub 上实现自动构建
2. 主仓库保留代码说明，但添加独立仓库链接
3. 使用 Git Subtree 保持同步关系

---

## ✅ 完成的工作

### 1. **Git Subtree 拆分（保留完整历史）**

```bash
# 添加远程仓库
git remote add logviewer-repo git@github.com:RSJWY/TEngine-LogView.git

# 推送 Tools/LogViewer 目录到子仓库（保留 1605 个 commit 历史）
git subtree push --prefix=Tools/LogViewer logviewer-repo main
```

**性能说明**：
- 首次推送耗时较长（需遍历 1605 个提交过滤路径）
- 后续增量推送会快很多（只处理新增提交）

---

### 2. **独立仓库配置 (TEngine-LogView)**

#### README 更新
- 添加与主仓库的关系说明
- 说明贡献方式（在主仓库提 PR/Issue）
- 保留完整的构建和开发文档

#### GitHub Actions 工作流 (`.github/workflows/build.yml`)

**触发方式**：
- ✅ 手动触发：Actions 页面 "Run workflow"
- ✅ Tag 发布：推送 `v*` tag 自动构建并发布 Release

**构建流程**：
1. Windows runner
2. 安装 Go + Wails CLI
3. 安装 WebView2 Runtime
4. 编译 `LogViewer.exe`
5. 上传 artifact
6. （仅 tag）创建 GitHub Release 并附加 exe

**问题修复**：
- ❌ 初次运行遇到 `403 Resource not accessible by integration` 错误
- ✅ **解决方案**：
  1. 工作流添加 `permissions: contents: write`
  2. 仓库设置启用 "Read and write permissions"（Settings → Actions）
- ⚠️ Node 20 弃用警告
- ✅ **解决方案**：添加 `env: FORCE_JAVASCRIPT_ACTIONS_TO_NODE24: true`

---

### 3. **主仓库更新 (TEngine)**

**`Tools/LogViewer/README.md` 修改**：
```markdown
> **📦 独立仓库**：本工具已拆分到 **[TEngine-LogView](https://github.com/RSJWY/TEngine-LogView)**  
> - 前往独立仓库下载最新 [Releases](https://github.com/RSJWY/TEngine-LogView/releases)  
> - 提交问题反馈或功能建议到 [Issues](https://github.com/RSJWY/TEngine-LogView/issues)  
> - 代码仍在主仓库维护，通过 Git Subtree 自动同步
```

**提交记录**：
- `7b2bf2d0` - docs(LogViewer): 添加独立仓库链接说明

---

### 4. **v1.0.0 Release 发布**

- 📦 Release 地址：https://github.com/RSJWY/TEngine-LogView/releases/tag/v1.0.0
- 包含 `LogViewer.exe` Windows 可执行文件
- 功能包括：拖拽日志文件、级别筛选、关键词检索、富文本剥离、堆栈折叠

---

## 🔄 后续发布流程

### 日常开发（在主仓库）
```bash
cd E:/WorkSpace/TEngine/Tools/LogViewer
# 修改代码...
git add .
git commit -m "feat: xxx"
git push origin main
```

### 同步到子仓库
```bash
cd E:/WorkSpace/TEngine
git subtree push --prefix=Tools/LogViewer logviewer-repo main
```

### 发布新版本（在子仓库）
```bash
cd /tmp/TEngine-LogView
git pull
git tag v1.0.1
git push origin v1.0.1  # 自动触发 GitHub Actions 构建并发布 Release
```

---

## 🔧 技术细节

### Git Subtree vs Submodule

**选择 Subtree 的原因**：
- ✅ 用户 clone 主仓库时自动包含 LogViewer 完整代码
- ✅ 保留完整提交历史
- ✅ 可以单向推送到子仓库
- ❌ 首次推送慢（一次性成本）

**vs Submodule**：
- Submodule 需要 `--recursive` clone
- 历史记录独立

### GitHub Actions 权限说明

**问题根因**：
- 默认 `GITHUB_TOKEN` 只有 `contents: read` 权限
- 创建 Release 需要 `contents: write`

**两层配置**：
1. **仓库级**：Settings → Actions → Workflow permissions → "Read and write permissions"
2. **工作流级**：`permissions: contents: write`

两者都需要配置，缺一不可。

---

## 📁 最终仓库状态

### 主仓库 (TEngine)
```
Tools/LogViewer/
├── .gitignore
├── README.md          # 简洁版 + 独立仓库链接
├── main.go
├── parser/
├── frontend/
├── build/
├── wails.json
├── go.mod
└── build scripts...
```

### 子仓库 (TEngine-LogView)
```
.
├── .github/workflows/build.yml  # CI 配置（主仓库无此文件）
├── .gitignore
├── README.md                    # 完整版，包含仓库关系说明
├── main.go
├── parser/
├── frontend/
├── build/
├── wails.json
├── go.mod
└── build scripts...
```

---

## 📝 经验总结

### 1. **Git Subtree 首次推送很慢**
- 1605 个 commit 需要逐个过滤路径并重写
- 这是一次性成本，后续增量推送很快
- 不要中途 Ctrl+C，让它跑完

### 2. **GitHub Actions Release 权限问题**
- 必须同时配置仓库级和工作流级权限
- 先改仓库设置，再改工作流文件
- 旧 tag 需要删除重打，指向修复后的 commit

### 3. **Node 运行时弃用警告**
- 警告不影响功能
- 用 `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24=true` 提前迁移
- 避免 2026-06-16 强制切换时突然失效

### 4. **Subtree 工作流是单向的**
- 主仓库 → 子仓库（subtree push）
- 子仓库的修改（如 CI 配置）通常不回流主仓库
- README 可以两边维护不同版本（主仓库简洁，子仓库详细）

---

## 🔗 相关链接

- **主仓库**：https://github.com/RSJWY/TEngine
- **子仓库**：https://github.com/RSJWY/TEngine-LogView
- **v1.0.0 Release**：https://github.com/RSJWY/TEngine-LogView/releases/tag/v1.0.0
- **Actions 工作流**：https://github.com/RSJWY/TEngine-LogView/actions

---

**Co-Authored-By**: Claude Opus 4.8 <noreply@anthropic.com>
