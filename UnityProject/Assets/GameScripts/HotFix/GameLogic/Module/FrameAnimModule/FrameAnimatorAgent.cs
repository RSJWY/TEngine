using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// 场景帧动画播放状态。
    /// </summary>
    public enum FrameAnimState
    {
        /// <summary>
        /// 待机状态。
        /// </summary>
        Idle,
        /// <summary>
        /// 移动状态。
        /// </summary>
        Move,
        /// <summary>
        /// 死亡状态。
        /// </summary>
        Death,
        /// <summary>
        /// 攻击状态。
        /// </summary>
        Attack,
        /// <summary>
        /// 技能状态。
        /// </summary>
        Skill,
        /// <summary>
        /// 技能1状态。
        /// </summary>
        Skill1,
        /// <summary>
        /// 技能2状态。
        /// </summary>
        Skill2,
        /// <summary>
        /// 受击1状态。
        /// </summary>
        Hurt1,
        /// <summary>
        /// 受击2状态。
        /// </summary>
        Hurt2,
        /// <summary>
        /// 状态数量上限。
        /// </summary>
        Max
    }

    /// <summary>
    /// 场景帧动画参数定义。
    /// </summary>
    public static class FrameAnimParamDefine
    {
        /// <summary>
        /// 移动状态参数。
        /// </summary>
        public static readonly int Moving = Animator.StringToHash("Moving");
        /// <summary>
        /// 技能索引参数。
        /// </summary>
        public static readonly int SkillIndex = Animator.StringToHash("SkillIndex");
        /// <summary>
        /// 攻击状态参数。
        /// </summary>
        public static readonly int Attack = Animator.StringToHash("Attack");
        /// <summary>
        /// 受击表现参数。
        /// </summary>
        public static readonly int ImpactId = Animator.StringToHash("ImpactId");
        /// <summary>
        /// 死亡状态参数。
        /// </summary>
        public static readonly int Death = Animator.StringToHash("Death");
        /// <summary>
        /// 移动速度缩放参数。
        /// </summary>
        public static readonly int MoveSpeedScale = Animator.StringToHash("MoveSpeed");
        /// <summary>
        /// 技能速度缩放参数。
        /// </summary>
        public static readonly int SkillSpeedScale = Animator.StringToHash("SkillSpeed");
        /// <summary>
        /// 显示状态参数。
        /// </summary>
        public static readonly int Show = Animator.StringToHash("Show");
        /// <summary>
        /// 受击动画索引参数。
        /// </summary>
        public static readonly int HurtIndex = Animator.StringToHash("HurtIndex");
    }

    /// <summary>
    /// 场景帧动画播放代理。绑定 <see cref="SpriteRenderer"/> 显示序列帧。
    /// </summary>
    public sealed class FrameAnimatorAgent : MemoryObject
    {
        #region 字段

        /// <summary>
        /// 基础帧间隔，默认一秒八帧。
        /// </summary>
        private const float FRAME_INTERVAL = 0.125f; // 1秒8帧

        /// <summary>
        /// 普通模型基础播放速度。
        /// </summary>
        private const float NORMAL_BASE_SPEED = 1.5f; // 1秒12帧

        /// <summary>
        /// 精英模型基础播放速度。
        /// </summary>
        private const float ELITE_BASE_SPEED = 1.5f; // 1秒12帧

        /// <summary>
        /// 移动动画初始帧随机数生成器。
        /// </summary>
        private static readonly System.Random s_random = new System.Random();

        /// <summary>
        /// 当前使用的帧动画资源池。
        /// </summary>
        private FrameSpritePool _frameSpritePool;

        /// <summary>
        /// 场景模型显示用的精灵渲染器。
        /// </summary>
        private SpriteRenderer _spriteRenderer;

        /// <summary>
        /// 是否已经完成资源初始化。
        /// </summary>
        private bool _isInit;

        /// <summary>
        /// 当前播放的帧动画状态。
        /// </summary>
        private FrameAnimState _curFrameAnimName = FrameAnimState.Idle;

        /// <summary>
        /// 初始化完成前缓存的目标帧动画状态。
        /// </summary>
        private FrameAnimState _changeFrameAnimName = FrameAnimState.Idle;

        /// <summary>
        /// 各状态对应的帧动画片段缓存。
        /// </summary>
        private FrameClip[] _animClips = new FrameClip[(int)FrameAnimState.Max];

        /// <summary>
        /// 浮点动画参数缓存。
        /// </summary>
        private readonly Dictionary<int, float> _floatMap = new Dictionary<int, float>();

        /// <summary>
        /// 当前帧动画配置资源地址。
        /// </summary>
        private string _curCfgLocation;

        /// <summary>
        /// 是否已经绑定显示用的精灵渲染器。
        /// </summary>
        private bool _isBindSpriteRenderer;

        /// <summary>
        /// 死亡动画播放速度。
        /// </summary>
        private float _deathSpeed = 1.0f;

        /// <summary>
        /// 模型显示缩放。
        /// </summary>
        private Vector3 _modelScale;

        /// <summary>
        /// 是否已经设置首帧。
        /// </summary>
        private bool _isSetFirstFrame;

        /// <summary>
        /// 是否使用非缩放时间驱动。
        /// </summary>
        private bool _isUnscaledTime;

        /// <summary>
        /// 上一次推进动画帧时的时间戳。
        /// </summary>
        private float _preFrameTime;

        /// <summary>
        /// 动画速度缩放系数。
        /// </summary>
        private float _speedScale = 1.0f;

        /// <summary>
        /// 当前基础播放速度。
        /// </summary>
        private float _curBaseSpeed;

        /// <summary>
        /// 是否已经释放或销毁。
        /// </summary>
        private bool _isDestroy;

        /// <summary>
        /// 是否已经请求开始播放。
        /// </summary>
        private bool _isStarted;

        /// <summary>
        /// 当前代理在调度列表中的索引；-1表示未注册。
        /// </summary>
        internal int UpdateIndex { get; set; } = -1;

        /// <summary>
        /// 当前代理是否由非缩放时间调度。
        /// </summary>
        internal bool IsUnscaledTime => _isUnscaledTime;

        /// <summary>
        /// 是否有效（未销毁、已初始化、已绑定SpriteRenderer）
        /// </summary>
        public bool IsValid => !_isDestroy && _isInit && _spriteRenderer != null;

        private int _initVersion;
        private CancellationTokenSource _initCts;

        #endregion

        /// <summary>
        /// 从内存池获取时回调（清理旧状态）。
        /// </summary>
        public override void InitFromPool()
        {
            _isDestroy = false;
        }

        /// <summary>
        /// 创建帧动画代理实例
        /// </summary>
        public static FrameAnimatorAgent Create()
        {
            return MemoryPool.Alloc<FrameAnimatorAgent>();
        }

        /// <summary>
        /// 初始化帧动画代理，异步加载帧动画资源
        /// </summary>
        /// <param name="config">帧动画配置</param>
        public async UniTask Init(FrameAnimConfig config)
        {
            _initCts?.Cancel();
            _initCts?.Dispose();
            _initCts = new CancellationTokenSource();
            int version = ++_initVersion;
            var ct = _initCts.Token;
            if (string.IsNullOrEmpty(config.FrameCfgLocation))
            {
                Log.Error($"请检查帧动画配置，FrameCfgLocation 为空");
                return;
            }
            _curCfgLocation = config.FrameCfgLocation;
            try
            {
                var frameSpritePool = await FrameSpriteMgr.Instance.GetFrameSpritePool(_curCfgLocation, ct);
                if (ct.IsCancellationRequested || version != _initVersion || _isDestroy)
                {
                    return;
                }

                if (frameSpritePool == null)
                {
                    Log.Error($"没有找到帧动画配置文件: {_curCfgLocation}");
                    return;
                }

                _frameSpritePool = frameSpritePool;
            }
            catch (OperationCanceledException)
            {
                return;
            }

            _modelScale = config.ModelScale > 0 ? new Vector3(config.ModelScale, config.ModelScale, config.ModelScale) : Vector3.one;
            _deathSpeed = config.DeathFrameSpeed > 0 ? config.DeathFrameSpeed : 1;
            _curBaseSpeed = NORMAL_BASE_SPEED;
            _isBindSpriteRenderer = false;

            if (_isDestroy)
            {
                return;
            }

            _animClips[(int)FrameAnimState.Idle] = new FrameClip(FrameAnimName.idle,
                _frameSpritePool.GetSprites(FrameAnimName.idle), IsLoopAnim(FrameAnimState.Idle));
            var moveClip = new FrameClip(FrameAnimName.run,
                _frameSpritePool.GetSprites(FrameAnimName.run), IsLoopAnim(FrameAnimState.Move));
            moveClip.RandomSetInitIndex(s_random);
            _animClips[(int)FrameAnimState.Move] = moveClip;
            _animClips[(int)FrameAnimState.Death] = new FrameClip(FrameAnimName.death,
                _frameSpritePool.GetSprites(FrameAnimName.death), IsLoopAnim(FrameAnimState.Death));
            _animClips[(int)FrameAnimState.Attack] = new FrameClip(FrameAnimName.attack,
                _frameSpritePool.GetSprites(FrameAnimName.attack), IsLoopAnim(FrameAnimState.Attack));
            _animClips[(int)FrameAnimState.Skill] = new FrameClip(FrameAnimName.skill,
                _frameSpritePool.GetSprites(FrameAnimName.skill), IsLoopAnim(FrameAnimState.Skill));
            _animClips[(int)FrameAnimState.Skill1] = new FrameClip(FrameAnimName.skill1,
                _frameSpritePool.GetSprites(FrameAnimName.skill1), IsLoopAnim(FrameAnimState.Skill1));
            _animClips[(int)FrameAnimState.Skill2] = new FrameClip(FrameAnimName.skill2,
                _frameSpritePool.GetSprites(FrameAnimName.skill2), IsLoopAnim(FrameAnimState.Skill2));
            _animClips[(int)FrameAnimState.Hurt1] = new FrameClip(FrameAnimName.hurt1,
                _frameSpritePool.GetSprites(FrameAnimName.hurt1), IsLoopAnim(FrameAnimState.Hurt1));
            _animClips[(int)FrameAnimState.Hurt2] = new FrameClip(FrameAnimName.hurt2,
                _frameSpritePool.GetSprites(FrameAnimName.hurt2), IsLoopAnim(FrameAnimState.Hurt2));
            _isInit = true;
            SetFirstFrame();
        }

        /// <summary>
        /// 设置是否使用不受时间缩放影响的时间
        /// </summary>
        /// <param name="isUnscaledTime">true=使用UnscaledTime，false=使用普通Time</param>
        public void SetUnscaledTime(bool isUnscaledTime)
        {
            if (_isUnscaledTime == isUnscaledTime)
            {
                return;
            }

            bool isRegistered = UpdateIndex >= 0;
            if (isRegistered && FrameSpriteMgr.IsValid)
            {
                FrameSpriteMgr.Instance.UnregisterAnimator(this);
            }

            _isUnscaledTime = isUnscaledTime;

            if (_isStarted && IsValid)
            {
                _preFrameTime = isUnscaledTime ? Time.unscaledTime : Time.time;
            }

            if (isRegistered && FrameSpriteMgr.IsValid)
            {
                FrameSpriteMgr.Instance.RegisterAnimator(this);
            }
        }

        /// <summary>
        /// 绑定显示用的SpriteRenderer组件
        /// </summary>
        /// <param name="spriteRenderer">SpriteRenderer组件</param>
        public void BindDisplayRender(SpriteRenderer spriteRenderer)
        {
            if (_isBindSpriteRenderer)
            {
                return;
            }
            _isBindSpriteRenderer = true;
            _spriteRenderer = spriteRenderer;
            SetFirstFrame();
        }

        /// <summary>
        /// 绑定显示用的SpriteRenderer组件。
        /// </summary>
        /// <param name="spriteRenderer">SpriteRenderer组件。</param>
        public void BindSpriteRenderer(SpriteRenderer spriteRenderer)
        {
            BindDisplayRender(spriteRenderer);
        }

        /// <summary>
        /// 在初始化和绑定显示组件都满足后设置首帧图片。
        /// </summary>
        private void SetFirstFrame()
        {
            if (!_isInit)
            {
                if (_spriteRenderer != null)
                {
                    _spriteRenderer.sprite = null;
                }
                return;
            }

            if (_isSetFirstFrame || _spriteRenderer == null)
            {
                return;
            }

            _curFrameAnimName = _changeFrameAnimName;
            var curClip = _animClips[(int)_curFrameAnimName];

            if (curClip == null)
            {
                Log.Warning($"没找到动画Clip: {_curFrameAnimName}");
                return;
            }

            SetSprite(curClip.GetNext());
            SetSpriteRendererSize();
            _preFrameTime = _isUnscaledTime ? Time.unscaledTime : Time.time;
            _isSetFirstFrame = true;
        }

        /// <summary>
        /// 设置当前帧图片到精灵渲染器。
        /// </summary>
        /// <param name="sprite">待显示的帧图片。</param>
        private void SetSprite(Sprite sprite)
        {
            if (_isDestroy || !_isInit || _spriteRenderer == null || sprite == null)
            {
                return;
            }

            _spriteRenderer.sprite = sprite;
        }

        /// <summary>
        /// 开始播放帧动画
        /// </summary>
        public void StartAnim()
        {
            if (!IsValid)
            {
                return;
            }

            _isStarted = true;
            FrameSpriteMgr.Instance.RegisterAnimator(this);
            _preFrameTime = _isUnscaledTime ? Time.unscaledTime : Time.time;
        }

        /// <summary>
        /// 调度器驱动的帧动画更新。
        /// </summary>
        /// <param name="currentTime">当前时间域下的时间戳。</param>
        /// <returns>true表示仍需继续调度；false表示已无效或播放结束。</returns>
        internal bool Tick(float currentTime)
        {
            if (!IsValid)
            {
                return false;
            }

            var curClip = _animClips[(int)_curFrameAnimName];

            if (curClip == null)
            {
                return false;
            }

            if (curClip.IsStop())
            {
                return false;
            }

            var deltaTime = currentTime - _preFrameTime;

            if (deltaTime * GetSpeed() > FRAME_INTERVAL)
            {
                SetSprite(curClip.GetNext());
                SetSpriteRendererSize();
                _preFrameTime = currentTime;
            }

            return true;
        }

        /// <summary>
        /// 设置或还原精灵渲染器所在节点的模型缩放。
        /// </summary>
        /// <param name="revert">是否还原为默认缩放。</param>
        private void SetSpriteRendererSize(bool revert = false)
        {
            if (_spriteRenderer == null)
            {
                return;
            }

            _spriteRenderer.transform.localScale = revert ? Vector3.one : _modelScale;
        }

        /// <summary>
        /// 获取当前动画播放速度
        /// </summary>
        /// <returns>当前动画播放速度。</returns>
        public float GetSpeed()
        {
            if (_curFrameAnimName == FrameAnimState.Move)
            {
                return _curBaseSpeed;
            }

            if (_curFrameAnimName == FrameAnimState.Death)
            {
                return _deathSpeed;
            }

            return _speedScale * _curBaseSpeed;
        }

        /// <summary>
        /// 切换动画状态
        /// </summary>
        /// <param name="animName">目标动画状态</param>
        public void SwitchAnim(FrameAnimState animName)
        {
            if (!IsValid)
            {
                _changeFrameAnimName = animName;
                return;
            }

            var oldAnimName = _curFrameAnimName;
            if (animName != oldAnimName)
            {
                _curFrameAnimName = animName;
                var oldClip = _animClips[(int)oldAnimName];
                oldClip?.Leave();

                if (_isStarted)
                {
                    FrameSpriteMgr.Instance.RegisterAnimator(this);
                }
            }
        }

        /// <summary>
        /// 根据布尔参数切换动画状态。
        /// </summary>
        /// <param name="id">动画参数ID。</param>
        /// <param name="value">参数值。</param>
        public void SwitchAnim(int id, bool value)
        {
            var state = FrameAnimState.Idle;
            if (id == FrameAnimParamDefine.Death)
            {
                if (value)
                {
                    state = FrameAnimState.Death;
                }
            }
            else if (id == FrameAnimParamDefine.Show)
            {
                if (value)
                {
                    state = FrameAnimState.Idle;
                }
            }
            else if (id == FrameAnimParamDefine.Moving)
            {
                if (value)
                {
                    state = FrameAnimState.Move;
                }
            }
            else if (id == FrameAnimParamDefine.Attack)
            {
                if (value)
                {
                    state = FrameAnimState.Attack;
                }
            }

            SwitchAnim(state);
        }

        /// <summary>
        /// 根据整型参数切换动画状态。
        /// </summary>
        /// <param name="id">动画参数ID。</param>
        /// <param name="value">参数值。</param>
        public void SwitchAnim(int id, int value)
        {
            var state = FrameAnimState.Idle;
            if (id == FrameAnimParamDefine.SkillIndex)
            {
                switch (value)
                {
                    case 0:
                        state = FrameAnimState.Skill;
                        break;
                    case 1:
                        state = FrameAnimState.Skill1;
                        break;
                    case 2:
                        state = FrameAnimState.Skill2;
                        break;
                    default:
                        state = FrameAnimState.Idle;
                        break;
                }
            }
            else if (id == FrameAnimParamDefine.HurtIndex)
            {
                switch (value)
                {
                    case 1:
                        state = FrameAnimState.Hurt1;
                        break;
                    case 2:
                        state = FrameAnimState.Hurt2;
                        break;
                    default:
                        state = FrameAnimState.Idle;
                        break;
                }
            }
            else if (id == FrameAnimParamDefine.ImpactId)
            {
                state = _isInit ? _curFrameAnimName : _changeFrameAnimName;
            }

            SwitchAnim(state);
        }

        /// <summary>
        /// 重播动画
        /// </summary>
        /// <param name="animName"></param>
        public void ReplayAnim(FrameAnimState animName)
        {
            if (!IsValid)
            {
                _changeFrameAnimName = animName;
                return;
            }

            if (animName != _curFrameAnimName)
            {
                var oldClip = _animClips[(int)_curFrameAnimName];
                oldClip?.Leave();
                _curFrameAnimName = animName;
            }

            var clip = _animClips[(int)_curFrameAnimName];
            if (clip == null)
            {
                return;
            }

            clip.Leave();
            SetSprite(clip.GetNext());
            SetSpriteRendererSize();
            _preFrameTime = _isUnscaledTime ? Time.unscaledTime : Time.time;

            if (_isStarted)
            {
                FrameSpriteMgr.Instance.RegisterAnimator(this);
            }
        }

        /// <summary>
        /// 判断指定动画是否循环播放
        /// </summary>
        /// <param name="animName">动画状态</param>
        /// <returns>true=循环播放</returns>
        public bool IsLoopAnim(FrameAnimState animName)
            => animName == FrameAnimState.Idle || animName == FrameAnimState.Move;

        /// <summary>
        /// 设置动画播放速度缩放
        /// </summary>
        /// <param name="speed">速度缩放倍数</param>
        public void SetAnimSpeed(float speed)
        {
            _speedScale = speed;
        }

        /// <summary>
        /// 设置全部动画播放速度缩放。
        /// </summary>
        /// <param name="speed">速度缩放倍数。</param>
        public void SetAllAnimSpeed(float speed)
        {
            SetAnimSpeed(speed);
        }

        /// <summary>
        /// 设置浮点类型动画参数。
        /// </summary>
        /// <param name="id">动画参数ID。</param>
        /// <param name="value">参数值。</param>
        public void SetFloat(int id, float value)
        {
            if (_floatMap.ContainsKey(id))
            {
                _floatMap[id] = value;
            }
            else
            {
                _floatMap.Add(id, value);
            }
        }

        /// <summary>
        /// 设置是否使用不受时间缩放影响的时间。
        /// </summary>
        /// <param name="isUnScale">true=使用UnscaledTime，false=使用普通Time。</param>
        public void SetUnScale(bool isUnScale)
        {
            SetUnscaledTime(isUnScale);
        }

        #region 释放资源

        /// <summary>
        /// 主动释放
        /// </summary>
        public void Release()
        {
            MemoryPool.Dealloc(this);
        }

        /// <summary>
        /// 回收到内存池时清理状态。
        /// </summary>
        public override void RecycleToPool()
        {
            _initVersion++;
            _initCts?.Cancel();
            _initCts?.Dispose();
            _initCts = null;
            if (FrameSpriteMgr.IsValid)
            {
                FrameSpriteMgr.Instance.UnregisterAnimator(this);
            }
            _isInit = false;
            _isDestroy = true;
            _isStarted = false;
            _frameSpritePool = null;
            SetSpriteRendererSize(true);
            if (_spriteRenderer != null)
            {
                _spriteRenderer.sprite = null;
            }
            _spriteRenderer = null;
            _curFrameAnimName = FrameAnimState.Idle;
            _changeFrameAnimName = FrameAnimState.Idle;
            _curCfgLocation = string.Empty;
            _isBindSpriteRenderer = false;
            _deathSpeed = 1.0f;
            _modelScale = Vector3.one;
            _isSetFirstFrame = false;
            _isUnscaledTime = false;
            _preFrameTime = 0;
            _speedScale = 1.0f;
            _floatMap.Clear();

            for (int i = 0; i < _animClips.Length; i++)
            {
                _animClips[i]?.OnDestroy();
                _animClips[i] = null;
            }
        }

        #endregion
    }
}
