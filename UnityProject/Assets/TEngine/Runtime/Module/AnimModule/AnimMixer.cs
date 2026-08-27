using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace TEngine
{
    internal sealed class AnimMixer : AnimNode
    {
        private const float HIDE_DURATION = 0.25f;
        private readonly List<AnimClip> _animClips = new List<AnimClip>(8);
        private AnimationMixerPlayable _mixer;
        private bool _isQuiting = false;

        /// <summary>
        /// 动画层级
        /// </summary>
        public int Layer { get; private set; }

        public AnimMixer(PlayableGraph graph, int layer) : base(graph)
        {
            Layer = layer;
            _mixer = AnimationMixerPlayable.Create(graph);
            SetSourcePlayable(_mixer);
        }

        public override void Update(float elapsedSeconds)
        {
            base.Update(elapsedSeconds);

            for (int i = 0; i < _animClips.Count; i++)
            {
                var animClip = _animClips[i];

                if (animClip != null)
                {
                    animClip.Update(elapsedSeconds);
                }
            }

            bool isAllDone = true;

            for (int i = 0; i < _animClips.Count; i++)
            {
                var animClip = _animClips[i];
                if (animClip != null)
                {
                    if (!animClip.IsDone)
                    {
                        isAllDone = false;
                        break;
                    }
                }
            }

            // 当所有子节点都已完成 断开连接
            if (isAllDone && !_isQuiting)
            {
                _isQuiting = true;
                StartWeightFade(0, HIDE_DURATION);
            }

            if (_isQuiting && Mathf.Approximately(Weight, 0f))
            {
                DisconnectMixer();
            }
        }

        private void DisconnectMixer()
        {
            for (int i = 0; i < _animClips.Count; i++)
            {
                var animClip = _animClips[i];
                if (animClip != null && animClip.IsConnected)
                {
                    animClip.Disconnect();
                    _animClips[i] = null;
                }
            }

            Disconnect();
        }

        /// <summary>
        /// 播放指定动画
        /// </summary>
        /// <param name="animClip"></param>
        /// <param name="fadeDuration"></param>
        public void Play(AnimClip animClip, float fadeDuration)
        {
            _isQuiting = false;
            StartWeightFade(1f, 0);
            if(!ContainsAnimClip(animClip))
            {
                int index = _animClips.FindIndex(s => s == null);

                if (index == -1)
                {
                    int inputCount = _mixer.GetInputCount();
                    _mixer.SetInputCount(inputCount + 1);
                    animClip.Connect(_mixer, inputCount);
                    _animClips.Add(animClip);
                }
                else
                {
                    animClip.Connect(_mixer, index);
                    _animClips[index] = animClip;
                }
            }
            fadeDuration = fadeDuration <= 0 ? animClip.FadeDuration : fadeDuration;
            for (int i = 0; i < _animClips.Count; i++)
            {
                var clip = _animClips[i];

                if (clip == null)
                {
                    continue;
                }

                if (clip == animClip)
                {
                    clip.StartWeightFade(1f, fadeDuration);
                    clip.Play();
                }
                else
                {
                    clip.StartWeightFade(0f, fadeDuration);
                    clip.Pause();
                }
            }
        }

        public void Stop(int animHashCode)
        {
            AnimClip animClip = FindAnimClip(animHashCode);

            if (animClip == null)
            {
                return;
            }
            animClip.Pause();
            animClip.Reset();
        }


        public void PauseAll()
        {
            for (int i = 0; i < _animClips.Count; i++)
            {
                var animClip = _animClips[i];

                if (animClip != null)
                {
                    animClip.Pause();
                }
            }
        }

        public void RemoveAnimClip(int animHashCode)
        {
            AnimClip animClip = FindAnimClip(animHashCode);

            if (animClip == null)
            {
                return;
            }

            if (animClip.IsConnected)
            {
                animClip.Disconnect();
            }

            _animClips[animClip.InputPort] = null;
            animClip.Destroy();
        }

        private AnimClip FindAnimClip(int animHashCode)
        {
            foreach (var item in _animClips)
            {
                if (item != null && item.AnimHashCode == animHashCode)
                {
                    return item;
                }
            }

            Log.Warning($"{nameof(AnimClip)} 不存在：{animHashCode}");
            return null;
        }

        public bool ContainsAnimClip(AnimClip animClip)
        {
            foreach (var clip in _animClips)
            {
                if (clip == animClip)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
