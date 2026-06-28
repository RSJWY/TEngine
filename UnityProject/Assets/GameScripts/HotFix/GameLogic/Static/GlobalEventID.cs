using TEngine;

namespace GameLogic
{
    /// <summary>
    /// 全局事件 ID 常量集合（主文件）。
    /// </summary>
    /// <remarks>
    /// 采用 <see cref="RuntimeId.ToRuntimeId"/> 把字符串约定转为 int 事件 ID，供
    /// <c>GameEvent.Send / AddEventListener / RemoveEventListener</c> 与 UI 侧的 <c>AddUIEvent</c> 使用。
    /// <para>
    /// 模块间通信优先使用 <c>[EventInterface]</c> 接口事件；仅简单通知或 UI 内部事件继续放在这里。
    /// </para>
    /// <para>
    /// <b>注意</b>：发送方与监听方必须使用同一字符串，否则事件无法匹配；
    /// 自定义事件字符串建议统一前缀以便检索。
    /// </para>
    /// <para>
    /// <b>文件组织</b>：按功能分类拆分为 partial 文件：
    /// <list type="bullet">
    /// <item>场景相关事件已迁移到 <see cref="IGameSceneEvent"/></item>
    /// <item>其他模块事件根据需要新增对应 partial 文件</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static partial class GlobalEventID
    {
        // 通用事件放在此主文件
        // 具体功能模块事件分散到对应 partial 文件
    }
}
