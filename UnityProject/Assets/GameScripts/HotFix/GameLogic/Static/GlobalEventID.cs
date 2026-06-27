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
    /// 选用 int 事件而非 <c>[EventInterface]</c> 接口事件的原因：加载页流程需要跨
    /// <see cref="GameSceneManager"/>（非 UI 类，须手动 Add/RemoveEventListener）与
    /// <see cref="LoadingUI"/>（UI 类，用 AddUIEvent 随窗口销毁自动清理）两侧，
    /// int 事件在两边调用形式对称、无需定义额外接口，最契合该场景。
    /// </para>
    /// <para>
    /// <b>注意</b>：发送方与监听方必须使用同一字符串，否则事件无法匹配；
    /// 自定义事件字符串建议统一前缀以便检索。
    /// </para>
    /// <para>
    /// <b>文件组织</b>：按功能分类拆分为 partial 文件：
    /// <list type="bullet">
    /// <item>GlobalEventID.Scene.cs — 场景相关事件</item>
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
