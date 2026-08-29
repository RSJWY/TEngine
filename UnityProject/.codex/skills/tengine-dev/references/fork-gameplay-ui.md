# Fork 业务数据、UI 与运行时工具

## 目录

- [DataBinding](#databinding)
- [ClientSaveData 与 DataCenter](#clientsavedata-与-datacenter)
- [FrameAnimModule](#frameanimmodule)
- [UGUI 扩展组件](#ugui-扩展组件)
- [Utility 扩展](#utility-扩展)
- [事件批量清理](#事件批量清理)
- [日志与计时工具](#日志与计时工具)

## DataBinding

DataBinding 是纯数据变化通知，不依赖 `UIWindow`、`UIWidget` 或 `GameEvent`，适合高频状态同步。

```csharp
[DataBindingModel]
public sealed class PlayerViewData
{
    public int hp;

    [DataBindTolerance(0.01f)]
    public float progress;

    [DataBindSignal]
    public bool confirmDown;

    [DataBindIgnore]
    public object runtimeCache;
}
```

运行 `Tools/数据绑定/生成` 后使用生成的 `PlayerViewDataBinder`：

```csharp
private readonly DataBindingScope _scope = new();
private readonly PlayerViewDataBinder _binder = new();

_scope.Add(_binder.hp.Subscribe(OnHpChanged));
_binder.SyncFrom(data);
_binder.Flush();

_scope.Dispose();
```

- 批量初始化可用 `SyncAndFlush(data)`。
- 高频字段可直接 `SetDirty`，最后统一 `Flush()`。
- `[DataBindSignal]` 只支持 bool，只在 `false -> true` 时发一次。
- 格式化文本和跨字段组合留在订阅方，不写入 Binder。
- 生成文件位于 `Assets/GameScripts/HotFix/GameLogic/Generated/DataBinding/`，不要手改。

## ClientSaveData 与 DataCenter

存档系统位于热更 `GameLogic/DataCenter/`，使用 `SingletonSystem` 自动驱动。

```csharp
[ClientSaveData("PlayerSave", perRoleID: true,
    StorageMode: ClientSaveDataStorageMode.JsonFile)]
public sealed class PlayerSave : BaseClientSaveData
{
    public int Level { get; private set; }
    public static PlayerSave Get => BaseClientSaveData.Get<PlayerSave>();

    protected override int CurrentSaveDataVersion => 2;

    protected override void OnUpgradeData(int oldVersion, int newVersion)
    {
        if (oldVersion < 2) { /* 补字段 */ }
    }
}
```

```csharp
PlayerSave.Get.Save();
await PlayerSave.Get.SaveAsync();
ClientSaveDataMgr.Instance.SaveAllClientData();
await ClientSaveDataMgr.Instance.SaveAllClientDataAsync();
```

- 支持 PlayerPrefs/JsonFile、版本升级、坏档 `.corrupt` 备份和 PlayerPrefs 到文件的懒迁移。
- `PerRoleID=true` 的存档应在登录后访问；未登录会退化为全局 key。
- 首次加载空存储不会自动落盘，业务修改后必须显式保存。
- 应用退出、切后台或定时节点由业务调用批量保存。
- `DataCenterSys` 是玩家运行时数据中枢，不要用存档对象替代当前会话状态。

## FrameAnimModule

序列帧动画位于 HotFix，提供三种代理：

```csharp
FrameAnimatorAgent       // SpriteRenderer
UIFrameAnimatorAgent     // UGUI Image
UIFrameRawAnimatorAgent  // UGUI RawImage
```

```csharp
var agent = UIFrameAnimatorAgent.Create();
await agent.Init(config);
agent.BindDisplayRender(image);
agent.SwitchAnim(UIFrameAnimState.Idle);
agent.StartAnim();
```

- `FrameAnimConfig` 由调用方构造，不依赖 Luban `ModelConfig`。
- `FrameSpritePool.Gen.cs` 是手写映射。新增 `FrameAnimName` 时同步补字段和 `GetSprites` case。
- RawImage 版只适合每帧独立 PNG；SpriteAtlas 中多帧共享 Texture 时会显示整张图集。
- Agent 来自 `MemoryPool`，按类提供的生命周期 API 创建和回收，不直接 `new`。

## UGUI 扩展组件

热更程序集提供：

- `UIButton`：点击保护、缩放、长按、双击、点击音效。
- `UIImage`：圆角、遮罩、镜像。
- `UIText`：描边、渐变、阴影、字间距、顶点色、环形排布。
- `RichTextItem`：图标、动画表情、超链接。
- Utility 组件：`EmptyGraph`、`NestedScrollRect`、`CircleLayoutGroup`、`UIEffectSortingOrder`、`UIDragListener`、`UIExtension`、`UIImageEffect`。

关键约束：

- `UIButton` 点击音效使用资源地址字符串：`SetClickSoundLocation(string)`，不查 Luban 音效表。
- `AudioType` 同名冲突时写 `using AudioType = TEngine.AudioType;`。
- `UIText` 描边依赖 YooAsset location `UGUIPro_UIText`。
- `UIButton` 默认点击音效地址 `btn_click`，资源不存在时只影响音效。
- `SuperScrollView` 未迁移，不要生成 `LoopListView2` / `LoopGridView` 依赖。
- Editor Inspector 脚本放 `Assets/Editor/UIModuleExpansion/`，基类写 `UnityEditor.Editor` 完全限定名。

## Utility 扩展

`Utility.Unity` 已补充组件增删、子节点查找、递归 Layer、EventTrigger、随机数、实例化、射线、正则、材质、触摸、数组和分辨率 API。

常用调用：

```csharp
var comp = Utility.Unity.AddMonoBehaviour<MyComponent>(go);
Transform child = Utility.Unity.FindChild(root, "Node");
Utility.Unity.SetLayer(go, layer);
button.AddCustomEventListener(EventTriggerType.PointerDown, OnDown);
```

JSON 支持覆盖已有对象：

```csharp
Utility.Json.FromJsonOverwrite(json, existingObject);
```

命名空间陷阱：不要在 `namespace GameLogic` 下创建 `Utility` partial class，否则会遮蔽 `TEngine.Utility`。DGame 迁移代码中的工具类应改为独立类名或使用 `TEngine.Utility` 完全限定名。

## 事件批量清理

需要按事件 ID 清空全部监听时：

```csharp
GameEvent.RemoveAllListeners(eventId);
GameEvent.RemoveAllListeners("event-name");
GameEvent.RemoveAllListeners(IGameSceneEvent.OnSceneLoadOver);
```

这是全量清空该 ID 的监听，不是定向解绑。组件能保存原委托时仍优先 `RemoveEventListener`；UI 正常使用 `AddUIEvent` 自动清理。

## 日志与计时工具

- `UnityLoggerBridge` 自动把 Unity、Task、UniTask 日志及未观察异常落到 `Application.persistentDataPath/Logs/yyyy-MM-dd/`。
- TouchSocket 可通过 `registrator.AddUnityDebugLogger()` 接入 Unity Console。
- `GameTickWatcher` 位于独立 `RuntimeTools` 程序集，构造即计时，`ElapseTime()` 返回秒，`Restart()` 重置。
- 日志桥接有重入保护；不要再套一层把文件日志重新写回 Unity Console 的循环。
