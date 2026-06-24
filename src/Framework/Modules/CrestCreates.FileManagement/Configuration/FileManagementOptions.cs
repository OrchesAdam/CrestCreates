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
}
