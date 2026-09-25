using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityWebRequest = UnityEngine.Networking.UnityWebRequest;

namespace TEngine
{
    /// <summary>
    /// 轻量运行时配置模块。按覆盖链 persistentDataPath/Configs -> StreamingAssets/Configs 读取清单声明的 JSON/TOML 文件并缓存。
    /// 通过 GameModule.Config 访问，DTO 由业务层定义。非线程安全，仅支持主线程调用。
    /// </summary>
    internal sealed class RuntimeConfigModule : Module, IRuntimeConfigModule
    {
        /// <summary>
        /// 配置根目录名，相对 StreamingAssets 与 persistentDataPath（Configs）。
        /// </summary>
        private const string CONFIG_ROOT = "Configs";

        /// <summary>
        /// 清单文件名，声明需要加载的配置列表。
        /// </summary>
        private const string MANIFEST_FILE = "config_manifest.toml";

        /// <summary>
        /// 配置名 -> 原始配置文本缓存。键忽略大小写，保留相对子目录（如 sub/Foo）。
        /// </summary>
        private readonly Dictionary<string, string> _textByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 配置名 -> 原始文件名缓存，用于 Reload 时定位回源文件。键忽略大小写。
        /// </summary>
        private readonly Dictionary<string, string> _fileByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 配置名 -> 配置文件格式缓存。键忽略大小写。
        /// </summary>
        private readonly Dictionary<string, RuntimeConfigFormat> _formatByName = new Dictionary<string, RuntimeConfigFormat>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// "配置名:类型全名" -> 已反序列化对象缓存，避免重复解析。键忽略大小写。
        /// </summary>
        private readonly Dictionary<string, object> _objectByKey = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 是否已完成一次加载流程；个别配置加载失败被跳过时仍为 true，失败项通过 TryGet 返回 false 兜底。
        /// </summary>
        public bool IsLoaded { get; private set; }

        /// <summary>
        /// 模块初始化。本模块无需预热，加载时机由调用方通过 LoadAllAsync 控制。
        /// </summary>
        public override void OnInit()
        {
        }

        /// <summary>
        /// 模块关闭，清空所有缓存。
        /// </summary>
        public override void Shutdown()
        {
            Clear();
        }

        /// <summary>
        /// 读取 TOML 清单并加载其中声明的全部配置到文本缓存。
        /// 每次调用前先清空旧缓存；清单为空时仅记录警告并标记加载完成。
        /// 读取顺序为 persistentDataPath/Configs 覆盖 StreamingAssets/Configs，两层均缺失才视为失败。
        /// 单个配置条目失败（重名、格式不支持、读取失败）只记录错误并跳过，不中断其余配置；
        /// 仅清单读取或解析失败、以及取消令牌触发时抛出异常。
        /// </summary>
        public async UniTask LoadAllAsync(CancellationToken cancellationToken = default)
        {
            Clear();

            RuntimeConfigManifest manifest = await LoadManifestAsync(cancellationToken);

            if (manifest?.files == null || manifest.files.Count == 0)
            {
                Log.Warning("Runtime config manifest is empty.");
                IsLoaded = true;
                return;
            }

            foreach (string file in manifest.files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(file))
                {
                    Log.Warning("Runtime config manifest contains a blank entry, skipped.");
                    continue;
                }

                try
                {
                    string normalizedName = NormalizeConfigName(file);
                    if (_textByName.ContainsKey(normalizedName))
                    {
                        Log.Error("Runtime config name is duplicated, skipped: {0} ({1})", normalizedName, file);
                        continue;
                    }

                    RuntimeConfigFormat format = GetConfigFormat(file);
                    var (text, _) = await ReadConfigTextWithRootAsync(file, cancellationToken);
                    _textByName[normalizedName] = text;
                    _fileByName[normalizedName] = file;
                    _formatByName[normalizedName] = format;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Log.Error("Runtime config load failed, skipped: {0}, reason: {1}", file, exception.Message);
                }
            }

            IsLoaded = true;
        }

        /// <summary>
        /// 重新加载指定配置：按覆盖链重新读取文件覆盖文本缓存，并清理该配置的对象缓存。
        /// 若该配置不在文件映射中，则按配置名推断文件名（默认追加 .toml）。
        /// </summary>
        public async UniTask ReloadAsync(string configName, CancellationToken cancellationToken = default)
        {
            string normalizedName = NormalizeConfigName(configName);
            string fileName = _fileByName.TryGetValue(normalizedName, out string mappedFile)
                ? mappedFile
                : NormalizeConfigFileName(configName);

            var (text, _) = await ReadConfigTextWithRootAsync(fileName, cancellationToken);
            _textByName[normalizedName] = text;
            _fileByName[normalizedName] = fileName;
            _formatByName[normalizedName] = GetConfigFormat(fileName);
            RemoveObjectCache(normalizedName);
        }

        /// <summary>
        /// 获取强类型配置；未找到或解析失败抛 GameFrameworkException。
        /// configName 为空时以 typeof(T).Name 作为配置名。
        /// </summary>
        public T Get<T>(string configName = null) where T : class
        {
            string normalizedName = NormalizeConfigName(configName ?? typeof(T).Name);

            if (!TryGet<T>(out T config, normalizedName))
            {
                throw new GameFrameworkException($"Runtime config not found or parse failed: {normalizedName}, type: {typeof(T).FullName}");
            }

            return config;
        }

        /// <summary>
        /// 尝试获取强类型配置；未找到或解析失败返回 false。
        /// 命中对象缓存但类型不兼容时移除旧缓存并回源重新解析，避免同配置名跨不兼容类型永久失败。
        /// </summary>
        public bool TryGet<T>(out T config, string configName = null) where T : class
        {
            string normalizedName = NormalizeConfigName(configName ?? typeof(T).Name);
            string objectKey = GetObjectKey(normalizedName, typeof(T));

            if (_objectByKey.TryGetValue(objectKey, out object cachedConfig))
            {
                if (cachedConfig is T typedConfig)
                {
                    config = typedConfig;
                    return true;
                }

                Log.Warning("Runtime config object cache type mismatch, re-parsing: {0}, cached: {1}, requested: {2}",
                    normalizedName, cachedConfig.GetType().FullName, typeof(T).FullName);
                _objectByKey.Remove(objectKey);
            }

            if (!_textByName.TryGetValue(normalizedName, out string text))
            {
                config = null;
                return false;
            }

            if (!_formatByName.TryGetValue(normalizedName, out RuntimeConfigFormat format))
            {
                config = null;
                return false;
            }

            try
            {
                config = Deserialize<T>(text, format);
            }
            catch (Exception exception)
            {
                Log.Warning("Runtime config parse failed: {0}, type: {1}, reason: {2}", normalizedName, typeof(T).FullName, exception.Message);
                config = null;
                return false;
            }

            if (config == null)
            {
                return false;
            }

            _objectByKey[objectKey] = config;
            return true;
        }

        /// <summary>
        /// 获取原始配置文本；未找到抛 GameFrameworkException。
        /// </summary>
        public string GetText(string configName)
        {
            string normalizedName = NormalizeConfigName(configName);

            if (!TryGetText(normalizedName, out string text))
            {
                throw new GameFrameworkException($"Runtime config not found: {normalizedName}");
            }

            return text;
        }

        /// <summary>
        /// 尝试获取原始配置文本；未找到返回 false。
        /// </summary>
        public bool TryGetText(string configName, out string text)
        {
            return _textByName.TryGetValue(NormalizeConfigName(configName), out text);
        }

        /// <summary>
        /// 是否包含指定配置（按文本缓存判断）。
        /// </summary>
        public bool Contains(string configName)
        {
            return _textByName.ContainsKey(NormalizeConfigName(configName));
        }

        /// <summary>
        /// 获取已加载的全部配置名列表（相对 Configs 的子目录路径形式，无扩展名）。
        /// </summary>
        public IReadOnlyList<string> GetConfigNames()
        {
            return new List<string>(_textByName.Keys);
        }

        /// <summary>
        /// 清空文本缓存、文件映射与对象缓存，并重置加载标记。
        /// </summary>
        public void Clear()
        {
            _textByName.Clear();
            _fileByName.Clear();
            _formatByName.Clear();
            _objectByKey.Clear();
            IsLoaded = false;
        }

        /// <summary>
        /// 按覆盖链读取 TOML 清单：persistent 层存在则优先，否则读 streaming 层；两层均缺失抛异常。
        /// </summary>
        private static async UniTask<RuntimeConfigManifest> LoadManifestAsync(CancellationToken cancellationToken)
        {
            string manifestText = await ReadOptionalConfigTextAsync(MANIFEST_FILE, cancellationToken);

            if (manifestText == null)
            {
                throw new GameFrameworkException($"Runtime config manifest not found in both persistent and streaming: {GetRelativePath(MANIFEST_FILE)}");
            }

            return Utility.Toml.ToObject<RuntimeConfigManifest>(manifestText);
        }

        /// <summary>
        /// 将配置名规范为文件名：统一分隔符，无扩展名时默认使用 .toml。
        /// </summary>
        private static string NormalizeConfigFileName(string configName)
        {
            string normalizedFile = configName.Trim().Replace("\\", "/");
            return Path.HasExtension(normalizedFile)
                ? normalizedFile
                : normalizedFile + ".toml";
        }

        /// <summary>
        /// 将配置名规范为缓存键：统一分隔符、去扩展名并去空白，保留目录部分（如 sub/Foo.toml -> sub/Foo）。
        /// 空白名抛 GameFrameworkException。
        /// </summary>
        private static string NormalizeConfigName(string configName)
        {
            if (string.IsNullOrWhiteSpace(configName))
            {
                throw new GameFrameworkException("Runtime config name is invalid.");
            }

            string normalizedName = configName.Trim().Replace("\\", "/");
            string extension = Path.GetExtension(normalizedName);
            if (!string.IsNullOrEmpty(extension))
            {
                normalizedName = normalizedName.Substring(0, normalizedName.Length - extension.Length);
            }

            if (string.IsNullOrEmpty(normalizedName))
            {
                throw new GameFrameworkException($"Runtime config name is invalid: {configName}");
            }

            return normalizedName;
        }

        /// <summary>
        /// 根据文件扩展名判断配置格式。
        /// </summary>
        private static RuntimeConfigFormat GetConfigFormat(string fileName)
        {
            string extension = Path.GetExtension(fileName);
            if (extension.Equals(".toml", StringComparison.OrdinalIgnoreCase))
            {
                return RuntimeConfigFormat.Toml;
            }

            if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                return RuntimeConfigFormat.Json;
            }

            throw new GameFrameworkException($"Runtime config format is unsupported: {fileName}");
        }

        /// <summary>
        /// 按配置格式反序列化为强类型对象。
        /// </summary>
        private static T Deserialize<T>(string text, RuntimeConfigFormat format) where T : class
        {
            return format switch
            {
                RuntimeConfigFormat.Toml => Utility.Toml.ToObject<T>(text),
                RuntimeConfigFormat.Json => Utility.Json.ToObject<T>(text),
                _ => throw new GameFrameworkException($"Runtime config format is unsupported: {format}")
            };
        }

        /// <summary>
        /// 生成对象缓存键："配置名:类型全名"，同名配置按不同类型分别缓存。
        /// </summary>
        private static string GetObjectKey(string configName, Type type)
        {
            return $"{configName}:{type.FullName}";
        }

        /// <summary>
        /// 移除指定配置名下所有类型的对象缓存（Reload 时调用）。
        /// </summary>
        private void RemoveObjectCache(string configName)
        {
            string prefix = configName + ":";
            List<string> removeKeys = null;

            foreach (string key in _objectByKey.Keys)
            {
                if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                removeKeys ??= new List<string>();
                removeKeys.Add(key);
            }

            if (removeKeys == null)
            {
                return;
            }

            foreach (string key in removeKeys)
            {
                _objectByKey.Remove(key);
            }
        }

        /// <summary>
        /// 按覆盖链读取配置文本：persistentDataPath/Configs 存在则优先，否则读 StreamingAssets/Configs。
        /// 两层均缺失抛 GameFrameworkException；读取过程中其他错误同样抛异常，由调用方决定跳过或中断。
        /// </summary>
        private static async UniTask<(string text, ConfigRoot root)> ReadConfigTextWithRootAsync(string fileName, CancellationToken cancellationToken)
        {
            string persistentText = await ReadOptionalTextFromRootAsync(ConfigRoot.Persistent, fileName, cancellationToken);
            if (persistentText != null)
            {
                return (persistentText, ConfigRoot.Persistent);
            }

            string streamingText = await ReadRequiredTextFromRootAsync(ConfigRoot.Streaming, fileName, cancellationToken);
            return (streamingText, ConfigRoot.Streaming);
        }

        /// <summary>
        /// 按覆盖链读取配置文本；两层均不存在返回 null，其他读取错误继续抛异常（用于清单探测）。
        /// </summary>
        private static async UniTask<string> ReadOptionalConfigTextAsync(string fileName, CancellationToken cancellationToken)
        {
            string persistentText = await ReadOptionalTextFromRootAsync(ConfigRoot.Persistent, fileName, cancellationToken);
            return persistentText ?? await ReadOptionalTextFromRootAsync(ConfigRoot.Streaming, fileName, cancellationToken);
        }

        /// <summary>
        /// 从指定来源根目录尝试读取文本；文件不存在返回 null，其他读取错误抛异常。
        /// persistentDataPath 永远是本地文件系统路径；StreamingAssets 路径含 "://"（如 Android）时走 UnityWebRequest。
        /// </summary>
        private static async UniTask<string> ReadOptionalTextFromRootAsync(ConfigRoot root, string fileName, CancellationToken cancellationToken)
        {
            string path = GetRootAbsolutePath(root, GetRelativePath(fileName));

            if (path.Contains("://"))
            {
                return await ReadOptionalViaWebRequestAsync(path, cancellationToken);
            }

            await UniTask.SwitchToThreadPool();

            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            finally
            {
                await UniTask.SwitchToMainThread(cancellationToken);
            }
        }

        /// <summary>
        /// 从指定来源根目录读取文本；文件不存在抛 GameFrameworkException。
        /// </summary>
        private static async UniTask<string> ReadRequiredTextFromRootAsync(ConfigRoot root, string fileName, CancellationToken cancellationToken)
        {
            string text = await ReadOptionalTextFromRootAsync(root, fileName, cancellationToken);

            if (text == null)
            {
                throw new GameFrameworkException($"Config file not found in both persistent and streaming: {GetRelativePath(fileName)}");
            }

            return text;
        }

        /// <summary>
        /// 通过 UnityWebRequest 读取可选文本；404/不存在返回 null，其他错误抛异常。
        /// </summary>
        private static async UniTask<string> ReadOptionalViaWebRequestAsync(string path, CancellationToken cancellationToken)
        {
            using UnityWebRequest request = UnityWebRequest.Get(path);
            await request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);

            if (request.result == UnityWebRequest.Result.Success)
            {
                return request.downloadHandler.text;
            }

            if (request.responseCode == 404)
            {
                return null;
            }

            throw new GameFrameworkException($"Read config via web request failed: {path}, error: {request.error}");
        }

        /// <summary>
        /// 拼接配置文件相对 Configs 根目录的路径（Configs/文件名）。
        /// </summary>
        private static string GetRelativePath(string fileName)
        {
            return $"{CONFIG_ROOT}/{fileName}";
        }

        /// <summary>
        /// 获取指定来源根目录下配置文件的绝对路径。
        /// </summary>
        private static string GetRootAbsolutePath(ConfigRoot root, string relativePath)
        {
            string basePath = root == ConfigRoot.Persistent ? Application.persistentDataPath : Application.streamingAssetsPath;
            return Path.Combine(basePath, relativePath).Replace("\\", "/");
        }

        private enum RuntimeConfigFormat
        {
            Json,
            Toml
        }

        private enum ConfigRoot
        {
            Streaming,
            Persistent
        }
    }
}
