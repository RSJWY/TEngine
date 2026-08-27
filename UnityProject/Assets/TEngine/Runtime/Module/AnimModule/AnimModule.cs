using System;
using System.Collections.Generic;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// 动画模块。
    /// <para>基于 PlayableGraph 的代码驱动 3D 动画图，封装 Unity 底层 Playable API，
    /// 支持多层级混合、权重过渡、动画片段动态增删等。</para>
    /// </summary>
    internal sealed class AnimModule : Module, IUpdateModule, IAnimModule
    {
        private readonly Dictionary<string, IAnimPlayable> _animPlayables;
        private readonly List<IAnimPlayable> _tempAnimPlayableList;

        public int Count => _animPlayables.Count;

        public override int Priority => 1;

        public AnimModule()
        {
            _animPlayables = new Dictionary<string, IAnimPlayable>();
            _tempAnimPlayableList = new List<IAnimPlayable>();
        }

        public override void OnInit()
        { }

        public override void Shutdown()
        {
            foreach (var item in _animPlayables.Values)
            {
                item?.DestroyGraph();
            }
            _animPlayables.Clear();
            _tempAnimPlayableList.Clear();
        }

        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            if (_animPlayables == null || _animPlayables.Count <= 0)
            {
                return;
            }
            _tempAnimPlayableList.Clear();

            foreach (var item in _animPlayables.Values)
            {
                _tempAnimPlayableList.Add(item);
            }

            for (int i = 0; i < _tempAnimPlayableList.Count; i++)
            {
                var animPlayable = _tempAnimPlayableList[i];

                if (animPlayable == null || animPlayable.IsDestroyed)
                {
                    continue;
                }
                animPlayable.Update(elapseSeconds);
            }
        }

        public bool ContainsAnimPlayable(string name) => _animPlayables.ContainsKey(name);

        public IAnimPlayable GetAnimPlayable(string name) => _animPlayables.GetValueOrDefault(name, null);

        public IAnimPlayable[] GetAllAnimPlayable()
        {
            if (_animPlayables == null || _animPlayables.Count <= 0)
            {
                return null;
            }
            IAnimPlayable[] results = new IAnimPlayable[Count];
            int index = 0;
            foreach (var item in _animPlayables.Values)
            {
                results[index++] = item;
            }
            return results;
        }

        public void GetAllAnimPlayable(List<IAnimPlayable> results)
        {
            if (results == null)
            {
                throw new Exception("传入参数无效");
            }
            results.Clear();
            foreach (var item in _animPlayables.Values)
            {
                results.Add(item);
            }
        }

        public IAnimPlayable CreateAnimPlayable(Animator animator)
        {
            var animPlayable = AnimPlayable.Create(animator);
            if (!_animPlayables.TryAdd(animPlayable.Name, animPlayable))
            {
                throw new Exception($"已存在同名的动画图: {animPlayable.Name}");
            }

            return animPlayable;
        }

        public IAnimPlayable CreateAnimPlayable(Animator animator, List<AnimationClip> animations)
        {
            List<AnimationWrapper> warps = new List<AnimationWrapper>(animations.Count);

            for (int i = 0; i < animations.Count; i++)
            {
                warps.Add(new AnimationWrapper()
                {
                    Clip = animations[i],
                    WrapMode = animations[i].wrapMode,
                    Layer = 0
                });
            }

            return CreateAnimPlayable(animator, warps);
        }

        public IAnimPlayable CreateAnimPlayable(Animator animator, List<AnimationWrapper> animations)
        {
            var animPlayable = AnimPlayable.Create(animator);
            if (!_animPlayables.TryAdd(animPlayable.Name, animPlayable))
            {
                throw new Exception($"已存在同名的动画图: {animPlayable.Name}");
            }
            for (int i = 0; i < animations.Count; i++)
            {
                var animation = animations[i];
                animPlayable.AddAnimationClip(animation.Clip.name, animation.Clip, animation.WrapMode, animation.Layer, animation.FadeDuration);
            }
            return animPlayable;
        }

        public IAnimPlayable CreateAnimPlayable(Animator animator, params AnimationWrapper[] animations)
        {
            var animPlayable = AnimPlayable.Create(animator);
            if (!_animPlayables.TryAdd(animPlayable.Name, animPlayable))
            {
                throw new Exception($"已存在同名的动画图: {animPlayable.Name}");
            }
            for (int i = 0; i < animations.Length; i++)
            {
                var animation = animations[i];
                animPlayable.AddAnimationClip(animation.Clip.name, animation.Clip, animation.WrapMode, animation.Layer, animation.FadeDuration);
            }
            return animPlayable;
        }

        public bool DestroyAnimPlayable(IAnimPlayable animPlayable)
        {
            if (animPlayable == null)
            {
                throw new Exception("传入的动画图无效");
            }

            if (_animPlayables.ContainsKey(animPlayable.Name))
            {
                animPlayable.DestroyGraph();
                _animPlayables.Remove(animPlayable.Name);
                return true;
            }
            return false;
        }

        public bool DestroyAnimPlayable(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new Exception("传入的动画图名称无效");
            }
            var animPlayable = GetAnimPlayable(name);
            if (animPlayable != null)
            {
                animPlayable.DestroyGraph();
                _animPlayables.Remove(animPlayable.Name);
                return true;
            }
            return false;
        }
    }
}
