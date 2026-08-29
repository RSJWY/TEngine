namespace TEngine
{
    /// <summary>
    /// 资源模块的加密类型枚举。
    /// <remarks>用于定义资源加载时的不同加密方式。</remarks>
    /// </summary>
    public enum EncryptionType
    {
        /// <summary>
        /// 无加密。
        /// <remarks>资源将以原始形式加载，不进行任何加密处理。</remarks>
        /// </summary>
        None,

        /// <summary>
        /// 文件偏移加密。
        /// <remarks>通过在文件开头添加偏移量来隐藏真实文件内容的加密方式。</remarks>
        /// </summary>
        FileOffSet,

        /// <summary>
        /// 文件流加密。
        /// <remarks>使用加密流对文件内容进行加密处理的加密方式。</remarks>
        /// </summary>
        FileStream,

        /// <summary>
        /// ChaCha20 流密码加密（RFC 7539）。
        /// <remarks>本地文件系统流式解密，Web 文件系统内存式解密。</remarks>
        /// </summary>
        ChaCha20,
    }
}