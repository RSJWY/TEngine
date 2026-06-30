using Cysharp.Threading.Tasks;
using System.Threading;

namespace TEngine
{
    /// <summary>
    /// 轻量运行时配置模块接口。从 StreamingAssets/Configs 读取清单声明的文本配置并缓存，供任意位置便捷访问。
    /// </summary>
    public interface IRuntimeConfigModule
    {
        /// <summary>
        /// 是否已完成加载。
        /// </summary>
        bool IsLoaded { get; }

        /// <summary>
        /// 读取清单并加载其中声明的全部配置。
        /// </summary>
        UniTask LoadAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 重新加载指定配置（重新读文件并清理其对象缓存）。
        /// </summary>
        UniTask ReloadAsync(string configName, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取强类型配置；未找到或解析失败抛异常。默认以 typeof(T).Name 作为配置名。
        /// </summary>
        T Get<T>(string configName = null) where T : class;

        /// <summary>
        /// 尝试获取强类型配置；未找到或解析失败返回 false。
        /// </summary>
        bool TryGet<T>(out T config, string configName = null) where T : class;

        /// <summary>
        /// 获取原始配置文本；未找到抛异常。
        /// </summary>
        string GetText(string configName);

        /// <summary>
        /// 尝试获取原始配置文本；未找到返回 false。
        /// </summary>
        bool TryGetText(string configName, out string text);

        /// <summary>
        /// 是否包含指定配置。
        /// </summary>
        bool Contains(string configName);

        /// <summary>
        /// 清空所有缓存。
        /// </summary>
        void Clear();
    }
}
