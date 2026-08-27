namespace GameLogic
{
    /// <summary>
    /// 帧动画初始化配置。
    /// <para>替代 DGame 原先依赖 Luban 生成的 ModelConfig，仅保留帧动画相关字段。</para>
    /// <para>调用方自行从任意数据源（SO/配置表/手填）构造此结构体后传入 Agent.Init。</para>
    /// </summary>
    public struct FrameAnimConfig
    {
        /// <summary>
        /// 帧动画配置资源定位地址（挂有 FrameSpritePool 的 Prefab）。
        /// </summary>
        public string FrameCfgLocation;

        /// <summary>
        /// 场景模型缩放（<=0 视为 1）。
        /// </summary>
        public float ModelScale;

        /// <summary>
        /// 死亡动画播放速度（<=0 视为 1）。
        /// </summary>
        public float DeathFrameSpeed;

        /// <summary>
        /// UI 模型缩放（<=0 视为 1）。
        /// </summary>
        public float UIScale;
    }
}
