using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Shared;
using TenantInitStepStatus = CrestCreates.MultiTenancy.Abstract.TenantInitializationStepStatus;
using Microsoft.Extensions.Logging;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Tenants;

public class TenantInitializationOrchestratorTests
{
    private readonly Mock<ITenantDatabaseProvisioner> _dbInitializerMock;
    private readonly Mock<ITenantSchemaMigrator> _migrationRunnerMock;
    private readonly Mock<ITenantDataSeedContributor> _dataSeederMock;
    private readonly Mock<ITenantSettingDefaultsSeeder> _settingsSeederMock;
    private readonly Mock<ITenantFeatureDefaultsSeeder> _featuresSeederMock;
    private readonly Mock<ITenantInitializationStore> _storeMock;
    private readonly TenantInitializationOrchestrator _orchestrator;

    public TenantInitializationOrchestratorTests()
    {
        _dbInitializerMock = new Mock<ITenantDatabaseProvisioner>();
        _migrationRunnerMock = new Mock<ITenantSchemaMigrator>();
        _dataSeederMock = new Mock<ITenantDataSeedContributor>();
        _settingsSeederMock = new Mock<ITenantSettingDefaultsSeeder>();
        _featuresSeederMock = new Mock<ITenantFeatureDefaultsSeeder>();
        _storeMock = new Mock<ITenantInitializationStore>();

        _orchestrator = new TenantInitializationOrchestrator(
            _dbInitializerMock.Object,
            _migrationRunnerMock.Object,
            new[] { _dataSeederMock.Object },
            _settingsSeederMock.Object,
            _featuresSeederMock.Object,
            _storeMock.Object,
            Mock.Of<CrestCreates.MultiTenancy.Abstract.ICurrentTenant>(),
            Mock.Of<CrestCreates.MultiTenancy.Abstract.ITenantInitializationEventSink>());
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
        result.Steps[0].Status.Should().Be(TenantInitStepStatus.Succeeded);
        result.Steps[1].Name.Should().Be("Migration");
        result.Steps[1].Status.Should().Be(TenantInitStepStatus.Succeeded);
        result.Steps[2].Name.Should().Be("DataSeed");
        result.Steps[2].Status.Should().Be(TenantInitStepStatus.Succeeded);
        result.Steps[3].Name.Should().Be("SettingsDefaults");
        result.Steps[3].Status.Should().Be(TenantInitStepStatus.Succeeded);
        result.Steps[4].Name.Should().Be("FeatureDefaults");
        result.Steps[4].Status.Should().Be(TenantInitStepStatus.Succeeded);

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
        result.Steps[0].Status.Should().Be(TenantInitStepStatus.Succeeded);
        result.Steps[1].Name.Should().Be("SettingsDefaults");
        result.Steps[1].Status.Should().Be(TenantInitStepStatus.Succeeded);
        result.Steps[2].Name.Should().Be("FeatureDefaults");
        result.Steps[2].Status.Should().Be(TenantInitStepStatus.Succeeded);

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
        result.Steps[0].Status.Should().Be(TenantInitStepStatus.Succeeded);
        result.Steps[1].Name.Should().Be("Migration");
        result.Steps[1].Status.Should().Be(TenantInitStepStatus.Failed);
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

    [Fact]
    public async Task InitializeAsync_NewConstructor_ExecutesAllDataSeedContributors()
    {
        var provisionerMock = new Mock<ITenantDatabaseProvisioner>();
        var migratorMock = new Mock<ITenantSchemaMigrator>();
        var contributor1Mock = new Mock<ITenantDataSeedContributor>();
        var contributor2Mock = new Mock<ITenantDataSeedContributor>();
        var eventSinkMock = new Mock<ITenantInitializationEventSink>();
        var storeMock = new Mock<ITenantInitializationStore>();
        var context = CreateContext(null);
        var record = CreateRecord(context.TenantId, context.CorrelationId);

        storeMock
            .Setup(s => s.TryBeginInitializationAsync(
                context.TenantId, context.CorrelationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        storeMock
            .Setup(s => s.UpdateAsync(It.IsAny<TenantInitializationRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        storeMock
            .Setup(s => s.CompleteInitializationAsync(
                It.IsAny<Guid>(), It.IsAny<TenantInitializationRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        storeMock
            .Setup(s => s.FailInitializationAsync(
                It.IsAny<Guid>(), It.IsAny<TenantInitializationRecord>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        contributor1Mock
            .Setup(c => c.SeedAsync(context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantSeedResult.Succeeded());
        contributor2Mock
            .Setup(c => c.SeedAsync(context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantSeedResult.Succeeded());
        _settingsSeederMock
            .Setup(s => s.SeedAsync(context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantSettingDefaultsResult.Succeeded());
        _featuresSeederMock
            .Setup(f => f.SeedAsync(context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantFeatureDefaultsResult.Succeeded());
        eventSinkMock.Setup(s => s.PhaseStartedAsync(It.IsAny<TenantInitializationContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        eventSinkMock.Setup(s => s.PhaseSucceededAsync(It.IsAny<TenantInitializationContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        eventSinkMock.Setup(s => s.PhaseFailedAsync(It.IsAny<TenantInitializationContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        eventSinkMock.Setup(s => s.InfrastructureFailureAsync(It.IsAny<TenantInitializationContext>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orchestrator = new TenantInitializationOrchestrator(
            provisionerMock.Object,
            migratorMock.Object,
            new[] { contributor1Mock.Object, contributor2Mock.Object },
            _settingsSeederMock.Object,
            _featuresSeederMock.Object,
            storeMock.Object,
            Mock.Of<CrestCreates.MultiTenancy.Abstract.ICurrentTenant>(),
            eventSinkMock.Object);

        var result = await orchestrator.InitializeAsync(context);

        result.Success.Should().BeTrue();
        result.Steps.Should().HaveCount(3);
        contributor1Mock.Verify(c => c.SeedAsync(context, It.IsAny<CancellationToken>()), Times.Once);
        contributor2Mock.Verify(c => c.SeedAsync(context, It.IsAny<CancellationToken>()), Times.Once);
        provisionerMock.Verify(
            p => p.InitializeAsync(It.IsAny<TenantInitializationContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
        migratorMock.Verify(
            m => m.RunAsync(It.IsAny<TenantInitializationContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
