namespace CrestCreates.FileManagement.Configuration
{
    /// <summary>
    /// 文件URL配置
    /// </summary>
    public class FileUrlOptions
    {
        /// <summary>
        /// 基础URL
        /// </summary>
        public string BaseUrl { get; set; } = "/files";
        
        /// <summary>
        /// 是否使用绝对URL
        /// </summary>
        public bool UseAbsoluteUrl { get; set; } = false;
        
        /// <summary>
        /// 绝对URL前缀
        /// </summary>
        public string AbsoluteUrlPrefix { get; set; } = string.Empty;
    }
}
