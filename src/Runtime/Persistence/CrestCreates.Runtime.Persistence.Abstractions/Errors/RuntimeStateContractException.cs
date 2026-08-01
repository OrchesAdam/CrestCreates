namespace CrestCreates.Runtime.Persistence.Abstractions.Errors;

public sealed class RuntimeStateContractException : RuntimePersistenceException
{
    public RuntimeStateContractException(string message)
        : base(message)
    {
    }
}
