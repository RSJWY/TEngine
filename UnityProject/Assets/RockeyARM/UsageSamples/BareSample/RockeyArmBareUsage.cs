// ============================================================================================
// RockeyArmBareUsage.cs —— 示例：不依赖 TEngine 的“裸使用”调用参考（纯 C# 流程）
// ============================================================================================
// 本文件不参与任何业务逻辑，仅演示在没有 TEngine 的环境里如何调用核心 RockeyArmManager；
// 方法不会被自动调用，可整块复制到新工程。
// 想直接跑？用同目录的 RockeyArmBareBootstrap.cs：空场景挂一个 GameObject 即可。
//
// 与 TEngine 用法（见 ../TEngineSample/RockeyArmTEngineUsage.cs）的唯一区别是宿主接入方式：
//   - 没有 ModuleSystem / GameModule：直接 new RockeyArmManager()，生命周期自己管（Release）；
//   - 没有 TEngine.Log：把三个 Logger 接到 Debug.Log*；
//   - 没有 GameEvent：订阅 LicenseError（普通 C# 事件，结构化 LicenseErrorInfo）；
//   - 没有 GameModule.UI：弹窗用核心自带的 RockeyUIMgr（Resources.Load 方式，仿 Assets/Launcher）。
// 核心校验 / 看门狗逻辑完全一致，均来自 Assets/RockeyARM/RockeyArmManager.cs。
// ============================================================================================

using System;
using Cysharp.Threading.Tasks;
using RockeyArm;
using UnityEngine;

namespace RockeyArm.Bare
{
    /// <summary>裸使用流程的纯 C# 参考（可由任意入口调用）。</summary>
    public static class RockeyArmBareUsage
    {
        /// <summary>
        /// 示例：创建 → 接日志 → 订阅错误 → 验证 → 启动看门狗 → 失败弹窗。
        /// </summary>
        public static async UniTaskVoid RunBareAsync()
        {
            // 1) 直接创建 + 初始化（无 ModuleSystem）
            var arm = new RockeyArmManager();
            arm.Init();

            // 2) 接日志（不接则静默；也可接到任意日志框架）
            arm.InfoLogger = Debug.Log;
            arm.WarningLogger = Debug.LogWarning;
            arm.ErrorLogger = Debug.LogError;

            // 3) 订阅授权错误（无 GameEvent，用 C# 事件；UI 提示取 UserMessage）
            arm.LicenseError += info =>
            {
                Debug.LogError($"[Bare] 授权错误：{info}");
                RockeyUIMgr.ShowError(info.UserMessage); // 退出由弹窗内置（冻结时间 + 编辑器停播放 / 打包 Quit）
            };

            // 4) 异步验证（失败原因已写入 LastError 并经 LicenseError 事件抛出）
            bool pass = await arm.CheckPermissionsAsync();
            if (!pass)
            {
                RockeyUIMgr.ShowError(arm.LastError?.UserMessage ?? "未知授权错误!");
                return;
            }

            // 5) 通过后启动看门狗
            arm.StartWatchdog().Forget();

            // ... 进入游戏 ...
            // 6) 宿主销毁时务必 Release（TEngine 版这一步由 RockeyArmModule.Shutdown 自动做）
            // arm.Release();
        }

        // -----------------------------------------------------------------------------------
        // 使用提示（Tips）
        // -----------------------------------------------------------------------------------
        // 1) 依赖：本程序集引用核心 RockeyARM（其内部只依赖 UniTask + UnityEngine + uGUI/TMP），
        //    不引用 TEngine（见 RockeyArmBareSample.asmdef）。若目标环境连 UniTask 都没有：
        //    把 UniTask 换成 Task / 协程 / 自定义主线程调度即可，核心枚举与锁内 IO 均为同步阻塞调用，
        //    异步只是为了避免卡主线程。
        // 2) 线程约束：FindMyDog/RunGuard 可能在线程池跑，禁止在其内部触碰 Unity API；
        //    指纹计算（SystemInfo）必须留主线程（CheckPermissionsAsync 已如此处理）。
        // 3) 平台：仅 Windows / Linux x86_64；其它平台会直接报 UnsupportedPlatform。
        //    记得把 Assets/RockeyARM/Plugins/ROCKEY/<平台>/x86_64 下的原生库随包带上。
        // 4) 生命周期：这是与 TEngine 版最大的坑——没人再帮你调 Release，
        //    必须在宿主销毁/切场景时手动调用，否则看门狗与狗句柄会泄漏。
        //    参考 RockeyArmBareBootstrap.OnDestroy 的标准写法。
        // 5) Editor 短路：RockeyArmManager 内部带 #if UNITY_EDITOR 宏，编辑器默认跳过真实校验直接放行；
        //    打包后该分支不参与编译，恒走真实校验。编辑器联调真狗需临时改代码。
        // 6) UI：授权模块本身不含 UI；RockeyUIMgr 只是“脱框架弹窗”的一个可用范例
        //    （自动补 EventSystem，空场景可用），你可以换成任何自己的 UI 方案，模块与 UI 保持解耦。
    }
}
