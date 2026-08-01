namespace CrestCreates.Runtime.Persistence.Abstractions.Transactions;

public interface IRuntimeTransactionCoordinator
{
    ValueTask ExecuteAsync(
        Func<CancellationToken, ValueTask> work,
        CancellationToken cancellationToken = default);

    ValueTask<T> ExecuteAsync<T>(
        Func<CancellationToken, ValueTask<T>> work,
        CancellationToken cancellationToken = default);
}
