using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// 帧动画图片资源池。
    /// <para>挂在帧动画配置 Prefab 上，Inspector 中按 <see cref="FrameAnimName"/> 枚举成员填入对应帧序列。</para>
    /// <para>原 DGame 版本通过 Roslyn SourceGenerator 生成 partial 补全字段与 GetSprites 方法，
    /// 此版本改为直接手写，与生成器输出等价，避免引入额外编译期生成器依赖。</para>
    /// </summary>
    public partial class FrameSpritePool : MonoBehaviour
    {
        // 字段与 GetSprites/SortAllSprites 等由 FrameSpritePool.Gen.cs 补全。
    }
}
