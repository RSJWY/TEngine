namespace GameLogic
{
    /// <summary>
    /// 全局运行时数据（仅本次游戏循环，不持久化）。
    /// </summary>
    public static class GameValueStatic
    {
        /// <summary>
        /// 当前场景类型。
        /// </summary>
        public static SceneType? CurrentSceneType { get; internal set; }

        /// <summary>
        /// 上一个场景类型。
        /// </summary>
        public static SceneType? PreviousSceneType { get; internal set; }
            

        /// <summary>重置所有数据（切换账号/重新开始时调用）。</summary>
        public static void Reset()
        {
        }
    }
}