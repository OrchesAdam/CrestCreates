namespace CrestCreates.Runtime.Persistence.Abstractions.Errors;

public sealed class RuntimePersistenceUnavailableException : RuntimePersistenceException
{
    public RuntimePersistenceUnavailableException(string message)
        : base(message)
    {
    }

    public RuntimePersistenceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
