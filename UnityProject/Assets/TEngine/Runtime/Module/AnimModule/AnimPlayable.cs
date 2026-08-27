using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace TEngine
{
    internal class AnimPlayable : MemoryObject, IAnimPlayable
    {
        private readonly List<AnimClip> _animClips;
        private readonly List<AnimMixer> _animMixers;

        private Animator _animator;
        private bool _isDestroyed;

        public string Name => _animator != null ? _animator.name : string.Empty;
        public int AnimClipCount => _animClips.Count;
        public int AnimMixerCount => _animMixers.Count;
        public bool IsDestroyed => _isDestroyed;

        private PlayableGraph _graph;
        private AnimationPlayableOutput _output;
        private AnimationLayerMixerPlayable _mixerRoot;

        public AnimPlayable()
        {
            _animClips = new List<AnimClip>(8);
            _animMixers = new List<AnimMixer>(8);
            _isDestroyed = true;
        }

        public override void InitFromPool()
        {
        }

        public override void RecycleToPool()
        {
            for (int i = 0; i < _animClips.Count; i++)
            {
                var animClip = _animClips[i];
                animClip?.Destroy();
            }
            for (int i = 0; i < _animMixers.Count; i++)
            {
                var animMixer = _animMixers[i];
                animMixer?.Destroy();
            }
            _animClips?.Clear();
            _animMixers?.Clear();
            _graph.Destroy();
            _isDestroyed = true;
        }

        public static AnimPlayable Create(Animator animator)
        {
            if (animator == null || animator.gameObject == null)
            {
                throw new Exception("传入的Animator无效");
            }
            string name = animator.gameObject.name;
            AnimPlayable animPlayable = MemoryPool.Alloc<AnimPlayable>();
            animPlayable._animator = animator;
            animPlayable._graph = PlayableGraph.Create(name);
            animPlayable._graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            animPlayable._mixerRoot = AnimationLayerMixerPlayable.Create(animPlayable._graph);
            animPlayable._output = AnimationPlayableOutput.Create(animPlayable._graph, name, animator);
            animPlayable._output.SetSourcePlayable(animPlayable._mixerRoot);
            animPlayable._isDestroyed = false;
            return animPlayable;
        }

        public void Update(float elapsedSeconds)
        {
            _graph.Evaluate(elapsedSeconds);

            // 更新所有层级
            for (int i = 0; i < _animMixers.Count; i++)
            {
                var animMixer = _animMixers[i];

                if (animMixer.IsConnected)
                {
                    animMixer.Update(elapsedSeconds);
                }
            }
        }

        public void DestroyGraph() => MemoryPool.Dealloc(this);

        public void PlayGraph() => _graph.Play();

        public void StopGraph() => _graph.Stop();

        /// <summary>
        /// 获取动画的信息
        /// </summary>
        /// <param name="animName"></param>
        /// <returns></returns>
        public AnimInfo GetAnimInfo(string animName)
        {
            int animHashCode = Animator.StringToHash(animName);
            for (int i = 0; i < _animClips.Count; i++)
            {
                var animClip = _animClips[i];

                if (animClip != null && animClip.AnimHashCode == animHashCode)
                {
                    return animClip.Info;
                }
            }
            return null;
        }

        private AnimClip GetAnimClip(string animName)
        {
            int animHashCode = Animator.StringToHash(animName);
            for (int i = 0; i < _animClips.Count; i++)
            {
                var animClip = _animClips[i];

                if (animClip != null && animClip.AnimHashCode == animHashCode)
                {
                    return animClip;
                }
            }
            return null;
        }

        public bool IsPlaying(string animName)
        {
            var animInfo = GetAnimClip(animName);

            return animInfo != null && animInfo.IsConnected && animInfo.IsPlaying;
        }

        private AnimMixer GetAnimMixer(int layer)
        {
            for (int i = 0; i < _animMixers.Count; i++)
            {
                var animMixer = _animMixers[i];

                if (animMixer != null && animMixer.Layer == layer)
                {
                    return animMixer;
                }
            }
            return null;
        }

        public void Play(string animName, float fadeLength)
        {
            var animClip = GetAnimClip(animName);

            if (animClip == null)
            {
                Log.Warning($"没有找到动画：{animName}");
                return;
            }

            int layer = animClip.Layer;
            var animMixer = GetAnimMixer(layer);

            if (animMixer == null)
            {
                animMixer = CreateAnimMixer(layer);
            }

            if (!animMixer.IsConnected)
            {
                animMixer.Connect(_mixerRoot, animMixer.Layer);
            }

            animMixer.Play(animClip, fadeLength);
        }

        public void Stop(string animName)
        {
            var animClip = GetAnimClip(animName);
            if (animClip == null)
            {
                Log.Warning($"没有找到动画：{animName}");
                return;
            }

            if (!animClip.IsConnected)
            {
                return;
            }

            var animMixer = GetAnimMixer(animClip.Layer);

            if (animMixer == null)
            {
                throw new Exception("animMixer无效");
            }
            animMixer.Stop(animClip.AnimHashCode);
        }

        /// <summary>
        /// 添加一个动画片段
        /// </summary>
        /// <param name="name">动画名</param>
        /// <param name="clip">资源</param>
        /// <param name="layer">层级</param>
        /// <param name="fadeDuration">过渡时间</param>
        /// <returns></returns>
        public bool AddAnimationClip(string name, AnimationClip clip, WrapMode wrapMode, int layer = 0, float fadeDuration = 0f)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new Exception("动画名称无效");
            }

            if (clip == null)
            {
                throw new Exception("动画片段无效");
            }

            if (layer < 0)
            {
                throw new Exception("动画层级必须大于等于0");
            }

            if (ContainsAnimationClip(name))
            {
                Log.Warning($"动画片段已经存在{name}");
                return false;
            }

            AnimClip animClip = new AnimClip(_graph, clip, name, wrapMode, layer, fadeDuration);
            _animClips.Add(animClip);
            return true;
        }

        public bool RemoveAnimationClip(string name)
        {
            if (!ContainsAnimationClip(name))
            {
                Log.Warning($"动画片段不存在{name}");
                return false;
            }
            AnimClip animClip = GetAnimClip(name);
            AnimMixer animMixer = GetAnimMixer(animClip.Layer);

            if (animMixer != null)
            {
                animMixer.RemoveAnimClip(animClip.AnimHashCode);
            }
            animClip.Destroy();
            _animClips.Remove(animClip);
            return true;
        }

        public bool ContainsAnimationClip(string name)
        {
            int animHashCode = Animator.StringToHash(name);
            for (int i = 0; i < _animClips.Count; i++)
            {
                var animClip = _animClips[i];

                if (animClip != null && animClip.AnimHashCode == animHashCode)
                {
                    return true;
                }
            }
            return false;
        }

        private AnimMixer CreateAnimMixer(int layer)
        {
            int inputCount = _mixerRoot.GetInputCount();

            if (layer == 0 && inputCount == 0)
            {
                _mixerRoot.SetInputCount(1);
            }
            else
            {
                if (layer > inputCount - 1)
                {
                    _mixerRoot.SetInputCount(layer + 1);
                }
            }
            var animMixer = new AnimMixer(_graph, layer);
            _animMixers.Add(animMixer);
            return animMixer;
        }
    }
}
