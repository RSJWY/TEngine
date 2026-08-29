using System;
using System.Collections.Generic;
using TEngine;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    [Serializable]
    public class UIRawImageMirrorExtend
    {
        public enum MirrorType
        {
            /// <summary>
            /// 水平
            /// </summary>
            Horizontal,

            /// <summary>
            /// 垂直
            /// </summary>
            Vertical,

            /// <summary>
            /// 四分之一
            /// 相当于水平，然后再垂直
            /// </summary>
            Quarter,
        }

        [SerializeField] private bool m_isUseRawImageMirror;
        /// <summary>
        /// 镜像类型
        /// </summary>
        [SerializeField]
        private MirrorType m_mirrorType = MirrorType.Horizontal;

        private RawImage m_rawImage;

        public bool isUseRawImageMirror
        {
            get => m_isUseRawImageMirror;
            set
            {
                if (m_isUseRawImageMirror == value)
                {
                    return;
                }
                m_isUseRawImageMirror = value;
                Refresh();
            }
        }

        public void Initialize(RawImage rawImage)
        {
            m_rawImage = rawImage;
        }


#if UNITY_EDITOR
        public void EditorInitialize(RawImage rawImage)
        {
            m_rawImage = rawImage;
        }
#endif

        public void SetUseRawImageMirror(bool useRawImageMirror)
        {
            isUseRawImageMirror = useRawImageMirror;
        }

        public void SetMirrorType(MirrorType mirrorType)
        {
            m_mirrorType = mirrorType;
            Refresh();
        }

        public void Refresh()
        {
            m_rawImage?.SetVerticesDirty();
        }

        #region Mirror

        [NonSerialized]
        private RectTransform m_RectTransform;

        public RectTransform rectTransform
        {
            get
            {
                var component = m_RectTransform;

                if (component is not null)
                {
                    return component;
                }
                return m_RectTransform = m_rawImage?.GetComponent<RectTransform>();
            }
        }

        /// <summary>
        /// 设置原始尺寸
        /// </summary>
        public void SetNativeSize()
        {
            if (m_rawImage != null)
            {
                Texture texture = m_rawImage.texture;

                if(texture != null){
                    float w = texture.width;
                    float h = texture.height;
                    rectTransform.anchorMax = rectTransform.anchorMin;

                    switch (m_mirrorType)
                    {
                        case MirrorType.Horizontal:
                            rectTransform.sizeDelta = new Vector2(w * 2, h);
                            break;
                        case MirrorType.Vertical:
                            rectTransform.sizeDelta = new Vector2(w, h * 2);
                            break;
                        case MirrorType.Quarter:
                            rectTransform.sizeDelta = new Vector2(w * 2, h * 2);
                            break;
                    }

                    m_rawImage.SetVerticesDirty();
                }
            }
        }

        public void ModifyMesh(VertexHelper vh)
        {
            if (m_rawImage == null || !m_rawImage.IsActive() || !m_isUseRawImageMirror)
            {
                return;
            }

            var output = ListPool<UIVertex>.Get();

            try
            {
                vh.GetUIVertexStream(output);

                int count = output.Count;

                //RawImage 只有 Simple 四边形绘制，无 Sliced/Tiled/Filled 分支
                DrawSimple(output, count);

                vh.Clear();
                vh.AddUIVertexTriangleStream(output);
            }
            finally
            {
                if (output != null)
                {
                    ListPool<UIVertex>.Recycle(output);
                }
            }
        }

        /// <summary>
        /// 绘制Simple版
        /// </summary>
        /// <param name="output"></param>
        /// <param name="count"></param>
        protected void DrawSimple(List<UIVertex> output, int count)
        {
            if (m_rawImage == null)
            {
                return;
            }
            Rect rect = m_rawImage.GetPixelAdjustedRect();

            SimpleScale(rect, output, count);

            switch (m_mirrorType)
            {
                case MirrorType.Horizontal:
                    ExtendCapacity(output, count);
                    MirrorVerts(rect, output, count, true);
                    break;
                case MirrorType.Vertical:
                    ExtendCapacity(output, count);
                    MirrorVerts(rect, output, count, false);
                    break;
                case MirrorType.Quarter:
                    ExtendCapacity(output, count * 3);
                    MirrorVerts(rect, output, count, true);
                    MirrorVerts(rect, output, count * 2, false);
                    break;
            }
        }

        /// <summary>
        /// 扩展容量
        /// </summary>
        /// <param name="verts"></param>
        /// <param name="addCount"></param>
        protected void ExtendCapacity(List<UIVertex> verts, int addCount)
        {
            var neededCapacity = verts.Count + addCount;
            if (verts.Capacity < neededCapacity)
            {
                verts.Capacity = neededCapacity;
            }
        }

        /// <summary>
        /// Simple缩放位移顶点（减半）
        /// </summary>
        /// <param name="rect"></param>
        /// <param name="verts"></param>
        /// <param name="count"></param>
        protected void SimpleScale(Rect rect, List<UIVertex> verts, int count)
        {
            for (int i = 0; i < count; i++)
            {
                UIVertex vertex = verts[i];

                Vector3 position = vertex.position;

                if (m_mirrorType == MirrorType.Horizontal || m_mirrorType == MirrorType.Quarter)
                {
                    position.x = (position.x + rect.x) * 0.5f;
                }

                if (m_mirrorType == MirrorType.Vertical || m_mirrorType == MirrorType.Quarter)
                {
                    position.y = (position.y + rect.y) * 0.5f;
                }

                vertex.position = position;

                verts[i] = vertex;
            }
        }

        /// <summary>
        /// 镜像顶点
        /// </summary>
        /// <param name="rect"></param>
        /// <param name="verts"></param>
        /// <param name="count"></param>
        /// <param name="isHorizontal"></param>
        protected void MirrorVerts(Rect rect, List<UIVertex> verts, int count, bool isHorizontal = true)
        {
            for (int i = 0; i < count; i++)
            {
                UIVertex vertex = verts[i];

                Vector3 position = vertex.position;

                if (isHorizontal)
                {
                    position.x = rect.center.x * 2 - position.x;
                }
                else
                {
                    position.y = rect.center.y * 2 - position.y;
                }

                vertex.position = position;

                verts.Add(vertex);
            }
        }

        #endregion
    }
}