using System;
using UnityEngine;
using TEngine;

#if ENABLE_OBFUZ && !UNITY_EDITOR
using Cysharp.Threading.Tasks;
using Obfuz;
using Obfuz.EncryptionVM;
using Launcher;
#endif

namespace TEngine
{
    /// <summary>
    /// Obfuz 密钥运行时初始化器（静态 + 动态）。
    /// </summary>
    /// <remarks>
    /// <para>静态密钥时机：<see cref="RuntimeInitializeLoadType.AfterAssembliesLoaded"/>——主包/AOT 程序集刚加载完、
    /// 任何被混淆代码执行前。这是官方推荐的静态密钥初始化最早时机，保证常量/字段在被读取前解密器就绪。</para>
    /// <para>动态密钥时机：由 <c>ProcedureLoadAssembly</c> 在 <c>Assembly.Load</c> 热更 DLL 前调用
    /// <see cref="SetUpDynamicSecretKeyAsync"/>。动态密钥文件作为 YooAsset 热更资源（<c>Assets/AssetRaw/DLL/Obfuz/</c>），
    /// 不随主包出包，可随热更版本轮换。</para>
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
        /// 加载动态密钥。由 <c>ProcedureLoadAssembly</c> 在加载热更 DLL 前调用。
        /// 密钥文件作为 YooAsset 热更资源（<c>Assets/AssetRaw/DLL/Obfuz/defaultDynamicSecretKey.bytes</c>），
        /// 不随主包出包。
        /// </summary>
        /// <param name="packageName">热更资源包名（与热更 DLL 同包）。</param>
        /// <returns><c>true</c>=成功；<c>false</c>=失败（已记 <see cref="Log.Fatal"/>，调用方应阻断 <c>Assembly.Load</c>）。</returns>
        public static async UniTask<bool> SetUpDynamicSecretKeyAsync(string packageName)
        {
            if (s_Failed)
            {
                return false;
            }

            TextAsset keyAsset = null;
            try
            {
                keyAsset = await ModuleSystem.GetModule<IResourceModule>()
                    .LoadAssetAsync<TextAsset>("defaultDynamicSecretKey", default, packageName);
            }
            catch (Exception e)
            {
                s_Failed = true;
                s_ErrorMsg = $"Obfuz 动态密钥加载失败：YooAsset 加载异常。{e.Message}";
                Log.Fatal($"[Obfuz] {s_ErrorMsg}");
                return false;
            }

            if (keyAsset == null || keyAsset.bytes == null || keyAsset.bytes.Length == 0)
            {
                s_Failed = true;
                s_ErrorMsg = "Obfuz 动态密钥加载失败：defaultDynamicSecretKey 资源缺失或为空。"
                    + "已将热更程序集纳入 assembliesUsingDynamicSecretKeys，但无动态密钥将无法解密混淆代码。";
                Log.Fatal($"[Obfuz] {s_ErrorMsg}");
                if (keyAsset != null)
                {
                    ModuleSystem.GetModule<IResourceModule>().UnloadAsset(keyAsset);
                }
                return false;
            }

            var dynamicSecretBytes = keyAsset.bytes;
            ModuleSystem.GetModule<IResourceModule>().UnloadAsset(keyAsset);

            EncryptionService<DefaultDynamicEncryptionScope>.Encryptor =
                new GeneratedEncryptionVirtualMachine(dynamicSecretBytes);
            Log.Info("[Obfuz] Dynamic secret key initialized (before Assembly.Load).");
            return true;
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
