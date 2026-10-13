// IRockeyArmModule.cs
// 作用：TEngine 侧加密锁授权模块接口（适配层门面，等价于核心 RockeyArm.IRockeyArm）。
// 搭配文件：RockeyArmModule.cs（适配层实现，包装零框架依赖的 RockeyArm.RockeyArmManager）。
//
// 业务侧一律通过 GameModule.RockeyArm 访问本接口；注册在启动入口完成：
//     ModuleSystem.RegisterModule<IRockeyArmModule>(new RockeyArmModule());

namespace TEngine
{
    /// <summary>
    /// TEngine 侧加密锁授权模块接口。
    /// <remarks>能力与子接口 <see cref="RockeyArm.IRockeyArm"/> 完全一致，仅作为 TEngine 模块门面存在。</remarks>
    /// </summary>
    public interface IRockeyArmModule : RockeyArm.IRockeyArm
    {
    }
}
