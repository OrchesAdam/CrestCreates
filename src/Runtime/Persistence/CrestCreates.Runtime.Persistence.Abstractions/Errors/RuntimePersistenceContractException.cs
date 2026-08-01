namespace CrestCreates.Runtime.Persistence.Abstractions.Errors;

public sealed class RuntimePersistenceContractException : RuntimePersistenceException
{
    public RuntimePersistenceContractException(
        RuntimePersistenceContractErrorCode code,
        string message)
        : base(message)
    {
        Code = code;
    }

    public RuntimePersistenceContractErrorCode Code { get; }
}
