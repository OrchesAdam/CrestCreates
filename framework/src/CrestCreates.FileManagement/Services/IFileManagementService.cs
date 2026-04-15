using Microsoft.AspNetCore.Http;
using System.IO;
using System.Threading.Tasks;
using CrestCreates.FileManagement.Models;

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
        Task<FileInformation> GetFileInfoAsync(string filePath);
        
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