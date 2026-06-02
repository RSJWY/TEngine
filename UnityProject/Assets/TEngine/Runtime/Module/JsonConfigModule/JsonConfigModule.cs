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
    /// 轻量 JSON 配置模块。从 StreamingAssets/Configs 读取 manifest 声明的 JSON 文件并缓存。
    /// 通过 GameModule.JsonConfig 访问，DTO 由业务层定义。
    /// </summary>
    internal sealed class JsonConfigModule : Module, IJsonConfigModule
    {
        /// <summary>
        /// 配置根目录，相对 StreamingAssets（StreamingAssets/Configs）。
        /// </summary>
        private const string CONFIG_ROOT = "Configs";

        /// <summary>
        /// 清单文件名，声明需要加载的 JSON 配置列表。
        /// </summary>
        private const string MANIFEST_FILE = "config_manifest.json";

        /// <summary>
        /// 配置名 -> 原始 JSON 文本缓存。键忽略大小写。
        /// </summary>
        private readonly Dictionary<string, string> _jsonByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 配置名 -> 原始文件名缓存，用于 Reload 时定位回源文件。键忽略大小写。
        /// </summary>
        private readonly Dictionary<string, string> _fileByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
        /// 读取 manifest 并加载其中声明的全部 JSON 配置到文本缓存。
        /// 每次调用前先清空旧缓存；manifest 为空时仅记录警告并标记加载完成。
        /// </summary>
        public async UniTask LoadAllAsync(CancellationToken cancellationToken = default)
        {
            Clear();

            string manifestJson = await ReadStreamingAssetsTextAsync(GetRelativePath(MANIFEST_FILE), cancellationToken);
            JsonConfigManifest manifest = Utility.Json.ToObject<JsonConfigManifest>(manifestJson);

            if (manifest?.files == null || manifest.files.Count == 0)
            {
                Log.Warning("Json config manifest is empty.");
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
                string json = await ReadStreamingAssetsTextAsync(GetRelativePath(file), cancellationToken);
                _jsonByName[normalizedName] = json;
                _fileByName[normalizedName] = file;
            }

            IsLoaded = true;
        }

        /// <summary>
        /// 重新加载指定配置：重新读取文件覆盖文本缓存，并清理该配置的对象缓存。
        /// 若该配置不在文件映射中，则按配置名推断文件名（追加 .json）。
        /// </summary>
        public async UniTask ReloadAsync(string configName, CancellationToken cancellationToken = default)
        {
            string normalizedName = NormalizeConfigName(configName);
            string fileName = _fileByName.TryGetValue(normalizedName, out string mappedFile)
                ? mappedFile
                : NormalizeConfigFileName(configName);

            string json = await ReadStreamingAssetsTextAsync(GetRelativePath(fileName), cancellationToken);
            _jsonByName[normalizedName] = json;
            _fileByName[normalizedName] = fileName;
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
                throw new GameFrameworkException($"Json config not found or parse failed: {normalizedName}, type: {typeof(T).FullName}");
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

            if (!_jsonByName.TryGetValue(normalizedName, out string json))
            {
                config = null;
                return false;
            }

            config = Utility.Json.ToObject<T>(json);
            if (config == null)
            {
                return false;
            }

            _objectByKey[objectKey] = config;
            return true;
        }

        /// <summary>
        /// 获取原始 JSON 文本；未找到抛 GameFrameworkException。
        /// </summary>
        public string GetJson(string configName)
        {
            string normalizedName = NormalizeConfigName(configName);

            if (!TryGetJson(normalizedName, out string json))
            {
                throw new GameFrameworkException($"Json config not found: {normalizedName}");
            }

            return json;
        }

        /// <summary>
        /// 尝试获取原始 JSON 文本；未找到返回 false。
        /// </summary>
        public bool TryGetJson(string configName, out string json)
        {
            return _jsonByName.TryGetValue(NormalizeConfigName(configName), out json);
        }

        /// <summary>
        /// 是否包含指定配置（按文本缓存判断）。
        /// </summary>
        public bool Contains(string configName)
        {
            return _jsonByName.ContainsKey(NormalizeConfigName(configName));
        }

        /// <summary>
        /// 清空文本缓存、文件映射与对象缓存，并重置加载标记。
        /// </summary>
        public void Clear()
        {
            _jsonByName.Clear();
            _fileByName.Clear();
            _objectByKey.Clear();
            IsLoaded = false;
        }

        /// <summary>
        /// 拼接配置文件相对 StreamingAssets 的路径（Configs/文件名）。
        /// </summary>
        private static string GetRelativePath(string fileName)
        {
            return $"{CONFIG_ROOT}/{fileName}";
        }

        /// <summary>
        /// 将配置名规范为文件名：统一分隔符并确保以 .json 结尾。
        /// </summary>
        private static string NormalizeConfigFileName(string configName)
        {
            string normalizedFile = configName.Trim().Replace("\\", "/");
            return normalizedFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? normalizedFile
                : normalizedFile + ".json";
        }

        /// <summary>
        /// 将配置名规范为缓存键：去扩展名并去空白。空白名抛 GameFrameworkException。
        /// </summary>
        private static string NormalizeConfigName(string configName)
        {
            if (string.IsNullOrWhiteSpace(configName))
            {
                throw new GameFrameworkException("Json config name is invalid.");
            }

            string normalizedName = Path.GetFileNameWithoutExtension(configName).Trim();
            if (string.IsNullOrEmpty(normalizedName))
            {
                throw new GameFrameworkException($"Json config name is invalid: {configName}");
            }

            return normalizedName;
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
    }
}
