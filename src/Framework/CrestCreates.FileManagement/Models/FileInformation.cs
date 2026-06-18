namespace CrestCreates.FileManagement.Models;

/// <summary>
/// 文件信息
/// </summary>
public class FileInformation
{
    /// <summary>
    /// 文件路径
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// 文件名
    /// </summary>
    public string Name { get; set; } = string.Empty;

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
    public string ContentType { get; set; } = string.Empty;
}