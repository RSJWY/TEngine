# DynamicSpawn 动态加载系统 — 使用教程

## 这是干嘛的

把场景里的预制体（树、建筑、装饰物等）从"直接放场景里"改成"运行时自动加载进来"。

好处：场景文件变小、加载变快、不卡顿。

## 原理（一句话）

场景里只放**空节点当占位**，告诉它"要加载哪个预制体"。游戏运行时系统自动把真东西加载出来，放到占位点的位置上。

---

## 第一步：写一个你的场景专属脚本

每个场景需要一个自己的加载脚本，继承 `DynamicSceneSpawner`。很短：

```csharp
using System.Collections.Generic;

namespace GameLogic
{
    public class 飞行测试Spawner : DynamicSceneSpawner
    {
        protected override List<SpawnItem> CollectSpawnItems()
        {
            return CollectFromSpawnPoints();
        }
    }
}
```

就这么多。把这个 .cs 文件放到 `Scenes/DynamicSpawn/` 目录下。

想在所有东西加载完之后做点什么？加一个方法：

```csharp
protected override void OnAllSpawned()
{
    // 比如：打个日志、播个动画、通知别的系统
}
```

---

## 第二步：场景里搭结构

打开你的场景，按这个结构搭：

```
场景根
 └─ DynamicSpawnRoot          ← 新建空物体，挂你写的 飞行测试Spawner 脚本
      ├─ 机库占位              ← 新建空物体，挂 DynamicSpawnPoint
      ├─ 树丛A占位             ← 新建空物体，挂 DynamicSpawnPoint
      └─ 跑道灯占位            ← 新建空物体，挂 DynamicSpawnPoint
```

### 具体操作

1. 场景里找到你要动态化的预制体（比如一个机库）
2. 记住它的位置和旋转（Inspector 里的 Position / Rotation）
3. 删掉这个预制体
4. 在同样的位置新建一个空物体
5. 给它挂上 `DynamicSpawnPoint` 脚本
6. 填好 `location`（见下一步）

---

## 第三步：填 location（告诉系统加载哪个预制体）

`location` 就是预制体的文件名（不要路径、不要 .prefab 后缀）。

比如预制体文件是 `Assets/AssetRaw/Prefabs/Hangar_01.prefab`，那 location 填 `Hangar_01`。

### 两种填法

**方法 A：手打**

直接在 Inspector 里的 `location` 框里输入文件名。

**方法 B：拖预制体自动填**

1. 把预制体拖到 Inspector 里的 `Prefab Reference` 框
2. 右键点组件标题 → 选"从预制体引用填充 Location"
3. 自动填好了

---

## 第四步：选对齐模式（大部分时候不用动）

Inspector 里有个 `Align Mode` 下拉框：

| 选项 | 什么意思 | 什么时候用 |
|------|---------|-----------|
| `AlignToPlaceholder` | 预制体出现在占位点的**精确位置** | 99% 的情况都用这个（默认） |
| `KeepPrefabLocal` | 预制体保留自己存的偏移，占位点只是个"容器" | 预制体原点不在中心/底部，改不了预制体时才用 |

**简单记**：不确定选哪个？就用默认的 `AlignToPlaceholder`。

---

## 第五步：调参数（可选）

在挂 Spawner 脚本的物体上，Inspector 里有两个参数：

| 参数 | 干什么 | 默认值 | 怎么调 |
|------|--------|--------|--------|
| `Batch Size` | 每一帧加载几个东西 | 3 | 东西多就调大点（5~10），模型大就调小点（1~2） |
| `Auto Start On Scene Ready` | 场景好了自动开始加载 | 勾选 | 保持勾选就行 |

---

## 运行效果

你什么代码都不用额外写，运行时自动发生：

```
进入场景 → 场景准备好了 → 系统开始逐批加载预制体 → 加载完毕
```

加载过程中不卡——因为是分批的，每帧只加载几个。

---

## 完整例子

假设飞行测试场景里有 3 个大建筑想动态化：

**1. 写脚本**（一次性）

```csharp
// 文件：Scenes/DynamicSpawn/FlyTestSpawner.cs
using System.Collections.Generic;

namespace GameLogic
{
    public class FlyTestSpawner : DynamicSceneSpawner
    {
        protected override List<SpawnItem> CollectSpawnItems()
        {
            return CollectFromSpawnPoints();
        }
    }
}
```

**2. 场景里搭**

```
DynamicSpawnRoot              [挂 FlyTestSpawner，Batch Size = 2]
  ├─ [Spawn] 机库             [挂 DynamicSpawnPoint，location = "Hangar_01"]
  │     Position = (100, 0, 50)   ← 和原来预制体一样的位置
  │     Rotation = (0, 45, 0)
  ├─ [Spawn] 塔台             [挂 DynamicSpawnPoint，location = "ControlTower"]
  │     Position = (200, 0, 80)
  └─ [Spawn] 油库             [挂 DynamicSpawnPoint，location = "FuelDepot"]
        Position = (150, 0, 120)
```

**3. 运行** → 三个建筑自动出现在正确位置。

---

## 资源释放

不用管。切场景时系统自动清理，不会内存泄漏。

---

## 文件清单

| 文件 | 作用 |
|------|------|
| `DynamicSceneSpawner.cs` | 基类，处理加载逻辑（不用改） |
| `DynamicSpawnPoint.cs` | 占位组件（不用改，直接挂节点上用） |
| 你写的 XxxSpawner.cs | 你的场景专属脚本（每个场景写一个） |
