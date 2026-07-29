namespace CrestCreates.Accountability.Testing.Sinks;

public sealed class AuditSinkContractAssertionException : Exception
{
    public AuditSinkContractAssertionException(string message) : base(message)
    {
    }
}
