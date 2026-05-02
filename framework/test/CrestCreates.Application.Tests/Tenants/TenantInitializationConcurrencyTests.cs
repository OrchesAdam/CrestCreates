using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Application.Tenants;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Tenants;

public class TenantInitializationConcurrencyTests
{
    private readonly Mock<ITenantDatabaseInitializer> _dbInitializerMock;
    private readonly Mock<ITenantMigrationRunner> _migrationRunnerMock;
    private readonly Mock<ITenantDataSeeder> _dataSeederMock;
    private readonly Mock<ITenantSettingDefaultsSeeder> _settingsSeederMock;
    private readonly Mock<ITenantFeatureDefaultsSeeder> _featuresSeederMock;
    private readonly Mock<ITenantInitializationStore> _storeMock;
    private readonly TenantInitializationOrchestrator _orchestrator;
    private readonly TenantInitializationContext _sharedContext;

    public TenantInitializationConcurrencyTests()
    {
        _dbInitializerMock = new Mock<ITenantDatabaseInitializer>();
        _migrationRunnerMock = new Mock<ITenantMigrationRunner>();
        _dataSeederMock = new Mock<ITenantDataSeeder>();
        _settingsSeederMock = new Mock<ITenantSettingDefaultsSeeder>();
        _featuresSeederMock = new Mock<ITenantFeatureDefaultsSeeder>();
        _storeMock = new Mock<ITenantInitializationStore>();

        _storeMock
            .Setup(s => s.UpdateAsync(
                It.IsAny<TenantInitializationRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var loggerMock = new Mock<ILogger<TenantInitializationOrchestrator>>();
        _orchestrator = new TenantInitializationOrchestrator(
            _dbInitializerMock.Object,
            _migrationRunnerMock.Object,
            _dataSeederMock.Object,
            _settingsSeederMock.Object,
            _featuresSeederMock.Object,
            _storeMock.Object,
            loggerMock.Object);

        _sharedContext = new TenantInitializationContext
        {
            TenantId = Guid.NewGuid(),
            TenantName = "TestTenant",
            ConnectionString = null,
            CorrelationId = Guid.NewGuid().ToString("N"),
            RequestedByUserId = null
        };
    }

    private static TenantInitializationRecord CreateRecord(Guid tenantId, string correlationId, int attemptNo = 1)
        => new(Guid.NewGuid(), tenantId, attemptNo, correlationId);

    [Fact]
    public async Task ConcurrentRequest_WhenTryBeginReturnsNull_ShouldReturnConflict()
    {
        _storeMock
            .Setup(s => s.TryBeginInitializationAsync(
                _sharedContext.TenantId, _sharedContext.CorrelationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantInitializationRecord?)null);

        var result = await _orchestrator.InitializeAsync(_sharedContext);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("already initializing");
    }

    [Fact]
    public async Task RetryInitialization_OnAlreadyFailed_ShouldSucceed()
    {
        var tenantId = Guid.NewGuid();
        var correlationId1 = Guid.NewGuid().ToString("N");
        var correlationId2 = Guid.NewGuid().ToString("N");

        var record1 = CreateRecord(tenantId, correlationId1, 1);
        var record2 = CreateRecord(tenantId, correlationId2, 2);

        var independentContext1 = new TenantInitializationContext
        {
            TenantId = tenantId,
            TenantName = "TestTenant",
            ConnectionString = "Server=.;Database=TestDb;",
            CorrelationId = correlationId1,
            RequestedByUserId = null
        };

        var independentContext2 = new TenantInitializationContext
        {
            TenantId = tenantId,
            TenantName = "TestTenant",
            ConnectionString = "Server=.;Database=TestDb;",
            CorrelationId = correlationId2,
            RequestedByUserId = null
        };

        // First call: TryBegin returns record1, db init succeeds, migration fails
        _storeMock
            .Setup(s => s.TryBeginInitializationAsync(
                tenantId, correlationId1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record1);

        _dbInitializerMock
            .Setup(d => d.InitializeAsync(independentContext1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantDatabaseInitializeResult.Succeeded());

        _migrationRunnerMock
            .Setup(m => m.RunAsync(independentContext1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantMigrationResult.Failed("Migration failed"));

        // Shared phases (always run after migration)
        _dataSeederMock
            .Setup(d => d.SeedAsync(It.IsAny<TenantInitializationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantSeedResult.Succeeded());
        _settingsSeederMock
            .Setup(s => s.SeedAsync(It.IsAny<TenantInitializationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantSettingDefaultsResult.Succeeded());
        _featuresSeederMock
            .Setup(f => f.SeedAsync(It.IsAny<TenantInitializationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantFeatureDefaultsResult.Succeeded());

        var result1 = await _orchestrator.InitializeAsync(independentContext1);

        result1.Success.Should().BeFalse();

        // Second call: TryBegin returns record2, all phases succeed
        _storeMock
            .Setup(s => s.TryBeginInitializationAsync(
                tenantId, correlationId2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record2);

        _dbInitializerMock
            .Setup(d => d.InitializeAsync(independentContext2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantDatabaseInitializeResult.Succeeded());

        _migrationRunnerMock
            .Setup(m => m.RunAsync(independentContext2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantMigrationResult.Succeeded());

        var result2 = await _orchestrator.InitializeAsync(independentContext2);

        result2.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ForceRetry_FromInitializing_ShouldUseForceBegin()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new Tenant(tenantId, "ForceTenant");
        tenant.SetDefaultConnectionString("Server=.;Database=ForceDb;");
        tenant.SetInitializationStatus(TenantInitializationStatus.Initializing);

        var record = CreateRecord(tenantId, Guid.NewGuid().ToString("N"), 2);

        var tenantRepositoryMock = new Mock<ITenantRepository>();
        tenantRepositoryMock
            .Setup(r => r.FindByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        tenantRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<Tenant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var tenantManagerMock = new Mock<ITenantManager>();

        // ForceBegin returns a new record (simulating force-retry store transition)
        _storeMock
            .Setup(s => s.ForceBeginInitializationAsync(
                tenantId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        // TryBegin (called inside orchestrator.InitializeAsync) returns a record
        _storeMock
            .Setup(s => s.TryBeginInitializationAsync(
                tenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        // All phase services succeed
        _dbInitializerMock
            .Setup(d => d.InitializeAsync(It.IsAny<TenantInitializationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantDatabaseInitializeResult.Succeeded());
        _migrationRunnerMock
            .Setup(m => m.RunAsync(It.IsAny<TenantInitializationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantMigrationResult.Succeeded());
        _dataSeederMock
            .Setup(d => d.SeedAsync(It.IsAny<TenantInitializationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantSeedResult.Succeeded());
        _settingsSeederMock
            .Setup(s => s.SeedAsync(It.IsAny<TenantInitializationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantSettingDefaultsResult.Succeeded());
        _featuresSeederMock
            .Setup(f => f.SeedAsync(It.IsAny<TenantInitializationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantFeatureDefaultsResult.Succeeded());

        var appService = new TenantAppService(
            tenantManagerMock.Object,
            tenantRepositoryMock.Object,
            _orchestrator,
            _storeMock.Object);

        var result = await appService.ForceRetryInitializationAsync(tenantId);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task OnFailure_ShouldPreserveTenantAndNotDelete()
    {
        var tenantId = Guid.NewGuid();
        var correlationId = Guid.NewGuid().ToString("N");
        var record = CreateRecord(tenantId, correlationId);

        var independentContext = new TenantInitializationContext
        {
            TenantId = tenantId,
            TenantName = "TestTenant",
            ConnectionString = "Server=.;Database=TestDb;",
            CorrelationId = correlationId,
            RequestedByUserId = null
        };

        _storeMock
            .Setup(s => s.TryBeginInitializationAsync(
                tenantId, correlationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        _dbInitializerMock
            .Setup(d => d.InitializeAsync(independentContext, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantDatabaseInitializeResult.Succeeded());

        _migrationRunnerMock
            .Setup(m => m.RunAsync(independentContext, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantMigrationResult.Failed("Migration failed"));

        var result = await _orchestrator.InitializeAsync(independentContext);

        result.Success.Should().BeFalse();
    }
}
