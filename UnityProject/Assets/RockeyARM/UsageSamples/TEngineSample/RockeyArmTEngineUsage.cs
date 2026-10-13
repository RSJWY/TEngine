// ============================================================================================
// RockeyArmTEngineUsage.cs —— 示例：在 TEngine 工程内的“正规用法”调用参考
// ============================================================================================
// 本文件不参与任何业务逻辑，仅作为写法参考；方法不会被自动调用，可整块复制走。
// 核心逻辑（校验/看门狗/诊断）零框架依赖，位于 Assets/RockeyARM/RockeyArmManager.cs；
// 本示例只做 TEngine 宿主接入：注册适配层模块、订阅 GameEvent 错误、用核心自带弹窗。
//
// 关键事实（阅读现实现后确认，勿凭记忆改动）：
//   * 适配层 RockeyArmModule 位于命名空间 TEngine（程序集 RockeyARM.TEngine），继承 TEngine.Module；
//     内部包装零框架核心 RockeyArm.RockeyArmManager，桥接 TEngine.Log 与 GameEvent。
//   * 注册用：ModuleSystem.RegisterModule<IRockeyArmModule>(new RockeyArmModule())。
//   * 对外能力（IRockeyArmModule，与核心一致）：IsLicensed / SessionToken / LastError；
//     CheckPermissions(同步)、CheckPermissionsAsync(异步)、StartWatchdog(看门狗) 三个方法；
//     另有 LicenseError 结构化 C# 事件（主包可用；热更域推荐用下面的 GameEvent）。
//   * 授权错误经适配层转 GameEvent 广播，事件 ID 为 RockeyArmModule.LICENSE_ERROR_EVENT_ID，
//     实际发送时用 RuntimeId.ToRuntimeId(id) 转成 int 通道；订阅方拿到的是一条 string（见下）。
//   * 生命周期：模块的 Shutdown 由 TEngine 的 ModuleSystem 接管（转发核心的 Release，
//     关狗句柄、停看门狗），业务侧不需要手动 Release；但 GameEvent 监听必须自己成对注销。
//   * 错误弹窗用核心自带的 RockeyUIMgr（零框架依赖，仿 Assets/Launcher）；
//     启动器阶段也可改用 Launcher.LauncherMgr.ShowMessageBox。
// ============================================================================================

using System;
using Cysharp.Threading.Tasks;
using RockeyArm;
using TEngine;

namespace RockeyArm.TEngineSample
{
    /// <summary>
    /// TEngine 下的加密锁授权模块用法参考。
    /// </summary>
    public static class RockeyArmTEngineUsage
    {
        // 事件通道：订阅/注销都要用到，抽成常量避免拼错。
        static readonly int ErrorEventId = RuntimeId.ToRuntimeId(RockeyArmModule.LICENSE_ERROR_EVENT_ID);

        /// <summary>
        /// 示例：注册模块 → 异步验证 → 通过后启动看门狗并订阅错误事件。
        ///
        /// 时机提示：
        ///   * 建议在启动流程（主包 ProcedureLaunch）做一次即可，不要每帧/每次进场景重复注册；
        ///     ModuleSystem.RegisterModule 重复注册同名模块会覆盖/告警，务必只注册一次。
        ///   * 注册后热更域可直接用 GameModule.RockeyArm 拿到同一实例（GameModule 在热更程序集，
        ///     本示例为避免引用热更程序集，这里直接用返回的实例引用，效果一致）。
        /// </summary>
        public static async UniTaskVoid RegisterAndVerifyAsync()
        {
            // 1) 注册适配层模块（只做一次；实例由框架接管生命周期，OnInit 会自动接好日志桥）
            RockeyArmModule module = new RockeyArmModule();
            ModuleSystem.RegisterModule<IRockeyArmModule>(module);

            // 2) 异步验证：设备指纹在主线程算，枚举/锁内 IO 在线程池，await 后回主线程。
            //    失败时不会抛异常，而是把结构化原因写进 LastError 并经 GameEvent 广播。
            bool pass = await module.CheckPermissionsAsync();
            if (!pass)
            {
                Log.Error("[RockeyArmSample] 授权验证失败：{0}", module.LastError?.UserMessage ?? "未知原因");
                return;
            }

            // 3) 通过后启动看门狗（每 2 秒复查；狗被拔/被拒会自动处理并广播错误）
            module.StartWatchdog().Forget();

            // 4) 订阅运行期授权错误（必须先注销再重订，避免重复；见 Unsubscribe）
            GameEvent.AddEventListener<string>(ErrorEventId, OnLicenseError);
        }

        /// <summary>
        /// 运行期授权错误回调：这里只拿到一条 string 提示；要完整诊断请读 module.LastError。
        /// </summary>
        static void OnLicenseError(string message)
        {
            Log.Error("[RockeyArmSample] 运行期授权错误：{0}", message);

            // UI 二选一（授权模块本身不持有任何 UI）：
            //   - 启动器阶段：Launcher.LauncherMgr.ShowMessageBox(message, onConfirm, onCancel);
            //   - 任意阶段：  RockeyUIMgr.ShowError(message, onConfirm: Application.Quit);
            //     （ExitErrorUI prefab 位于 Assets/RockeyARM/Resources/UIWindow/，自带 Canvas）
        }

        /// <summary>
        /// 注销监听：隐藏≠销毁，模块停止或被销毁前务必成对调用，否则会持有已销毁对象的引用。
        /// </summary>
        public static void Unsubscribe()
        {
            GameEvent.RemoveEventListener<string>(ErrorEventId, OnLicenseError);
        }

        // ---------------------------------------------------------------------------------------
        // 使用提示（Tips）
        // ---------------------------------------------------------------------------------------
        // 1) 同步 or 异步：主包 FSM/Procedure 用 CheckPermissions(onSuccess)；能 await 的地方用
        //    CheckPermissionsAsync()，后者把锁内 IO 丢线程池，不卡主线程。
        // 2) 看门狗只启动一次：StartWatchdog 内部有防重入标记，但语义上仍建议在验证通过后启动一次即可。
        // 3) Editor 短路：核心内部带 #if UNITY_EDITOR 宏，编辑器默认跳过真实校验直接放行；
        //    打包后该分支不参与编译，恒走真实校验。编辑器联调真狗需临时改代码。
        // 4) 线程约束：核心枚举/锁内 IO 可能在线程池执行，禁止在其中触碰 Unity API
        //    （核心已把指纹计算留在主线程，DeviceFingerprint 缓存复用）。
        // 5) 平台守卫：仅 Windows / Linux x86_64 支持，其它平台会直接返回 UnsupportedPlatform。
        // 6) 事件正文策略：适配层 SEND_FULL_ERROR_TO_GAME_EVENT=false，事件只发用户可读短提示，
        //    完整诊断走日志与 LastError；改成 true 则事件发完整诊断串（界面会很长，慎用）。
    }
}
