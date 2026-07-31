using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlRuntimeSession
{
    public required NpgsqlConnection Connection { get; init; }
    public required NpgsqlTransaction Transaction { get; init; }
}
