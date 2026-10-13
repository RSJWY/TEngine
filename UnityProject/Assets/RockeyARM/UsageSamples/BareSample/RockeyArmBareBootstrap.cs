// ============================================================================================
// RockeyArmBareBootstrap.cs —— 可直接使用的裸接入 MonoBehaviour（空场景即可跑）
// ============================================================================================
// 用法：任意空场景新建一个 GameObject，把本脚本挂上去，直接进 Play 即可。
//   - 依赖只需 Assets/RockeyARM 目录（核心 + UI/prefab）与 Assets/Plugins/ROCKEY 原生库；
//   - 场景不需要 UIRoot/UICanvas/Camera/EventSystem：弹窗 prefab 自带 Canvas，
//     EventSystem 由 RockeyUIMgr 自动补；
//   - Editor 默认走 #if UNITY_EDITOR 短路（跳过真狗校验直接放行），打包后恒真实校验；
//   - 想看弹窗效果：勾选 Inspector 上的「空场景测试弹错误窗」，Start 时会模拟一次失败弹窗。
//
// 生命周期：Start 创建并驱动 RockeyArmManager；OnDestroy 成对注销事件并 Release（关句柄/停看门狗）。
// ============================================================================================

using Cysharp.Threading.Tasks;
using RockeyArm;
using UnityEngine;

namespace RockeyArm.Bare
{
    /// <summary>裸接入启动器：空场景挂上即可测试加密锁授权全流程。</summary>
    public sealed class RockeyArmBareBootstrap : MonoBehaviour
    {
        [Header("授权")]
        [Tooltip("通过验证后是否启动看门狗（每 2 秒复查）")]
        [SerializeField] bool m_startWatchdog = true;

        [Header("测试")]
        [Tooltip("空场景测试：Start 时直接弹一次授权错误窗（模拟失败路径，便于看 UI 效果）")]
        [SerializeField] bool m_testShowErrorUI;

        RockeyArmManager _arm;

        async void Start()
        {
            // UI 根节点可选：场景有 "UIRoot/UICanvas" 则挂其下，没有则弹窗自带 Canvas 独立显示。
            RockeyUIMgr.Initialize();

            if (m_testShowErrorUI)
            {
                RockeyUIMgr.ShowError("（测试）这是一条模拟的授权错误提示。");
            }

            _arm = new RockeyArmManager();
            _arm.Init();
            _arm.InfoLogger = Debug.Log;
            _arm.WarningLogger = Debug.LogWarning;
            _arm.ErrorLogger = Debug.LogError;
            _arm.LicenseError += OnLicenseError;

            bool pass = await _arm.CheckPermissionsAsync();
            if (pass)
            {
                Debug.Log("[BareBootstrap] 授权验证通过");
                if (m_startWatchdog)
                {
                    _arm.StartWatchdog().Forget();
                }
            }
            else
            {
                // 弹窗退出按钮内置退出动作（冻结时间 + 编辑器停播放 / 打包 Quit），无需再传回调。
                RockeyUIMgr.ShowError(_arm.LastError?.UserMessage ?? "未知授权错误!");
            }
        }

        void OnLicenseError(LicenseErrorInfo info)
        {
            // 运行期错误（如看门狗复查发现狗被拔出）：弹窗提示，退出动作内置。
            RockeyUIMgr.ShowError(info.UserMessage);
        }

        void OnDestroy()
        {
            // C# 事件成对注销 + 关闭狗句柄/停看门狗（裸环境没有框架托管，必须手动调）。
            if (_arm != null)
            {
                _arm.LicenseError -= OnLicenseError;
                _arm.Release();
                _arm = null;
            }
        }
    }
}
