using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Application.Tenants;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Shared;
using Microsoft.Extensions.Logging;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Tenants;

public class TenantInitializationOrchestratorTests
{
    private readonly Mock<ITenantDatabaseInitializer> _dbInitializerMock;
    private readonly Mock<ITenantMigrationRunner> _migrationRunnerMock;
    private readonly Mock<ITenantDataSeeder> _dataSeederMock;
    private readonly Mock<ITenantSettingDefaultsSeeder> _settingsSeederMock;
    private readonly Mock<ITenantFeatureDefaultsSeeder> _featuresSeederMock;
    private readonly Mock<ITenantInitializationStore> _storeMock;
    private readonly TenantInitializationOrchestrator _orchestrator;

    public TenantInitializationOrchestratorTests()
    {
        _dbInitializerMock = new Mock<ITenantDatabaseInitializer>();
        _migrationRunnerMock = new Mock<ITenantMigrationRunner>();
        _dataSeederMock = new Mock<ITenantDataSeeder>();
        _settingsSeederMock = new Mock<ITenantSettingDefaultsSeeder>();
        _featuresSeederMock = new Mock<ITenantFeatureDefaultsSeeder>();
        _storeMock = new Mock<ITenantInitializationStore>();

        var loggerMock = new Mock<ILogger<TenantInitializationOrchestrator>>();

        _orchestrator = new TenantInitializationOrchestrator(
            _dbInitializerMock.Object,
            _migrationRunnerMock.Object,
            _dataSeederMock.Object,
            _settingsSeederMock.Object,
            _featuresSeederMock.Object,
            _storeMock.Object,
            Mock.Of<CrestCreates.MultiTenancy.Abstract.ICurrentTenant>(),
            loggerMock.Object);
    }

    private static TenantInitializationContext CreateContext(string? connectionString)
    {
        return new TenantInitializationContext
        {
            TenantId = Guid.NewGuid(),
            TenantName = "test-tenant",
            ConnectionString = connectionString,
            CorrelationId = Guid.NewGuid().ToString("N"),
            RequestedByUserId = Guid.NewGuid()
        };
    }

    private static TenantInitializationRecord CreateRecord(Guid tenantId, string correlationId)
    {
        return new TenantInitializationRecord(
            Guid.NewGuid(),
            tenantId,
            1,
            correlationId);
    }

    private void SetupStoreBeginReturns(TenantInitializationRecord record)
    {
        _storeMock
            .Setup(s => s.TryBeginInitializationAsync(
                record.TenantId, record.CorrelationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
    }

    private void SetupStorePassthrough()
    {
        _storeMock
            .Setup(s => s.UpdateAsync(
                It.IsAny<TenantInitializationRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _storeMock
            .Setup(s => s.CompleteInitializationAsync(
                It.IsAny<Guid>(), It.IsAny<TenantInitializationRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _storeMock
            .Setup(s => s.FailInitializationAsync(
                It.IsAny<Guid>(), It.IsAny<TenantInitializationRecord>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task InitializeAsync_IndependentDb_RunsAllFivePhases()
    {
        var context = CreateContext("Server=.;Database=TestDb;");
        var record = CreateRecord(context.TenantId, context.CorrelationId);
        SetupStoreBeginReturns(record);
        SetupStorePassthrough();

        _dbInitializerMock
            .Setup(x => x.InitializeAsync(context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantDatabaseInitializeResult.Succeeded());

        _migrationRunnerMock
            .Setup(x => x.RunAsync(context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantMigrationResult.Succeeded());

        _dataSeederMock
            .Setup(x => x.SeedAsync(context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantSeedResult.Succeeded());

        _settingsSeederMock
            .Setup(x => x.SeedAsync(context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantSettingDefaultsResult.Succeeded());

        _featuresSeederMock
            .Setup(x => x.SeedAsync(context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantFeatureDefaultsResult.Succeeded());

        var result = await _orchestrator.InitializeAsync(context);

        result.Success.Should().BeTrue();
        result.Steps.Should().HaveCount(5);
        result.Steps[0].Name.Should().Be("DatabaseInitialize");
        result.Steps[0].Status.Should().Be(TenantInitializationStepStatus.Succeeded);
        result.Steps[1].Name.Should().Be("Migration");
        result.Steps[1].Status.Should().Be(TenantInitializationStepStatus.Succeeded);
        result.Steps[2].Name.Should().Be("DataSeed");
        result.Steps[2].Status.Should().Be(TenantInitializationStepStatus.Succeeded);
        result.Steps[3].Name.Should().Be("SettingsDefaults");
        result.Steps[3].Status.Should().Be(TenantInitializationStepStatus.Succeeded);
        result.Steps[4].Name.Should().Be("FeatureDefaults");
        result.Steps[4].Status.Should().Be(TenantInitializationStepStatus.Succeeded);

        _dbInitializerMock.Verify(
            x => x.InitializeAsync(context, It.IsAny<CancellationToken>()), Times.Once);
        _migrationRunnerMock.Verify(
            x => x.RunAsync(context, It.IsAny<CancellationToken>()), Times.Once);
        _dataSeederMock.Verify(
            x => x.SeedAsync(context, It.IsAny<CancellationToken>()), Times.Once);
        _settingsSeederMock.Verify(
            x => x.SeedAsync(context, It.IsAny<CancellationToken>()), Times.Once);
        _featuresSeederMock.Verify(
            x => x.SeedAsync(context, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_SharedDb_SkipsDbInitAndMigration()
    {
        var context = CreateContext(null); // ConnectionString is null → IsIndependentDatabase = false
        var record = CreateRecord(context.TenantId, context.CorrelationId);
        SetupStoreBeginReturns(record);
        SetupStorePassthrough();

        _dataSeederMock
            .Setup(x => x.SeedAsync(context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantSeedResult.Succeeded());

        _settingsSeederMock
            .Setup(x => x.SeedAsync(context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantSettingDefaultsResult.Succeeded());

        _featuresSeederMock
            .Setup(x => x.SeedAsync(context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantFeatureDefaultsResult.Succeeded());

        var result = await _orchestrator.InitializeAsync(context);

        result.Success.Should().BeTrue();
        result.Steps.Should().HaveCount(3);
        result.Steps[0].Name.Should().Be("DataSeed");
        result.Steps[0].Status.Should().Be(TenantInitializationStepStatus.Succeeded);
        result.Steps[1].Name.Should().Be("SettingsDefaults");
        result.Steps[1].Status.Should().Be(TenantInitializationStepStatus.Succeeded);
        result.Steps[2].Name.Should().Be("FeatureDefaults");
        result.Steps[2].Status.Should().Be(TenantInitializationStepStatus.Succeeded);

        _dbInitializerMock.Verify(
            x => x.InitializeAsync(It.IsAny<TenantInitializationContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _migrationRunnerMock.Verify(
            x => x.RunAsync(It.IsAny<TenantInitializationContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _dataSeederMock.Verify(
            x => x.SeedAsync(context, It.IsAny<CancellationToken>()), Times.Once);
        _settingsSeederMock.Verify(
            x => x.SeedAsync(context, It.IsAny<CancellationToken>()), Times.Once);
        _featuresSeederMock.Verify(
            x => x.SeedAsync(context, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_StoreReturnsNull_ReturnsConflict()
    {
        var context = CreateContext(null);

        _storeMock
            .Setup(s => s.TryBeginInitializationAsync(
                context.TenantId, context.CorrelationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantInitializationRecord?)null);

        var result = await _orchestrator.InitializeAsync(context);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("already initializing");
        result.Steps.Should().BeEmpty();

        _dbInitializerMock.Verify(
            x => x.InitializeAsync(It.IsAny<TenantInitializationContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _migrationRunnerMock.Verify(
            x => x.RunAsync(It.IsAny<TenantInitializationContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _dataSeederMock.Verify(
            x => x.SeedAsync(It.IsAny<TenantInitializationContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task InitializeAsync_MigrationFails_StopsAndRecordsFailure()
    {
        var context = CreateContext("Server=.;Database=TestDb;");
        var record = CreateRecord(context.TenantId, context.CorrelationId);
        SetupStoreBeginReturns(record);
        SetupStorePassthrough();

        _dbInitializerMock
            .Setup(x => x.InitializeAsync(context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantDatabaseInitializeResult.Succeeded());

        _migrationRunnerMock
            .Setup(x => x.RunAsync(context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantMigrationResult.Failed("Migration failed: timeout"));

        var result = await _orchestrator.InitializeAsync(context);

        result.Success.Should().BeFalse();
        result.Steps.Should().HaveCount(2);
        result.Steps[0].Name.Should().Be("DatabaseInitialize");
        result.Steps[0].Status.Should().Be(TenantInitializationStepStatus.Succeeded);
        result.Steps[1].Name.Should().Be("Migration");
        result.Steps[1].Status.Should().Be(TenantInitializationStepStatus.Failed);
        result.Steps[1].Error.Should().Be("Migration failed: timeout");

        _dataSeederMock.Verify(
            x => x.SeedAsync(It.IsAny<TenantInitializationContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _settingsSeederMock.Verify(
            x => x.SeedAsync(It.IsAny<TenantInitializationContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _featuresSeederMock.Verify(
            x => x.SeedAsync(It.IsAny<TenantInitializationContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task InitializeAsync_SanitizesConnectionStrings()
    {
        var context = CreateContext("Server=secret;Password=p@ss");
        var record = CreateRecord(context.TenantId, context.CorrelationId);
        SetupStoreBeginReturns(record);
        SetupStorePassthrough();

        _dbInitializerMock
            .Setup(x => x.InitializeAsync(context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantDatabaseInitializeResult.Succeeded());

        _migrationRunnerMock
            .Setup(x => x.RunAsync(context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantMigrationResult.Succeeded());

        _dataSeederMock
            .Setup(x => x.SeedAsync(context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantSeedResult.Failed("Server=secret;Password=p@ss"));

        var result = await _orchestrator.InitializeAsync(context);

        result.Success.Should().BeFalse();
        result.Error.Should().NotContain("Server=");
        result.Error.Should().NotContain("Password=");
    }
}
