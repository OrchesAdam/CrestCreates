namespace CrestCreates.FileManagement.Configuration
{
    /// <summary>
    /// 本地文件系统配置
    /// </summary>
    public class LocalFileSystemOptions
    {
        /// <summary>
        /// 根目录路径
        /// </summary>
        public string RootPath { get; set; } = "wwwroot/files";
        
        /// <summary>
        /// 是否使用绝对路径
        /// </summary>
        public bool UseAbsolutePath { get; set; } = false;
    }
}
