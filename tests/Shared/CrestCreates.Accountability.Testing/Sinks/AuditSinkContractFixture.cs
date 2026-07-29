namespace CrestCreates.Accountability.Testing.Sinks;

public sealed class AuditSinkContractFixture(IAuditSinkContractDriver driver)
{
    public IAuditSinkContractDriver Driver { get; } = driver;
}
