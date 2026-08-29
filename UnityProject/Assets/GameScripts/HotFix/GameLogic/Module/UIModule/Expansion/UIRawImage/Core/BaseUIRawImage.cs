using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    //RawImage 不像 Image 那样实现 ICanvasRaycastFilter，需显式实现以支持不规则图形的射线检测
    [Serializable]
    public class BaseUIRawImage : RawImage, IMeshModifier, ICanvasRaycastFilter
    {
        [SerializeField] private UIRawImageMaskExtend m_uiRawImageMaskExtend = new UIRawImageMaskExtend();
        [SerializeField] private UIRawImageRoundedCornersExtend m_uiRawImageRoundedCornersExtend = new UIRawImageRoundedCornersExtend();
        [SerializeField] private UIRawImageMirrorExtend m_uiRawImageMirrorExtend = new UIRawImageMirrorExtend();
        private RectTransform m_target;

        public UIRawImageMirrorExtend UIRawImageMirrorExtend => m_uiRawImageMirrorExtend;

        protected override void Awake()
        {
            base.Awake();
            UIRawImageMirrorExtend?.Initialize(this);
            m_uiRawImageMaskExtend?.Initialize(this);
            m_uiRawImageRoundedCornersExtend?.Initialize(this);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            m_uiRawImageMaskExtend.EditorInitialize(this);
            m_uiRawImageRoundedCornersExtend.EditorInitialize(this);
            UIRawImageMirrorExtend.EditorInitialize(this);
            SetVerticesDirty();
        }
#endif

        public void SetFillPercent(float value)
        {
            m_uiRawImageMaskExtend.SetFillPercent(value);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount <= 0)
            {
                base.OnPopulateMesh(vh);
                return;
            }

            bool isOverride = m_uiRawImageMaskExtend.UseMaskImage || m_uiRawImageRoundedCornersExtend.IsUseRoundedCorners;

            if (!isOverride)
            {
                base.OnPopulateMesh(vh);
            }
        }

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (m_target != null)
            {
                return !RectTransformUtility.RectangleContainsScreenPoint(m_target, screenPoint, eventCamera);
            }

            if (!m_uiRawImageMaskExtend.UseMaskImage)
            {
                //RawImage 无基类射线检测（无 alphaHitTest），默认整矩形命中
                return true;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, eventCamera, out var local);
            return m_uiRawImageMaskExtend.Contains(local, m_uiRawImageMaskExtend.OuterVertices, m_uiRawImageMaskExtend.InnerVertices);
        }

        public void SetTarget(RectTransform target)
        {
            m_target = target;
        }

        public void DrawPolygon(int vertCnt, List<float> percents, float rotation = -1)
        {
            m_uiRawImageRoundedCornersExtend.IsUseRoundedCorners = false;
            m_uiRawImageMaskExtend.DrawPolygon(vertCnt, percents, rotation);
        }

        public void ModifyMesh(Mesh mesh)
        {
        }

        public void ModifyMesh(VertexHelper verts)
        {
            if (!IsActive() || verts.currentVertCount <= 0)
            {
                return;
            }

            if (m_uiRawImageMaskExtend.UseMaskImage)
            {
                m_uiRawImageMaskExtend.OnPopulateMesh(verts);
            }

            if (m_uiRawImageRoundedCornersExtend.IsUseRoundedCorners)
            {
                m_uiRawImageRoundedCornersExtend.OnPopulateMesh(verts);
            }

            UIRawImageMirrorExtend?.ModifyMesh(verts);
        }
    }
}
