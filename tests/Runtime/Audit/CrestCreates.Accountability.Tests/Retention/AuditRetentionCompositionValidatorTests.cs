using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Accountability.Abstractions.Retention;
using CrestCreates.Accountability.Retention;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace CrestCreates.Accountability.Tests.Retention;

public sealed class AuditRetentionCompositionValidatorTests
{
    [Fact]
    public void ValidComposition_HasNoErrors()
    {
        var validator = CreateValidator(
            [new StubStore("store-a", "sink-a")],
            new Mock<IAuditRetentionService>().Object);

        var report = validator.Validate();

        report.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void MissingStores_Fails()
    {
        var validator = CreateValidator([], new Mock<IAuditRetentionService>().Object);

        var report = validator.Validate();

        report.Issues.Select(issue => issue.Code?.Value)
            .Should().Contain("RETENTION_STORE_REQUIRED");
    }

    [Fact]
    public void MissingService_Fails()
    {
        var validator = CreateValidator([new StubStore("store-a", "sink-a")], service: null);

        var report = validator.Validate();

        report.Issues.Select(issue => issue.Code?.Value)
            .Should().Contain("RETENTION_SERVICE_MISSING");
    }

    [Fact]
    public void DuplicateStoreId_Fails()
    {
        var validator = CreateValidator(
            [new StubStore("store-a", "sink-a"), new StubStore("store-a", "sink-b")],
            new Mock<IAuditRetentionService>().Object);

        var report = validator.Validate();

        report.Issues.Select(issue => issue.Code?.Value)
            .Should().Contain("RETENTION_DUPLICATE_STORE_ID");
    }

    [Fact]
    public void SharedSinkAcrossStores_Fails()
    {
        var validator = CreateValidator(
            [new StubStore("store-a", "sink-a"), new StubStore("store-b", "sink-a")],
            new Mock<IAuditRetentionService>().Object);

        var report = validator.Validate();

        report.Issues.Select(issue => issue.Code?.Value)
            .Should().Contain("RETENTION_SHARED_SINK");
    }

    [Fact]
    public void IncompleteCapabilities_Fails()
    {
        var validator = CreateValidator(
            [new StubStore("store-a", "sink-a", mutate: capabilities => capabilities with { SupportsPurge = false })],
            new Mock<IAuditRetentionService>().Object);

        var report = validator.Validate();

        report.Issues.Select(issue => issue.Code?.Value)
            .Should().Contain("RETENTION_CAPABILITY_MISSING");
    }

    [Fact]
    public void UnstableIdentifiers_Fail()
    {
        var validator = CreateValidator(
            [new StubStore("Store_A", "sink-a")],
            new Mock<IAuditRetentionService>().Object);

        var report = validator.Validate();

        report.Issues.Select(issue => issue.Code?.Value)
            .Should().Contain("RETENTION_INVALID_STORE_IDENTITY");
    }

    [Fact]
    public async Task HostedValidator_WithoutStore_FailsStartup()
    {
        var services = new ServiceCollection();
        services.AddAuditRetention();
        using var provider = services.BuildServiceProvider();

        var validator = provider.GetServices<IHostedService>()
            .Single(service => service is AuditRetentionCompositionValidator);

        await validator.Invoking(service => service.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*RETENTION_STORE_REQUIRED*");
    }

    [Fact]
    public async Task HostedValidator_WithFullStore_PassesStartup()
    {
        var services = new ServiceCollection();
        services.AddAuditRetention();
        services.AddSingleton<IAuditRetentionStore>(new StubStore("store-a", "sink-a"));
        using var provider = services.BuildServiceProvider();

        var validator = provider.GetServices<IHostedService>()
            .Single(service => service is AuditRetentionCompositionValidator);

        await validator.Invoking(service => service.StartAsync(CancellationToken.None))
            .Should().NotThrowAsync();
        provider.GetRequiredService<IAuditRetentionService>()
            .GetStoreIdentity("store-a")
            .SinkId.Should().Be("sink-a");
    }

    private static AuditRetentionCompositionValidator CreateValidator(
        IReadOnlyList<IAuditRetentionStore> stores,
        IAuditRetentionService? service)
        => new(stores, service);

    private sealed class StubStore : IAuditRetentionStore
    {
        public StubStore(
            string storeId,
            string sinkId,
            Func<AuditRetentionStoreCapabilities, AuditRetentionStoreCapabilities>? mutate = null)
            => Identity = new AuditRetentionStoreIdentity
            {
                StoreId = storeId,
                SinkId = sinkId,
                Capabilities = mutate is null
                    ? AuditRetentionStoreCapabilities.Full
                    : mutate(AuditRetentionStoreCapabilities.Full)
            };

        public AuditRetentionStoreIdentity Identity { get; }

        public ValueTask<AuditRetentionRunRecord> CreateOrReadRunAsync(
            AuditRetentionRunRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<AuditRetentionPreview> PreviewAsync(
            AuditRetentionPreviewRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<AuditRetentionBatchOutcome> ExecuteBatchAsync(
            AuditRetentionBatchRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<AuditRetentionRunRecord?> ReadRunAsync(
            AuditRetentionRunKey key,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<AuditRetentionBatchReceipt?> ReadBatchAsync(
            AuditRetentionRunKey key,
            long batchOrdinal,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<AuditRetentionRunRecord> FinalizeRunAsync(
            AuditRetentionRunFinalization finalization,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<AuditHold> PlaceHoldAsync(
            AuditHoldPlacement placement,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<AuditHold> ReleaseHoldAsync(
            AuditHoldRelease release,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
