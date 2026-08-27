using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// 帧动画播放片段。
    /// </summary>
    public sealed class FrameClip
    {
        /// <summary>
        /// 当前片段持有的帧图片列表。
        /// </summary>
        private List<Sprite> _sprites;

        /// <summary>
        /// 当前即将播放的帧索引。
        /// </summary>
        private int _curIndex;

        /// <summary>
        /// 缓存的帧图片数量。
        /// </summary>
        private int _cacheCount;

        /// <summary>
        /// 当前片段是否循环播放。
        /// </summary>
        private bool _isLoop;

        /// <summary>
        /// 初始化帧动画播放片段。
        /// </summary>
        /// <param name="animName">动画资源名称。</param>
        /// <param name="sprites">动画帧图片列表。</param>
        /// <param name="isLoop">是否循环播放。</param>
        public FrameClip(FrameAnimName animName, List<Sprite> sprites, bool isLoop)
        {
            _sprites = sprites;
            _curIndex = 0;
            _cacheCount = sprites != null ? sprites.Count : 0;
            _isLoop = isLoop;
        }

        /// <summary>
        /// 获取下一帧图片。
        /// </summary>
        /// <returns>下一帧图片；没有可用帧时返回null。</returns>
        public Sprite GetNext()
        {
            if (_cacheCount <= 0)
            {
                return null;
            }

            if (_isLoop)
            {
                _curIndex %= _cacheCount;
            }
            else
            {
                _curIndex = Mathf.Min(_curIndex, _cacheCount - 1);
            }

            return _sprites[_curIndex++];
        }

        /// <summary>
        /// 判断非循环动画是否已经播放结束。
        /// </summary>
        /// <returns>true表示已经播放结束。</returns>
        public bool IsStop()
            => _cacheCount <= 0 || !_isLoop && _curIndex >= _cacheCount;

        /// <summary>
        /// 随机设置初始帧索引。
        /// </summary>
        /// <param name="random">随机数生成器。</param>
        public void RandomSetInitIndex(System.Random random)
        {
            if (_cacheCount <= 0 || random == null)
            {
                return;
            }

            _curIndex = random.Next(0, _cacheCount);
        }

        /// <summary>
        /// 离开当前动画时重置播放索引。
        /// </summary>
        public void Leave() => _curIndex = 0;

        /// <summary>
        /// 销毁并清理片段缓存。
        /// </summary>
        public void OnDestroy()
        {
            _sprites = null;
            _isLoop = false;
            _cacheCount = 0;
            _curIndex = 0;
        }
    }
}
