namespace CrestCreates.Aop.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class AuditedAttribute : Attribute
{
    public string? ActionName { get; set; }
    public bool IncludeParameters { get; set; } = true;
    public bool IncludeResult { get; set; } = false;
}
