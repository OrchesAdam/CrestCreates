using System.Collections.Generic;

namespace CrestCreates.AuditLogging.Options;

public class AuditLoggingOptions
{
    public const string SectionName = "AuditLogging";

    public bool IsEnabled { get; set; } = true;

    public bool IsEnabledForGetRequests { get; set; }

    public bool AlwaysLogOnException { get; set; } = true;

    public bool HideErrors { get; set; } = true;

    public bool IncludeRequestBody { get; set; } = true;

    public bool IncludeResponseBody { get; set; }

    public int MaxRequestBodyLength { get; set; } = 2048;

    public int MaxResponseBodyLength { get; set; } = 2048;

    public List<string> IgnoredUrls { get; set; } = new();

    /// <summary>
    /// 敏感属性名列表（不区分大小写匹配）
    /// </summary>
    public List<string> SensitivePropertyNames { get; set; } = new()
    {
        "password",
        "newPassword",
        "currentPassword",
        "pwd",
        "token",
        "refreshToken",
        "accessToken",
        "secret",
        "secretKey",
        "connectionString",
        "client_secret"
    };
}
