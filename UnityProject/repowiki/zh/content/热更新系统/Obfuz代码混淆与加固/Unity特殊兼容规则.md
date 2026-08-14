# Unity特殊兼容规则

## 为什么需要Unity策略

Unity 大量功能不是通过普通 IL 引用调用，而是通过引擎序列化、约定方法名、原生注册、生成代码或资产字符串连接。通用 .NET 混淆器若只按可见性改名，很容易得到“编译成功、运行失效”的 Player。

Obfuz 的 Unity RenamePolicy 和函数体策略会自动保护常见目标，但自动策略不能覆盖项目自定义协议。

## MonoBehaviour与ScriptableObject

常见保护点：

- 派生类型名和 namespace；
- Unity 可序列化字段；
- Unity 消息方法；
- RuntimeInitialize 入口；
- 与资产绑定的回调。

即使内置策略允许某些成员改名，也应对已有 Scene/Prefab/ScriptableObject 做全量 Player 加载回归，因为资产序列化行为随 Unity 版本、脚本后端和字段类型变化。

## Unity消息函数

Unity 按方法名调用：

- `Awake`、`OnEnable`、`Start`；
- `Update`、`LateUpdate`、`FixedUpdate`；
- `OnDestroy`；
- 碰撞、触发、渲染、应用生命周期等回调。

这些方法名不能被任意改写。函数体也不宜默认使用高成本 Pass，尤其 Update 系列。

## RuntimeInitializeOnLoadMethod

Obfuz 对这类方法有双重特殊处理：

- Symbol 策略保护类型/方法入口；
- 加载时机等于或早于 `AfterAssembliesLoaded` 时，函数体 Pass 默认禁用。

原因是这些入口可能负责初始化 EncryptionService。如果初始化逻辑自身被常量/调用等 Pass 改写，会形成“先解密才能初始化解密器”的循环依赖。

## UnityEvent与EventSystems

Inspector 中持久化 UnityEvent 可能保存目标对象与方法名。方法被改名后，事件可能静默不触发。需检查：

- Button.onClick 等持久化事件；
- Animation Event；
- EventTrigger；
- SendMessage/BroadcastMessage；
- 自研字符串事件绑定；
- UGUI/EventSystems 接口回调。

内置策略会保护部分接口实现，但 Inspector 中任意自定义方法仍应通过规则或扫描工具保名。

## Serializable类型与字段

对 `[Serializable]` class/struct、public 字段和 `[SerializeField]` 字段保持保守。特别是：

- 存档结构；
- Inspector 配置对象；
- 嵌套 serializable 类型；
- SerializeReference 多态类型；
- ManagedReferenceRegistry；
- 动画/Timeline 绑定。

SerializeReference 可能保存程序集限定类型名，是符号混淆高风险点，应实测当前 Unity 版本或禁用相关类型名混淆。

## Burst与DOTS

Burst、Jobs、Entities/DOTS 对 IL、泛型、布局和生成类型有额外要求。Obfuz 源码包含相应保护策略，但建议：

- Burst 编译方法禁用函数体 Pass；
- IComponentData、IBufferElementData 等组件类型和字段布局不做 FieldEncrypt；
- 生成代码、Baking、System 类型按当前 Entities 版本测试；
- 不让垃圾代码或辅助类型进入 Burst 扫描域；
- 升级 Unity/Entities/Burst 后重新做构建验证。

## IL2CPP与Mono

### Mono

官方 EncryptionService 将 `_encryptor` 暴露为 public，注释说明用于避免 Mono 访问 private 字段时的 FieldAccessException。说明混淆结果仍需兼顾旧 Mono 行为。

### IL2CPP

IL2CPP 对元数据名称、泛型、非法 IL 和裁剪更敏感。常见问题：

- 旧 mapping 保留了不合法名称；
- 第 4 类程序集未重写引用；
- link.xml 使用原始名且未经过转换；
- 某 Pass 生成的 IL 不被当前 IL2CPP 接受；
- 巨型控制流/dispatch 导致编译时间或 C++ 限制。

最终验收必须包含 IL2CPP Player，不可只用 Mono Editor。

## TEngine UI约束

[UIBase.cs](file://Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIBase.cs) 使用：

```csharp
CreateWidgetByPath<T>(parentTrans, typeof(T).Name)
```

类型名等于 YooAsset 地址，改名后会找不到资源。因此当前在 `UIBase` 上配置：

```csharp
[ObfuzIgnore(ObfuzScope.TypeName, ApplyToChildTypes = true)]
```

这是正确的兼容保护，但会保留所有 UI 派生类型名。若未来希望提高保护强度，应把资源地址改为稳定 Attribute/生成常量，而不是直接移除保护。

## TEngine热更新入口

主包通过反射寻找热更新 `GameApp.Entrance`，所以 [GameApp.cs](file://Assets/GameScripts/HotFix/GameLogic/GameApp.cs) 保护类型名和方法名。更稳的长期方案是：

- 入口类型/方法名使用稳定常量；
- 构建期校验混淆规则仍保留入口；
- 或生成入口映射而不是散落字符串。

## 资产与字符串协议扫描

建议在启用符号混淆前扫描：

```text
typeof(T).Name / FullName
GetType().Name / FullName
Type.GetType / Assembly.GetType
GetMethod / GetField / GetProperty
SendMessage / AnimationEvent
UnityEvent持久化方法
Resources.Load(typeof(T).Name)
YooAsset地址 = 类型名
SerializeReference / TypeNameHandling
```

## 官方来源

- [符号混淆](https://github.com/focus-creative-games/obfuz-doc/blob/main/docs/manual/symbol-obfuscation.md)
- [反射](https://github.com/focus-creative-games/obfuz-doc/blob/main/docs/manual/reflection.md)
- [序列化](https://github.com/focus-creative-games/obfuz-doc/blob/main/docs/manual/serialization.md)
- [函数体混淆](https://github.com/focus-creative-games/obfuz-doc/blob/main/docs/manual/method-body-obfuscation.md)

