using System;
using TMPro;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// TMP 文本阴影扩展：基于 TMP SDF shader 的 Underlay 特性（keyword UNDERLAY_ON + _Underlay* 材质参数），不做顶点复制。
    /// 与 UGUI 版 UITextShadowExtend 的语义差异：
    /// 1. 单色阴影——UGUI 版支持四角各一色（顶点复制 + 位置重映射），TMP Underlay 无对应能力，四角彩色阴影不做；
    /// 2. 偏移单位——UGUI 版 effectDistance 为像素；_UnderlayOffsetX/Y 为相对字号的浮点（典型 -1~1，shader 内乘 _GradientScale）。
    /// 阴影不进 OnPreRenderText 顶点链，仅在属性 setter / OnEnable / OnValidate 时更新材质。
    /// 注意：首次访问 fontMaterial 会实例化材质（HideAndDontSave），该实例会断开与使用同款共享材质文本的合批，属预期行为。
    /// </summary>
    [Serializable]
    public class TMPShadowExtend
    {
        // Underlay 偏移为相对字号比例，超过 ±2 已无视觉意义，做防御性钳制
        private const float MAX_EFFECT_DISTANCE = 2f;

        [SerializeField] private bool m_isUseTextShadow;
        [SerializeField] private Color m_shadowColor = Color.black;
        [SerializeField] private Vector2 m_effectDistance = new Vector2(1f, -1f);
        [SerializeField, Range(0f, 1f)] private float m_shadowSoftness = 0f;

        public bool UseShadow
        {
            get => m_isUseTextShadow;
            set
            {
                if (m_isUseTextShadow == value)
                {
                    return;
                }
                m_isUseTextShadow = value;
                Refresh();
            }
        }

        public Color shadowColor
        {
            get => m_shadowColor;
            set
            {
                if (m_shadowColor == value)
                {
                    return;
                }
                m_shadowColor = value;
                Refresh();
            }
        }

        public Vector2 effectDistance
        {
            get => m_effectDistance;
            set
            {
                if (m_effectDistance == value)
                {
                    return;
                }
                m_effectDistance = ClampDistance(value);
                Refresh();
            }
        }

        public float shadowSoftness
        {
            get => m_shadowSoftness;
            set
            {
                if (Mathf.Approximately(m_shadowSoftness, value))
                {
                    return;
                }
                m_shadowSoftness = value;
                Refresh();
            }
        }

        private TextMeshProUGUI m_text;

        public void Initialize(TextMeshProUGUI text)
        {
            m_text = text;
        }

        /// <summary>
        /// 设置阴影颜色（单色，语义差异见类注释）。
        /// </summary>
        public void SetShadowColor(Color32 color)
        {
            shadowColor = color;
        }

        /// <summary>
        /// 设置阴影偏移（相对字号的浮点，语义差异见类注释）。
        /// </summary>
        public void SetShadowEffectDistance(Vector2 distance)
        {
            effectDistance = distance;
        }

        /// <summary>
        /// 应用 Underlay 材质状态（keyword + _Underlay* 参数）并标记材质脏。
        /// 材质实例不随场景序列化，OnEnable / OnValidate / 属性 setter 均需调用以恢复状态。
        /// </summary>
        public void Refresh()
        {
            if (m_text == null)
            {
                return;
            }

            // 一律操作实例材质（fontMaterial/fontMaterials），禁止改 fontSharedMaterial(s)（会污染同材质所有文本）
            // fontMaterials 为实例材质数组；textInfo 尚未生成时 materialCount 为 0，返回空数组，此时仅主材质
            Material[] mats = m_text.fontMaterials;
            if (mats != null && mats.Length > 0)
            {
                for (int i = 0; i < mats.Length; i++)
                {
                    ApplyUnderlayParams(mats[i]);
                }
            }
            else
            {
                ApplyUnderlayParams(m_text.fontMaterial);
            }

            m_text.SetMaterialDirty();
        }

        private void ApplyUnderlayParams(Material mat)
        {
            if (mat == null)
            {
                return;
            }

            if (m_isUseTextShadow)
            {
                mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                mat.SetColor(ShaderUtilities.ID_UnderlayColor, m_shadowColor);
                mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, m_effectDistance.x);
                mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, m_effectDistance.y);
                mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, m_shadowSoftness);
            }
            else
            {
                mat.DisableKeyword(ShaderUtilities.Keyword_Underlay);
            }
        }

        private static Vector2 ClampDistance(Vector2 value)
        {
            value.x = Mathf.Clamp(value.x, -MAX_EFFECT_DISTANCE, MAX_EFFECT_DISTANCE);
            value.y = Mathf.Clamp(value.y, -MAX_EFFECT_DISTANCE, MAX_EFFECT_DISTANCE);
            return value;
        }
    }
}
