using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlRuntimeTransactionCoordinator : IRuntimeTransactionCoordinator
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly PostgreSqlRuntimeTransactionAccessor _accessor;

    public PostgreSqlRuntimeTransactionCoordinator(
        NpgsqlDataSource dataSource,
        PostgreSqlRuntimeTransactionAccessor accessor)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
    }

    internal PostgreSqlRuntimeSession RequireSession()
        => _accessor.Current ?? throw new RuntimePersistenceContractException(
            RuntimePersistenceContractErrorCode.PersistedInvariantViolation,
            "A PostgreSQL Runtime Store operation requires an ambient Runtime transaction.");

    internal bool TryGetSession(out PostgreSqlRuntimeSession? session)
    {
        session = _accessor.Current;
        return session is not null;
    }

    public async ValueTask ExecuteAsync(Func<CancellationToken, ValueTask> work, CancellationToken cancellationToken = default)
        => await ExecuteAsync<object?>(async ct => { await work(ct).ConfigureAwait(false); return null; }, cancellationToken).ConfigureAwait(false);

    public async ValueTask<T> ExecuteAsync<T>(Func<CancellationToken, ValueTask<T>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (_accessor.Current is not null)
            return await work(cancellationToken).ConfigureAwait(false);

        return await ExecuteOwnedAsync(work, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Formal-curation commit boundary. Rejects any pre-existing ambient Runtime
    /// transaction before invoking the delegate so #56 can never publish a
    /// committed Accountability fact before the provider-owned COMMIT completes.
    /// </summary>
    public async ValueTask<T> ExecuteTopLevelAsync<T>(Func<CancellationToken, ValueTask<T>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (_accessor.Current is not null)
        {
            throw new RuntimePersistenceContractException(
                RuntimePersistenceContractErrorCode.AmbientCommitBoundaryUnsupported,
                "Formal curation requires a provider-owned top-level COMMIT boundary and cannot join an ambient Runtime transaction.");
        }

        return await ExecuteOwnedAsync(work, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ExecuteTopLevelAsync(Func<CancellationToken, ValueTask> work, CancellationToken cancellationToken = default)
        => await ExecuteTopLevelAsync<object?>(async ct => { await work(ct).ConfigureAwait(false); return null; }, cancellationToken).ConfigureAwait(false);

    private async ValueTask<T> ExecuteOwnedAsync<T>(Func<CancellationToken, ValueTask<T>> work, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            _accessor.Set(new PostgreSqlRuntimeSession { Connection = connection, Transaction = transaction });
            try
            {
                var result = await work(cancellationToken).ConfigureAwait(false);
                await PostgreSqlRuntimeTestHooks.NotifyBeforeCommitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                    await PostgreSqlRuntimeTestHooks.NotifyAfterCommitAsync().ConfigureAwait(false);
                }
                catch (PostgresException)
                {
                    throw;
                }
                catch (Exception)
                {
                    throw new RuntimeTransactionCommitUnknownException(
                        "PostgreSQL COMMIT completed with an indeterminate acknowledgement.");
                }
                return result;
            }
            catch (RuntimeTransactionCommitUnknownException)
            {
                throw;
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // The original operation failure is authoritative; rollback is best effort.
                }
                throw;
            }
            finally
            {
                _accessor.Set(null);
            }
        }
        catch (RuntimePersistenceException)
        {
            throw;
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            throw PostgreSqlRuntimeStoreSupport.TranslateForeignKeyViolation(ex);
        }
        catch (NpgsqlException ex)
        {
            throw new RuntimePersistenceUnavailableException(
                "PostgreSQL Runtime persistence is unavailable.",
                ex);
        }
    }
}
