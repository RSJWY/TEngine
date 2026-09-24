using Nino.Core;

namespace GameLogic
{
    /// <summary>
    /// 系统设置存档数据。
    /// </summary>
    [NinoType(containNonPublicMembers: true)]
    [ClientSaveData("SystemSaveData")]
    public sealed partial class SystemSaveData : BaseClientSaveData
    {
        /// <summary>
        /// 系统保存数据类型枚举
        /// </summary>
        public enum SaveType
        {
            Max,
        }

        /// <summary>
        /// 系统设置参数数组
        /// </summary>
        public int[] SettingParams { get; internal set; } = new int[(int)SaveType.Max];

        /// <summary>
        /// 获取系统保存数据实例
        /// </summary>
        public static SystemSaveData Get => BaseClientSaveData.Get<SystemSaveData>();
    }
}
