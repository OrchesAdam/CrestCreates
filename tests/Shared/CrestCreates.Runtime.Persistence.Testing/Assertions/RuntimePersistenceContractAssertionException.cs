namespace CrestCreates.Runtime.Persistence.Testing.Assertions;

public sealed class RuntimePersistenceContractAssertionException : Exception
{
    public RuntimePersistenceContractAssertionException(string message)
        : base(message)
    {
    }
}
