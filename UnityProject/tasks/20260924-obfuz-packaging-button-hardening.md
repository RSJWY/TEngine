# TEngine 打包工具 Obfuz 开关路径与产物校验

- ID: 20260924-obfuz-packaging-button-hardening
- 状态: planned（暂存；仅在用户明确要求执行时实施）
- 负责人: 待执行时确定
- 研究依据: [Obfuz 与 AOT 元数据审查](../conversation-summaries/code-research/2026-09-24-obfuz-packaging-aot-audit.md)

## 目标与验收

- [ ] Obfuz 开启和关闭时，打包工具的 GenerateAll、编译并拷贝热更 DLL、AOT 清单同步、AOT DLL 拷贝、快速构建及独立 Player 构建调用链与目标平台和开关一致。
- [ ] 缺少本次构建所需的 AOT DLL、热更 DLL 或混淆 DLL 时，构建报出目标平台和缺失路径并失败，不以已有 `.dll.bytes` 冒充本次产物。
- [ ] `AOTGenericReferences.cs` 缺失或解析失败、AOT 清单资产缺失时，同步按钮明确失败；依赖同步的编译拷贝和 CodePackage 构建停止，不沿用旧 JSON 清单。
- [ ] 关闭 Obfuz 并重新 GenerateAll 后，过期的生成项不再被误认作手动添加项；明确的手动附加项保留。无法判定来源的现有条目不得自动删除。
- [ ] Player 平台切换失败时，独立 Player 构建不调用 `BuildPipeline.BuildPlayer`；Obfuz 宏与 Player 混淆开关不一致时，相关生成和构建入口拒绝执行。

## 范围与决策

- 修改范围候选: `Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs`、`Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs`、必要的 AOT 清单数据结构和对应 Editor 测试。执行前以当前源码重新确认范围。
- 不修改: 官方 HybridCLR、Obfuz 和 Obfuz4HybridCLR 包入口；不把 GenerateAll 接入一键构建；不开发活动平台与目标平台不同时的跨平台 GenerateAll。
- 关键选择: 优先让构建在关键输入缺失时失败，并在复制前检查整批源文件；避免复制一部分后才发现缺失。清单的生成项与手动项应有可追溯的区别，首次迁移必须保守处理来源不明的旧条目。
- 当前调用链: 通用 GenerateAll 按 Obfuz 状态分流；普通 CodePackage 构建仅在启用热更 DLL 编译时同步清单、编译和复制；Obfuz 路径另行混淆并可选转多态。此处是源码结论，尚未完成 Unity 按钮验收。

## 授权

- 用户本轮要求“根据规则存储为计划，后续需要的时候再实现”，仅授权创建暂存任务文档。
- 未授权实施修复、修改 Unity 对象或项目设置、切换平台、真实 GenerateAll/打包/Player 构建。
- 后续用户明确要求执行时，先读本文件及当时的源码和 `git status`；涉及 Unity 写入、平台切换或真实构建时，按仓库当时的授权规则单独处理。

## 实施与证据

- [ ] 修复缺失 DLL 仍继续打包: 预检目标平台的 AOT、热更和混淆源文件；缺失时中止。用隔离输入分别模拟缺 AOT 和缺混淆 DLL，检查报错路径、构建失败以及旧 `.bytes` 未被当作成功产物；正常输入核对源与复制结果。
- [ ] 修复清单同步失败后继续构建: 将缺文件、解析失败和清单资产缺失变成可传播的失败；分别验证单独同步、编译并拷贝和 CodePackage 构建三个入口。正常输入核对清单与 JSON 一致。
- [ ] 处理切换 Obfuz 后的旧条目: 区分上次生成项和明确手动添加项，设计兼容旧清单的保守迁移。用开到关重新 GenerateAll 的隔离用例验证过期生成项移除、手动项保留；来源不明时阻止自动删除并给出核对信息。
- [ ] 修复 Player 平台和开关错配: 验证切换平台失败立即停止且未调用 `BuildPlayer`；验证 Obfuz 宏与 Player 构建回调开关不一致时入口拒绝执行；一致时检查开、关两种调用路径。
- [ ] 执行适当的 .NET 构建与 Editor 测试，并在授权后分别验收 Obfuz 开/关下的首包及普通热更包流程。
- 当前证据: 上述风险来自源码审查；未实施修复，也未运行缺失输入模拟或真实 Unity 构建。检查时 `UnityProject/Tools/unity.exe` 不存在，Unity CLI 验证不可用。
- 运行报告路径: 待实施时填写。

## 剩余风险

- AOT 生成列表和裁剪 DLL 目录都可能保留上次平台或开关的产物；仅靠文件存在无法证明来源。实施时应决定并验证本次产物的新鲜度或来源检查。
- 当前清单不能可靠区分所有历史手动项与生成项，迁移规则需先检查现有数据，不能直接清空“额外项”。
- 项目资源、设置和生成产物存在未提交修改；后续实施必须保留用户改动，不直接删除或覆盖现有 DLL 来制造失败场景。
