using System;
using UnityEngine;
using UnityEngine.Playables;

namespace TEngine
{
    internal abstract class AnimNode
    {
        private readonly PlayableGraph _graph;
        private Playable _curSourcePlayable;
        private Playable _parent;

        private float _fadeSpeed = 0f;
        private float _fadeWeight = 0f;
        private bool _isFading = false;

        /// <summary>
        /// 是否已链接
        /// </summary>
        public bool IsConnected { get; private set; } = false;

        /// <summary>
        /// 输入端口
        /// </summary>
        public int InputPort { get; private set; }

        /// <summary>
        /// 是否已完成
        /// </summary>
        public bool IsDone => !_curSourcePlayable.IsNull() && _curSourcePlayable.IsDone();

        /// <summary>
        /// 是否有效
        /// </summary>
        public bool IsValid => !_curSourcePlayable.IsNull() && _curSourcePlayable.IsValid();

        /// <summary>
        /// 是否正在播放中
        /// </summary>
        public bool IsPlaying => !_curSourcePlayable.IsNull() && _curSourcePlayable.GetPlayState() == PlayState.Playing;

        /// <summary>
        /// 时间轴
        /// </summary>
        public float Time
        {
            get => (float)_curSourcePlayable.GetTime();
            set => _curSourcePlayable.SetTime(value);
        }

        /// <summary>
        /// 播放速度
        /// </summary>
        public float Speed
        {
            get => (float)_curSourcePlayable.GetSpeed();
            set => _curSourcePlayable.SetSpeed(value);
        }

        /// <summary>
        /// 权重值
        /// </summary>
        public float Weight
        {
            get => IsConnected ? _parent.GetInputWeight(InputPort) : 0f;
            set
            {
                if (IsConnected)
                {
                    _parent.SetInputWeight(InputPort, value);
                }
            }
        }

        protected AnimNode(PlayableGraph graph) => _graph = graph;

        protected void SetSourcePlayable(Playable playable) => _curSourcePlayable = playable;

        public virtual void Update(float elapsedSeconds)
        {
            if (_isFading)
            {
                Weight = Mathf.MoveTowards(Weight, _fadeWeight, elapsedSeconds * _fadeSpeed); // 原本是 / 改成了 * 有问题再看

                if (Mathf.Approximately(Weight, _fadeWeight))
                {
                    _isFading = false;
                }
            }
        }

        public virtual void Destroy()
        {
            if (IsValid)
            {
                _graph.DestroySubgraph(_curSourcePlayable);
            }
        }

        public virtual void Play()
        {
            _curSourcePlayable.Play();
            _curSourcePlayable.SetDone(false);
        }

        public virtual void Pause()
        {
            _curSourcePlayable.Pause();
            _curSourcePlayable.SetDone(true);
        }

        public virtual void Reset()
        {
            _fadeSpeed = 0;
            _fadeWeight = 0;
            _isFading = false;

            Time = 0;
            Speed = 1;
            Weight = 0;
        }

        /// <summary>
        /// 链接父节点
        /// </summary>
        /// <param name="parent">父节点对象</param>
        /// <param name="parentInputPort">父节点上的输入端口</param>
        public void Connect(Playable parent, int parentInputPort)
        {
            if (IsConnected)
            {
                throw new Exception("当前节点已经链接父节点");
            }
            _parent = parent;
            InputPort = parentInputPort;
            Reset();
            // 官方推荐使用
            _graph.Connect(_curSourcePlayable, 0, parent, parentInputPort);
            IsConnected = true;
        }

        /// <summary>
        /// 同父节点断开连接
        /// </summary>
        /// <exception cref="Exception"></exception>
        public void Disconnect()
        {
            if (!IsConnected)
            {
                throw new Exception("当前节点没有链接父节点");
            }

            _parent.DisconnectInput(InputPort);
            IsConnected = false;
        }

        /// <summary>
        /// 开始权重过渡
        /// </summary>
        /// <param name="destWeight">目标权重值</param>
        /// <param name="fadeDuration">过渡时间</param>
        public void StartWeightFade(float destWeight, float fadeDuration)
        {
            if (fadeDuration <= 0)
            {
                Weight = destWeight;
                _isFading = false;
                return;
            }

            _fadeSpeed = 1f / fadeDuration;
            _fadeWeight = destWeight;
            _isFading = true;
        }
    }
}
