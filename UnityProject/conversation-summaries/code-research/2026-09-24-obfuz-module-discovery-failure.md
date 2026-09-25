# Obfuz 启用后业务模块发现失败

**研究日期**：2026-09-24
**关键词**：Obfuz、GameLogic、ModuleSystem、RegisterModule、GameSceneModule、IGameSceneModule、UIJumpControl、IUIJumpControl、Type.GetType

## 现象与定位

Player 日志 `2026-09-24/0000.log` 在 16:40:24.672 记录 `GameLogic.dll` 加载成功，随后 `GameApp.Entrance` 和 `StartGameLogic` 均进入。16:40:24.694 抛出 `Can not find Game Framework module type '$a.H, GameLogic'`。因此失败发生在业务模块创建阶段，不是热更 DLL 加载、密钥初始化或 ScreenModule 的窗口布局调用。

`GameApp.StartGameLogic` 先调用 `GameModule.Screen.ApplyAll()`，再通过 `GameModule.GameScene` 调用 `LoadScene`。后者进入 `GameModule.Get<IGameSceneModule>()`，再进入 `ModuleSystem.GetModule<T>()`。框架按接口的当前运行时名称拼接实现类型：`interfaceType.Namespace + "." + interfaceType.Name.Substring(1) + ", " + assemblyName`，然后调用 `Type.GetType`。

当前 `Assets/Obfuz/SymbolObfus/symbol-mapping.xml` 记录：

| 原类型 | 混淆后类型 | 框架拼出的实现名 |
| --- | --- | --- |
| `GameLogic.IGameSceneModule` | `$a.$H` | `$a.H` |
| `GameLogic.GameSceneModule` | `$a.$h` | 不匹配 `$a.H` |
| `GameLogic.IUIJumpControl` | `$a.$k` | `$a.k` |
| `GameLogic.UIJumpControl` | `$a.$K` | 不匹配 `$a.k` |

`GameSceneModule.OnInit` 也获取 `GameModule.UIJumpControl`，因此只修复场景模块仍会遇到第二组同类错误。`GameLogic` 热更程序集内继承 `TEngine.Module` 的业务类只有这两组。

## 处理方式

最终方案是在 `GameApp.StartGameLogic` 首次获取业务模块前，先通过 `ModuleSystem.RegisterModule<IUIJumpControl>(new UIJumpControl())` 注册 UI 跳转模块，再通过 `ModuleSystem.RegisterModule<IGameSceneModule>(new GameSceneModule())` 注册场景模块。`RegisterModule` 会立即调用 `OnInit`，而 `GameSceneModule.OnInit` 会获取 `GameModule.UIJumpControl`，因此顺序不可颠倒。注册后 `GetModule<T>()` 命中接口缓存，不再走 `Type.GetType`；两组接口与实现类均可继续混淆类型名。之前临时加在四个类型上的 `ObfuzIgnore(ObfuzScope.TypeName)` 已移除。

不使用 `ObfuscationInstincts.RegisterReflectionType<T>()`：它注册的类型映射只服务于调用方显式使用 `ObfuscationTypeMapper.GetTypeByOriginalFullName` 的情况，不会改变框架此处的 `Type.GetType` 行为。也不需要修改 `TEngine.Runtime` 框架模块发现实现。`GameApp` 仍需保留入口类型名和方法名，因为主包按字符串反射调用 `GameApp.Entrance`。

## 验证边界

源码修复需要重新执行 Obfuz、热更 DLL 同步、CodePackage 资源构建，并运行与当前资源版本匹配的 Player。旧映射与旧 DLL 不能证明新产物行为。当前项目版本为 Unity `2022.3.51f1c1`，项目 Unity CLI 要求 Unity 6+，因此不能用该 CLI 完成 Editor 侧验证；应在可用的 Unity Editor 中确认四个类型仍被混淆，并检查 Player 启动链路。
