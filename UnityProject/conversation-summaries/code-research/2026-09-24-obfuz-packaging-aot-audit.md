# TEngine 打包工具启用 Obfuz 后的热更 DLL 与 AOT 元数据审查

日期：2026-09-24。范围：源码和磁盘产物审查，以及目标平台传递修复；未执行 Unity 生成、打包或运行时测试。下文的文件哈希和开关状态属于各次检查时的快照，不能代表随后一次 GenerateAll 的产物。

## 热更 DLL 链路

- `ReleaseTools.BuildWithConfig` 仅在选中程序集包且 `BuildHotFixDll` 开启时调用 `BuildDLLCommand.BuildAndCopyDlls()`；窗口中的“编译并拷贝热更DLL”也调用它。单独的 Player 构建入口不重编热更 DLL。
- `BuildAndCopyDlls` 先同步 AOT 清单、编译，再由 `CopyAOTHotUpdateDlls` 拷贝 AOT 和原始热更 DLL。编译条件同时满足 `ENABLE_HYBRIDCLR && ENABLE_OBFUZ` 时，后者再次编译，调用 `ObfuscateHotUpdateAssemblies`，从 `Library/Obfuz/<target>/ObfuscatedHotUpdateAssemblies` 拷贝 GameProto/GameLogic 的混淆 DLL 覆盖原始 `.dll.bytes`。`polymorphicDllSettings.enable` 为真时，在覆盖前另生成多态格式 DLL。
- 检查时 `ProjectSettings/Obfuz.asset` 的多态开关开启；最新读取的 `buildPipelineSettings.enable` 为 `0`，即 Player 构建回调混淆未启用。热更 DLL 混淆走 `ENABLE_OBFUZ` 条件编译的独立路径；这几个开关不能互相代替，也不能仅凭混淆目录存在推断本次 Player/AOT 或 CodePackage 已混淆。
- 当前 Windows64 磁盘证据：`HotDll/GameLogic.dll.bytes` 与原始 `HybridCLRData/HotUpdateDlls/.../GameLogic.dll` SHA256 同为 `95396540...FF1585`；GameProto 同为 `7C5592BE...E9D15A9B`。混淆目录对应文件分别为 `B6E21C3D...99C1467` 和 `C8EC6452...6D11D159`。因此当前待打包的 `.bytes` 是原始 DLL，不能据混淆目录存在推断 CodePackage 已使用混淆产物。

## AOT 补充元数据链路

- 扩展版 `HybridCLR/ObfuzExtension/GenerateAll` 编译热更 DLL、生成 IL2CPP 定义和可选的多态加载 C++，通过临时 Player 导出生成裁剪 AOT DLL；随后混淆热更 DLL，以混淆后程序集参与 MethodBridge 和 AOT 泛型引用分析。普通打包入口不自动执行 GenerateAll。
- `SyncAOTMetadataManifest` 从已存在的 `Assets/HybridCLRGenerate/AOTGenericReferences.cs` 中解析 `PatchedAOTAssemblyList`，与现有 manifest 合并后写 `AOTMetadataManifest.asset` 和 `.json.bytes`。当前生成列表包含 `Obfuz.Runtime.dll`；当前 manifest 也包含它及手动附加项。
- `CopyAOTAssembliesToAssetPath` 从 `HybridCLRData/AssembliesPostIl2CppStrip/<target>` 复制裁剪 AOT DLL 到 `Assets/AssetRaw/DLL/AOT/*.dll.bytes`，复制阶段不调用热更 DLL 混淆或多态转换。此前检查到 `Obfuz.Runtime.dll.bytes` 与当时的裁剪源 DLL SHA256 同为 `B3FB4DAC...A3A4`，`mscorlib.dll.bytes` 亦与对应源文件一致；这只证明复制结果等于源文件，不能证明源文件在临时 Player 构建时未经 Obfuz 处理。
- 运行时从 `AOTMetadataManifest.json.bytes` 取加载列表，调用 `HybridCLR.RuntimeApi.LoadMetadataForAOTAssembly(bytes, SuperSet)`。当前 `disableLoadStandardDll: 0` 允许标准格式 AOT 元数据与多态热更 DLL 混用；若改为 1，当前 AOT 拷贝流程不再适用。

## 审查发现

1. **高：此前待打包热更产物未混淆，且开关可能不同步。** 此前检查的 define 缺 `ENABLE_OBFUZ`，且 HotDll 文件哈希等于原始编译产物。Player 的 Obfuz 构建回调受 `buildPipelineSettings.enable` 控制，而 `ObfuzRuntimeInitializer` 受 `ENABLE_OBFUZ` 控制；两个开关不一致会造成 Player 与热更 DLL 的处理状态不一致。最新读取的 Player 回调开关为 `0`；不能把“Obfuz 配置了待混淆程序集”当成已生成混淆 AOT/CodePackage 的证据。
2. **已修复，待 Unity 验证：平台可能不一致。** 原先 `ReleaseTools.BuildWithConfig` 用 `config.BuildTarget` 构建资源包，但无参 `BuildAndCopyDlls` 用 `EditorUserBuildSettings.activeBuildTarget` 编译/拷贝 DLL。现改为按“快速构建”所选平台先激活目标，再向热更 DLL 编译和 AOT 拷贝显式传入 `config.BuildTarget`；独立按钮也使用窗口选择的平台。切换失败则中断，不沿用旧平台。
3. **前提：需先为目标平台运行扩展 GenerateAll。** 官方标准流程正是先 GenerateAll，使用其中临时 Player 导出产生的裁剪 AOT DLL 打资源包，最后构建正式 Player。本项目运行时使用 `HomologousImageMode.SuperSet`，允许两次构建的裁剪 DLL 有差异；无需单纯因为正式 Player 后构建就重打 CodePackage。真正的风险是跳过/沿用过期 GenerateAll 结果，或改了代码、平台宏和裁剪设置却未重新生成，使元数据缺少所需类型/函数。
4. **中：缺失文件可静默混入旧内容。** 热更混淆覆盖阶段遇到源 DLL 不存在时直接 `continue`，此前已拷贝的原始 `.dll.bytes` 保留。AOT 源 DLL 不存在时也只警告并跳过，旧 `.bytes` 可能留在资源目录。
5. **中：AOT 清单可能滞后或分叉。** 同步在本轮编译之前执行，使用已有 AOTGenericReferences；拷贝列表取 manifest、生成列表和 UpdateSetting 三者并集，但写入运行时 JSON 的仅是 manifest 与生成列表。如果仅在 UpdateSetting 新增条目，该 DLL 会被拷贝却不会被运行时按清单加载。
6. **中：运行时元数据失败未阻断。** `LoadMetadataForAOTAssembly` 的错误码仅记录日志，资源缺失也只警告；流程仍可进入游戏，随后才暴露泛型/元数据异常。

验收缺口：未运行扩展 GenerateAll、TEngine 构建、CodePackage 产物检查和目标平台 Player 加载。当前结论只证明源码行为和本机现有文件状态。

## 官方流程核对

- HybridCLR [打包工作流](https://www.hybridclr.cn/docs/basic/buildpipeline)：`Generate/All` → 把热更 DLL 与 `AssembliesPostIl2CppStrip` 下的补充元数据 DLL 加入资源系统 → 按项目原流程打包。文档明确 GenerateAll 会导出一次临时工程生成裁剪 AOT DLL。
- HybridCLR [Package 手册](https://www.hybridclr.cn/docs/basic/com.code-philosophy.hybridclr)：`Consistent` 要求精确匹配正式打包时的裁剪 DLL；`SuperSet` 容许 GenerateAll 与正式打包的裁剪结果有差别，只要求所需类型和函数存在。
- Obfuz [协同工作](https://www.obfuz.com/docs/manual/hybridclr/work-with-hybridclr)和[示例流程](https://www.obfuz.com/docs/beginner/work-with-hybridclr)：启用混淆时用 `HybridCLR/ObfuzExtension/GenerateAll` 代替原命令，再编译/混淆并复制热更 DLL，最后构建 Player。

## GenerateAll 接入一键构建的时序方案（历史备选，已搁置）

- 当前 Obfuz 扩展的 `GenerateAll` 使用 `EditorUserBuildSettings.activeBuildTarget`，依次编译热更 DLL、生成 IL2CPP/Link、导出裁剪 AOT DLL、混淆热更 DLL，并基于该混淆产物生成 MethodBridge 与 `AOTGenericReferences.cs`；最后调用 `AssetDatabase.Refresh()`。
- 当前 `SyncAOTMetadataManifest` 直接用 `File.ReadAllText` 解析磁盘上的 `AOTGenericReferences.cs`，不需要等新生成类编译后才能得到 `PatchedAOTAssemblyList`。但 Unity 导入 `.cs` 时会重新编译，后续构建应等编译、资源导入和可能的程序集重载完成；不能把同一调用栈里的 `GenerateAll -> BuildWithConfig` 当作可靠的一键流程。
- 建议由快速构建选中的 `BuildConfig.BuildTarget` 驱动整个构建任务。启动时冻结完整配置、所选包和是否构建 Player，并在可能触发重载的切平台或生成操作之前，把阶段与请求标识写入 `SessionState`。阶段至少包括切换目标平台、执行 GenerateAll、等待脚本编译/资源导入、验证生成产物、同步 AOT 清单、复制 DLL 并构建资源包/Player。重载后由 Editor 初始化与资源导入完成回调唤醒，检查 `EditorApplication.isCompiling` 等状态和目标平台，再从已完成阶段续跑；异常或编译错误立即终止并清理挂起状态。`SessionState` 只跨程序集重载，不跨 Editor 退出，重启后应视为中断而非自动发布。
- `BuildAndCopyDlls` 当前先编译一次，Obfuz 分支的 `CopyAOTHotUpdateDlls` 又编译、混淆一次；若在 GenerateAll 后直接调用，会在生成泛型引用和 MethodBridge 之后再次改写参与打包的热更 DLL。建议拆出“复用本轮 GenerateAll 产物的验证/复制”步骤，校验目标平台、文件存在及生成前后指纹，不再对同一轮产物重新编译/混淆；多态 DLL 转换仍在复制前执行。缺 AOT DLL、热更 DLL 或清单解析失败应让一键任务失败，避免旧 `.bytes` 静默进入包。
- 窗口当前 `SaveSettings` 延迟落盘，因此续跑状态必须保存本次构建配置快照，不能重载后重读窗口字段或假定设置资产已写盘。仅在选中 CodePackage 且启用热更 DLL 构建时进入此路径；没有相关构建需求时不要触发耗时的 GenerateAll。
- Unity 2022.3 依据：[资源库刷新流程](https://docs.unity3d.com/2022.3/Documentation/Manual/AssetDatabaseRefreshing.html)、[SessionState](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/SessionState.html)、[OnPostprocessAllAssets](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AssetPostprocessor.OnPostprocessAllAssets.html)。其中脚本主动调用 Refresh 时，文档不保证在该次刷新内立刻重载域；应以实际编译/导入完成状态作续跑条件，而非只等固定秒数或只等 `didDomainReload`。

待验证：未运行 Unity GenerateAll、脚本重载续跑、CodePackage 或 Player 构建；上述为源码和官方 API 文档推导的设计。

## YooAsset 目标平台参数核对（仅源码）

- 当前安装的 YooAsset 3.0.6 的 `BuildParameters.BuildTarget` 可显式设置，参数校验只要求它不是 `NoTarget`。TEngine `ReleaseTools.BuildInternalWithConfig` 赋值为快速构建配置的 `config.BuildTarget`，再调用 `pipeline.Run`。
- TEngine 使用的 RawFile、ArchiveFile 和 ScriptableBuildPipeline 均不要求通过 YooAsset API 切换 Editor 当前平台：RawFile 复制原生文件，ArchiveFile 生成归档，Scriptable 管线把 `BuildTarget` 及由它推导的 `BuildTargetGroup` 传给 `BundleBuildParameters`。YooAsset 自带构建窗口会把 `activeBuildTarget` 用作初始选择，但这不约束 TEngine 的参数化入口。
- 当前一键构建仍会在热更 DLL 前调用 `BuildDLLCommand.ActivateBuildTarget(config.BuildTarget)`，Player 构建入口 `BuildImp` 也会切换 Editor 平台。这是 TEngine 额外步骤，不是 YooAsset 资源包参数的要求。未实测目标平台与 Editor 活动平台不同时的完整资源构建；对 Unity 资源导入/平台特定资产的影响还需实际构建验证。

## 真正跨平台 GenerateAll 的历史备选（未实现）

- 若以后要在编辑器活动平台与构建目标不同时执行完整 GenerateAll，可在项目 Editor 代码里重组其子步骤而不修改 Obfuz 包原有无参入口。原方法中的编译、LinkXml、裁剪 AOT DLL、Obfuz 混淆、MethodBridge、AOTGenericReferences 等步骤已有公开的目标平台参数；无参的 Il2CppDef 生成和多态代码生成不按活动平台选输出目录。本次新增的 `BuildDLLCommand.GenerateAllForTarget` **不是**这种跨平台实现：它明确要求目标与活动平台一致，再调用官方无参入口。
- 这并不自动实现“不切 Editor 平台”：`StripAOTDllCommand.GenerateStripedAOTDlls(target)` 会进行一次 `BuildPipeline.BuildPlayer`。本项目 HybridCLR 的 `CheckSettings` 回调按 `activeBuildTarget` 查脚本后端，`FilterHotFixAssemblies` 按 `activeBuildTarget` 找热更 DLL，Unity 2022.3 适用的 `CopyStrippedAOTAssembliesHook` 也按 `activeBuildTarget` 写裁剪 DLL 目录。选中目标与活动平台不同会有校验/产物写错目标目录的风险；不能仅封装现成公开方法就宣称平台完全独立。
- 可行路线是在保持 Obfuz 官方入口原样的同时，改造这些 HybridCLR 构建回调，使它们使用实际 Player 构建目标；缺少目标参数的 `IFilterBuildAssemblies` 需要显式的一次性构建上下文，并在 `finally` 清理。之后还须审查 TEngine 自身的 `ActivateBuildTarget` 和 Player 构建入口，并用活动平台与目标平台不同的 Unity Editor 构建验证。若不改本项目 HybridCLR 包回调，另一条路线是独立 Unity 进程/工程副本以目标平台启动并生成，再回收产物，成本较高。
- 此结论只基于源码，不包含跨平台实际构建或运行验证。`BuildPlayerOptions.target` 可指定 Player 目标，但不能替这些回调纠正其读取的 `activeBuildTarget`。

## 手动 GenerateAll 与资源构建分离（Editor 入口已实现，待 Unity 验证）

- 首包或要重新构建 Player 时，在 TEngine 打包工具“热更 DLL”操作区手动点击通用 `GenerateAll`。`BuildDLLCommand.GenerateAllForTarget(_buildTarget)` 在当前平台与快速构建所选平台一致时，按 `ENABLE_OBFUZ` 状态调用 Obfuz 扩展或 HybridCLR 自带的官方 `GenerateAll()`；两个官方入口均保留原样。宏与 Obfuz Player 构建混淆开关不一致时直接中止，避免生成不匹配的产物。普通热更包仍只运行既有 DLL 编译/混淆及 CodePackage/AB 构建，不自动触发 GenerateAll。
- 两个窗口原先仅在“多态未注入”时可用的 Obfuz GenerateAll 按钮已移除；多态状态的错误提示仍保留，并指向通用入口。两个“清理多态注入”操作仍保留，调用共用入口先检查平台和配置、再清理残留注入代码，然后按混淆状态选择 Obfuz 或普通 HybridCLR GenerateAll。混淆配置窗口会先保存待写入的多态设置。
- 官方无参 GenerateAll 仍取 `EditorUserBuildSettings.activeBuildTarget`，因此通用入口要求活动平台与 `_buildTarget` 一致，不自动切换。混淆配置窗口清理操作使用当前活动平台；它没有快速构建目标选择。
- 后续构建入口仍会同步磁盘上的 `AOTGenericReferences.cs` 到运行时清单，故不需在 GenerateAll 按钮中直接接续 AB 构建。热更包只适用于沿用现有 Player 能力的更新；若改动需要新增 MethodBridge、AOT 泛型元数据或多态注入，则应重新执行 GenerateAll 并评估重新构建 Player，不能笼统视为只打 AB。
- Editor 入口的源码已修改；尚未点击 GenerateAll、等待 Unity 重编译或执行真实构建，功能验收不能标为通过。
- 验证：`dotnet build UnityProject/UnityProject.sln --no-restore --nologo -v:q` 完整方案编译通过（0 错误，27 个既有告警）。使用本机已安装的 Python 3.12 执行仓库 `workflow.py doctor`，因 `UnityProject/Tools/unity.exe` 缺失而返回 blocked；未进行 Unity Editor 编译或按钮交互验证。

## Obfuz 开启后裁剪 AOT DLL 的来源与差异（补充核查）

- Obfuz 扩展 `PrebuildCommandExt.GenerateAll()` 调用 HybridCLR 的 `StripAOTDllCommand.GenerateStripedAOTDlls(target)`，临时构建 Player 以取得裁剪 AOT DLL。HybridCLR 使用 `HybridCLRSettings.asset` 中的 `strippedAOTDllOutputRootDir: HybridCLRData/AssembliesPostIl2CppStrip`；`SettingsUtil.GetAssembliesPostIl2CppStripDir(target)` 在根目录后附加平台名。Obfuz 扩展与普通 HybridCLR GenerateAll **共用同一输出目录**，没有单独的 Obfuz AOT 目录。当前磁盘仅查到 `UnityProject/HybridCLRData/AssembliesPostIl2CppStrip/StandaloneWindows64/`，但未核对其生成时的开关或时间，不能判定它属于哪一次 GenerateAll。
- Obfuz 的 `ObfuscationProcess.OnPostBuildPlayerScriptDLLs` 是独立的 Player 构建回调。只有 `ObfuzSettings.buildPipelineSettings.enable` 为真，它才对 Player 构建脚本 DLL 中配置的程序集执行混淆，并把结果写回构建暂存目录；后续 HybridCLR 收集的裁剪 AOT DLL **可能**随之改变。开关为假时此回调直接返回。是否变化还取决于具体程序集是否在混淆范围、裁剪结果及其他构建输入，不能断言所有 AOT DLL 都不同，也不能断言开启 Obfuz 后 AOT DLL 必然相同。
- TEngine `CopyAOTAssembliesToAssetPath(target)` 只从这个平台的共享裁剪目录按清单复制 `.dll` 为 `.dll.bytes`，没有二次混淆。开启 Obfuz 后应先用相同平台和预期的 Player 回调开关运行 Obfuz 扩展 GenerateAll，再复制其新生成的裁剪 DLL。共享目录可能保留上一次不同配置的文件；只看目录名、文件存在或目标文件与源文件哈希相等，不足以确认产物属于本次生成。
- 热更 DLL 的另一路径不同：`BuildAndCopyDlls(target)` 使用 HybridCLR `CompileDll` 编译原始 DLL；在 `ENABLE_HYBRIDCLR && ENABLE_OBFUZ` 下调用 `ObfuscateHotUpdateAssemblies`，可选再调用 `GeneratePolymorphicDll`，最后复制热更 `.dll.bytes`。这不等于运行普通 HybridCLR GenerateAll。首包/需重新生成 Player 相关代码时，Obfuz 混淆热更 DLL 无论是否开启多态，都应使用 Obfuz 扩展 GenerateAll，使 MethodBridge 和 AOT 泛型引用基于混淆后热更程序集生成；普通热更包不必每次跑完整 GenerateAll，但新增桥接/AOT 需求时需要重新评估。
- 以上是源码推导；尚未在同一目标平台分别以 Player 回调开/关运行 GenerateAll 并对比各 AOT DLL，也未验证当前磁盘目录的产物来源。
