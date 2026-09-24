# ClientSaveData 存档系统与 DataCenter 数据中心

本页记录 fork 从 DGame 项目迁移的客户端存档系统和数据中心，以及后续使用 Nino 二进制序列化器替换 Newtonsoft.Json 的改造。TEngine 原生只有 `Utility.PlayerPrefs` 薄封装，缺少对象级存档、文件存储、版本管理和坏档保护；本改动将 DGame 的成熟存档框架与数据中枢整体迁入 `GameLogic` 程序集，复用 TEngine 已有的 `Singleton<T>`、`IUpdate` 和 `SingletonSystem` 自动驱动，并在此基础上将序列化层切换为高性能的 Nino 二进制格式。

## 改动摘要

- 新增 `ClientSaveDataMgr`（存档管理器）：特性驱动注册、实例缓存、反射缓存、key 冲突校验、批量同步/异步保存。继承 `Singleton<ClientSaveDataMgr>` + `IUpdate`，由 `SingletonSystem` 自动注册和每帧驱动。
- 新增 `BaseClientSaveData`（存档基类）：**Nino 二进制序列化**、双存储后端（PlayerPrefs / BinaryFile）、引用反序列化（`Deserialize(data, Type, ref object)` 填充已有对象保留引用）、版本升级（`CurrentSaveDataVersion` / `OnUpgradeData`）、坏档备份（`.corrupt`）、PlayerPrefs→BinaryFile 懒迁移、**旧版 JSON 存档一次性迁移到 Nino 二进制**、`SaveAsync` 切线程池写文件。
- 新增 `[ClientSaveData]` 特性：声明 `SaveKey` / `PerRoleID` / `StorageMode`，空 key 时类名兜底，key 字符自动清洗非法字符。
- 新增 `DataCenterSys`（数据中心）：玩家运行时数据中枢、业务子模块生命周期宿主（`OnInit` / `OnRoleLogin` / `OnRoleLogout` / `OnUpdate` / `OnMainPlayerMapChange`）。继承 `Singleton<DataCenterSys>` + `IUpdate`。
- 新增 `DataCenterModule<T>` 基类与 `IDataCenterModule` 接口：业务子模块模板，自管理单例。
- 新增 `PlayerData` / `BasePlayerData`：玩家运行时数据（RoleID / RoleNo / Uin / CreateTime / RoleName / IsInit），`internal set` 不可变风格。
- 新增 `SystemSaveData` 示例存档与 `ClientSaveDataHelper` 扩展方法。
- 新增 `Utility.Nino`（TEngine.Runtime 下）：Nino 序列化工具类，封装 `Serialize<T>` / `Deserialize<T>` / `DeserializeIntoClass<T>` / `SerializeToFile<T>` / `DeserializeFromFile<T>` / `SerializeToBase64<T>` / `DeserializeFromBase64<T>` / 旧版 JSON 迁移辅助方法。
- 序列化引擎从 `Newtonsoft.Json` 切换为 `Nino.Core`（Source Generator 编译时生成代码，零运行时反射）。

## 背景

TEngine 的 `Utility.PlayerPrefs` 只是 PlayerPrefs 薄封装（int/float/string/bool + userId 前缀隔离），缺少实际项目刚需的对象级序列化、文件存储、版本管理和坏档保护。DGame 的 `ClientSaveData` 系统已处理了生产环境的坑（坏档隔离、版本迁移、异步 IO），迁移到 `GameLogic` 而非 `RuntimeTools`，是为了复用 `Singleton<T>` / `IUpdate` / `SingletonSystem`，避免同一功能多种实现。

在此基础上，将序列化层从 Newtonsoft.JSON 切换为 Nino 二进制序列化器：
- **性能**：Nino 基于 Source Generator 在编译时生成序列化代码，零运行时反射，低 GC 压力、低内存分配。
- **体积**：二进制格式比 JSON 文本紧凑得多，尤其适合大体量存档（玩家进度、背包、任务状态等嵌套复杂数据结构）。
- **类型安全**：内置类型检查确保数据一致性，多态序列化自动保留类型元数据。
- **可维护性**：`[NinoType]` 显式标记哪些类要序列化，比 Newtonsoft 的"默认全序列化"更可控。

## 使用方式

### 定义存档类

```csharp
using Nino.Core;

[ClientSaveData("MySaveData", perRoleID: true, storageMode: ClientSaveDataStorageMode.BinaryFile)]
[NinoType(containNonPublicMembers: true)]
public sealed partial class MySaveData : BaseClientSaveData
{
    public int Level { get; internal set; }
    public string Name { get; internal set; }
    public static MySaveData Get => BaseClientSaveData.Get<MySaveData>();

    protected override int CurrentSaveDataVersion => 2;
    protected override void OnUpgradeData(int oldVersion, int newVersion)
    {
        if (oldVersion < 2) { /* 补齐新字段 */ }
    }
}
```

读写存档：

```csharp
var save = MySaveData.Get;              // 首次访问自动加载
save.Level = 99;
save.Save();                            // 同步保存
await save.SaveAsync();                 // 异步保存（BinaryFile 切线程池）

ClientSaveDataMgr.Instance.SaveAllClientData();       // 批量同步保存
await ClientSaveDataMgr.Instance.SaveAllClientDataAsync(); // 批量异步保存
```

数据中心：

```csharp
DataCenterSys.Instance.SetCurPlayerData(playerData);  // 登录成功后填充
ulong roleID = DataCenterSys.Instance.CurRoleID;
DataCenterSys.Instance.ClearClientData();            // 登出清理
```

### 使用 Utility.Nino 工具类

```csharp
using TEngine;

// 序列化为 byte[]
byte[] data = Utility.Nino.Serialize(myObject);

// 从 byte[] 反序列化
var obj = Utility.Nino.Deserialize<MyType>(data);

// 引用反序列化（填充已有对象，零分配）
Utility.Nino.DeserializeIntoClass(data, existingObject);

// 文件读写
Utility.Nino.SerializeToFile(myObject, filePath);
var obj = Utility.Nino.DeserializeFromFile<MyType>(filePath);

// Base64（PlayerPrefs 等字符串存储）
string base64 = Utility.Nino.SerializeToBase64(myObject);
var obj = Utility.Nino.DeserializeFromBase64<MyType>(base64);
```

## Nino 存档开发要求

> 以下要求适用于所有使用 Nino 序列化的存档类和业务类型。

### 1. 类型标记规则

- **所有需要序列化的 managed 类型（class / record / 含引用的 struct）必须标记 `[NinoType]`**。
- **unmanaged 类型**（基元类型 int/float/double、enum、只含 unmanaged 字段的 struct）不需要任何标记，Nino 自动序列化。
- **存档类继承链上每一层都需要标记 `[NinoType]`**：基类 `BaseClientSaveData` 已标记，子类必须额外标记。
- 如果类型包含 `private` / `protected` 成员（如 private/internal setter 的属性），使用 `[NinoType(containNonPublicMembers: true)]`。

### 2. partial 修饰符要求

- 在 Unity / Mono 环境（非 .NET 8+）下，使用 `containNonPublicMembers: true` 的类型**必须加 `partial` 修饰符**，否则 Source Generator 无法生成访问非 public 成员的代码。
- 建议：所有 Nino 序列化的存档类统一加 `partial`，避免环境差异导致的编译问题。

### 3. 属性 setter 可见性

- Nino 的 `containNonPublicMembers: true` 在 3.x 版本下可能无法直接写入 `private set` 的属性。
- 存档类的序列化属性应使用 `internal set` 而非 `private set`，确保 generator 能正常生成写入代码。
- 如果必须保持 `private set`，需将类标记为 `partial` 并验证 Source Generator 生成的代码能否编译通过。

### 4. 成员排除

- 不需要序列化的成员（运行时缓存、临时状态等）用 `[NinoIgnore]` 标记。
- `[NinoIgnore]` 仅在自动收集模式（`[NinoType]` 默认）下有效；手动收集模式（`[NinoType(false)]`）下不需要。

### 5. 版本兼容

- 字段结构变更时递增 `CurrentSaveDataVersion`，在 `OnUpgradeData` 中处理迁移。
- Nino 支持 `[NinoFormerName]` 处理成员重命名（需定义 `WEAK_VERSION_TOLERANCE` 符号以支持新增字段）。
- `[NinoMember(id)]` 可手动指定成员序列化顺序，确保新增成员排在已有成员之后。

### 6. 存储模式

- `ClientSaveDataStorageMode.PlayerPrefs`：Nino 序列化为 byte[] → Base64 → PlayerPrefs，适合轻量数据。
- `ClientSaveDataStorageMode.BinaryFile`：Nino 序列化为 byte[] → `.bin` 文件，适合大体量数据。
- 旧版 `JsonFile` 已重命名为 `BinaryFile`；首次加载时若发现旧版 `.json` 文件，会自动用 Newtonsoft.Json 读取并转换为 Nino 二进制格式，迁移成功后删除旧文件。

### 7. 多态与继承

- 接口、抽象类、派生类都需要标记 `[NinoType]` 才能参与多态序列化。
- Nino 自动保留运行时类型元数据，反序列化时自动恢复真实类型。

### 8. 集合类型

- 支持 `List<T>`、`Dictionary<TKey, TValue>`、`T[]`、`HashSet<T>`、`ICollection<T>`、`IDictionary<TKey, TValue>` 等。
- **自定义字典子类必须定义 public indexer**，否则不会生成序列化代码。

### 9. 热更程序集兼容

- Nino Source Generator 在 Unity 里对所有 asmdef 全局生效，包括热更程序集（如 `GameLogic`）。
- 热更程序集无需额外配置即可使用 Nino。
- 如果使用外部 C# 项目编译热更 DLL（HybridCLR / ILRuntime），需在 .NET Core 项目中通过 NuGet 安装 Nino。

## 注意事项

- **自动驱动**：`DataCenterSys` 和 `ClientSaveDataMgr` 继承 `Singleton<T>` + 实现 `IUpdate`，首次访问 `Instance` 时自动注册到 `SingletonSystem`，每帧 `OnUpdate()` 自动调用，无需手动驱动。
- **`PerRoleID` 依赖登录态**：`PerRoleID=true` 但未登录（`DataCenterSys.TryGetCurRoleID` 返回 false）时，退化为全局 key，可能读到共享旧数据。按角色区分的存档应在登录后访问。
- **首次运行不落盘**：Load 时若存储为空，只初始化 `SaveDataVersion`，不调 `Save()`；业务改完数据需显式 `Save()`。
- **PlayerPrefs 模式无坏档备份**：`BackupCorruptFile` 仅 BinaryFile 模式生效。
- **批量保存时机**：`SaveAllClientData(Sync/Async)` 需在应用退出 / 切后台 / 定时等流程中由业务方调用。
- **旧版 JSON 迁移**：首次加载发现 `.json` 文件时自动迁移到 `.bin`，迁移成功后删除 `.json`；迁移失败会走坏档备份逻辑。
- **二进制不可读**：Nino 存档为二进制格式，无法像 JSON 那样肉眼排查；开发期可通过日志和 `Utility.Nino.Deserialize<T>` 工具函数反序列化查看。

## 关键文件

- `Assets/TEngine/Runtime/Core/Utility/Utility.Nino.cs`（Nino 序列化工具类）
- `Assets/GameScripts/HotFix/GameLogic/DataCenter/ClientSaveData/ClientSaveDataAttribute.cs`
- `Assets/GameScripts/HotFix/GameLogic/DataCenter/ClientSaveData/BaseClientSaveData.cs`
- `Assets/GameScripts/HotFix/GameLogic/DataCenter/ClientSaveData/ClientSaveDataMgr.cs`
- `Assets/GameScripts/HotFix/GameLogic/DataCenter/ClientSaveData/ClientSaveDataHelper.cs`
- `Assets/GameScripts/HotFix/GameLogic/DataCenter/ClientSaveData/SystemSaveData.cs`
- `Assets/GameScripts/HotFix/GameLogic/DataCenter/DataCenterModule.cs`
- `Assets/GameScripts/HotFix/GameLogic/DataCenter/DataCenterSys.cs`
- `Assets/GameScripts/HotFix/GameLogic/DataCenter/PlayerData/BasePlayerData.cs`
- `Assets/GameScripts/HotFix/GameLogic/DataCenter/PlayerData/PlayerData.cs`

## 相关记录

- 迁移自 [DGame](https://github.com/AmaniDawn/DGame) `Assets/Scripts/HotFix/GameLogic/DataCenter/`。
- 存档系统深度分析：`UnityProject/conversation-summaries/code-research/2026-08-26-dgame-clientsavedata-research.md`
- 迁移会话总结：`UnityProject/conversation-summaries/2026-08-26-clientsavedata-datacenter-migration-summary.md`
- Nino 序列化库：[GitHub](https://github.com/JasonXuDeveloper/Nino) | [文档](https://nino.xgamedev.net/en/doc/start) | OpenUPM 包 `com.jasonxudeveloper.nino`
