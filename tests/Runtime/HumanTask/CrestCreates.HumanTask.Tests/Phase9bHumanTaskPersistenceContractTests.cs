using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Runtime.Persistence.InMemory;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.HumanTask.Tests;

public sealed class Phase9bHumanTaskPersistenceContractTests
{
    [Fact]
    public async Task AddAndGet_ShouldAssignRevisionAndReturnDetachedSnapshot()
    {
        using var provider = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence().BuildServiceProvider();
        var store = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var instance = New("tenant-a", "task-1");
        await store.AddAsync(instance);

        instance.Revision.Should().Be(0);
        var loaded = await store.GetAsync(instance.Key);
        loaded.Should().NotBeNull();
        loaded!.Revision.Should().Be(1);
        loaded.Status = HumanTaskInstanceStatus.Completed;
        (await store.GetAsync(instance.Key))!.Status.Should().Be(HumanTaskInstanceStatus.Created);
    }

    [Fact]
    public async Task ConcurrentUpdate_FromSameRevision_ShouldAllowOneWinner()
    {
        using var provider = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence().BuildServiceProvider();
        var store = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var instance = New("tenant-a", "task-1");
        await store.AddAsync(instance);
        var first = (await store.GetAsync(instance.Key))!;
        var second = first.Snapshot();
        first.Status = HumanTaskInstanceStatus.Completed;
        first.CompletedAt = DateTimeOffset.UtcNow;
        second.Status = HumanTaskInstanceStatus.Cancelled;
        second.CancelledAt = DateTimeOffset.UtcNow;

        await store.UpdateAsync(first, 1);
        var act = () => store.UpdateAsync(second, 1);
        await Assert.ThrowsAsync<RuntimeConcurrencyException>(act);
    }

    [Fact]
    public async Task SameTaskIdAcrossTenants_ShouldRemainDistinct()
    {
        using var provider = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence().BuildServiceProvider();
        var store = provider.GetRequiredService<IHumanTaskInstanceStore>();
        await store.AddAsync(New(null, "same"));
        await store.AddAsync(New("tenant-a", "same"));

        (await store.GetAsync(new RuntimeInstanceKey(null, "same"))).Should().NotBeNull();
        (await store.GetAsync(new RuntimeInstanceKey("tenant-a", "same"))).Should().NotBeNull();
    }

    private static HumanTaskInstance New(string? tenantId, string id) => new()
    {
        Key = new RuntimeInstanceKey(tenantId, id),
        HumanTaskPin = Pin(),
        Status = HumanTaskInstanceStatus.Created,
    };

    private static RuntimeDescriptorPin Pin() => new()
    {
        Ref = new DescriptorRef("humantask", "review", 1),
        ContractHash = Hash("contract", "Contract"),
        DefinitionHash = Hash("definition", "Definition")
    };

    private static CanonicalHash Hash(string value, string purpose) => new()
    {
        Value = value,
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = "Descriptor",
        DescriptorKind = "HumanTask",
        Scope = "InternalFull",
        Purpose = purpose,
        ContractVersion = "canonical-hash-v1",
        CanonicalShapeVersion = "human-task-v1"
    };
}
