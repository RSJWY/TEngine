using System;
using TMPro;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// UITMPText 基类（BaseUIText 的 TextMeshPro 版）。
    /// 与 UGUI 版 BaseUIText 的关键差异：TextMeshProUGUI 不走 OnPopulateMesh/IMeshModifier 管线，
    /// 顶点修改唯一官方挂点是 OnPreRenderText 事件（TMP 生成网格后、上传 mesh 前触发，参数 TMP_TextInfo，每字符 4 顶点）。
    /// 字距（characterSpacing）、BestFit（enableAutoSizing）、基础四角渐变（enableVertexGradient）均为 TMP 原生能力，直接使用不封装。
    /// </summary>
    [Serializable]
    public class BaseUITMPText : TextMeshProUGUI
    {
        [SerializeField] private TMPGradientColorExtend m_tmpGradientColorExtend = new TMPGradientColorExtend();
        [SerializeField] private TMPCircleExtend m_tmpCircleExtend = new TMPCircleExtend();
        [SerializeField] private TMPShadowExtend m_tmpShadowExtend = new TMPShadowExtend();

        // 描边状态：TMP 版走 SDF 材质参数封装，不需要独立扩展类，仅运行时 API 使用（不入 Inspector 面板）
        private bool m_useOutLine;
        private int m_outLineWidth = 1;
        private Color32 m_outLineColor = Color.black;
        // 上次应用到材质的描边宽度，用于判断是否需要重算 SDF padding
        private float m_appliedOutlineWidth = -1f;

        public TMPGradientColorExtend TMPGradientColorExtend => m_tmpGradientColorExtend;
        public TMPCircleExtend TMPCircleExtend => m_tmpCircleExtend;
        public TMPShadowExtend TMPShadowExtend => m_tmpShadowExtend;

        /// <summary>
        /// 当前可见的文字行数
        /// </summary>
        public int VisibleLines => textInfo != null ? textInfo.lineCount : 0;

        protected override void OnEnable()
        {
            base.OnEnable();
            m_tmpGradientColorExtend?.Initialize(this);
            m_tmpCircleExtend?.Initialize(this);
            m_tmpShadowExtend?.Initialize(this);
            // 阴影是 SDF 材质参数（keyword 与参数均保存在材质实例上，不随场景序列化），激活时重新应用以恢复状态
            m_tmpShadowExtend?.Refresh();
            // 订阅放 OnEnable、退订放 OnDisable，防止重复订阅
            OnPreRenderText += OnPreRenderTextHandler;
        }

        protected override void OnDisable()
        {
            OnPreRenderText -= OnPreRenderTextHandler;
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            // 各扩展均为纯托管数据（无 ListPool/非托管缓存），无需额外清理
            base.OnDestroy();
        }

        /// <summary>
        /// TMP 生成网格后、上传 mesh 前的官方扩展挂点。
        /// 红线：此处严禁内存分配、严禁调用 SetVerticesDirty/SetMaterialDirty（会引发重建死循环）；
        /// 修改 vertices/colors32 后不要手动上传 mesh（不要调 UploadMesh），事件返回后 TMP 自动上传。
        /// </summary>
        private void OnPreRenderTextHandler(TMP_TextInfo textInfo)
        {
            if (textInfo == null || !IsActive())
            {
                return;
            }

            // 快速检查：无任何顶点级扩展启用则直接返回（对齐 BaseUIText.ModifyMesh 的 hasAnyExtend 模式）
            bool hasAnyExtend = m_tmpCircleExtend.UseTextCircle || m_tmpGradientColorExtend.isUseGradientColor;
            if (!hasAnyExtend)
            {
                return;
            }

            // 顶点链顺序对齐现有 UIText：先改位置后算颜色；阴影走材质 Underlay，不进顶点链
            m_tmpCircleExtend.ModifyMesh(textInfo);
            m_tmpGradientColorExtend.ModifyMesh(textInfo);
        }

        #region 描边 API（SDF 材质参数封装）

        /// <summary>
        /// 开关描边。TMP 描边由 SDF 材质 _OutlineWidth 参数控制（无需 keyword），关闭时宽度置 0。
        /// 注意：首次访问 fontMaterial 会实例化材质（HideAndDontSave），该实例会断开与使用同款共享材质文本的合批，属预期行为。
        /// </summary>
        public void SetUseOutLine(bool useOutLine)
        {
            m_useOutLine = useOutLine;
            ApplyOutlineMaterial();
        }

        /// <summary>
        /// 设置描边透明度（写入描边颜色的 alpha，自动开启描边，对齐 UGUI 版 SetAlpha 语义）。
        /// </summary>
        public void SetOutLineAlpha(float alpha)
        {
            m_outLineColor.a = (byte)(Mathf.Clamp01(alpha) * 255f);
            m_useOutLine = true;
            ApplyOutlineMaterial();
        }

        /// <summary>
        /// 设置描边颜色（宽度沿用当前值，自动开启描边）。
        /// </summary>
        public void SetOutLineColor(Color32 c)
        {
            SetOutLineColor(c, m_outLineWidth);
        }

        /// <summary>
        /// 设置描边颜色与宽度（自动开启描边）。
        /// 与 UGUI 版语义差异：UGUI 版 outlineWidth 为顶点扩张像素数（int 1~10）；
        /// TMP _OutlineWidth 为 0~1 浮点（相对字号比例），映射 w = outlineWidth * 0.1f 并 clamp 到 0~1。
        /// </summary>
        public void SetOutLineColor(Color32 c, int outlineWidth)
        {
            m_outLineColor = c;
            m_outLineWidth = outlineWidth;
            m_useOutLine = true;
            ApplyOutlineMaterial();
        }

        private void ApplyOutlineMaterial()
        {
            float width = m_useOutLine ? Mathf.Clamp01(m_outLineWidth * 0.1f) : 0f;
            bool widthChanged = !Mathf.Approximately(width, m_appliedOutlineWidth);

            // 一律操作实例材质（fontMaterial/fontMaterials），禁止改 fontSharedMaterial(s)（会污染同材质所有文本）
            Material[] mats = fontMaterials;
            if (mats != null && mats.Length > 0)
            {
                for (int i = 0; i < mats.Length; i++)
                {
                    ApplyOutlineParams(mats[i], width);
                }
            }
            else
            {
                // textInfo 尚未生成（materialCount = 0）时 fontMaterials 返回空数组，此时仅有主材质
                ApplyOutlineParams(fontMaterial, width);
            }

            m_appliedOutlineWidth = width;
            // 描边宽度变化会扩大 SDF 采样范围，需重算字符 padding 防止边缘裁剪（仅宽度变化时执行）
            if (widthChanged)
            {
                UpdateMeshPadding();
            }
            SetMaterialDirty();
        }

        private void ApplyOutlineParams(Material mat, float width)
        {
            if (mat == null)
            {
                return;
            }
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
            mat.SetColor(ShaderUtilities.ID_OutlineColor, m_outLineColor);
        }

        #endregion

        #region 渐变 API（转发 TMPGradientColorExtend）

        /// <summary>
        /// 设置四角渐变色。
        /// </summary>
        public void SetGradientColor(Color32 topColor, Color32 bottomColor, Color32 leftColor = default, Color32 rightColor = default, float verticalOffset = 0f, float horizontalOffset = 0f, bool splitTextGradient = false)
        {
            m_tmpGradientColorExtend?.SetGradientColor(topColor, bottomColor, leftColor, rightColor, verticalOffset, horizontalOffset, splitTextGradient);
        }

        /// <summary>
        /// 设置上下渐变色（左右保持白色）。
        /// </summary>
        public void SetGradientTop2BottomColor(Color32 topColor, Color32 bottomColor, float verticalOffset = 0f, bool splitTextGradient = false)
        {
            m_tmpGradientColorExtend?.SetGradientColor(topColor, bottomColor, Color.white, Color.white, verticalOffset, 0, splitTextGradient);
        }

        /// <summary>
        /// 设置左右渐变色（上下保持白色）。
        /// </summary>
        public void SetGradientLeft2RightColor(Color32 leftColor, Color32 rightColor, float horizontalOffset, bool splitTextGradient = false)
        {
            m_tmpGradientColorExtend?.SetGradientColor(Color.white, Color.white, leftColor, rightColor, 0, horizontalOffset, splitTextGradient);
        }

        #endregion

        #region 阴影 API（转发 TMPShadowExtend，Underlay 单色实现）

        /// <summary>
        /// 设置阴影颜色。
        /// 与 UGUI 版语义差异：UGUI 版 SetShadowColor 支持四角各一色（顶点复制重映射）；
        /// TMP Underlay 仅支持单色阴影，四角彩色阴影无对应能力，不做顶点复制实现。
        /// </summary>
        public void SetShadowColor(Color32 color)
        {
            m_tmpShadowExtend?.SetShadowColor(color);
        }

        /// <summary>
        /// 设置阴影偏移。
        /// 与 UGUI 版语义差异：UGUI 版 distance 为像素；TMP Underlay 偏移为相对字号的浮点（典型 -1~1，shader 内乘 _GradientScale）。
        /// </summary>
        public void SetShadowEffectDistance(Vector2 distance)
        {
            m_tmpShadowExtend?.SetShadowEffectDistance(distance);
        }

        #endregion

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            // 阴影为材质参数，Inspector 序列化路径的修改不经过属性 setter，需在此重新应用
            m_tmpShadowExtend?.Refresh();
            SetAllDirty();
        }
#endif
    }
}
