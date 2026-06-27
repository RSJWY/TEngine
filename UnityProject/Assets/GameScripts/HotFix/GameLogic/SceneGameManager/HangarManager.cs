using Cysharp.Threading.Tasks;
using TEngine;

namespace GameLogic
{
    /// <summary>
    /// 机库场景业务管理器。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 该脚本挂在机库场景的管理节点上，负责在 <see cref="HangarSceneSpawner"/> 完成机库动态对象加载后，
    /// 执行机库场景自己的初始化逻辑。
    /// </para>
    /// <para>
    /// 通用的 Spawner 查找、完成事件监听、主动轮询兜底和防重复初始化逻辑由
    /// <see cref="SceneGameManagerBase{TSpawner}"/> 统一处理；本类只保留机库差异化逻辑。
    /// </para>
    /// </remarks>
    public class HangarManager : SceneGameManagerBase<HangarSceneSpawner>
    {
        /// <summary>
        /// 当前 Manager 只响应机库场景的动态加载完成事件。
        /// </summary>
        protected override SceneType TargetSceneType => SceneType.MainScene;


        /// <summary>
        /// Unity Start：先执行父类兜底完成检查，再打开机库 UI。
        /// </summary>
        /// <remarks>
        /// 机库 UI 不依赖动态场景对象完全加载，因此保持进入场景后立即打开；
        /// 真正依赖动态对象的逻辑放在 <see cref="OnSceneSpawnCompleted"/> 中。
        /// </remarks>
        protected override void Start()
        {
            base.Start();
        }

        /// <summary>
        /// 机库动态对象全部加载完成后的初始化入口。
        /// </summary>
        /// <remarks>
        /// 这里适合放置依赖机库动态对象的逻辑，例如：相机就位、启用交互、播放入场镜头、刷新展示模型等。
        /// 父类已保证本方法只会执行一次。
        /// </remarks>
        protected override void OnSceneSpawnCompleted()
        {
            Log.Info("[HangarManager] 机库场景动态加载完成，开始初始化。");
            // TODO: 机库特有逻辑（相机就位、UI 弹出、交互开启等）

            var _obj = GetSpawnedObject("");
            

            /*UniTask.Create(async () =>
            {
                var ui = await GameModule.UI.ShowUIAsyncAwait<>();
                ui.SetManager(this);
            });*/
        }
        
    }
}
