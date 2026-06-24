namespace CrestCreates.FileManagement.Configuration
{
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
}
