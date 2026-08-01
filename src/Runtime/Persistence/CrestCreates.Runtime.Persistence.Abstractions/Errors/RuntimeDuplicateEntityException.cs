namespace CrestCreates.Runtime.Persistence.Abstractions.Errors;

public sealed class RuntimeDuplicateEntityException : RuntimePersistenceException
{
    public RuntimeDuplicateEntityException(
        RuntimeDuplicateEntityCode code,
        string message)
        : base(message)
    {
        Code = code;
    }

    public RuntimeDuplicateEntityCode Code { get; }
}
