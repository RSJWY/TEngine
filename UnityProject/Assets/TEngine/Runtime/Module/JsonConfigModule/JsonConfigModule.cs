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
        private const string CONFIG_ROOT = "Configs";
        private const string MANIFEST_FILE = "config_manifest.json";

        private readonly Dictionary<string, string> _jsonByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _fileByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, object> _objectByKey = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public bool IsLoaded { get; private set; }

        public override void OnInit()
        {
        }

        public override void Shutdown()
        {
            Clear();
        }

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

        public T Get<T>(string configName = null) where T : class
        {
            string normalizedName = NormalizeConfigName(configName ?? typeof(T).Name);

            if (!TryGet<T>(out T config, normalizedName))
            {
                throw new GameFrameworkException($"Json config not found or parse failed: {normalizedName}, type: {typeof(T).FullName}");
            }

            return config;
        }

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

        public string GetJson(string configName)
        {
            string normalizedName = NormalizeConfigName(configName);

            if (!TryGetJson(normalizedName, out string json))
            {
                throw new GameFrameworkException($"Json config not found: {normalizedName}");
            }

            return json;
        }

        public bool TryGetJson(string configName, out string json)
        {
            return _jsonByName.TryGetValue(NormalizeConfigName(configName), out json);
        }

        public bool Contains(string configName)
        {
            return _jsonByName.ContainsKey(NormalizeConfigName(configName));
        }

        public void Clear()
        {
            _jsonByName.Clear();
            _fileByName.Clear();
            _objectByKey.Clear();
            IsLoaded = false;
        }

        private static string GetRelativePath(string fileName)
        {
            return $"{CONFIG_ROOT}/{fileName}";
        }

        private static string NormalizeConfigFileName(string configName)
        {
            string normalizedFile = configName.Trim().Replace("\\", "/");
            return normalizedFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? normalizedFile
                : normalizedFile + ".json";
        }

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

        private static string GetObjectKey(string configName, Type type)
        {
            return $"{configName}:{type.FullName}";
        }

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
