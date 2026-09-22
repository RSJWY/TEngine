using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;

namespace TEngine
{
    /// <summary>
    /// 轻量运行时配置模块接口。按覆盖链 persistentDataPath/Configs -> StreamingAssets/Configs 读取清单声明的文本配置并缓存，供任意位置便捷访问。
    /// 配置名支持相对 Configs 的子目录路径（统一 / 分隔、去扩展名），如 "sub/Foo"。
    /// 非线程安全，仅支持主线程调用。
    /// </summary>
    public interface IRuntimeConfigModule
    {
        /// <summary>
        /// 是否已完成一次加载流程；个别配置加载失败被跳过时仍为 true。
        /// </summary>
        bool IsLoaded { get; }

        /// <summary>
        /// 读取 TOML 清单（config_manifest.toml）并加载其中声明的全部配置。
        /// 读取顺序为 persistentDataPath/Configs 覆盖 StreamingAssets/Configs。
        /// 单个配置失败只记录错误并跳过，不中断整体流程；清单读取或解析失败时抛出异常。
        /// </summary>
        UniTask LoadAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 重新加载指定配置（按覆盖链重新读文件并清理其对象缓存）。
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
        /// 获取已加载的全部配置名列表（相对 Configs 的子目录路径形式，无扩展名）。
        /// </summary>
        IReadOnlyList<string> GetConfigNames();

        /// <summary>
        /// 清空所有缓存。
        /// </summary>
        void Clear();
    }
}
