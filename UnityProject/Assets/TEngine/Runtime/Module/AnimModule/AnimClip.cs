using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace TEngine
{
    internal sealed class AnimClip : AnimNode
    {
        public readonly string Name;
        public int AnimHashCode => Animator.StringToHash(Name);
        private readonly AnimationClip _clip;
        private readonly AnimationClipPlayable _clipPlayable;
        private readonly float _fadeDuration;

        public float FadeDuration => _fadeDuration;

        /// <summary>
        /// 动画层级
        /// </summary>
        public int Layer { get; private set; } = 0;

        /// <summary>
        /// 动画长度
        /// </summary>
        public float ClipLength
        {
            get
            {
                if (_clip == null)
                {
                    return 0f;
                }

                if (Speed == 0f)
                {
                    return Mathf.Infinity;
                }
                return _clip.length / Speed;
            }
        }

        /// <summary>
        /// 归一化时间轴
        /// </summary>
        public float NormalizedTime
        {
            get => _clip == null ? 1f : Time / _clip.length;
            set
            {
                if (_clip == null)
                {
                    return;
                }
                Time = _clip.length * value;
            }
        }

        /// <summary>
        /// 动画模式
        /// </summary>
        public WrapMode WrapMode { get; }

        /// <summary>
        /// 动画信息
        /// </summary>
        public AnimInfo Info { get; private set; }

        public AnimClip(PlayableGraph graph, AnimationClip clip, string name, WrapMode wrapMode, int layer, float fadeDuration = 0f) : base(graph)
        {
            _clip = clip;
            Name = name;
            Layer = layer;
            _fadeDuration = fadeDuration;
            _clipPlayable = AnimationClipPlayable.Create(graph, clip);
            _clipPlayable.SetApplyFootIK(false);
            _clipPlayable.SetApplyPlayableIK(false);
            SetSourcePlayable(_clipPlayable);
            WrapMode = wrapMode;
            if (WrapMode == WrapMode.Once)
            {
                _clipPlayable.SetDuration(clip.length);
            }

            Info = new AnimInfo(this);
        }

        public override void Play()
        {
            if (WrapMode == WrapMode.Once || WrapMode == WrapMode.ClampForever)
            {
                Time = 0;
            }

            base.Play();
        }
    }
}
