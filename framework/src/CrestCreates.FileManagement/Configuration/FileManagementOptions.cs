using System;

namespace CrestCreates.FileManagement.Configuration
{
    /// <summary>
    /// 文件管理配置选项
    /// </summary>
    public class FileManagementOptions
    {
        /// <summary>
        /// 存储提供者类型
        /// </summary>
        public StorageProviderType ProviderType { get; set; } = StorageProviderType.LocalFileSystem;
        
        /// <summary>
        /// 本地文件系统配置
        /// </summary>
        public LocalFileSystemOptions LocalFileSystem { get; set; } = new LocalFileSystemOptions();
        
        /// <summary>
        /// Azure Blob Storage配置
        /// </summary>
        public AzureBlobStorageOptions AzureBlobStorage { get; set; } = new AzureBlobStorageOptions();

        /// <summary>
        /// Amazon S3配置
        /// </summary>
        public S3StorageOptions AmazonS3 { get; set; } = new S3StorageOptions();

        /// <summary>
        /// 文件验证配置
        /// </summary>
        public FileValidationOptions Validation { get; set; } = new FileValidationOptions();
        
        /// <summary>
        /// 文件URL配置
        /// </summary>
        public FileUrlOptions Url { get; set; } = new FileUrlOptions();
    }
    
    /// <summary>
    /// 存储提供者类型
    /// </summary>
    public enum StorageProviderType
    {
        /// <summary>
        /// 本地文件系统
        /// </summary>
        LocalFileSystem,
        
        /// <summary>
        /// Azure Blob Storage
        /// </summary>
        AzureBlobStorage,
        
        /// <summary>
        /// Amazon S3
        /// </summary>
        AmazonS3
    }
    
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