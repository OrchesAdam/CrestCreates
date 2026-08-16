using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.PostgreSql;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

/// <summary>
/// Coordinator top-level commit-boundary contract tests. The ambient-rejection
/// path is proven without a live database by pre-setting the internal
/// transaction accessor; the top-level commit path is exercised by curation
/// integration tests against Testcontainers.
/// </summary>
public sealed class PostgreSqlRuntimeCoordinatorModeTests
{
    private static (PostgreSqlRuntimeTransactionCoordinator Coordinator, PostgreSqlRuntimeTransactionAccessor Accessor) Build()
    {
        var options = new PostgreSqlRuntimePersistenceOptions
        {
            ConnectionString = "Host=localhost",
            Schema = $"itest_{Guid.NewGuid():N}"
        };
        var accessor = new PostgreSqlRuntimeTransactionAccessor();
        var coordinator = new PostgreSqlRuntimeTransactionCoordinator(
            new NpgsqlSlimDataSourceBuilder(options.ConnectionString).Build(),
            accessor);
        return (coordinator, accessor);
    }

    [Fact]
    public async Task ExecuteTopLevelAsync_WithAmbientTransaction_Should_FailBeforeDelegate()
    {
        var (coordinator, accessor) = Build();
        var invoked = false;

        // Simulate an ambient Runtime transaction without opening a connection:
        // the top-level guard must reject before any delegate work or SQL.
        accessor.Set(new PostgreSqlRuntimeSession
        {
            Connection = null!,
            Transaction = null!
        });

        var failure = await Record.ExceptionAsync(async () =>
            await coordinator.ExecuteTopLevelAsync(ct =>
            {
                invoked = true;
                return ValueTask.CompletedTask;
            }));

        failure.Should().NotBeNull();
        failure.Should().BeOfType<RuntimePersistenceContractException>();
        ((RuntimePersistenceContractException)failure!).Code
            .Should().Be(RuntimePersistenceContractErrorCode.AmbientCommitBoundaryUnsupported);
        invoked.Should().BeFalse("top-level mode must reject ambient before invoking its delegate.");
    }

    [Fact]
    public void AmbientCommitBoundaryUnsupported_Should_Be_Code5()
    {
        ((int)RuntimePersistenceContractErrorCode.AmbientCommitBoundaryUnsupported).Should().Be(5);
    }
}
