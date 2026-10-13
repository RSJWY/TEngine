// RockeyArmManager.Platform.cs
// 作用：ROCKEY-ARM 原生库（Dongle_* API）平台网关，Windows / Linux 双平台 P/Invoke 声明。
// 搭配文件：IRockeyArm.cs（接口与诊断类型）、RockeyArmManager.cs、RockeyArmManager.Consts.cs
//
// 平台差异（API 签名、错误码、结构体在两个平台完全一致，均以厂商 Dongle_API.h 为准）：
// - Windows：Dongle_d.dll，StdCall 调用约定，插件位于 Assets/Plugins/ROCKEY/Windows/x86_64/。
// - Linux  ：libRockeyARM.so，Cdecl 调用约定，插件位于 Assets/Plugins/ROCKEY/Linux/x86_64/；
//            目标机使用前必须以 root 运行 ROCKEY-ARM_SDK_LINUX/base/inst 安装 udev 规则，
//            否则 Dongle_Enum 返回 NOT_FOUND / COMM_ERROR。
// - DllImport 传 "RockeyARM"，Mono/Igor 运行时会自动尝试 libRockeyARM.so（lib 前缀 + .so 后缀）。

using System;
using System.Runtime.InteropServices;

namespace RockeyArm
{
    public sealed partial class RockeyArmManager
    {
        /// <summary>
        /// 原生 Dongle_* API 网关：按编译目标平台选择库名与调用约定。
        /// Windows（含 Windows 编辑器）加载 Dongle_d.dll；Linux 加载 libRockeyARM.so。
        /// </summary>
        static class RockeyArmNative
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
            /// <summary>当前编译目标是否带有原生库支持（入口平台守卫用）。</summary>
            public static bool IsSupportedPlatform => true;
#else
            /// <summary>当前编译目标是否带有原生库支持（入口平台守卫用）。</summary>
            public static bool IsSupportedPlatform => false;
#endif

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            const string LIB = "Dongle_d";
            const CallingConvention CC = CallingConvention.StdCall;
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
            const string LIB = "RockeyARM";
            const CallingConvention CC = CallingConvention.Cdecl;
#endif

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
            [DllImport(LIB, CallingConvention = CC)]
            public static extern uint Dongle_Enum(IntPtr pDongleInfo, ref int pCount);

            [DllImport(LIB, CallingConvention = CC)]
            public static extern uint Dongle_Open(out IntPtr phDongle, int nIndex);

            [DllImport(LIB, CallingConvention = CC)]
            public static extern uint Dongle_Close(IntPtr hDongle);

            [DllImport(LIB, CallingConvention = CC)]
            public static extern uint Dongle_RunExeFile(IntPtr hDongle, ushort wFileID,
                byte[] pInOutBuf, ushort wInOutBufLen, out int pMainRet);
#else
            public static uint Dongle_Enum(IntPtr pDongleInfo, ref int pCount)
            {
                throw NotSupported();
            }

            public static uint Dongle_Open(out IntPtr phDongle, int nIndex)
            {
                throw NotSupported();
            }

            public static uint Dongle_Close(IntPtr hDongle)
            {
                throw NotSupported();
            }

            public static uint Dongle_RunExeFile(IntPtr hDongle, ushort wFileID,
                byte[] pInOutBuf, ushort wInOutBufLen, out int pMainRet)
            {
                throw NotSupported();
            }

            static NotSupportedException NotSupported()
            {
                return new NotSupportedException("ROCKEY-ARM 加密锁仅支持 Windows / Linux x86_64 平台");
            }
#endif
        }
    }
}
