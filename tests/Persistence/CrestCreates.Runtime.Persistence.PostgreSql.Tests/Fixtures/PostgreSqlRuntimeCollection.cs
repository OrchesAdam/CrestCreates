using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;

[CollectionDefinition(Name)]
public sealed class PostgreSqlRuntimeCollection : ICollectionFixture<PostgreSqlRuntimeCollectionFixture>
{
    public const string Name = "runtime-postgresql";
}
