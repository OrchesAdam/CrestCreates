using CrestCreates.Runtime.Persistence.Abstractions.Errors;

namespace CrestCreates.Runtime.Persistence.Abstractions.Transactions;

public sealed class RuntimeTransactionCommitUnknownException : RuntimePersistenceException
{
    public RuntimeTransactionCommitUnknownException(string message)
        : base(message)
    {
    }
}
