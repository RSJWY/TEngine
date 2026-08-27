using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// UI帧动画播放状态。
    /// </summary>
    public enum UIFrameAnimState
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
        /// 技能状态。
        /// </summary>
        Skill,
        /// <summary>
        /// 受击状态。
        /// </summary>
        Hurt,
        /// <summary>
        /// 状态数量上限。
        /// </summary>
        Max
    }

    /// <summary>
    /// UI帧动画播放代理。绑定 UGUI <see cref="Image"/> 显示序列帧。
    /// </summary>
    public sealed class UIFrameAnimatorAgent : MemoryObject
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
        /// 当前使用的帧动画资源池。
        /// </summary>
        private FrameSpritePool _frameSpritePool;

        /// <summary>
        /// UI显示用的图片组件。
        /// </summary>
        private Image _image;

        /// <summary>
        /// 是否已经完成资源初始化。
        /// </summary>
        private bool _isInit;

        /// <summary>
        /// 当前播放的UI帧动画状态。
        /// </summary>
        private UIFrameAnimState _curFrameAnimName = UIFrameAnimState.Idle;

        /// <summary>
        /// 初始化完成前缓存的目标UI帧动画状态。
        /// </summary>
        private UIFrameAnimState _changeFrameAnimName = UIFrameAnimState.Idle;

        /// <summary>
        /// 各状态对应的帧动画片段缓存。
        /// </summary>
        private FrameClip[] _animClips = new FrameClip[(int)UIFrameAnimState.Max];

        /// <summary>
        /// 当前帧动画配置资源地址。
        /// </summary>
        private string _curCfgLocation;

        /// <summary>
        /// 是否已经绑定显示用的图片组件。
        /// </summary>
        private bool _isBindDisplayImage;

        /// <summary>
        /// 死亡动画播放速度。
        /// </summary>
        private float _deathSpeed = 1.0f;

        /// <summary>
        /// UI模型显示缩放。
        /// </summary>
        private Vector3 _uiModelScale;

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
        /// 是否有效（未销毁、已初始化、已绑定Image）
        /// </summary>
        public bool IsValid => !_isDestroy && _isInit && _image != null;

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
        public static UIFrameAnimatorAgent Create()
        {
            return MemoryPool.Alloc<UIFrameAnimatorAgent>();
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
            _uiModelScale = config.UIScale > 0 ? new Vector3(config.UIScale, config.UIScale, config.UIScale) : Vector3.one;
            _deathSpeed = config.DeathFrameSpeed > 0 ? config.DeathFrameSpeed : 1;
            _curBaseSpeed = NORMAL_BASE_SPEED;
            _isBindDisplayImage = false;

            if (_isDestroy)
            {
                return;
            }

            _animClips[(int)UIFrameAnimState.Idle] = new FrameClip(FrameAnimName.idle,
                _frameSpritePool.GetSprites(FrameAnimName.idle), IsLoopAnim(UIFrameAnimState.Idle));
            _animClips[(int)UIFrameAnimState.Move] = new FrameClip(FrameAnimName.run,
                _frameSpritePool.GetSprites(FrameAnimName.run), IsLoopAnim(UIFrameAnimState.Move));
            _animClips[(int)UIFrameAnimState.Death] = new FrameClip(FrameAnimName.death,
                _frameSpritePool.GetSprites(FrameAnimName.death), IsLoopAnim(UIFrameAnimState.Death));
            _animClips[(int)UIFrameAnimState.Skill] = new FrameClip(FrameAnimName.skill,
                _frameSpritePool.GetSprites(FrameAnimName.skill), IsLoopAnim(UIFrameAnimState.Skill));
            _animClips[(int)UIFrameAnimState.Hurt] = new FrameClip(FrameAnimName.hurt,
                _frameSpritePool.GetSprites(FrameAnimName.hurt), IsLoopAnim(UIFrameAnimState.Hurt));
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
        /// 绑定显示用的Image组件
        /// </summary>
        /// <param name="image">Image组件</param>
        public void BindDisplayRender(Image image)
        {
            if (_isBindDisplayImage)
            {
                return;
            }
            _isBindDisplayImage = true;
            _image = image;
            SetFirstFrame();
        }

        /// <summary>
        /// 在初始化和绑定图片组件都满足后设置首帧图片。
        /// </summary>
        private void SetFirstFrame()
        {
            if (!_isInit)
            {
                if (_image != null)
                {
                    _image.sprite = null;
                }
                return;
            }

            if (_isSetFirstFrame || _image == null)
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
            SetImageSize();
            SetSprite(curClip.GetNext());
            _preFrameTime = _isUnscaledTime ? Time.unscaledTime : Time.time;
            _isSetFirstFrame = true;
        }

        /// <summary>
        /// 设置当前帧图片到UI图片组件。
        /// </summary>
        /// <param name="sprite">待显示的帧图片。</param>
        private void SetSprite(Sprite sprite)
        {
            if (_isDestroy || !_isInit || _image == null || sprite == null)
            {
                return;
            }

            _image.sprite = sprite;
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
        /// 调度器驱动的UI帧动画更新。
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
                SetImageSize();
                _preFrameTime = currentTime;
            }

            return true;
        }

        /// <summary>
        /// 设置或还原图片组件所在节点的UI模型缩放。
        /// </summary>
        /// <param name="revert">是否还原为默认缩放。</param>
        private void SetImageSize(bool revert = false)
        {
            if (_image == null)
            {
                return;
            }

            _image.transform.localScale = revert ? Vector3.one : _uiModelScale;
        }

        /// <summary>
        /// 获取当前动画播放速度
        /// </summary>
        /// <returns>当前动画播放速度。</returns>
        public float GetSpeed()
        {
            if (_curFrameAnimName == UIFrameAnimState.Move)
            {
                return _curBaseSpeed;
            }

            if (_curFrameAnimName == UIFrameAnimState.Death)
            {
                return _deathSpeed;
            }

            return _speedScale * _curBaseSpeed;
        }

        /// <summary>
        /// 切换动画状态
        /// </summary>
        /// <param name="animName">目标动画状态</param>
        public void SwitchAnim(UIFrameAnimState animName)
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
        /// 重播动画
        /// </summary>
        /// <param name="animName"></param>
        public void ReplayAnim(UIFrameAnimState animName)
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
            SetImageSize();
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
        public bool IsLoopAnim(UIFrameAnimState animName)
            => animName == UIFrameAnimState.Idle || animName == UIFrameAnimState.Move;

        /// <summary>
        /// 设置动画播放速度缩放
        /// </summary>
        /// <param name="speed">速度缩放倍数</param>
        public void SetAnimSpeed(float speed)
        {
            _speedScale = speed;
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
            SetImageSize(true);
            if (_image != null)
            {
                _image.sprite = null;
            }
            _image = null;
            _curFrameAnimName = UIFrameAnimState.Idle;
            _changeFrameAnimName = UIFrameAnimState.Idle;
            _curCfgLocation = string.Empty;
            _isBindDisplayImage = false;
            _deathSpeed = 1.0f;
            _uiModelScale = Vector3.one;
            _isSetFirstFrame = false;
            _isUnscaledTime = false;
            _preFrameTime = 0;
            _speedScale = 1.0f;

            for (int i = 0; i < _animClips.Length; i++)
            {
                _animClips[i]?.OnDestroy();
                _animClips[i] = null;
            }
        }

        #endregion
    }
}
