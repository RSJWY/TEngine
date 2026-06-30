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
    /// 轻量运行时配置模块。从 StreamingAssets/Configs 读取清单声明的 JSON/TOML 文件并缓存。
    /// 通过 GameModule.Config 访问，DTO 由业务层定义。
    /// </summary>
    internal sealed class RuntimeConfigModule : Module, IRuntimeConfigModule
    {
        /// <summary>
        /// 配置根目录，相对 StreamingAssets（StreamingAssets/Configs）。
        /// </summary>
        private const string CONFIG_ROOT = "Configs";

        /// <summary>
        /// 默认清单文件名，声明需要加载的配置列表。
        /// </summary>
        private const string TOML_MANIFEST_FILE = "config_manifest.toml";

        /// <summary>
        /// 旧清单文件名，仅用于迁移期兼容。
        /// </summary>
        private const string JSON_MANIFEST_FILE = "config_manifest.json";

        /// <summary>
        /// 配置名 -> 原始配置文本缓存。键忽略大小写。
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
        /// 是否已完成加载。
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
        /// 读取清单并加载其中声明的全部配置到文本缓存。
        /// 每次调用前先清空旧缓存；清单为空时仅记录警告并标记加载完成。
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
                    continue;
                }

                string normalizedName = NormalizeConfigName(file);
                if (_textByName.ContainsKey(normalizedName))
                {
                    throw new GameFrameworkException($"Runtime config name is duplicated: {normalizedName}");
                }

                string text = await ReadStreamingAssetsTextAsync(GetRelativePath(file), cancellationToken);
                _textByName[normalizedName] = text;
                _fileByName[normalizedName] = file;
                _formatByName[normalizedName] = GetConfigFormat(file);
            }

            IsLoaded = true;
        }

        /// <summary>
        /// 重新加载指定配置：重新读取文件覆盖文本缓存，并清理该配置的对象缓存。
        /// 若该配置不在文件映射中，则按配置名推断文件名（默认追加 .toml）。
        /// </summary>
        public async UniTask ReloadAsync(string configName, CancellationToken cancellationToken = default)
        {
            string normalizedName = NormalizeConfigName(configName);
            string fileName = _fileByName.TryGetValue(normalizedName, out string mappedFile)
                ? mappedFile
                : NormalizeConfigFileName(configName);

            string text = await ReadStreamingAssetsTextAsync(GetRelativePath(fileName), cancellationToken);
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
        /// 命中对象缓存直接返回，否则按需反序列化并写入对象缓存。
        /// </summary>
        public bool TryGet<T>(out T config, string configName = null) where T : class
        {
            string normalizedName = NormalizeConfigName(configName ?? typeof(T).Name);
            string objectKey = GetObjectKey(normalizedName, typeof(T));

            if (_objectByKey.TryGetValue(objectKey, out object cachedConfig))
            {
                config = cachedConfig as T;
                return config != null;
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
        /// 优先读取 TOML 清单；不存在时回退旧 JSON 清单。
        /// </summary>
        private static async UniTask<RuntimeConfigManifest> LoadManifestAsync(CancellationToken cancellationToken)
        {
            string manifestToml = await ReadOptionalStreamingAssetsTextAsync(GetRelativePath(TOML_MANIFEST_FILE), cancellationToken);
            if (manifestToml != null)
            {
                return Utility.Toml.ToObject<RuntimeConfigManifest>(manifestToml);
            }

            string manifestJson = await ReadStreamingAssetsTextAsync(GetRelativePath(JSON_MANIFEST_FILE), cancellationToken);
            return Utility.Json.ToObject<RuntimeConfigManifest>(manifestJson);
        }

        /// <summary>
        /// 拼接配置文件相对 StreamingAssets 的路径（Configs/文件名）。
        /// </summary>
        private static string GetRelativePath(string fileName)
        {
            return $"{CONFIG_ROOT}/{fileName}";
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
        /// 将配置名规范为缓存键：去扩展名并去空白。空白名抛 GameFrameworkException。
        /// </summary>
        private static string NormalizeConfigName(string configName)
        {
            if (string.IsNullOrWhiteSpace(configName))
            {
                throw new GameFrameworkException("Runtime config name is invalid.");
            }

            string normalizedName = Path.GetFileNameWithoutExtension(configName).Trim();
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
        /// 读取 StreamingAssets 下文本文件。
        /// 路径含 "://"（如 Android/远程）走 UnityWebRequest；否则切到线程池用 File 同步读，读完切回主线程。
        /// </summary>
        private static async UniTask<string> ReadStreamingAssetsTextAsync(string relativePath, CancellationToken cancellationToken)
        {
            string path = Path.Combine(Application.streamingAssetsPath, relativePath).Replace("\\", "/");

            if (path.Contains("://"))
            {
                using UnityWebRequest request = UnityWebRequest.Get(path);
                await request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);

                if (request.result != UnityWebRequest.Result.Success)
                {
                    throw new GameFrameworkException($"Read streaming assets failed: {path}, error: {request.error}");
                }

                return request.downloadHandler.text;
            }

            await UniTask.SwitchToThreadPool();

            try
            {
                return File.ReadAllText(path);
            }
            finally
            {
                await UniTask.SwitchToMainThread(cancellationToken);
            }
        }

        /// <summary>
        /// 尝试读取 StreamingAssets 下文本文件；文件不存在返回 null，其他读取错误继续抛异常。
        /// </summary>
        private static async UniTask<string> ReadOptionalStreamingAssetsTextAsync(string relativePath, CancellationToken cancellationToken)
        {
            string path = Path.Combine(Application.streamingAssetsPath, relativePath).Replace("\\", "/");

            if (path.Contains("://"))
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

                throw new GameFrameworkException($"Read streaming assets failed: {path}, error: {request.error}");
            }

            await UniTask.SwitchToThreadPool();

            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                return File.ReadAllText(path);
            }
            finally
            {
                await UniTask.SwitchToMainThread(cancellationToken);
            }
        }

        private enum RuntimeConfigFormat
        {
            Json,
            Toml
        }
    }
}
