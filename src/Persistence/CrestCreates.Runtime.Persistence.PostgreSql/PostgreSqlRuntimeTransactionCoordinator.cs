using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlRuntimeTransactionCoordinator : IRuntimeTransactionCoordinator
{
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly PostgreSqlRuntimeTransactionAccessor _accessor;
    public PostgreSqlRuntimeTransactionCoordinator(PostgreSqlRuntimePersistenceOptions options, PostgreSqlRuntimeTransactionAccessor accessor)
    { _options = options; _accessor = accessor; }

    internal PostgreSqlRuntimeSession RequireSession()
        => _accessor.Current ?? throw new InvalidOperationException("A PostgreSQL runtime store operation requires an ambient transaction.");

    public async ValueTask ExecuteAsync(Func<CancellationToken, ValueTask> work, CancellationToken cancellationToken = default)
        => await ExecuteAsync<object?>(async ct => { await work(ct); return null; }, cancellationToken).ConfigureAwait(false);

    public async ValueTask<T> ExecuteAsync<T>(Func<CancellationToken, ValueTask<T>> work, CancellationToken cancellationToken = default)
    {
        if (_accessor.Current is not null) return await work(cancellationToken).ConfigureAwait(false);
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        _accessor.Set(new PostgreSqlRuntimeSession { Connection = connection, Transaction = transaction });
        try
        {
            var result = await work(cancellationToken).ConfigureAwait(false);
            try { await transaction.CommitAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
            { throw new RuntimeTransactionCommitUnknownException("PostgreSQL commit acknowledgement was lost."); }
            return result;
        }
        catch
        {
            try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            throw;
        }
        finally { _accessor.Set(null); }
    }
}
