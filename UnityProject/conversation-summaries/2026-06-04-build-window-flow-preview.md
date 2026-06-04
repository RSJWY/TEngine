# 打包工具窗口「构建流程预览」面板

日期：2026-06-04
关联文件：`Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`

## 背景问题

TEngine 打包工具窗口（`TEngine/Build/打包工具窗口`）的折叠区域排列顺序，与点击构建按钮后的实际执行顺序不一致，用户反馈"打包操作先后顺序感觉有点模糊"。

- **UI 折叠区域顺序**：基础设置 → 资源包列表 → 发布整理 → 最小包设置 → 高级设置 → 热更DLL设置 → 打包Player设置
- **实际执行顺序**（`ReleaseTools.BuildWithConfig`）：编译热更DLL → 构建 AssetBundle → 发布整理 → 最小包处理 → 构建 Player

两者顺序错位，导致看不出"勾了哪些步骤、按什么顺序跑"。

## 方案选择

提供了三种方案：
- 方案 A：按执行顺序重排折叠区域 + 步骤编号（改动中等）
- 方案 B：分组 + 顶部流程图（改动大）
- 方案 C：保持现有顺序不动，新增独立「构建流程预览」面板（零侵入，最保守）

用户选择 **方案 C**。

## 实现内容

零侵入新增，现有所有折叠区域和操作按钮均未改动。

1. 新增折叠状态字段 `_showFlowPreview`（默认展开）。
2. 在 `OnGUI` 调用链中，于「打包Player设置」之后、「操作按钮」之前插入 `DrawFlowPreview()`。
3. 新增 `构建流程预览` region：
   - 内部类型 `FlowStepState`（Enabled/Skipped）与只读结构 `FlowStep`（State/Title/Detail）。
   - `BuildFlowSteps()` 根据当前 `_config` 动态生成 5 个步骤：
     1. 编译热更DLL（`BuildHotFixDll`）
     2. 构建 AssetBundle（始终启用，详情含平台/版本/资源包列表）
     3. 发布整理（`EnablePublishCopy`，详情含发布路径规则）
     4. 最小包处理（`MinimalPackage`，详情含保留 Tag）
     5. 构建 Player（`BuildPlayer`，详情含平台/输出路径）
   - `DrawFlowPreview()` 渲染：**序号只对启用步骤递增**（1、2、3…），跳过的步骤显示 `—` 并灰显（`textColor` 0.55 灰）；每步带一行动态详情（0.5 灰小字），随配置实时刷新。
   - 底部说明三个按钮的覆盖范围：AB 按钮跑 1~4、Player 按钮单独跑第 5、一键构建跑全部。
   - 辅助方法 `GetPreviewVersionText()`（版本号为空时显示"(自动生成)"）、`GetPreviewProjectName()`（取 `UpdateSetting.GetProjectName()`，缺失回退 "Demo"）。

## 验证

`dotnet build TEngine.Editor.csproj` 通过，0 错误（17 个既有警告）。

## 设计要点

- 不破坏用户既有操作习惯，仅把"模糊的先后顺序"显式摊开。
- 步骤详情全部从 `_config` 实时取值，改任意配置面板时预览同步更新。
- 复用了文件内既有方法 `GetBuildPackageLogText`、`ReleaseTools.GetRemotePlatformName`，未引入新依赖。
