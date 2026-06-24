using System;

namespace CrestCreates.FileManagement.Configuration
{
    /// <summary>
    /// 文件验证配置
    /// </summary>
    public class FileValidationOptions
    {
        /// <summary>
        /// 允许的文件扩展名
        /// </summary>
        public string[] AllowedExtensions { get; set; } = Array.Empty<string>();
        
        /// <summary>
        /// 最大文件大小（字节）
        /// </summary>
        public long MaxFileSize { get; set; } = 10485760; // 10MB
        
        /// <summary>
        /// 是否允许覆盖现有文件
        /// </summary>
        public bool AllowOverwrite { get; set; } = false;
    }
}
