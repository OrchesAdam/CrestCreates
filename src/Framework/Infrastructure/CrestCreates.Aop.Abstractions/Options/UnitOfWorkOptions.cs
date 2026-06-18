namespace CrestCreates.Aop.Abstractions.Options;

public class UnitOfWorkOptions
{
    public bool IsTransactional { get; set; } = true;
    public int DefaultTimeout { get; set; } = 30;
    public bool AutoCommit { get; set; } = true;
    public bool AutoRollbackOnException { get; set; } = true;
}
