# DGame ClientSaveData + DataCenter 迁移到 TEngine GameLogic

> 日期：2026-08-26
> 任务：将 DGame 项目的 ClientSaveData 存档系统 + DataCenter 数据中心迁移到 TEngine

## 背景

DGame（GitHub: AmaniDawn/DGame，本地 `E:\Unity\DGame\GameUnity`）有一套成熟的本地存档系统（ClientSaveData）和数据中心（DataCenterSys）。TEngine 完全没有对应物（`Utility.PlayerPrefs` 只是 PlayerPrefs 薄封装）。研究确认两者依赖与 TEngine 高度兼容（Newtonsoft.Json + UniTask 均已有）。

## 迁移过程

### 方案一（已废弃）：放 RuntimeTools

最初迁移到 `Assets/GameScripts/RuntimeTools/`，但因 RuntimeTools 程序集不引用 GameLogic，无法复用 TEngine 的 `Singleton<T>` 和 `IUpdate`，导致被迫自管理单例 + 自定义局部 IUpdate 接口——**同一功能多种实现**，违背"减少重复"原则。

### 方案二（最终采用）：放 GameLogic

迁移到 `Assets/GameScripts/HotFix/GameLogic/DataCenter/`，直接复用 GameLogic 已有的 `Singleton<T>`、`IUpdate`、`GameModule.UI`、`SingletonSystem` 自动驱动。

## 最终文件清单

```
Assets/GameScripts/HotFix/GameLogic/DataCenter/
├── DataCenterSys.cs                  继承 Singleton<DataCenterSys>, IUpdate
├── DataCenterModule.cs                IDataCenterModule 接口 + DataCenterModule<T> 单例基类
├── PlayerData/
│   ├── BasePlayerData.cs              玩家数据基类（m_roleID）
│   └── PlayerData.cs                  玩家数据（RoleID/RoleNo/Uin/CreateTime/RoleName/IsInit）
└── ClientSaveData/
    ├── ClientSaveDataAttribute.cs     特性：SaveKey/PerRoleID/StorageMode
    ├── BaseClientSaveData.cs          抽象基类：JSON序列化/双存储/版本升级/坏档备份/异步写入
    ├── ClientSaveDataMgr.cs           管理器：继承 Singleton<ClientSaveDataMgr>, IUpdate
    ├── ClientSaveDataHelper.cs        扩展方法：SystemSaveData 便捷读写
    └── SystemSaveData.cs              示例存档：系统设置参数数组
```

## asmdef 变更

| 程序集 | 变更 | GUID |
|--------|------|------|
| GameLogic.asmdef | **新增引用** Newtonsoft.Json.dll | `8c4bfcb5b17948478ccb955bccff9652` |
| RuntimeTools.asmdef | 无变更（恢复原状） | — |

## 相对 DGame 原版的适配点

| 原版（DGame） | 迁移版（TEngine GameLogic） | 原因 |
|---|---|---|
| `DGame.Utility.PlayerPrefsUtil` | `TEngine.Utility.PlayerPrefs` | TEngine 等价封装 |
| `DGame.DLogger.Error` | `TEngine.Log.Error` | TEngine 日志系统 |
| `DGameException` | `InvalidOperationException` | 不引入 DGame 依赖 |
| `Singleton<T>.OnDestroy()` | `Singleton<T>.OnRelease()` | TEngine Singleton 用 Release 命名 |
| `GameModule.UIModule.CloseAllWindows()` | `GameModule.UI.CloseAll()` | TEngine UIModule 方法名不同 |
| `CurPlayerData` 无外部 setter | 新增 `SetCurPlayerData()` 方法 | 原版 setter 在未提供的 partial 文件中 |
| `ClearClientData` 不置空 PlayerData | 新增 `CurPlayerData = null` | 修复原版遗漏 |

## 依赖验证

全部已验证可达：
- ✅ `Singleton<T>` / `IUpdate` — 同程序集 GameLogic 内
- ✅ `Newtonsoft.Json` — GameLogic.asmdef 新增引用
- ✅ `Cysharp.Threading.Tasks`（UniTask）— GameLogic 已有引用
- ✅ `TEngine.Utility.PlayerPrefs` / `TEngine.Log` — GameLogic 引用 TEngine.Runtime
- ✅ `UnityEngine.Application.persistentDataPath` — 引擎默认可见
- ✅ `GameModule.UI.CloseAll()` — 同程序集，方法名已验证

## 后续需业务侧接入

1. 登录成功后调 `DataCenterSys.Instance.SetCurPlayerData(playerData)` 填充玩家数据
2. 应用退出/切后台时调 `ClientSaveDataMgr.Instance.SaveAllClientData()` 批量保存
3. `DataCenterSys` 和 `ClientSaveDataMgr` 继承 `Singleton<T>` + 实现 `IUpdate`，会被 `SingletonSystem` 自动注册和每帧驱动（无需手动调 OnUpdate）
