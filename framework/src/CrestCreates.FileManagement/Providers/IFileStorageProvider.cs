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
        Task<FileInformation> GetFileInfoAsync(string filePath);
    }
    
    /// <summary>
    /// 文件信息
    /// </summary>
    public class FileInformation
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
        public System.DateTime CreatedAt { get; set; }
        
        /// <summary>
        /// 最后修改时间
        /// </summary>
        public System.DateTime LastModified { get; set; }
        
        /// <summary>
        /// 文件类型
        /// </summary>
        public string ContentType { get; set; }
    }
}