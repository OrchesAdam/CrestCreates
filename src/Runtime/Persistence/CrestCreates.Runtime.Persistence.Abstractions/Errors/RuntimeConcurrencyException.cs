namespace CrestCreates.Runtime.Persistence.Abstractions.Errors;

public sealed class RuntimeConcurrencyException : RuntimePersistenceException
{
    public RuntimeConcurrencyException(string message)
        : base(message)
    {
    }
}
