using Microsoft.AspNetCore.Http;
using System.IO;
using System;
using System.Threading.Tasks;
using CrestCreates.FileManagement.Configuration;
using System.Collections.Generic;

namespace CrestCreates.FileManagement.Providers
{
    /// <summary>
    /// 本地文件系统存储提供者
    /// </summary>
    public class LocalFileSystemProvider : IFileStorageProvider
    {
        private readonly string _rootPath;
        
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="options">本地文件系统配置</param>
        public LocalFileSystemProvider(LocalFileSystemOptions options)
        {
            if (options.UseAbsolutePath)
            {
                _rootPath = options.RootPath;
            }
            else
            {
                _rootPath = Path.Combine(Directory.GetCurrentDirectory(), options.RootPath);
            }
            
            // 确保根目录存在
            if (!Directory.Exists(_rootPath))
            {
                Directory.CreateDirectory(_rootPath);
            }
        }
        
        /// <summary>
        /// 上传文件
        /// </summary>
        /// <param name="file">文件</param>
        /// <param name="directory">目录</param>
        /// <returns>文件路径</returns>
        public async Task<string> UploadFileAsync(IFormFile file, string directory = "")
        {
            var targetDirectory = Path.Combine(_rootPath, directory);
            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }
            
            var fileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
            var filePath = Path.Combine(targetDirectory, fileName);
            
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }
            
            return Path.Combine(directory, fileName).Replace('\\', '/');
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
            var targetDirectory = Path.Combine(_rootPath, directory);
            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }
            
            var safeFileName = $"{Guid.NewGuid()}_{Path.GetFileName(fileName)}";
            var filePath = Path.Combine(targetDirectory, safeFileName);
            
            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await stream.CopyToAsync(fileStream);
            }
            
            return Path.Combine(directory, safeFileName).Replace('\\', '/');
        }
        
        /// <summary>
        /// 下载文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>文件字节数组</returns>
        public async Task<byte[]> DownloadFileAsync(string filePath)
        {
            var fullPath = Path.Combine(_rootPath, filePath);
            return await File.ReadAllBytesAsync(fullPath);
        }
        
        /// <summary>
        /// 下载文件到流
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="stream">目标流</param>
        public async Task DownloadToStreamAsync(string filePath, Stream stream)
        {
            var fullPath = Path.Combine(_rootPath, filePath);
            using (var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read))
            {
                await fileStream.CopyToAsync(stream);
            }
        }
        
        /// <summary>
        /// 删除文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        public Task DeleteFileAsync(string filePath)
        {
            var fullPath = Path.Combine(_rootPath, filePath);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            return Task.CompletedTask;
        }
        
        /// <summary>
        /// 检查文件是否存在
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>是否存在</returns>
        public Task<bool> FileExistsAsync(string filePath)
        {
            var fullPath = Path.Combine(_rootPath, filePath);
            return Task.FromResult(File.Exists(fullPath));
        }
        
        /// <summary>
        /// 获取文件信息
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>文件信息</returns>
        public Task<FileInformation> GetFileInfoAsync(string filePath)
        {
            var fullPath = Path.Combine(_rootPath, filePath);
            var fileInfo = new System.IO.FileInfo(fullPath);
            
            if (!fileInfo.Exists)
            {
                return Task.FromResult<FileInformation>(null);
            }
            
            return Task.FromResult(new FileInformation
            {
                Path = filePath,
                Name = fileInfo.Name,
                Size = fileInfo.Length,
                CreatedAt = fileInfo.CreationTime,
                LastModified = fileInfo.LastWriteTime,
                ContentType = GetContentType(fileInfo.Extension)
            });
        }
        
        /// <summary>
        /// 获取文件内容类型
        /// </summary>
        /// <param name="extension">文件扩展名</param>
        /// <returns>内容类型</returns>
        private string GetContentType(string extension)
        {
            var contentTypeMap = new Dictionary<string, string>
            {
                {".jpg", "image/jpeg"},
                {".jpeg", "image/jpeg"},
                {".png", "image/png"},
                {".gif", "image/gif"},
                {".pdf", "application/pdf"},
                {".doc", "application/msword"},
                {".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"},
                {".xls", "application/vnd.ms-excel"},
                {".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"},
                {".txt", "text/plain"},
                {".html", "text/html"},
                {".css", "text/css"},
                {".js", "application/javascript"}
            };
            
            return contentTypeMap.TryGetValue(extension.ToLower(), out var contentType) ? contentType : "application/octet-stream";
        }
    }
}