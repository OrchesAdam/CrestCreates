namespace CrestCreates.Aop.Abstractions.Options;

public class AuditOptions
{
    public bool EnableAudit { get; set; } = true;
    public bool IncludeParameters { get; set; } = true;
    public bool IncludeResult { get; set; } = false;
    public bool IncludeException { get; set; } = true;
    public int MaxParameterLength { get; set; } = 1000;
    public int MaxResultLength { get; set; } = 1000;
}
