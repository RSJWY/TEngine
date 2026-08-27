# 帧动画模块（FrameAnimModule）

## 背景

上游 TEngine 无序列帧动画系统。DGame 自研了一套纯 2D 序列帧动画（类 GIF/像素画，一秒换 8 张 Sprite），不依赖 Animator/Mecanim，用于 2D 像素角色、纸娃娃等场景。本 fork 将其迁移到热更层，并额外新增 RawImage 版本代理。

## 改动摘要

- 迁移 DGame `FrameAnimModule` 到 `GameLogic/Module/FrameAnimModule/`，包含场景版（`SpriteRenderer`）与 UI 版（UGUI `Image`）两套代理。
- **新增 `UIFrameRawAnimatorAgent`**：基于 UGUI `RawImage` 的帧动画代理，显示侧用 `rawImage.texture = sprite.texture`，适用于「每帧独立 PNG、不走图集打包」的场景。
- `FrameSpritePool` 的 Roslyn SourceGenerator（编译期生成 partial）改为**手写等价文件** `FrameSpritePool.Gen.cs`，不再依赖编译期生成器。
- `ModelConfig`（DGame Luban 生成类）依赖移除，改为新建 `FrameAnimConfig` 简单结构体（`FrameCfgLocation`/`ModelScale`/`DeathFrameSpeed`/`UIScale` 四字段）。
- 调度器 `FrameSpriteMgr` 用 `ITimerModule` 的 `int timerId` 替代 DGame 的 `GameTimer` 对象句柄。

### 行为变化

- `Agent.Init` 收 `FrameAnimConfig` 结构体而非 DGame 的 `ModelConfig`；调用方自行从任意数据源（SO/配置表/手填）构造。
- `FrameSpritePool` 字段名首字母大写（`Idle`/`Run`/`Attack`...），Inspector 填写需与此一致。
- 私有字段统一 `_小驼峰`（遵循 TEngine 命名规范）。

### 保持不变

- 帧动画核心算法、状态机逻辑、循环/非循环播放、速度缩放、UnscaledTime 支持完全保留。
- 1 秒 8 帧基础节奏（`FRAME_INTERVAL = 0.125f`）、1 秒 12 帧基础速度（`NORMAL_BASE_SPEED = 1.5f`）保持原值。

## 使用方式

```csharp
// 1. 构造配置
var config = new FrameAnimConfig
{
    FrameCfgLocation = "Assets/FrameAnim/Hero.prefab",
    ModelScale = 1.0f,
    DeathFrameSpeed = 1.0f,
    UIScale = 1.0f
};

// 2. UI 版（Image）
var agent = UIFrameAnimatorAgent.Create();
await agent.Init(config);
agent.BindDisplayRender(imageComponent);
agent.SwitchAnim(UIFrameAnimState.Move);
agent.StartAnim();

// 3. UI 版（RawImage）
var rawAgent = UIFrameRawAnimatorAgent.Create();
await rawAgent.Init(config);
rawAgent.BindDisplayRender(rawImageComponent);
rawAgent.SwitchAnim(UIFrameAnimState.Idle);
rawAgent.StartAnim();

// 4. 场景版（SpriteRenderer）
var sceneAgent = FrameAnimatorAgent.Create();
await sceneAgent.Init(config);
sceneAgent.BindDisplayRender(spriteRenderer);
sceneAgent.SwitchAnim(FrameAnimState.Attack);
sceneAgent.StartAnim();
```

`FrameSpritePool` Prefab 上 Inspector 按动画名填 Sprite 列表（字段名首字母大写：`Idle`/`Run`/`Attack`/`Skill`/`Hurt`/`Death` 等）。

## 注意事项

- **RawImage 版图集限制**：Sprite 经 SpriteAtlas 打包后多张共享同一 `Texture2D`，用 RawImage 会把整张大图当 texture 显示。`UIFrameRawAnimatorAgent` 仅适合每帧独立 PNG 的场景。
- **资源 Prefab**：帧动画 Prefab 需挂 `FrameSpritePool` 组件并填好各动画的 Sprite 列表。字段名与 `FrameSpritePool.Gen.cs` 一致。
- **新增动画枚举**：`FrameAnimName` 新增成员时需同步在 `FrameSpritePool.Gen.cs` 补字段与 `GetSprites` 的 case。
- **时间驱动**：代码用 `UnityEngine.Time.time`/`Time.unscaledTime`，由 `FrameSpriteMgr` 的 `ITimerModule` 循环计时器统一调度（采样间隔 `0.015625f`）。

## 关键文件

- `UnityProject/Assets/GameScripts/HotFix/GameLogic/Module/FrameAnimModule/FrameAnimConfig.cs`
- `UnityProject/Assets/GameScripts/HotFix/GameLogic/Module/FrameAnimModule/FrameAnimName.cs`
- `UnityProject/Assets/GameScripts/HotFix/GameLogic/Module/FrameAnimModule/FrameClip.cs`
- `UnityProject/Assets/GameScripts/HotFix/GameLogic/Module/FrameAnimModule/FrameSpritePool.cs`
- `UnityProject/Assets/GameScripts/HotFix/GameLogic/Module/FrameAnimModule/FrameSpritePool.Gen.cs`
- `UnityProject/Assets/GameScripts/HotFix/GameLogic/Module/FrameAnimModule/FrameSpriteMgr.cs`
- `UnityProject/Assets/GameScripts/HotFix/GameLogic/Module/FrameAnimModule/FrameAnimatorAgent.cs`
- `UnityProject/Assets/GameScripts/HotFix/GameLogic/Module/FrameAnimModule/UIFrameAnimatorAgent.cs`
- `UnityProject/Assets/GameScripts/HotFix/GameLogic/Module/FrameAnimModule/UIFrameRawAnimatorAgent.cs`

## 相关记录

- `UnityProject/conversation-summaries/code-research/2026-08-27-DGame模块迁移到TEngine.md`
- `UnityProject/conversation-summaries/2026-08-27-frame-anim-gameobject-pool-migration-summary.md`
