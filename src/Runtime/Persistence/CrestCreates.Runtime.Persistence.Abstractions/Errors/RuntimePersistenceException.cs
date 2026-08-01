namespace CrestCreates.Runtime.Persistence.Abstractions.Errors;

public abstract class RuntimePersistenceException : Exception
{
    protected RuntimePersistenceException(string message)
        : base(message)
    {
    }

    protected RuntimePersistenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
