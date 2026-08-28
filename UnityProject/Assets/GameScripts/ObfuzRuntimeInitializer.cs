using UnityEngine;
using TEngine;

#if ENABLE_OBFUZ && !UNITY_EDITOR
using Obfuz;
using Obfuz.EncryptionVM;
using Launcher;
#endif

namespace TEngine
{
    /// <summary>
    /// Obfuz 静态密钥运行时初始化器。
    /// </summary>
    /// <remarks>
    /// <para>时机：<see cref="RuntimeInitializeLoadType.AfterAssembliesLoaded"/>——主包/AOT 程序集刚加载完、
    /// 任何被混淆代码执行前。这是官方推荐的静态密钥初始化最早时机，保证常量/字段在被读取前解密器就绪。</para>
    /// <para>Editor 不执行：Obfuz 官方 FAQ 明确禁止在 Editor 下运行混淆后代码——Editor 已加载原始未混淆程序集，
    /// 混淆 DLL 引用混淆后类型会“找不到类”。且 EditorSimulateMode 加载原始未混淆程序集，常量未被加密，
    /// 注入 Encryptor 反而会把正常常量当密文解、破坏运行。故用 <c>!UNITY_EDITOR</c> 守卫。</para>
    /// <para>失败延迟报告：AfterAssembliesLoaded 时场景/UI 尚未就绪，无法弹窗。失败仅记标志 + <see cref="Log.Fatal"/>，
    /// 由 <c>ProcedureLaunch.OnEnter</c> 在 <c>LauncherMgr.Initialize()</c> 之后 UI 可用时调
    /// <see cref="CheckFailureAndReport"/> 消费，仅显示确认按钮，点击后 <see cref="Application.Quit"/>。</para>
    /// </remarks>
    public static class ObfuzRuntimeInitializer
    {
#if ENABLE_OBFUZ && !UNITY_EDITOR
        private static bool s_Failed;
        private static string s_ErrorMsg;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void SetUpStaticSecretKey()
        {
            var asset = Resources.Load<TextAsset>("Obfuz/defaultStaticSecretKey");
            if (asset == null || asset.bytes == null || asset.bytes.Length == 0)
            {
                s_Failed = true;
                s_ErrorMsg = "Obfuz 静态密钥加载失败：Resources/Obfuz/defaultStaticSecretKey.bytes 缺失或为空。"
                    + "已启用 ConstEncrypt/FieldEncrypt 等 Pass，但无密钥将无法解密混淆代码中的常量与字段，程序将退出。";
                Log.Fatal($"[Obfuz] {s_ErrorMsg}");
                return;
            }

            EncryptionService<DefaultStaticEncryptionScope>.Encryptor =
                new GeneratedEncryptionVirtualMachine(asset.bytes);
            Log.Info("[Obfuz] Static secret key initialized (AfterAssembliesLoaded).");
        }

        /// <summary>
        /// UI 就绪后由启动流程调用，报告初始化阶段延迟的致命错误。
        /// </summary>
        /// <returns>返回 <c>true</c> 表示存在致命错误、已弹出确认框，调用方应阻断后续流程。</returns>
        public static bool CheckFailureAndReport()
        {
            if (!s_Failed)
            {
                return false;
            }

            LauncherMgr.ShowMessageBox(s_ErrorMsg, Application.Quit);
            return true;
        }
#endif
    }
}
