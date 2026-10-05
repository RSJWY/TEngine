using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;

namespace TEngine.Editor
{
    /// <summary>
    /// 脚本宏定义操作类。
    /// </summary>
    public static class ScriptingDefineSymbols
    {
        private static readonly BuildTargetGroup[] BuildTargetGroups = new BuildTargetGroup[]
        {
            BuildTargetGroup.Standalone,
            BuildTargetGroup.iOS,
            BuildTargetGroup.Android,
            BuildTargetGroup.WSA,
            BuildTargetGroup.WebGL
        };

        /// <summary>
        /// 需要额外处理 Dedicated Server 子平台的 NamedBuildTarget 列表。
        /// <para>Unity 6 DS 构建（StandaloneBuildSubtarget.Server）用 NamedBuildTarget.Server，
        /// 它和 NamedBuildTarget.Standalone 是独立的 NamedBuildTarget，有独立的 scriptingDefineSymbols。
        /// 不处理 Server 会导致 DS 包缺 ENABLE_LOG 等 define，所有 [Conditional] 标记的日志调用被编译器跳过。</para>
        /// </summary>
        private static readonly NamedBuildTarget[] ExtraNamedBuildTargets = new NamedBuildTarget[]
        {
            NamedBuildTarget.Server,
        };

        /// <summary>
        /// 检查指定平台是否存在指定的脚本宏定义。
        /// </summary>
        /// <param name="buildTargetGroup">要检查脚本宏定义的平台。</param>
        /// <param name="scriptingDefineSymbol">要检查的脚本宏定义。</param>
        /// <returns>指定平台是否存在指定的脚本宏定义。</returns>
        public static bool HasScriptingDefineSymbol(BuildTargetGroup buildTargetGroup, string scriptingDefineSymbol)
        {
            if (string.IsNullOrEmpty(scriptingDefineSymbol))
            {
                return false;
            }

            string[] scriptingDefineSymbols = GetScriptingDefineSymbols(buildTargetGroup);
            foreach (string i in scriptingDefineSymbols)
            {
                if (i == scriptingDefineSymbol)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 为指定平台增加指定的脚本宏定义。
        /// </summary>
        /// <param name="buildTargetGroup">要增加脚本宏定义的平台。</param>
        /// <param name="scriptingDefineSymbol">要增加的脚本宏定义。</param>
        public static void AddScriptingDefineSymbol(BuildTargetGroup buildTargetGroup, string scriptingDefineSymbol)
        {
            if (string.IsNullOrEmpty(scriptingDefineSymbol))
            {
                return;
            }

            if (HasScriptingDefineSymbol(buildTargetGroup, scriptingDefineSymbol))
            {
                return;
            }

            List<string> scriptingDefineSymbols = new List<string>(GetScriptingDefineSymbols(buildTargetGroup))
            {
                scriptingDefineSymbol
            };

            SetScriptingDefineSymbols(buildTargetGroup, scriptingDefineSymbols.ToArray());
        }

        /// <summary>
        /// 为指定平台移除指定的脚本宏定义。
        /// </summary>
        /// <param name="buildTargetGroup">要移除脚本宏定义的平台。</param>
        /// <param name="scriptingDefineSymbol">要移除的脚本宏定义。</param>
        public static void RemoveScriptingDefineSymbol(BuildTargetGroup buildTargetGroup, string scriptingDefineSymbol)
        {
            if (string.IsNullOrEmpty(scriptingDefineSymbol))
            {
                return;
            }

            if (!HasScriptingDefineSymbol(buildTargetGroup, scriptingDefineSymbol))
            {
                return;
            }

            List<string> scriptingDefineSymbols = new List<string>(GetScriptingDefineSymbols(buildTargetGroup));
            while (scriptingDefineSymbols.Contains(scriptingDefineSymbol))
            {
                scriptingDefineSymbols.Remove(scriptingDefineSymbol);
            }

            SetScriptingDefineSymbols(buildTargetGroup, scriptingDefineSymbols.ToArray());
        }

        /// <summary>
        /// 为所有平台增加指定的脚本宏定义（含 DS Server 子平台）。
        /// </summary>
        /// <param name="scriptingDefineSymbol">要增加的脚本宏定义。</param>
        public static void AddScriptingDefineSymbol(string scriptingDefineSymbol)
        {
            if (string.IsNullOrEmpty(scriptingDefineSymbol))
            {
                return;
            }

            foreach (BuildTargetGroup buildTargetGroup in BuildTargetGroups)
            {
                AddScriptingDefineSymbol(buildTargetGroup, scriptingDefineSymbol);
            }

            // Unity 6 DS 构建（StandaloneBuildSubtarget.Server）用独立的 NamedBuildTarget.Server，
            // 不在 BuildTargetGroup.Standalone 的 scriptingDefineSymbols 范围内，需单独处理
            foreach (NamedBuildTarget namedBuildTarget in ExtraNamedBuildTargets)
            {
                AddScriptingDefineSymbolForNamedBuildTarget(namedBuildTarget, scriptingDefineSymbol);
            }
        }

        /// <summary>
        /// 为所有平台移除指定的脚本宏定义（含 DS Server 子平台）。
        /// </summary>
        /// <param name="scriptingDefineSymbol">要移除的脚本宏定义。</param>
        public static void RemoveScriptingDefineSymbol(string scriptingDefineSymbol)
        {
            if (string.IsNullOrEmpty(scriptingDefineSymbol))
            {
                return;
            }

            foreach (BuildTargetGroup buildTargetGroup in BuildTargetGroups)
            {
                RemoveScriptingDefineSymbol(buildTargetGroup, scriptingDefineSymbol);
            }

            foreach (NamedBuildTarget namedBuildTarget in ExtraNamedBuildTargets)
            {
                RemoveScriptingDefineSymbolForNamedBuildTarget(namedBuildTarget, scriptingDefineSymbol);
            }
        }

        /// <summary>
        /// 检查指定 NamedBuildTarget 是否存在指定的脚本宏定义。
        /// </summary>
        public static bool HasScriptingDefineSymbol(NamedBuildTarget namedBuildTarget, string scriptingDefineSymbol)
        {
            if (string.IsNullOrEmpty(scriptingDefineSymbol))
            {
                return false;
            }

            PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget, out var symbols);
            foreach (string i in symbols)
            {
                if (i == scriptingDefineSymbol)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 为指定 NamedBuildTarget 增加脚本宏定义。
        /// </summary>
        public static void AddScriptingDefineSymbolForNamedBuildTarget(NamedBuildTarget namedBuildTarget, string scriptingDefineSymbol)
        {
            if (string.IsNullOrEmpty(scriptingDefineSymbol))
            {
                return;
            }

            if (HasScriptingDefineSymbol(namedBuildTarget, scriptingDefineSymbol))
            {
                return;
            }

            PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget, out var symbols);
            var list = new List<string>(symbols) { scriptingDefineSymbol };
            PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, list.ToArray());
        }

        /// <summary>
        /// 为指定 NamedBuildTarget 移除脚本宏定义。
        /// </summary>
        public static void RemoveScriptingDefineSymbolForNamedBuildTarget(NamedBuildTarget namedBuildTarget, string scriptingDefineSymbol)
        {
            if (string.IsNullOrEmpty(scriptingDefineSymbol))
            {
                return;
            }

            if (!HasScriptingDefineSymbol(namedBuildTarget, scriptingDefineSymbol))
            {
                return;
            }

            PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget, out var symbols);
            var list = new List<string>(symbols);
            while (list.Contains(scriptingDefineSymbol))
            {
                list.Remove(scriptingDefineSymbol);
            }

            PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, list.ToArray());
        }

        /// <summary>
        /// 获取指定平台的脚本宏定义。
        /// </summary>
        /// <param name="buildTargetGroup">要获取脚本宏定义的平台。</param>
        /// <returns>平台的脚本宏定义。</returns>
        public static string[] GetScriptingDefineSymbols(BuildTargetGroup buildTargetGroup)
        {
#if UNITY_6000_0_OR_NEWER
            PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup), out var result);
            return result;
#else
            return PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup).Split(';');
#endif
        }

        /// <summary>
        /// 设置指定平台的脚本宏定义。
        /// </summary>
        /// <param name="buildTargetGroup">要设置脚本宏定义的平台。</param>
        /// <param name="scriptingDefineSymbols">要设置的脚本宏定义。</param>
        public static void SetScriptingDefineSymbols(BuildTargetGroup buildTargetGroup, string[] scriptingDefineSymbols)
        {
#if UNITY_6000_0_OR_NEWER
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup), scriptingDefineSymbols);
#else
            PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTargetGroup, string.Join(";", scriptingDefineSymbols));
#endif
        }
    }
}
