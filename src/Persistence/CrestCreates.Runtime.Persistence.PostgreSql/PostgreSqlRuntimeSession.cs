using System.Threading;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlRuntimeSession
{
    private int _commandInFlight;

    public required NpgsqlConnection Connection { get; init; }
    public required NpgsqlTransaction Transaction { get; init; }

    public IDisposable EnterCommand()
    {
        if (Interlocked.CompareExchange(ref _commandInFlight, 1, 0) != 0)
        {
            throw new RuntimePersistenceContractException(
                RuntimePersistenceContractErrorCode.ConcurrentAmbientUse,
                "Concurrent commands on one Runtime transaction session are not supported.");
        }
        return new Releaser(this);
    }

    private sealed class Releaser(PostgreSqlRuntimeSession owner) : IDisposable
    {
        private PostgreSqlRuntimeSession? _owner = owner;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is not null)
                Volatile.Write(ref owner._commandInFlight, 0);
        }
    }
}
