# DGame ClientSaveData 存档系统深度分析

> 日期：2026-08-26
> 研究对象：DGame 本地存档系统（GitHub AmaniDawn/DGame，本地 `E:\Unity\DGame\GameUnity`）
> 文件范围：`Assets/Scripts/HotFix/GameLogic/DataCenter/ClientSaveData/` 全部文件 + 依赖的 `DGame/Utility/PlayerPrefsUtil`

## 一、系统总览

DGame 的本地存档系统是一套**面向对象的存档框架**，核心思路是：每个存档类型是一个继承 `BaseClientSaveData` 的类，用 `[ClientSaveData]` 特性声明存储元信息，由 `ClientSaveDataMgr` 统一注册/缓存/分发。

### 架构分层

```
ClientSaveDataMgr (单例管理器)
  │  注册表 + 缓存 + key 冲突校验
  │
  ├─► Dictionary<string, BaseClientSaveData> m_saveDataDict    (实例缓存)
  ├─► Dictionary<Type, ClientSaveDataAttribute> m_cacheAttributeDict (特性反射缓存)
  └─► Dictionary<string, Type> m_storageKeyTypeDict             (key→类型 校验表)

BaseClientSaveData (抽象基类)
  │  序列化 + 存储后端读写 + 版本升级 + 坏档备份 + 异步写入
  │
  ├─► SystemSaveData : BaseClientSaveData   (具体存档示例)
  ├─► (业务自定义存档A)
  └─► (业务自定义存档B)

ClientSaveDataAttribute (特性)
  └─► 声明 SaveKey / PerRoleID / StorageMode

ClientSaveDataHelper (扩展方法)
  └─► 针对 SystemSaveData 的便捷读写 API
```

## 二、各文件职责详解

### 1. ClientSaveDataAttribute.cs（特性，49 行）

标记在**类**上的特性（`ClientSaveDataAttribute.cs:5`），声明存档元信息：

| 属性 | 类型 | 默认 | 作用 | 行号 |
|------|------|------|------|------|
| `SaveKey` | `string` | `""` | 存档键名；空时由 Mgr 用类名兜底 | `:11` |
| `PerRoleID` | `bool` | `false` | 是否按角色 ID 区分存储 | `:16` |
| `StorageMode` | `ClientSaveDataStorageMode` | `PlayerPrefs` | 存储方式 | `:21` |

**两个构造函数**：
- `(saveKey, perRoleID, storageMode)`（`:29`）——完整版
- `(saveKey, storageMode)`（`:44`）——省略 perRoleID，默认 false

**key 清洗**（`:33-34`）：构造时把 `/` 和 `\` 替换成 `_`，避免后续做文件名出问题。
> 注意：特性拿不到被标记类型，所以"类名兜底"逻辑放在 `ClientSaveDataMgr.GetStorageKey`。

### 2. ClientSaveDataStorageMode 枚举（BaseClientSaveData.cs:11-22）

```csharp
public enum ClientSaveDataStorageMode
{
    PlayerPrefs,  // 少量轻量数据（Unity PlayerPrefs）
    JsonFile,     // persistentDataPath/ClientSaveData/ 下的 JSON 文件
}
```

### 3. BaseClientSaveData.cs（抽象基类，299 行 —— 核心）

承载了存档系统几乎全部机制。

#### 3.1 关键字段

| 字段 | 作用 | 行号 |
|------|------|------|
| `m_saveKey` | 存档键（`saveData_{SaveKey}` 或 `saveData_{SaveKey}_{roleID}`） | `:28` |
| `m_storageMode` | 存储后端 | `:29` |
| `m_needMigratePlayerPrefsToJson` | JsonFile 模式懒迁移标记 | `:31` |
| `SaveDataVersion` | 已存档的版本（`[JsonProperty]` 确保序列化） | `:42-43` |
| `CurrentSaveDataVersion` | 代码当前版本，默认 1，子类重写递增 | `:48` |

#### 3.2 生命周期

```
Init(key, mode)  ← 由 ClientSaveDataMgr.GetSaveData<T>() 首次访问时调用
   └─► Load()   ← 立即触发加载
```

#### 3.3 Load() 流程（:66-95）

```csharp
protected virtual void Load()
{
    try
    {
        m_needMigratePlayerPrefsToJson = false;
        string jsonStr = ReadJsonFromStorage();      // 读存储后端
        if (!string.IsNullOrEmpty(jsonStr))
        {
            JsonConvert.PopulateObject(jsonStr, this); // 填充到当前实例
            if (TryUpgradeSaveDataVersion() || m_needMigratePlayerPrefsToJson)
            {
                Save();  // 版本升级 或 需迁移 时回写
            }
        }
        else
        {
            SaveDataVersion = CurrentSaveDataVersion; // 首次运行初始化版本
        }
    }
    catch (Exception e)
    {
        LogStorageError("Load", e, GetLogFilePath());
        BackupCorruptJsonFile();  // 坏档备份
    }
    finally { m_needMigratePlayerPrefsToJson = false; }
}
```

**关键设计点**：
- 用 `PopulateObject` 而非 `DeserializeObject`——把 JSON 填充到**现有对象实例**，保留对象引用身份和 `private set` 属性。
- 读到数据后若需要版本升级或迁移，立刻 `Save()` 回写。
- 首次运行（无数据）只初始化版本号，不落盘（等业务首次 `Save()` 时写入）。

#### 3.4 Save() / SaveAsync()（:101-155）

**同步 Save()**（:101）：`JsonConvert.SerializeObject(this, Formatting.None)` → `WriteJsonToStorage`。

**异步 SaveAsync()**（:116）：
- PlayerPrefs 模式：直接调 `Save()`（PlayerPrefs 无异步）。
- JsonFile 模式：
  1. 主线程序列化 JSON（:128）
  2. `UniTask.SwitchToThreadPool()`（:137）
  3. 线程池写文件（:140）
  4. `UniTask.SwitchToMainThread()`（:148）返回
  - 避免主线程 IO 阻塞。

#### 3.5 存储后端读写（:173-209）

**ReadJsonFromStorage()**（:173）：
```csharp
case JsonFile:
    if (File.Exists(filePath)) return File.ReadAllText(filePath, UTF8);
    // 文件不存在 → 尝试读同 key 的 PlayerPrefs（懒迁移）
    string ppJson = PlayerPrefsUtil.GetString(m_saveKey);
    m_needMigratePlayerPrefsToJson = !string.IsNullOrEmpty(ppJson);
    return ppJson;
case PlayerPrefs:
    return PlayerPrefsUtil.GetString(m_saveKey);
```

**WriteJsonToStorage()**（:196）：
- JsonFile：`WriteJsonFile(filePath, jsonStr)`（自动建目录）
- PlayerPrefs：`PlayerPrefsUtil.SetString(m_saveKey, jsonStr)`

> **懒迁移机制**：当存档从 PlayerPrefs 升级到 JsonFile 时，旧数据还在 PlayerPrefs 里。JsonFile 模式读不到文件时会去读 PlayerPrefs，读到就标记 `m_needMigratePlayerPrefsToJson=true`，Load 完成后立刻 `Save()` 回写到文件，下次就直接读文件了。

#### 3.6 版本管理（:220-232）

```csharp
private bool TryUpgradeSaveDataVersion()
{
    if (SaveDataVersion >= CurrentSaveDataVersion) return false;
    int oldVersion = SaveDataVersion;
    OnUpgradeData(oldVersion, currentVersion);  // 虚方法，子类迁移字段
    SaveDataVersion = currentVersion;
    return true;
}
```
子类重写 `OnUpgradeData(old, new)`（:165）做字段补齐/迁移。

#### 3.7 坏档备份（:237-264）

仅 JsonFile 模式。读取/反序列化失败时把坏文件改名为 `xxx.json.corrupt`（若已存在则带时间戳 `xxx.json.{yyyyMMddHHmmss}.corrupt`），用 `File.Move` 移走，避免下次继续读坏档。

#### 3.8 便捷访问（:157-158）

```csharp
protected static T Get<T>() where T : BaseClientSaveData, new()
    => ClientSaveDataMgr.Instance.GetSaveData<T>();
```
子类可定义 `public static XXXSaveData Get => BaseClientSaveData.Get<XXXSaveData>();` 简化访问。

### 4. ClientSaveDataMgr.cs（管理器，112 行）

继承 `Singleton<ClientSaveDataMgr>`，维护三个字典。

#### 4.1 GetSaveData<T>()（:24-36）核心入口

```csharp
public T GetSaveData<T>() where T : BaseClientSaveData, new()
{
    ClientSaveDataAttribute attr = GetSaveDataAttribute<T>();  // 反射+缓存
    string key = GetStorageKey(typeof(T), attr);               // 生成key+校验
    if (!m_saveDataDict.TryGetValue(key, out var saveData))
    {
        saveData = new T();                                    // 无参构造
        saveData.Init(key, attr.StorageMode);                  // Init内部立刻Load
        m_saveDataDict[key] = saveData;                        // 缓存
    }
    return saveData as T;
}
```
**语义**：同一类型/key 全局只有一份实例（单例语义）。首次访问自动加载。

#### 4.2 特性反射缓存（:61-76）

```csharp
private ClientSaveDataAttribute GetSaveDataAttribute<T>()
{
    if (!m_cacheAttributeDict.TryGetValue(type, out var attr))
    {
        attr = type.GetCustomAttribute<ClientSaveDataAttribute>();
        if (attr == null) throw new DGameException($"未标记 SaveData: {type.Name}");
        m_cacheAttributeDict[type] = attr;
    }
    return attr;
}
```
未标记 `[ClientSaveData]` 直接抛异常——**强制声明元信息**。

#### 4.3 key 生成与冲突校验（:81-103）

```csharp
private string GetStorageKey(Type type, ClientSaveDataAttribute attr)
{
    string saveKey = string.IsNullOrWhiteSpace(attr.SaveKey) ? type.Name : attr.SaveKey;
    string storageKey;
    if (attr.PerRoleID && DataCenterSys.Instance.TryGetCurRoleID(out var roleID))
        storageKey = $"saveData_{saveKey}_{roleID}";   // 按角色
    else
        storageKey = $"saveData_{saveKey}";             // 全局

    // key 冲突校验
    if (m_storageKeyTypeDict.TryGetValue(storageKey, out var cacheType) && cacheType != type)
        throw new DGameException($"ClientSaveData key冲突: ...");
    m_storageKeyTypeDict[storageKey] = type;
    return storageKey;
}
```

**key 格式**：
- `PerRoleID=true`：`saveData_{SaveKey或类名}_{roleID}`
- `PerRoleID=false`：`saveData_{SaveKey或类名}`

**冲突校验**：同一 storageKey 被不同 Type 使用时抛异常——防误用保护。

> **耦合点**：`GetStorageKey` 依赖 `DataCenterSys.Instance.TryGetCurRoleID`（:86），即按角色区分存档需要数据中心已登录。

#### 4.4 批量保存（:41-59）

```csharp
public void SaveAllClientData()              // 同步遍历全部 Save()
public async UniTask SaveAllClientDataAsync() // 异步遍历逐个 await SaveAsync()
```
> 异步版是**逐个 await**（非并行），避免多文件并发写竞争。

#### 4.5 OnDestroy（:105-110）

仅清空三个缓存字典，**不触发保存**。保存时机由业务方决定。

### 5. SystemSaveData.cs（具体存档示例，31 行）

```csharp
[ClientSaveData("SystemSaveData")]
public sealed class SystemSaveData : BaseClientSaveData
{
    public enum SaveType { Max }  // 当前只有 Max，用作数组长度上限
    public int[] SettingParams { get; private set; } = new int[(int)SaveType.Max];
    public static SystemSaveData Get => BaseClientSaveData.Get<SystemSaveData>();
}
```
- 用 int 数组存系统设置，每下标对应一项（音量、画质等，待业务扩展）。
- 默认 PlayerPrefs 模式，未重写 `CurrentSaveDataVersion`（版本=1）。

### 6. ClientSaveDataHelper.cs（扩展方法，37 行）

针对 `SystemSaveData` 的便捷 API：

| 方法 | 作用 | 行号 |
|------|------|------|
| `GetSystemSettingVal(SaveType)` | 读 `SettingParams[(int)saveType]` | `:11` |
| `SaveSystemSettingVal(SaveType, int)` | 设置值 + **立即 Save()** | `:20` |
| `SetSystemSettingVal(SaveType, int)` | 仅设置值，不保存 | `:33` |

## 三、使用方式总结

### 定义存档类
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

### 读写存档
```csharp
// 读取（首次访问自动加载）
var save = MySaveData.Get;
save.Level = 99;
save.Save();              // 同步保存
await save.SaveAsync();   // 异步保存

// 批量保存所有存档
ClientSaveDataMgr.Instance.SaveAllClientData();
await ClientSaveDataMgr.Instance.SaveAllClientDataAsync();

// 泛型获取
var save = ClientSaveDataMgr.Instance.GetSaveData<MySaveData>();
```

## 四、设计亮点

1. **特性驱动注册**：`[ClientSaveData]` 声明元信息，Mgr 反射发现，业务侧零样板。
2. **双存储后端**：PlayerPrefs（轻量）+ JsonFile（大量），按需选择。
3. **按角色隔离**：`PerRoleID` 一键开启多角色存档隔离。
4. **版本升级**：`CurrentSaveDataVersion` + `OnUpgradeData` 支持字段结构演进。
5. **懒迁移**：PlayerPrefs→JsonFile 无感升级，读不到文件自动找旧数据并回写。
6. **坏档保护**：反序列化失败自动备份 `.corrupt`，避免反复读坏档。
7. **异步写入**：JsonFile 模式 `SaveAsync` 切线程池写文件，不阻塞主线程。
8. **key 冲突校验**：运行期发现两个类型用同一 key 立即报错。
9. **单例缓存**：同类型全局一份实例，避免重复加载。

## 五、潜在问题与注意点

### 5.1 SaveAllClientData 无调用方
在本次研究的 10 个文件中，`SaveAllClientData(Sync/Async)` 没有任何调用点。保存触发时机（应用退出/切后台/定时）需在其它入口流程文件确认。

### 5.2 PerRoleID 依赖 DataCenterSys 登录态
`GetStorageKey` 调 `DataCenterSys.Instance.TryGetCurRoleID`（:86）。若 `PerRoleID=true` 但未登录，`TryGetCurRoleID` 返回 false，会退化为全局 key `saveData_{saveKey}`——可能导致未登录时就访问按角色存档的数据时，读到了全局共享的旧数据。需注意调用时机。

### 5.3 首次运行不落盘
Load 时若存储为空，只设 `SaveDataVersion`，不调 `Save()`。若业务从不下显式 Save，存档永远不落盘（但这种情况罕见，正常业务都会改完数据调 Save）。

### 5.4 PlayerPrefs 模式无坏档备份
`BackupCorruptJsonFile` 只在 JsonFile 模式生效（:239）。PlayerPrefs 模式若 JSON 损坏，无法备份隔离，下次仍读坏数据。不过 PlayerPrefs 损坏概率远低于文件。

### 5.5 PopulateObject 的反序列化限制
`PopulateObject` 填充现有实例，要求字段/属性可写。`private set` 配合 `[JsonProperty]` 可以工作，但若字段标记了 `[JsonIgnore]` 不会被填充。新增字段时旧存档无此字段会保留默认值。

### 5.6 PlayerPrefs 存储的是完整 JSON 字符串
即使 StorageMode=PlayerPrefs，存的也是 `JsonConvert.SerializeObject(this)` 的完整 JSON 字符串到单个 key。意味着一个存档类占一个 PlayerPrefs string key。对于结构复杂的存档，PlayerPrefs 的单 key 容量（Windows 注册表值有上限）需注意——这正是 JsonFile 模式存在的理由。

## 六、与 TEngine 现有能力的对比

| 能力 | DGame ClientSaveData | TEngine |
|------|---------------------|---------|
| 对象存档 | ✅ 整个对象 JSON 序列化 | ❌ 只有 `Utility.PlayerPrefs` 原始类型 |
| 文件存储 | ✅ JsonFile 模式 | ❌ 无 |
| 按角色隔离 | ✅ `PerRoleID` | ⚠️ 只有 `SetUserId` 的 `userId_key` 前缀 |
| 版本升级 | ✅ `OnUpgradeData` | ❌ 无 |
| 坏档保护 | ✅ `.corrupt` 备份 | ❌ 无 |
| 异步写入 | ✅ `SaveAsync` 线程池 | ❌ 无 |
| 特性驱动注册 | ✅ `[ClientSaveData]` | ❌ 无 |
| key 冲突校验 | ✅ | ❌ 无 |

**结论**：DGame 的 ClientSaveData 是一套工程成熟的本地存档框架，TEngine 完全没有对应物（`Utility.PlayerPrefs` 仅是 PlayerPrefs 薄封装）。若要在 TEngine 上实现类似能力，可参考本系统设计移植。
