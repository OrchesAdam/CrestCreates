namespace CrestCreates.Aop.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class UnitOfWorkAttribute : Attribute
{
    public bool IsTransactional { get; set; } = true;
    public int Timeout { get; set; } = 30;
    public bool RequiresNew { get; set; } = false;
}
