using Microsoft.AspNetCore.Http;
using System.IO;
using System.Threading.Tasks;
using CrestCreates.FileManagement.Providers;
using CrestCreates.FileManagement.Configuration;

namespace CrestCreates.FileManagement.Services
{
    /// <summary>
    /// 文件管理服务
    /// </summary>
    public class FileManagementService : IFileManagementService
    {
        private readonly IFileStorageProvider _storageProvider;
        private readonly FileValidationOptions _validationOptions;
        private readonly FileUrlOptions _urlOptions;
        
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="storageProvider">存储提供者</param>
        /// <param name="validationOptions">验证配置</param>
        /// <param name="urlOptions">URL配置</param>
        public FileManagementService(
            IFileStorageProvider storageProvider,
            FileValidationOptions validationOptions,
            FileUrlOptions urlOptions)
        {
            _storageProvider = storageProvider;
            _validationOptions = validationOptions;
            _urlOptions = urlOptions;
        }
        
        /// <summary>
        /// 上传文件
        /// </summary>
        /// <param name="file">文件</param>
        /// <param name="directory">目录</param>
        /// <returns>文件路径</returns>
        public async Task<string> UploadFileAsync(IFormFile file, string directory = "")
        {
            // 验证文件
            if (!ValidateFile(file))
            {
                throw new System.InvalidOperationException("File validation failed");
            }
            
            return await _storageProvider.UploadFileAsync(file, directory);
        }
        
        /// <summary>
        /// 上传文件流
        /// </summary>
        /// <param name="stream">文件流</param>
        /// <param name="fileName">文件名</param>
        /// <param name="directory">目录</param>
        /// <returns>文件路径</returns>
        public async Task<string> UploadStreamAsync(Stream stream, string fileName, string directory = "")
        {
            // 验证文件名
            if (!ValidateFileName(fileName))
            {
                throw new System.InvalidOperationException("File name validation failed");
            }
            
            return await _storageProvider.UploadStreamAsync(stream, fileName, directory);
        }
        
        /// <summary>
        /// 下载文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>文件字节数组</returns>
        public Task<byte[]> DownloadFileAsync(string filePath)
        {
            return _storageProvider.DownloadFileAsync(filePath);
        }
        
        /// <summary>
        /// 下载文件到流
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="stream">目标流</param>
        public Task DownloadToStreamAsync(string filePath, Stream stream)
        {
            return _storageProvider.DownloadToStreamAsync(filePath, stream);
        }
        
        /// <summary>
        /// 删除文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        public Task DeleteFileAsync(string filePath)
        {
            return _storageProvider.DeleteFileAsync(filePath);
        }
        
        /// <summary>
        /// 检查文件是否存在
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>是否存在</returns>
        public Task<bool> FileExistsAsync(string filePath)
        {
            return _storageProvider.FileExistsAsync(filePath);
        }
        
        /// <summary>
        /// 获取文件信息
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>文件信息</returns>
        public Task<FileInformation> GetFileInfoAsync(string filePath)
        {
            return _storageProvider.GetFileInfoAsync(filePath);
        }
        
        /// <summary>
        /// 获取文件URL
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>文件URL</returns>
        public Task<string> GetFileUrlAsync(string filePath)
        {
            if (_urlOptions.UseAbsoluteUrl && !string.IsNullOrEmpty(_urlOptions.AbsoluteUrlPrefix))
            {
                return Task.FromResult($"{_urlOptions.AbsoluteUrlPrefix}{_urlOptions.BaseUrl}/{filePath}".Replace("//", "/"));
            }
            
            return Task.FromResult($"{_urlOptions.BaseUrl}/{filePath}".Replace("//", "/"));
        }
        
        /// <summary>
        /// 验证文件
        /// </summary>
        /// <param name="file">文件</param>
        /// <returns>验证结果</returns>
        public bool ValidateFile(IFormFile file)
        {
            // 检查文件大小
            if (file.Length > _validationOptions.MaxFileSize)
            {
                return false;
            }
            
            // 检查文件扩展名
            if (_validationOptions.AllowedExtensions.Length > 0)
            {
                var extension = Path.GetExtension(file.FileName).ToLower();
                if (!System.Array.Exists(_validationOptions.AllowedExtensions, ext => ext.Equals(extension)))
                {
                    return false;
                }
            }
            
            return true;
        }
        
        /// <summary>
        /// 验证文件名
        /// </summary>
        /// <param name="fileName">文件名</param>
        /// <returns>验证结果</returns>
        private bool ValidateFileName(string fileName)
        {
            if (_validationOptions.AllowedExtensions.Length > 0)
            {
                var extension = Path.GetExtension(fileName).ToLower();
                if (!System.Array.Exists(_validationOptions.AllowedExtensions, ext => ext.Equals(extension)))
                {
                    return false;
                }
            }
            
            return true;
        }
    }
}