using System.Collections.Generic;
using System.Linq;

namespace TEngine.SceneTools
{
    /// <summary>
    /// 场景条目的「身份派生」工具：解决多人协同下枚举值/枚举名撞车。
    /// <para>根因：旧实现用「本地计数器 max+1」分配枚举值，两个分支各加一个场景时
    /// 都会从同一个最大值往上加，必然得到相同值，合并后冲突。</para>
    /// <para>方案：枚举值改为由场景资源 GUID <b>确定性派生</b>——GUID 全局唯一、改名不变、
    /// 跨分支一致，因此不同场景在任何机器/分支上算出的值都不同，合并后不会撞车。
    /// 同时去掉「删除后保留占位」的概念：值随场景身份固定，无需占位。</para>
    /// </summary>
    public static class SceneEnumIdentity
    {
        /// <summary>
        /// 由场景 GUID 派生枚举值（非负 int，避开 0 以便与「未初始化」区分）。
        /// <para>FNV-1a 32 位哈希：纯函数、无状态，同一 GUID 在任何分支结果一致。</para>
        /// <para>31 位有效空间下，约 100 个场景的哈希碰撞概率约 2.3e-6，可忽略；
        /// 真出现碰撞时由生成前校验明确报错，不静默处理。</para>
        /// </summary>
        public static int DeriveValue(string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return 0;
            }

            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < guid.Length; i++)
                {
                    hash ^= guid[i];
                    hash *= 16777619u;
                }

                int value = (int)(hash & 0x7FFFFFFF);
                return value == 0 ? 1 : value;
            }
        }

        /// <summary>GUID 短后缀（4 位十六进制），用于枚举名冲突时的确定性去重。</summary>
        public static string NameSuffix(string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return "unkn";
            }

            return guid.Length >= 4 ? guid.Substring(0, 4) : guid;
        }

        /// <summary>
        /// 修复重复枚举名：对每个冲突组内的条目统一追加 <c>_&lt;GUID4&gt;</c> 后缀。
        /// <para>规则只依赖「是否冲突」与自身 GUID，不依赖列表顺序或分支差异，
        /// 因此各分支合并后重新执行会收敛到同一结果（幂等：已带后缀者再次执行会重复追加，
        /// 故仅在体检发现冲突时调用）。</para>
        /// </summary>
        /// <returns>是否存在冲突并已修复。</returns>
        public static bool TryRepairDuplicateNames(IList<SceneEnumConfig.SceneEntry> entries, out int repaired)
        {
            repaired = 0;
            if (entries == null || entries.Count == 0)
            {
                return false;
            }

            var groups = entries
                .Where(e => !string.IsNullOrWhiteSpace(e.EnumName))
                .GroupBy(e => e.EnumName)
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var group in groups)
            {
                foreach (SceneEnumConfig.SceneEntry entry in group)
                {
                    entry.EnumName = $"{group.Key}_{NameSuffix(entry.SceneGuid)}";
                    repaired++;
                }
            }

            return repaired > 0;
        }
    }
}
