using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.OrmProviders.EFCore.MultiTenancy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CrestCreates.OrmProviders.Tests.MultiTenancy;

public class EfCoreTenantMigrationRunnerTests
{
    [Fact]
    public void Implements_ITenantMigrationRunner()
    {
        var logger = Mock.Of<ILogger<EfCoreTenantMigrationRunner>>();
        var runner = new EfCoreTenantMigrationRunner(logger);

        runner.Should().BeAssignableTo<ITenantMigrationRunner>();
    }

    [Fact]
    public void Constructor_AcceptsLogger()
    {
        var logger = Mock.Of<ILogger<EfCoreTenantMigrationRunner>>();
        var runner = new EfCoreTenantMigrationRunner(logger);

        runner.Should().NotBeNull();
    }

    [Fact]
    public void RunAsync_ReturnsTaskOfTenantMigrationResult()
    {
        var logger = Mock.Of<ILogger<EfCoreTenantMigrationRunner>>();
        var runner = new EfCoreTenantMigrationRunner(logger);

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
        var runner = new EfCoreTenantMigrationRunner(logger);

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
        // caught and converted to Failed.
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }
}
