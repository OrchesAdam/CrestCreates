namespace CrestCreates.Application.Permissions;

public class PermissionSyncOptions
{
    public const string SectionName = "PermissionSync";

    /// <summary>
    /// 是否启用权限同步
    /// </summary>
    public bool EnableSync { get; set; } = true;

    /// <summary>
    /// 清单文件路径
    /// </summary>
    public string ManifestPath { get; set; } = "EntityPermissionsManifest.json";

    /// <summary>
    /// 授权中心端点URL
    /// </summary>
    public string? AuthorizationCenterUrl { get; set; }

    /// <summary>
    /// 授权中心API密钥
    /// </summary>
    public string? AuthorizationCenterApiKey { get; set; }

    /// <summary>
    /// 是否同步到数据库
    /// </summary>
    public bool SyncToDatabase { get; set; } = true;

    /// <summary>
    /// 是否同步到授权中心
    /// </summary>
    public bool SyncToAuthorizationCenter { get; set; } = false;
}
