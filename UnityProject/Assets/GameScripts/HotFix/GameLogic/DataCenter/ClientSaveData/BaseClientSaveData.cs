using System;
using System.IO;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Nino.Core;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// 客户端存档存储方式。
    /// </summary>
    public enum ClientSaveDataStorageMode
    {
        /// <summary>
        /// 使用Unity PlayerPrefs存储，适合少量轻量数据。
        /// </summary>
        PlayerPrefs,

        /// <summary>
        /// 使用persistentDataPath下的二进制文件存储（Nino序列化），适合体量更大的客户端数据。
        /// </summary>
        BinaryFile,
    }

    /// <summary>
    /// 客户端存档数据基类。
    /// </summary>
    /// <remarks>
    /// 使用 Nino 二进制序列化，提供双存储后端读写（PlayerPrefs/BinaryFile）、版本升级、坏档备份、
    /// PlayerPrefs→BinaryFile懒迁移、旧版JSON存档一次性迁移到Nino二进制、异步写入。
    /// 子类必须标记 <see cref="NinoTypeAttribute"/>。
    /// </remarks>
    [NinoType(containNonPublicMembers: true)]
    public abstract partial class BaseClientSaveData
    {
        private const string SAVE_DIRECTORY = "ClientSaveData";

        private string m_saveKey;
        private ClientSaveDataStorageMode m_storageMode;

        /// <summary>
        /// JsonFile模式首次找不到文件时，会尝试从同key的PlayerPrefs读取并迁移。
        /// </summary>
        private bool m_needMigratePlayerPrefsToBinary;

        /// <summary>
        /// 存档目录。
        /// </summary>
        public static string SaveDirectoryPath
            => Path.Combine(Application.persistentDataPath, SAVE_DIRECTORY);

        /// <summary>
        /// 当前存档已保存的数据版本。
        /// </summary>
        public int SaveDataVersion { get; internal set; }

        /// <summary>
        /// 当前代码支持的存档版本；子类字段结构变更时递增。
        /// </summary>
        protected virtual int CurrentSaveDataVersion => 1;

        /// <summary>
        /// 初始化保存数据。
        /// </summary>
        /// <param name="saveKey">保存数据的键名</param>
        /// <param name="storageMode">保存数据的存储方式</param>
        public void Init(string saveKey, ClientSaveDataStorageMode storageMode)
        {
            m_saveKey = saveKey;
            m_storageMode = storageMode;
            Load();
        }

        /// <summary>
        /// 加载数据。
        /// </summary>
        /// <remarks>子类可重写实现解密。</remarks>
        protected virtual void Load()
        {
            try
            {
                m_needMigratePlayerPrefsToBinary = false;
                byte[] data = ReadFromStorage();

                if (data != null && data.Length > 0)
                {
                    object obj = this;
                    NinoDeserializer.Deserialize(data, GetType(), ref obj);
                    if (TryUpgradeSaveDataVersion() || m_needMigratePlayerPrefsToBinary)
                    {
                        Save();
                    }
                }
                else
                {
                    SaveDataVersion = CurrentSaveDataVersion;
                }
            }
            catch (Exception e)
            {
                LogStorageError("Load", e, GetLogFilePath());
                BackupCorruptFile();
            }
            finally
            {
                m_needMigratePlayerPrefsToBinary = false;
            }
        }

        /// <summary>
        /// 保存数据到本地存储。
        /// </summary>
        /// <remarks>子类可重写实现加密。</remarks>
        public virtual void Save()
        {
            try
            {
                byte[] data = NinoSerializer.Serialize(this);
                WriteToStorage(data);
            }
            catch (Exception e)
            {
                LogStorageError("Save", e, GetLogFilePath());
            }
        }

        /// <summary>
        /// 异步保存数据到本地存储，BinaryFile模式会切到线程池执行文件写入。
        /// </summary>
        public virtual async UniTask SaveAsync()
        {
            if (m_storageMode == ClientSaveDataStorageMode.PlayerPrefs)
            {
                Save();
                return;
            }

            byte[] data;
            string filePath = GetSaveFilePath();
            try
            {
                data = NinoSerializer.Serialize(this);
            }
            catch (Exception e)
            {
                LogStorageError("Serialize", e, filePath);
                return;
            }

            Exception exception = null;
            await UniTask.SwitchToThreadPool();
            try
            {
                WriteBinaryFile(filePath, data);
            }
            catch (Exception e)
            {
                exception = e;
            }
            finally
            {
                await UniTask.SwitchToMainThread();
            }

            if (exception != null)
            {
                LogStorageError("SaveAsync", exception, filePath);
            }
        }

        /// <summary>
        /// 通过管理器获取指定类型的存档实例。
        /// </summary>
        protected static T Get<T>() where T : BaseClientSaveData, new()
            => ClientSaveDataMgr.Instance.GetSaveData<T>();

        /// <summary>
        /// 存档版本升级入口；子类按oldVersion分段补齐或迁移字段。
        /// </summary>
        /// <param name="oldVersion">旧存档版本，未带版本字段的历史存档为0</param>
        /// <param name="newVersion">当前目标版本</param>
        protected virtual void OnUpgradeData(int oldVersion, int newVersion)
        {
        }

        /// <summary>
        /// 从当前存储后端读取二进制数据。
        /// BinaryFile模式下若文件不存在，会尝试读取旧PlayerPrefs数据用于懒迁移；
        /// 若旧版 JSON 文件存在，会先用 Newtonsoft.Json 读取并转换为 Nino 二进制格式。
        /// </summary>
        protected byte[] ReadFromStorage()
        {
            switch (m_storageMode)
            {
                case ClientSaveDataStorageMode.BinaryFile:
                {
                    string filePath = GetSaveFilePath();
                    if (File.Exists(filePath))
                    {
                        return File.ReadAllBytes(filePath);
                    }

                    // 尝试从旧版 JSON 文件迁移
                    string legacyJsonPath = Utility.Nino.GetLegacyJsonPath(filePath);
                    if (legacyJsonPath != null)
                    {
                        return MigrateFromLegacyJson(legacyJsonPath);
                    }

                    // 尝试从 PlayerPrefs 迁移
                    string playerPrefsBase64 = Utility.PlayerPrefs.GetString(m_saveKey);
                    if (!string.IsNullOrEmpty(playerPrefsBase64))
                    {
                        m_needMigratePlayerPrefsToBinary = true;
                        return Convert.FromBase64String(playerPrefsBase64);
                    }

                    return null;
                }
                case ClientSaveDataStorageMode.PlayerPrefs:
                default:
                {
                    string base64Str = Utility.PlayerPrefs.GetString(m_saveKey);
                    return string.IsNullOrEmpty(base64Str) ? null : Convert.FromBase64String(base64Str);
                }
            }
        }

        /// <summary>
        /// 将二进制数据写入当前存储后端。
        /// </summary>
        protected void WriteToStorage(byte[] data)
        {
            switch (m_storageMode)
            {
                case ClientSaveDataStorageMode.BinaryFile:
                    string filePath = GetSaveFilePath();
                    WriteBinaryFile(filePath, data);
                    break;
                case ClientSaveDataStorageMode.PlayerPrefs:
                default:
                    Utility.PlayerPrefs.SetString(m_saveKey, Convert.ToBase64String(data));
                    break;
            }
        }

        /// <summary>
        /// 获取当前存档对应的二进制文件路径。
        /// </summary>
        protected string GetSaveFilePath()
            => Utility.Nino.GetSaveFilePath(SaveDirectoryPath, GetSafeFileName(m_saveKey));

        /// <summary>
        /// 检查并升级存档版本；升级后由调用方保存当前对象。
        /// </summary>
        private bool TryUpgradeSaveDataVersion()
        {
            int currentVersion = CurrentSaveDataVersion;
            if (SaveDataVersion >= currentVersion)
            {
                return false;
            }

            int oldVersion = SaveDataVersion;
            OnUpgradeData(oldVersion, currentVersion);
            SaveDataVersion = currentVersion;
            return true;
        }

        /// <summary>
        /// 从旧版 JSON 文件读取数据并转换为 Nino 二进制格式。
        /// 迁移成功后删除旧 JSON 文件。
        /// </summary>
        private byte[] MigrateFromLegacyJson(string jsonFilePath)
        {
            try
            {
                string jsonStr = File.ReadAllText(jsonFilePath);
                JsonConvert.PopulateObject(jsonStr, this);

                // 将迁移后的对象序列化为 Nino 二进制
                byte[] data = NinoSerializer.Serialize(this);

                // 删除旧 JSON 文件
                Utility.Nino.DeleteLegacyJson(jsonFilePath);

                Log.Info($"[ClientSaveData] Migrated legacy JSON to Nino binary: {jsonFilePath}");
                return data;
            }
            catch (Exception e)
            {
                LogStorageError("MigrateFromLegacyJson", e, jsonFilePath);
                return null;
            }
        }

        /// <summary>
        /// BinaryFile读取或反序列化失败时备份坏档，避免下次启动继续读取同一个坏文件。
        /// </summary>
        private void BackupCorruptFile()
        {
            if (m_storageMode != ClientSaveDataStorageMode.BinaryFile)
            {
                return;
            }

            string filePath = GetSaveFilePath();
            if (!File.Exists(filePath))
            {
                return;
            }

            try
            {
                string backupFilePath = $"{filePath}.corrupt";
                if (File.Exists(backupFilePath))
                {
                    backupFilePath = $"{filePath}.{DateTime.Now:yyyyMMddHHmmss}.corrupt";
                }

                File.Move(filePath, backupFilePath);
            }
            catch (Exception e)
            {
                LogStorageError("BackupCorruptFile", e, filePath);
            }
        }

        private string GetLogFilePath()
            => m_storageMode == ClientSaveDataStorageMode.BinaryFile ? GetSaveFilePath() : string.Empty;

        private void LogStorageError(string operation, Exception exception, string filePath)
        {
            Log.Error($"[ClientSaveData] {operation} failed, saveKey={m_saveKey}, storageMode={m_storageMode}, filePath={filePath}, error={exception}");
        }

        /// <summary>
        /// 写入二进制文件，写入前确保目录存在。
        /// </summary>
        private static void WriteBinaryFile(string filePath, byte[] data)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(filePath, data);
        }

        private static string GetSafeFileName(string fileName)
        {
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalidChar, '_');
            }

            return fileName;
        }
    }
}
