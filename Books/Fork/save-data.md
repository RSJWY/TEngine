# ClientSaveData 存档系统与 DataCenter 数据中心

本页记录 fork 从 DGame 项目迁移的客户端存档系统和数据中心。TEngine 原生只有 `Utility.PlayerPrefs` 薄封装，缺少对象级存档、文件存储、版本管理和坏档保护；本改动将 DGame 的成熟存档框架与数据中枢整体迁入 `GameLogic` 程序集，复用 TEngine 已有的 `Singleton<T>`、`IUpdate` 和 `SingletonSystem` 自动驱动。

## 改动摘要

- 新增 `ClientSaveDataMgr`（存档管理器）：特性驱动注册、实例缓存、反射缓存、key 冲突校验、批量同步/异步保存。继承 `Singleton<ClientSaveDataMgr>` + `IUpdate`，由 `SingletonSystem` 自动注册和每帧驱动。
- 新增 `BaseClientSaveData`（存档基类）：Newtonsoft.Json 序列化、双存储后端（PlayerPrefs / JsonFile）、`PopulateObject` 填充保留对象引用、版本升级（`CurrentSaveDataVersion` / `OnUpgradeData`）、坏档备份（`.corrupt`）、PlayerPrefs→JsonFile 懒迁移、`SaveAsync` 切线程池写文件。
- 新增 `[ClientSaveData]` 特性：声明 `SaveKey` / `PerRoleID` / `StorageMode`，空 key 时类名兜底，key 字符自动清洗非法字符。
- 新增 `DataCenterSys`（数据中心）：玩家运行时数据中枢、业务子模块生命周期宿主（`OnInit` / `OnRoleLogin` / `OnRoleLogout` / `OnUpdate` / `OnMainPlayerMapChange`）。继承 `Singleton<DataCenterSys>` + `IUpdate`。
- 新增 `DataCenterModule<T>` 基类与 `IDataCenterModule` 接口：业务子模块模板，自管理单例。
- 新增 `PlayerData` / `BasePlayerData`：玩家运行时数据（RoleID / RoleNo / Uin / CreateTime / RoleName / IsInit），`private set` 不可变风格。
- 新增 `SystemSaveData` 示例存档与 `ClientSaveDataHelper` 扩展方法。
- `GameLogic.asmdef` 新增 `Newtonsoft.Json` 程序集引用。

## 背景

TEngine 的 `Utility.PlayerPrefs` 只是 PlayerPrefs 薄封装（int/float/string/bool + userId 前缀隔离），缺少实际项目刚需的对象级序列化、文件存储、版本管理和坏档保护。DGame 的 `ClientSaveData` 系统已处理了生产环境的坑（坏档隔离、版本迁移、异步 IO），依赖全是 TEngine 已有的（Newtonsoft.Json + UniTask），无新引入重依赖。迁移到 `GameLogic` 而非 `RuntimeTools`，是为了复用 `Singleton<T>` / `IUpdate` / `SingletonSystem`，避免同一功能多种实现。

## 使用方式

定义存档类：

```csharp
[ClientSaveData("MySaveData", perRoleID: true, StorageMode: ClientSaveDataStorageMode.JsonFile)]
public sealed class MySaveData : BaseClientSaveData
{
    public int Level { get; private set; }
    public string Name { get; private set; }
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
await save.SaveAsync();                 // 异步保存（JsonFile 切线程池）

ClientSaveDataMgr.Instance.SaveAllClientData();       // 批量同步保存
await ClientSaveDataMgr.Instance.SaveAllClientDataAsync(); // 批量异步保存
```

数据中心：

```csharp
DataCenterSys.Instance.SetCurPlayerData(playerData);  // 登录成功后填充
ulong roleID = DataCenterSys.Instance.CurRoleID;
DataCenterSys.Instance.ClearClientData();            // 登出清理
```

## 注意事项

- **自动驱动**：`DataCenterSys` 和 `ClientSaveDataMgr` 继承 `Singleton<T>` + 实现 `IUpdate`，首次访问 `Instance` 时自动注册到 `SingletonSystem`，每帧 `OnUpdate()` 自动调用，无需手动驱动。
- **`PerRoleID` 依赖登录态**：`PerRoleID=true` 但未登录（`DataCenterSys.TryGetCurRoleID` 返回 false）时，退化为全局 key，可能读到共享旧数据。按角色区分的存档应在登录后访问。
- **首次运行不落盘**：Load 时若存储为空，只初始化 `SaveDataVersion`，不调 `Save()`；业务改完数据需显式 `Save()`。
- **PlayerPrefs 模式无坏档备份**：`BackupCorruptJsonFile` 仅 JsonFile 模式生效。
- **批量保存时机**：`SaveAllClientData(Sync/Async)` 需在应用退出 / 切后台 / 定时等流程中由业务方调用。
- **`Newtonsoft.Json` 引用**：`GameLogic.asmdef` 新增了 `GUID:8c4bfcb5b17948478ccb955bccff9652`（`com.unity.nuget.newtonsoft-json`）。

## 关键文件

- `Assets/GameScripts/HotFix/GameLogic/DataCenter/ClientSaveData/ClientSaveDataAttribute.cs`
- `Assets/GameScripts/HotFix/GameLogic/DataCenter/ClientSaveData/BaseClientSaveData.cs`
- `Assets/GameScripts/HotFix/GameLogic/DataCenter/ClientSaveData/ClientSaveDataMgr.cs`
- `Assets/GameScripts/HotFix/GameLogic/DataCenter/ClientSaveData/ClientSaveDataHelper.cs`
- `Assets/GameScripts/HotFix/GameLogic/DataCenter/ClientSaveData/SystemSaveData.cs`
- `Assets/GameScripts/HotFix/GameLogic/DataCenter/DataCenterModule.cs`
- `Assets/GameScripts/HotFix/GameLogic/DataCenter/DataCenterSys.cs`
- `Assets/GameScripts/HotFix/GameLogic/DataCenter/PlayerData/BasePlayerData.cs`
- `Assets/GameScripts/HotFix/GameLogic/DataCenter/PlayerData/PlayerData.cs`
- `Assets/GameScripts/HotFix/GameLogic/GameLogic.asmdef`（新增 Newtonsoft.Json 引用）

## 相关记录

- 迁移自 [DGame](https://github.com/AmaniDawn/DGame) `Assets/Scripts/HotFix/GameLogic/DataCenter/`。
- 存档系统深度分析：`UnityProject/conversation-summaries/code-research/2026-08-26-dgame-clientsavedata-research.md`
- 迁移会话总结：`UnityProject/conversation-summaries/2026-08-26-clientsavedata-datacenter-migration-summary.md`
