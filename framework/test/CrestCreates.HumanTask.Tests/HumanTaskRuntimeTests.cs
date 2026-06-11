using CrestCreates.EventBus.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.HumanTask.Tests;

public class HumanTaskRuntimeTests
{
    private static HumanTaskDescriptor CreateDescriptor(string id, string name, int version,
        params CompletionCondition[] conditions)
    {
        return new HumanTaskDescriptor
        {
            Id = id,
            Name = name,
            Version = version,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor>("form_01", 1),
            Outcomes = conditions
                .Select(c => new CompletionOutcome { Condition = c })
                .ToList()
        };
    }

    private class TestHumanTaskProvider : IDescriptorProvider<HumanTaskDescriptor>
    {
        private readonly List<HumanTaskDescriptor> _descriptors;
        public TestHumanTaskProvider(List<HumanTaskDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<HumanTaskDescriptor> GetDescriptors() => _descriptors;
    }

    private static HumanTaskRegistry CreateRegistry(params HumanTaskDescriptor[] descriptors)
    {
        var engine = new RegistryValidationEngine<HumanTaskDescriptor>(
            Array.Empty<IRegistryValidator<HumanTaskDescriptor>>());
        var registry = new HumanTaskRegistry(engine);
        registry.Build([new TestHumanTaskProvider(descriptors.ToList())]);
        return registry;
    }

    private static (DefaultHumanTaskRuntime runtime, InMemoryHumanTaskInstanceStore store, Mock<ILocalEventBus> eventBusMock)
        CreateRuntime(HumanTaskRegistry registry, Mock<ILocalEventBus>? busMock = null)
    {
        var store = new InMemoryHumanTaskInstanceStore();
        var eventBus = busMock ?? new Mock<ILocalEventBus>();
        var runtime = new DefaultHumanTaskRuntime(registry, store, eventBus.Object);
        return (runtime, store, eventBus);
    }

    [Fact]
    public async Task CreateAsync_Creates_Instance_From_Descriptor()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1));
        var (runtime, store, _) = CreateRuntime(registry);

        var instance = await runtime.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01",
            Input = new { key = "value" }
        });

        instance.Id.Should().NotBeNullOrEmpty();
        instance.HumanTaskId.Should().Be("ht_01");
        instance.HumanTaskVersion.Should().Be(1);
        instance.Status.Should().Be(HumanTaskInstanceStatus.Created);
        instance.Input.Should().NotBeNull();

        var stored = await store.GetByIdAsync(instance.Id);
        stored.Should().NotBeNull();
        stored!.Id.Should().Be(instance.Id);
    }

    [Fact]
    public async Task CreateAsync_Throws_When_Descriptor_Not_Found()
    {
        var registry = CreateRegistry();
        var (runtime, _, _) = CreateRuntime(registry);

        await runtime.Invoking(r => r.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "nonexistent"
        })).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CompleteAsync_Completes_Instance_And_Publishes_Event()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1,
            CompletionCondition.Approve, CompletionCondition.Reject));
        var eventBusMock = new Mock<ILocalEventBus>();
        var (runtime, store, _) = CreateRuntime(registry, eventBusMock);

        var instance = await runtime.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01",
            Input = new { key = "value" }
        });

        var completed = await runtime.CompleteAsync(new HumanTaskCompletionRequest
        {
            HumanTaskInstanceId = instance.Id,
            Outcome = "Approve",
            Result = new { Score = 95 }
        });

        completed.Status.Should().Be(HumanTaskInstanceStatus.Completed);
        completed.Outcome.Should().Be("Approve");
        completed.Output.Should().NotBeNull();
        completed.CompletedAt.Should().NotBeNull();

        eventBusMock.Verify(
            b => b.PublishAsync(
                It.Is<HumanTaskCompletedEvent>(e =>
                    e.HumanTaskInstanceId == instance.Id &&
                    e.HumanTaskId == "ht_01" &&
                    e.HumanTaskVersion == 1 &&
                    e.Outcome == "Approve" &&
                    e.Result != null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CompleteAsync_Throws_When_Outcome_Invalid()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1,
            CompletionCondition.Approve));
        var (runtime, store, _) = CreateRuntime(registry);

        var instance = await runtime.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01"
        });

        await runtime.Invoking(r => r.CompleteAsync(new HumanTaskCompletionRequest
        {
            HumanTaskInstanceId = instance.Id,
            Outcome = "NonExistent"
        })).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CompleteAsync_Throws_When_Instance_Already_Completed()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1,
            CompletionCondition.Approve));
        var (runtime, store, _) = CreateRuntime(registry);

        var instance = await runtime.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01"
        });

        await runtime.CompleteAsync(new HumanTaskCompletionRequest
        {
            HumanTaskInstanceId = instance.Id,
            Outcome = "Approve"
        });

        await runtime.Invoking(r => r.CompleteAsync(new HumanTaskCompletionRequest
        {
            HumanTaskInstanceId = instance.Id,
            Outcome = "Approve"
        })).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CancelAsync_Cancels_Instance()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1));
        var (runtime, store, _) = CreateRuntime(registry);

        var instance = await runtime.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01"
        });

        var cancelled = await runtime.CancelAsync(instance.Id, "No longer needed");

        cancelled.Status.Should().Be(HumanTaskInstanceStatus.Cancelled);
        cancelled.CancellationReason.Should().Be("No longer needed");
        cancelled.CancelledAt.Should().NotBeNull();

        var stored = await store.GetByIdAsync(instance.Id);
        stored!.Status.Should().Be(HumanTaskInstanceStatus.Cancelled);
    }

    [Fact]
    public async Task CompleteAsync_DoesNotPublishEvent_When_SaveConcurrencyFails()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1,
            CompletionCondition.Approve));
        var eventBusMock = new Mock<ILocalEventBus>();

        // Create a fake store that throws RuntimeConcurrencyException on second save
        var throwingStore = new ConcurrencyThrowingHumanTaskInstanceStore();

        var runtime = new DefaultHumanTaskRuntime(registry, throwingStore, eventBusMock.Object);

        var instance = await runtime.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01"
        });

        // CompleteAsync will call SaveAsync which throws — event must NOT be published
        await runtime.Invoking(r => r.CompleteAsync(new HumanTaskCompletionRequest
        {
            HumanTaskInstanceId = instance.Id,
            Outcome = "Approve"
        })).Should().ThrowAsync<RuntimeConcurrencyException>();

        eventBusMock.Verify(
            b => b.PublishAsync(
                It.IsAny<HumanTaskCompletedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

sealed class ConcurrencyThrowingHumanTaskInstanceStore : IHumanTaskInstanceStore
{
    readonly InMemoryHumanTaskInstanceStore _inner = new();
    bool _firstSave = true;

    public Task SaveAsync(HumanTaskInstance instance, CancellationToken ct = default)
    {
        if (_firstSave)
        {
            _firstSave = false;
            return _inner.SaveAsync(instance, ct);
        }
        throw new RuntimeConcurrencyException(
            $"Concurrency conflict for HumanTaskInstance '{instance.Id}'.");
    }

    public Task<HumanTaskInstance?> GetByIdAsync(string instanceId, CancellationToken ct = default)
        => _inner.GetByIdAsync(instanceId, ct);

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByAssigneeAsync(
        string assigneeUserId, CancellationToken ct = default)
        => _inner.GetPendingByAssigneeAsync(assigneeUserId, ct);

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByWorkflowAsync(
        string workflowInstanceId, CancellationToken ct = default)
        => _inner.GetPendingByWorkflowAsync(workflowInstanceId, ct);
}
