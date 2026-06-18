namespace CrestCreates.MultiTenancy.Abstract;

public class TenantResolutionResult
{
    public bool IsResolved { get; set; }
    public string? TenantId { get; set; }
    public string? TenantName { get; set; }
    public string? ResolvedBy { get; set; }
    public string? ConnectionString { get; set; }
    public TenantResolutionError? Error { get; set; }

    public static TenantResolutionResult Success(string tenantId, string tenantName, string? connectionString, string resolvedBy) => new()
    {
        IsResolved = true,
        TenantId = tenantId,
        TenantName = tenantName,
        ConnectionString = connectionString,
        ResolvedBy = resolvedBy
    };

    public static TenantResolutionResult NotFound(string resolvedBy) => new()
    {
        IsResolved = false,
        Error = new TenantResolutionError { Code = "TENANT_NOT_FOUND", Message = "租户未找到" },
        ResolvedBy = resolvedBy
    };

    public static TenantResolutionResult Inactive(string resolvedBy) => new()
    {
        IsResolved = false,
        Error = new TenantResolutionError { Code = "TENANT_INACTIVE", Message = "租户已停用" },
        ResolvedBy = resolvedBy
    };

    public static TenantResolutionResult NotResolved(string resolvedBy) => new()
    {
        IsResolved = false,
        Error = new TenantResolutionError { Code = "TENANT_NOT_RESOLVED", Message = "无法解析租户" },
        ResolvedBy = resolvedBy
    };
}

public class TenantResolutionError
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
