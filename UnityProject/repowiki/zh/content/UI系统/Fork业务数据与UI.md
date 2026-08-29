# Fork 业务数据与 UI

本文汇总当前 fork 的 DataBinding、客户端存档、序列帧动画、UGUI 扩展和相关工具。迁移背景与关键文件见 [Fork 定制改动总览](../../../../../Books/Fork/README.md)。

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
}
```

生成入口：

- `Tools/数据绑定/生成`
- `Tools/数据绑定/生成器面板`

生成后使用 Binder：

```csharp
private readonly DataBindingScope _scope = new();
private readonly PlayerViewDataBinder _binder = new();

_scope.Add(_binder.hp.Subscribe(OnHpChanged));
_binder.SyncFrom(data);
_binder.Flush();

_scope.Dispose();
```

约束：

- `[DataBindSignal]` 只支持 bool，并只在 `false -> true` 时通知。
- 格式化文本和跨字段组合留在订阅方。
- 生成文件位于 `Assets/GameScripts/HotFix/GameLogic/Generated/DataBinding/`，不要手工修改。

详细说明见 [data-binding.md](../../../../../Books/Fork/data-binding.md)。

## ClientSaveData 与 DataCenter

客户端存档位于热更层 `GameLogic/DataCenter/`：

```csharp
[ClientSaveData("PlayerSave", perRoleID: true,
    storageMode: ClientSaveDataStorageMode.JsonFile)]
public sealed class PlayerSave : BaseClientSaveData
{
    public int Level { get; private set; }
    public static PlayerSave Get => BaseClientSaveData.Get<PlayerSave>();

    protected override int CurrentSaveDataVersion => 2;

    protected override void OnUpgradeData(int oldVersion, int newVersion)
    {
        if (oldVersion < 2)
        {
            // 补齐新版本字段
        }
    }
}
```

```csharp
PlayerSave.Get.Save();
await PlayerSave.Get.SaveAsync();
await ClientSaveDataMgr.Instance.SaveAllClientDataAsync();
```

- 支持 PlayerPrefs 和 JsonFile。
- 支持版本升级、坏档 `.corrupt` 备份以及 PlayerPrefs 到文件的懒迁移。
- `PerRoleID=true` 的数据应在登录完成后访问。
- 首次空存储不会自动写入，数据修改后必须显式保存。
- `DataCenterSys` 管理当前会话数据，不要用存档对象代替运行时状态。

详细说明见 [save-data.md](../../../../../Books/Fork/save-data.md)。

## FrameAnimModule

序列帧动画提供三种代理：

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

- `FrameAnimConfig` 由业务构造，不依赖 Luban `ModelConfig`。
- `FrameSpritePool.Gen.cs` 是手写映射，新增动画名时需要同步更新。
- RawImage 版本适合每帧独立纹理，不适合共享 Texture 的 SpriteAtlas 多帧。
- Agent 来自内存池，必须按类型提供的生命周期 API 回收。

详细说明见 [frame-anim.md](../../../../../Books/Fork/frame-anim.md)。

## UGUI 扩展组件

| 组件 | 能力 |
| --- | --- |
| `UIButton` | 点击保护、缩放、长按、双击、点击音效 |
| `UIImage` | 圆角、遮罩、镜像 |
| `UIText` | 描边、渐变、阴影、字间距、顶点色、环形排布 |
| `RichTextItem` | 图标、动画表情、超链接 |

Utility 组件包括 `EmptyGraph`、`NestedScrollRect`、`CircleLayoutGroup`、`UIEffectSortingOrder`、`UIDragListener`、`UIExtension` 和 `UIImageEffect`。

关键约束：

- 点击音效使用资源地址：`SetClickSoundLocation(string)`，不依赖 Luban 音效表。
- `UIText` 描边依赖 YooAsset location `UGUIPro_UIText`。
- `SuperScrollView` 未迁移，不要生成 `LoopListView2` 或 `LoopGridView` 依赖。
- Inspector 脚本放在 `Assets/Editor/UIModuleExpansion/`。

详细说明见 [ui-expansion.md](../../../../../Books/Fork/ui-expansion.md)。

## Utility 与事件扩展

```csharp
var component = Utility.Unity.AddMonoBehaviour<MyComponent>(gameObject);
Transform child = Utility.Unity.FindChild(root, "Node");
Utility.Unity.SetLayer(gameObject, layer);
button.AddCustomEventListener(EventTriggerType.PointerDown, OnPointerDown);

Utility.Json.FromJsonOverwrite(json, existingObject);
```

不要在 `namespace GameLogic` 下声明名为 `Utility` 的类，否则会遮蔽 `TEngine.Utility`。

按事件 ID 清空全部监听：

```csharp
GameEvent.RemoveAllListeners(eventId);
GameEvent.RemoveAllListeners("event-name");
```

这是全量清理；能够保存原委托时仍优先定向解绑，UI 内正常使用 `AddUIEvent` 自动释放。

## 日志与计时工具

- `UnityLoggerBridge` 将 Unity、Task、UniTask 和未观察异常写入持久化日志目录。
- TouchSocket 使用 `AddUnityDebugLogger()` 接入 Unity Console。
- `GameTickWatcher` 位于独立 `RuntimeTools` 程序集，`ElapseTime()` 返回秒，`Restart()` 重置计时。
- 日志桥接带重入保护，不要构造“文件日志 -> Unity Console -> 文件日志”的循环。
