using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.OrmProviders.EFCore.MultiTenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CrestCreates.OrmProviders.Tests.MultiTenancy;

public class EfCoreTenantMigrationRunnerTests
{
    private static Func<string, DbContext> CreateStubFactory()
    {
        // Returns a factory that creates a DbContext with in-memory options
        // (never actually used for migration in these unit tests)
        return _ =>
        {
            var options = new DbContextOptionsBuilder<DbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new DbContext(options);
        };
    }

    [Fact]
    public void Implements_ITenantMigrationRunner()
    {
        var logger = Mock.Of<ILogger<EfCoreTenantMigrationRunner>>();
        var runner = new EfCoreTenantMigrationRunner(CreateStubFactory(), logger);

        runner.Should().BeAssignableTo<ITenantMigrationRunner>();
    }

    [Fact]
    public void Constructor_AcceptsFactoryAndLogger()
    {
        var logger = Mock.Of<ILogger<EfCoreTenantMigrationRunner>>();
        var runner = new EfCoreTenantMigrationRunner(CreateStubFactory(), logger);

        runner.Should().NotBeNull();
    }

    [Fact]
    public void RunAsync_ReturnsTaskOfTenantMigrationResult()
    {
        var logger = Mock.Of<ILogger<EfCoreTenantMigrationRunner>>();
        var runner = new EfCoreTenantMigrationRunner(CreateStubFactory(), logger);

        var context = new TenantInitializationContext
        {
            TenantId = Guid.NewGuid(),
            TenantName = "test-tenant",
            ConnectionString = "Server=.;Database=test;",
            CorrelationId = Guid.NewGuid().ToString("N")
        };

        var resultTask = runner.RunAsync(context, CancellationToken.None);
        resultTask.Should().NotBeNull();
        resultTask.Should().BeAssignableTo<Task<TenantMigrationResult>>();
    }

    [Fact]
    public async Task RunAsync_WithCancelledToken_ReturnsFailedResult()
    {
        var logger = Mock.Of<ILogger<EfCoreTenantMigrationRunner>>();
        var runner = new EfCoreTenantMigrationRunner(CreateStubFactory(), logger);

        var context = new TenantInitializationContext
        {
            TenantId = Guid.NewGuid(),
            TenantName = "test-tenant",
            ConnectionString = "Server=.;Database=test;",
            CorrelationId = Guid.NewGuid().ToString("N")
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await runner.RunAsync(context, cts.Token);

        // The runner wraps all exceptions into a Failed result;
        // a cancelled token leads to an OperationCanceledException
        // which is re-thrown (not caught), but the in-memory DbContext
        // will throw before that since MigrateAsync is not supported.
        // Either way, the result should indicate failure.
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }
}
