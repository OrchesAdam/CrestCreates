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

    public List<string> SensitivePropertyNames { get; set; } = new()
    {
        "password",
        "pwd",
        "token",
        "access_token",
        "refresh_token",
        "secret",
        "client_secret"
    };
}
