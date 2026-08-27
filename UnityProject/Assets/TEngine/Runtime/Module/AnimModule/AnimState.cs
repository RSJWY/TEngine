using UnityEngine;

namespace TEngine
{
    public class AnimInfo
    {
        private readonly AnimClip _animClip;

        private AnimInfo(){ }

        internal AnimInfo(AnimClip animClip) => _animClip = animClip;

        /// <summary>
        /// 动画名字
        /// </summary>
        public string Name => _animClip.Name;

        /// <summary>
        /// 动画的哈希值
        /// </summary>
        public int AnimHashCode => _animClip.AnimHashCode;

        /// <summary>
        /// 动画长度
        /// </summary>
        public float Length => _animClip.ClipLength;

        /// <summary>
        /// 动画层级
        /// </summary>
        public int Layer => _animClip.Layer;

        /// <summary>
        /// 动画播放模式
        /// </summary>
        public WrapMode WrapMode => _animClip.WrapMode;

        /// <summary>
        /// 动画权重
        /// </summary>
        public float Weight
        {
            get => _animClip.Weight;
            set => _animClip.Weight = value;
        }

        /// <summary>
        /// 动画时间轴
        /// </summary>
        public float Time
        {
            get => _animClip.Time;
            set => _animClip.Time = value;
        }

        /// <summary>
        /// 归一化时间轴
        /// </summary>
        public float NormalizedTime
        {
            get => _animClip.NormalizedTime;
            set => _animClip.NormalizedTime = value;
        }

        /// <summary>
        /// 播放速度
        /// </summary>
        public float Speed
        {
            get => _animClip.Speed;
            set => _animClip.Speed = value;
        }
    }
}
