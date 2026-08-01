namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlRuntimeTransactionAccessor
{
    private readonly AsyncLocal<PostgreSqlRuntimeSession?> _current = new();
    public PostgreSqlRuntimeSession? Current => _current.Value;
    public void Set(PostgreSqlRuntimeSession? session) => _current.Value = session;
}
