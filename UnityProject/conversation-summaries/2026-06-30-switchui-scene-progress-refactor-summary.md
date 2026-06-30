# 2026-06-30 SwitchUI 场景加载进度拆分到 GameSceneModule 会话总结

## 背景

旧 `LoadingUI` 是“胖 UI”：三段式进度状态机、场景资源加载（suspendLoad）、场景激活（UnSuspend）、完成回调、Tips 文案、关闭时机全部塞在 UIWindow 里。且 `LoadingUI.cs` 引用了仓库中不存在的 `GameTipsData` 类型与已迁移的旧全局事件，实际已无法编译。

用户要求：参考 `LoadingUI` 的代码迁移到 `SwitchUI`；并把进度等相关内容从 UI 拆出，交给 `Assets/GameScripts/HotFix/GameLogic/Module/GameScene` 内管理——UI 仅做展示，场景加载进度与控制由 `GameSceneModule` 管理。

## 本次改动

### 1. `GameSceneModule.cs` —— 承载全部进度与加载控制

- 类签名由 `: Module, IGameSceneModule` 改为 `: Module, IGameSceneModule, IUpdateModule`，借 `ModuleSystem.Update` 每帧驱动状态机（`RootModule` 调 `ModuleSystem.Update(GameTime.deltaTime, GameTime.unscaledDeltaTime)`）。
- 新增 `float DisplayProgress`（只读，暴露平滑后的展示进度供 UI 渲染）。
- 把原 LoadingUI 的三段式进度逻辑原样迁入 `Update(elapse, realElapse)`，用 `realElapse`（unscaled）驱动，暂停时加载页动画不冻结。空闲期 `_isActive=false` 早退，避免每帧空转。
- `StartSceneLoad`（`LoadScene` / `JumpToMainScene` 共用启动器）：重置会话上下文 + 状态机 + `GameModule.UI.ShowUI<SwitchUI>()`（不传 UserData）。
- `StartRealLoading`：`GameModule.Scene.LoadSceneAsync(sceneName, Single, suspendLoad:true, priority:100, gcCollect:true, progressCallBack:OnLoadProgress)` fire-and-forget。
- `OnLoadProgress`：YooAsset 0~0.9 → 10%~90% 映射；phase≥2 拒更新防 target 被打回。
- `EnterFinishPhase`：90% 直接 `GameModule.Scene.UnSuspend(_sceneName)` 激活，并派发 `IGameSceneEvent.OnSceneLoadOver` 作对外通知（不再 UI 自发事件→模块自收）。
- `FinishAndClose`（统一终结出口）：`finishCallBack → CloseUI<SwitchUI> → OnSceneReady` 顺序，满足 `DynamicSceneSpawner` “SwitchUI 关闭后才收 OnSceneReady” 契约。
- 移除原 `_eventMgr` 字段与 `OnSceneLoadOver` 自监听（激活改直连）；`Shutdown` 不再触发业务回调。

### 2. `IGameSceneModule.cs`

- 新增 `float DisplayProgress { get; }` 接口成员。

### 3. `SwitchUI.cs` —— 纯展示

- 仅 override `OnUpdate`：读 `GameModule.GameScene.DisplayProgress`，写 `m_img_progress.fillAmount` 与 `m_tmp_progressText.text = "{percent}%"`。
- `[Window]` 层级 `UILayer.UI` → `UILayer.Top`（全屏遮罩，对齐 LoadingUI）。
- 不持任何加载状态、不主动关闭自身（由模块 `CloseUI<SwitchUI>` 关闭）。

### 4. `LoadSceneDataBody.cs` + `.meta` —— 删除

模块自持 `_sceneName` / `_finishCallBack`，不再需要数据载体透传给 UI。

### 5. `LoadingUI.cs` —— 整体块注释临时保留

按用户要求，整体用 `/* */` 注释并加废弃说明头，作功能逻辑参考；确认新逻辑无误后再删整个 `LoadingUI/` 目录。`LoadingUI_Gen.g.cs` 保留（孤立 partial，无 `[Window]`，编译无害、不被调用）。

## 执行流程（运行时）

```
GameSceneModule.LoadScene/JumpToMainScene(sceneType, finishCallBack)
  │  RecordScene(sceneType)                          记录上一关/当前关 + 同步 GameValueStatic
  │  GameEvent.Get<IGameSceneEvent>().OnSceneLoadStart(sceneType)   通知观察方
  ├─ StartSceneLoad: 重置状态机 _isActive=true, phase=0, target=0.10
  │                  （skip 模式: phase=1, display=0.10, 直接 StartRealLoading）
  └─ GameModule.UI.ShowUI<SwitchUI>()                打开加载页（纯展示）
       │
       ▼  SwitchUI.OnUpdate 每帧: 读 DisplayProgress → fillAmount + 百分比文本
       │
ModuleSystem.Update 每帧 → GameSceneModule.Update(elapse, realElapse)
  ├─ phase 0 预热 (0→10%): MoveTowards 0.10；到 0.10 → phase=1, StartRealLoading()
  │     └─ LoadSceneAsync(suspendLoad=true, cb=OnLoadProgress)   fire-and-forget
  │           OnLoadProgress(value): 0~0.9 → target 0.10~0.90；到 0.9 → _sceneLoadComplete=true
  ├─ phase 1 加载 (10%→90%): MoveTowards target；超时 5s 兜底进收尾
  │     到 _sceneLoadComplete && display≥0.89:
  │       ├─ skip 模式: display=1.0 → EnterFinishPhase → FinishAndClose
  │       └─ 正常: EnterFinishPhase
  └─ phase 2 收尾 (90%→100%+停留0.5s): delta 钳制≤0.05
        EnterFinishPhase: target=1.0; UnSuspend(sceneName) 激活; 派发 OnSceneLoadOver
        到 100% 停留满 → FinishAndClose:
          finishCallBack() → CloseUI<SwitchUI> → OnSceneReady(sceneType) → _isActive=false
                                                              │
                                                              ▼  DynamicSceneSpawner 收到后开始生成
```

## 关键设计点 / 陷阱（沿用原 LoadingUI 注释）

- **陷阱 1**：suspendLoad=true + progressCallBack 时 `LoadSceneAsync` 内部 `while(!IsDone)` 一直 yield，await 会死循环 → 只 fire-and-forget，进度全由 progressCallBack 驱动。
- **陷阱 2**：suspendLoad 时 IsDone 永远 false，progressCallBack 每帧回调 value=0.9 会反复覆盖 target → `OnLoadProgress` 在 phase≥2 直接 return，保护收尾 target=1.0 不被打回 0.90。
- **90% 激活而非 100%**：激活有一帧卡顿，留最后 10% 动画 + 100% 停留遮盖；100% 才激活会暴露突兀弹出。
- **phase 2 钳制 delta≤0.05**：激活帧 realElapse 可能数百毫秒，会把 90→100 动画和停留压成一帧。
- **GameTipsData 去除**：该类型仓库不存在（旧 LoadingUI 编译不过根因之一），SwitchUI prefab 也无 tips 节点，未迁移。
- **IUpdateModule 自动注册**：`ModuleSystem.RegisterUpdate` 检测 `IUpdateModule.IsInstanceOfType(module)` 自动加入轮询列表，只需实现接口。

## 验证状态

- 未在 Unity 编辑器中编译/运行验证（环境限制）。
- 已静态核对：`ShowUI<T>(params object[])` / `CloseUI<T>()` 重载存在；`IUpdateModule.Update` 签名匹配；`SwitchUI` prefab 位于 `Assets/AssetRaw/UI/SwitchUI.prefab`；全仓无残留 `LoadingUI` / `LoadSceneDataBody` / 旧全局事件 `Event_LoadOver`/`Event_SceneLoadStart` 的代码引用（仅出现在已注释的 LoadingUI 内）。
- **用户已在 Unity 编辑器实测通过**（`JumpToMainScene` / `LoadScene` 流程正常）。

## 后续收尾（验证通过后执行）

- 删除整个 `LoadingUI/` 目录（含 `LoadingUI.cs` / `LoadingUI_Gen.g.cs` 与对应 `.meta`）——**已执行**。
- 改动与流程分析追加到根目录 `README.md`「🛠️ 本 Fork 的定制改动」场景系统章节，并同步更新 `Books/Fork-定制改动说明.md` 场景系统小节——**已执行**。
