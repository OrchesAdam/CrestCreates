namespace CrestCreates.Runtime.Persistence.Abstractions.Errors;

public sealed class RuntimePersistenceUnavailableException : RuntimePersistenceException
{
    public RuntimePersistenceUnavailableException(string message)
        : base(message)
    {
    }
}
