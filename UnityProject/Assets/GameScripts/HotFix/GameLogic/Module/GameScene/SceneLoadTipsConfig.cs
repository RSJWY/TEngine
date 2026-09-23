using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// 场景加载页文案配置：阶段文本 + 小贴士列表。
    /// </summary>
    /// <remarks>
    /// <para><b>阶段文本</b>（<see cref="PhaseEntries"/>）：按进度区间返回自定义富文本，显示在 <c>m_tmp_progressText</c>。</para>
    /// <para><b>小贴士</b>（<see cref="Tips"/>）：加载期间轮播的玩家提示文本。</para>
    /// <para>资源地址：<c>Assets/AssetRaw/Configs/SceneLoadTipsConfig.asset</c>。</para>
    /// </remarks>
    [CreateAssetMenu(menuName = "TEngine/场景加载页文案", fileName = "SceneLoadTipsConfig")]
    public class SceneLoadTipsConfig : ScriptableObject
    {
        [Serializable]
        public class PhaseEntry
        {
            [Tooltip("区间上限（含），0~1。progress <= 此值时命中此阶段。最后一条建议填 1。")]
            public float Threshold = 1f;

            [Tooltip("富文本，支持 TMP rich text tag。")]
            [TextArea(2, 4)]
            public string Text = "";
        }

        [Tooltip("阶段文本表，按 Threshold 升序。GameSceneModule 传 progress 从头匹配第一条 progress<=Threshold 的。")]
        [SerializeField]
        private List<PhaseEntry> phaseEntries = new List<PhaseEntry>();

        [Tooltip("小贴士列表，加载期间随机轮播。")]
        [TextArea(2, 4)]
        [SerializeField]
        private List<string> tips = new List<string>();

        /// <summary>阶段文本表（只读视图）。</summary>
        public IReadOnlyList<PhaseEntry> PhaseEntries => phaseEntries;

        /// <summary>小贴士列表（只读视图）。</summary>
        public IReadOnlyList<string> Tips => tips;

        /// <summary>
        /// 按进度查阶段文本。
        /// </summary>
        /// <param name="progress">0~1。</param>
        /// <returns>匹配到的富文本；表为空或未命中返回空串。</returns>
        public string GetPhaseText(float progress)
        {
            if (phaseEntries == null || phaseEntries.Count == 0)
            {
                return string.Empty;
            }

            foreach (var entry in phaseEntries)
            {
                if (progress <= entry.Threshold)
                {
                    return entry.Text ?? string.Empty;
                }
            }

            return phaseEntries[phaseEntries.Count - 1].Text ?? string.Empty;
        }
    }
}
