# FileManagementModule 设计方案

## 当前实现分析

### 现有代码结构

```
CrestCreates.FileManagement/
├── Modules/
│   └── FileManagementModule.cs     # 文件管理模块
├── Services/
│   ├── IFileManagementService.cs   # 文件管理服务接口
│   └── FileManagementService.cs    # 文件管理服务实现
└── CrestCreates.FileManagement.csproj
```

### 当前实现的问题

1. **配置硬编码**：文件存储路径硬编码在代码中
2. **缺少配置系统集成**：无法从外部配置文件读取配置
3. **方法签名不正确**：使用了过时的 `ConfigureServices` 和 `Configure` 方法
4. **功能单一**：只支持本地文件系统存储
5. **缺少错误处理**：没有完善的错误处理机制
6. **缺少文件安全**：没有文件类型验证、大小限制等
7. **缺少存储提供者抽象**：无法切换不同的存储后端
8. **缺少依赖注入**：构造函数没有使用依赖注入
9. **路径处理不一致**：混合使用绝对路径和相对路径
10. **缺少文件元数据**：没有文件元数据管理

## 改进设计方案

### 1. 配置系统设计

#### 文件管理配置类

```csharp
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
```

### 2. 存储提供者抽象

#### 存储提供者接口

```csharp
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Threading.Tasks;

namespace CrestCreates.FileManagement.Providers
{
    /// <summary>
    /// 文件存储提供者接口
    /// </summary>
    public interface IFileStorageProvider
    {
        /// <summary>
        /// 上传文件
        /// </summary>
        /// <param name="file">文件</param>
        /// <param name="directory">目录</param>
        /// <returns>文件路径</returns>
        Task<string> UploadFileAsync(IFormFile file, string directory = "");
        
        /// <summary>
        /// 上传文件流
        /// </summary>
        /// <param name="stream">文件流</param>
        /// <param name="fileName">文件名</param>
        /// <param name="directory">目录</param>
        /// <returns>文件路径</returns>
        Task<string> UploadStreamAsync(Stream stream, string fileName, string directory = "");
        
        /// <summary>
        /// 下载文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>文件字节数组</returns>
        Task<byte[]> DownloadFileAsync(string filePath);
        
        /// <summary>
        /// 下载文件到流
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="stream">目标流</param>
        Task DownloadToStreamAsync(string filePath, Stream stream);
        
        /// <summary>
        /// 删除文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        Task DeleteFileAsync(string filePath);
        
        /// <summary>
        /// 检查文件是否存在
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>是否存在</returns>
        Task<bool> FileExistsAsync(string filePath);
        
        /// <summary>
        /// 获取文件信息
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>文件信息</returns>
        Task<FileInfo> GetFileInfoAsync(string filePath);
    }
    
    /// <summary>
    /// 文件信息
    /// </summary>
    public class FileInfo
    {
        /// <summary>
        /// 文件路径
        /// </summary>
        public string Path { get; set; }
        
        /// <summary>
        /// 文件名
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// 文件大小（字节）
        /// </summary>
        public long Size { get; set; }
        
        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }
        
        /// <summary>
        /// 最后修改时间
        /// </summary>
        public DateTime LastModified { get; set; }
        
        /// <summary>
        /// 文件类型
        /// </summary>
        public string ContentType { get; set; }
    }
}
```

#### 本地文件系统存储提供者

```csharp
using Microsoft.AspNetCore.Http;
using System.IO;
using System;
using System.Threading.Tasks;
using CrestCreates.FileManagement.Configuration;

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
        
        public async Task<byte[]> DownloadFileAsync(string filePath)
        {
            var fullPath = Path.Combine(_rootPath, filePath);
            return await File.ReadAllBytesAsync(fullPath);
        }
        
        public async Task DownloadToStreamAsync(string filePath, Stream stream)
        {
            var fullPath = Path.Combine(_rootPath, filePath);
            using (var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read))
            {
                await fileStream.CopyToAsync(stream);
            }
        }
        
        public Task DeleteFileAsync(string filePath)
        {
            var fullPath = Path.Combine(_rootPath, filePath);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            return Task.CompletedTask;
        }
        
        public Task<bool> FileExistsAsync(string filePath)
        {
            var fullPath = Path.Combine(_rootPath, filePath);
            return Task.FromResult(File.Exists(fullPath));
        }
        
        public Task<FileInfo> GetFileInfoAsync(string filePath)
        {
            var fullPath = Path.Combine(_rootPath, filePath);
            var fileInfo = new System.IO.FileInfo(fullPath);
            
            if (!fileInfo.Exists)
            {
                return Task.FromResult<FileInfo>(null);
            }
            
            return Task.FromResult(new FileInfo
            {
                Path = filePath,
                Name = fileInfo.Name,
                Size = fileInfo.Length,
                CreatedAt = fileInfo.CreationTime,
                LastModified = fileInfo.LastWriteTime,
                ContentType = GetContentType(fileInfo.Extension)
            });
        }
        
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
```

### 3. 增强文件管理服务

#### 文件管理服务接口

```csharp
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Threading.Tasks;
using CrestCreates.FileManagement.Providers;

namespace CrestCreates.FileManagement.Services
{
    /// <summary>
    /// 文件管理服务接口
    /// </summary>
    public interface IFileManagementService
    {
        /// <summary>
        /// 上传文件
        /// </summary>
        /// <param name="file">文件</param>
        /// <param name="directory">目录</param>
        /// <returns>文件路径</returns>
        Task<string> UploadFileAsync(IFormFile file, string directory = "");
        
        /// <summary>
        /// 上传文件流
        /// </summary>
        /// <param name="stream">文件流</param>
        /// <param name="fileName">文件名</param>
        /// <param name="directory">目录</param>
        /// <returns>文件路径</returns>
        Task<string> UploadStreamAsync(Stream stream, string fileName, string directory = "");
        
        /// <summary>
        /// 下载文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>文件字节数组</returns>
        Task<byte[]> DownloadFileAsync(string filePath);
        
        /// <summary>
        /// 下载文件到流
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="stream">目标流</param>
        Task DownloadToStreamAsync(string filePath, Stream stream);
        
        /// <summary>
        /// 删除文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        Task DeleteFileAsync(string filePath);
        
        /// <summary>
        /// 检查文件是否存在
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>是否存在</returns>
        Task<bool> FileExistsAsync(string filePath);
        
        /// <summary>
        /// 获取文件信息
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>文件信息</returns>
        Task<FileInfo> GetFileInfoAsync(string filePath);
        
        /// <summary>
        /// 获取文件URL
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>文件URL</returns>
        Task<string> GetFileUrlAsync(string filePath);
        
        /// <summary>
        /// 验证文件
        /// </summary>
        /// <param name="file">文件</param>
        /// <returns>验证结果</returns>
        bool ValidateFile(IFormFile file);
    }
}
```

#### 文件管理服务实现

```csharp
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
        
        public async Task<string> UploadFileAsync(IFormFile file, string directory = "")
        {
            // 验证文件
            if (!ValidateFile(file))
            {
                throw new InvalidOperationException("File validation failed");
            }
            
            return await _storageProvider.UploadFileAsync(file, directory);
        }
        
        public async Task<string> UploadStreamAsync(Stream stream, string fileName, string directory = "")
        {
            // 验证文件名
            if (!ValidateFileName(fileName))
            {
                throw new InvalidOperationException("File name validation failed");
            }
            
            return await _storageProvider.UploadStreamAsync(stream, fileName, directory);
        }
        
        public Task<byte[]> DownloadFileAsync(string filePath)
        {
            return _storageProvider.DownloadFileAsync(filePath);
        }
        
        public Task DownloadToStreamAsync(string filePath, Stream stream)
        {
            return _storageProvider.DownloadToStreamAsync(filePath, stream);
        }
        
        public Task DeleteFileAsync(string filePath)
        {
            return _storageProvider.DeleteFileAsync(filePath);
        }
        
        public Task<bool> FileExistsAsync(string filePath)
        {
            return _storageProvider.FileExistsAsync(filePath);
        }
        
        public Task<FileInfo> GetFileInfoAsync(string filePath)
        {
            return _storageProvider.GetFileInfoAsync(filePath);
        }
        
        public Task<string> GetFileUrlAsync(string filePath)
        {
            if (_urlOptions.UseAbsoluteUrl && !string.IsNullOrEmpty(_urlOptions.AbsoluteUrlPrefix))
            {
                return Task.FromResult($"{_urlOptions.AbsoluteUrlPrefix}{_urlOptions.BaseUrl}/{filePath}".Replace("//", "/"));
            }
            
            return Task.FromResult($"{_urlOptions.BaseUrl}/{filePath}".Replace("//", "/"));
        }
        
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
                if (!_validationOptions.AllowedExtensions.Contains(extension))
                {
                    return false;
                }
            }
            
            return true;
        }
        
        private bool ValidateFileName(string fileName)
        {
            if (_validationOptions.AllowedExtensions.Length > 0)
            {
                var extension = Path.GetExtension(fileName).ToLower();
                if (!_validationOptions.AllowedExtensions.Contains(extension))
                {
                    return false;
                }
            }
            
            return true;
        }
    }
}
```

### 4. 改进 FileManagementModule

```csharp
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using CrestCreates.FileManagement.Services;
using CrestCreates.FileManagement.Providers;
using CrestCreates.FileManagement.Configuration;

namespace CrestCreates.FileManagement.Modules;

/// <summary>
/// 文件管理模块
/// </summary>
public class FileManagementModule : ModuleBase
{
    private readonly FileManagementOptions _options;
    
    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="configuration">配置对象</param>
    public FileManagementModule(IConfiguration configuration)
    {
        // 从配置系统读取文件管理配置
        _options = new FileManagementOptions();
        configuration.GetSection("FileManagement").Bind(_options);
    }
    
    /// <summary>
    /// 配置服务
    /// </summary>
    /// <param name="services">服务集合</param>
    public override void OnConfigureServices(IServiceCollection services)
    {
        base.OnConfigureServices(services);
        
        // 注册配置
        services.AddSingleton(_options);
        services.AddSingleton(_options.LocalFileSystem);
        services.AddSingleton(_options.Validation);
        services.AddSingleton(_options.Url);
        
        // 注册存储提供者
        switch (_options.ProviderType)
        {
            case StorageProviderType.LocalFileSystem:
                services.AddSingleton<IFileStorageProvider, LocalFileSystemProvider>();
                break;
            // 可以添加其他存储提供者的注册
            // case StorageProviderType.AzureBlobStorage:
            //     services.AddSingleton<IFileStorageProvider, AzureBlobStorageProvider>();
            //     break;
            // case StorageProviderType.AmazonS3:
            //     services.AddSingleton<IFileStorageProvider, AmazonS3Provider>();
            //     break;
        }
        
        // 注册文件管理服务
        services.AddSingleton<IFileManagementService, FileManagementService>();
    }
}
```

### 5. 配置文件示例

```json
{
  "FileManagement": {
    "ProviderType": "LocalFileSystem",
    "LocalFileSystem": {
      "RootPath": "wwwroot/files",
      "UseAbsolutePath": false
    },
    "Validation": {
      "AllowedExtensions": [".jpg", ".jpeg", ".png", ".gif", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt"],
      "MaxFileSize": 10485760,
      "AllowOverwrite": false
    },
    "Url": {
      "BaseUrl": "/files",
      "UseAbsoluteUrl": false,
      "AbsoluteUrlPrefix": "https://example.com"
    }
  }
}
```

## 改进的优势

1. **配置灵活性**：通过配置文件可以轻松调整文件管理设置
2. **存储提供者抽象**：支持多种存储后端，易于扩展
3. **标准生命周期**：使用 `ModuleBase` 的标准方法，符合框架设计规范
4. **依赖注入**：充分利用依赖注入，便于测试和扩展
5. **文件安全**：添加了文件类型和大小验证
6. **功能增强**：支持更多文件操作，如流上传、文件信息获取等
7. **错误处理**：更好的错误处理机制
8. **路径处理**：统一的路径处理方式
9. **可扩展性**：易于添加新的存储提供者
10. **可维护性**：代码结构清晰，易于维护

## 实现建议

1. **添加更多存储提供者**：如 Azure Blob Storage、Amazon S3 等
2. **添加文件缓存**：提高文件访问性能
3. **添加文件版本控制**：支持文件版本管理
4. **添加文件压缩**：支持文件压缩存储
5. **添加文件加密**：支持敏感文件加密
6. **添加文件访问控制**：基于权限的文件访问控制
7. **添加文件上传进度**：支持大文件上传进度
8. **添加文件批量操作**：支持批量上传、下载、删除
9. **添加文件元数据管理**：支持自定义文件元数据
10. **添加文件搜索**：支持基于元数据的文件搜索

通过这种设计，FileManagementModule 将成为一个功能强大、灵活可扩展的文件管理系统，能够满足各种文件存储和管理需求。