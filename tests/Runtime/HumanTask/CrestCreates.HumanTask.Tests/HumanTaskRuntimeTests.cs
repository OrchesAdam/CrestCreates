using CrestCreates.EventBus.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Registry;
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

    private static (DefaultHumanTaskRuntime runtime, InMemoryHumanTaskInstanceStore store,
        Mock<ILocalEventBus> eventBusMock, Mock<IHumanTaskAssigneeResolver> resolverMock)
        CreateRuntime(HumanTaskRegistry registry,
            Mock<ILocalEventBus>? busMock = null,
            Mock<IHumanTaskAssigneeResolver>? resolverMock = null)
    {
        var store = new InMemoryHumanTaskInstanceStore();
        var eventBus = busMock ?? new Mock<ILocalEventBus>();
        var resolver = resolverMock ?? new Mock<IHumanTaskAssigneeResolver>();

        if (resolverMock == null)
        {
            resolver
                .Setup(r => r.ResolveAsync(
                    It.IsAny<HumanTaskDescriptor>(),
                    It.IsAny<HumanTaskCreationRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HumanTaskAssigneeResolution());
        }

        var runtime = new DefaultHumanTaskRuntime(registry, store, eventBus.Object, resolver.Object);
        return (runtime, store, eventBus, resolver);
    }

    [Fact]
    public async Task CreateAsync_Creates_Instance_From_Descriptor()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1));
        var (runtime, store, _, _) = CreateRuntime(registry);

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
        var (runtime, _, _, _) = CreateRuntime(registry);

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
        var (runtime, store, _, _) = CreateRuntime(registry, eventBusMock);

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
        var (runtime, store, _, _) = CreateRuntime(registry);

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
        var (runtime, store, _, _) = CreateRuntime(registry);

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
    public async Task CompleteAsync_WhenEventDispatchFails_RestoresRetryableState()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1,
            CompletionCondition.Approve));
        var eventBus = new Mock<ILocalEventBus>();
        eventBus.Setup(bus => bus.PublishAsync(
                It.IsAny<HumanTaskCompletedEvent>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("decision failed"));
        var (runtime, store, _, _) = CreateRuntime(registry, eventBus);
        var instance = await runtime.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01"
        });
        var request = new HumanTaskCompletionRequest
        {
            HumanTaskInstanceId = instance.Id,
            Outcome = "Approve"
        };

        await runtime.Invoking(value => value.CompleteAsync(request))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("decision failed");

        var retryable = await store.GetByIdAsync(instance.Id);
        retryable!.Status.Should().Be(HumanTaskInstanceStatus.Created);
        retryable.Outcome.Should().BeNull();
        retryable.CompletedAt.Should().BeNull();

        eventBus.Reset();
        var completed = await runtime.CompleteAsync(request);
        completed.Status.Should().Be(HumanTaskInstanceStatus.Completed);
    }

    [Fact]
    public async Task CancelAsync_Cancels_Instance()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1));
        var (runtime, store, _, _) = CreateRuntime(registry);

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

        var resolver = new Mock<IHumanTaskAssigneeResolver>();
        resolver
            .Setup(r => r.ResolveAsync(
                It.IsAny<HumanTaskDescriptor>(),
                It.IsAny<HumanTaskCreationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HumanTaskAssigneeResolution());

        var runtime = new DefaultHumanTaskRuntime(registry, throwingStore, eventBusMock.Object, resolver.Object);

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

    [Fact]
    public async Task HumanTaskRuntime_CreateAsync_Applies_AssigneeResolution_User()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1));
        var resolverMock = new Mock<IHumanTaskAssigneeResolver>();
        resolverMock
            .Setup(r => r.ResolveAsync(
                It.IsAny<HumanTaskDescriptor>(),
                It.IsAny<HumanTaskCreationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HumanTaskAssigneeResolution
            {
                AssigneeUserId = "resolved-user",
                CandidateRoleIds = new[] { "resolved-role" }
            });
        var (runtime, store, _, _) = CreateRuntime(registry, resolverMock: resolverMock);

        var instance = await runtime.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01"
        });

        instance.AssigneeUserId.Should().Be("resolved-user");
        instance.CandidateRoleIds.Should().BeEquivalentTo(new[] { "resolved-role" });
        instance.Status.Should().Be(HumanTaskInstanceStatus.Assigned);

        var stored = await store.GetByIdAsync(instance.Id);
        stored!.AssigneeUserId.Should().Be("resolved-user");
        stored!.CandidateRoleIds.Should().BeEquivalentTo(new[] { "resolved-role" });
    }

    [Fact]
    public async Task HumanTaskRuntime_CreateAsync_Applies_AssigneeResolution_Role()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1));
        var resolverMock = new Mock<IHumanTaskAssigneeResolver>();
        resolverMock
            .Setup(r => r.ResolveAsync(
                It.IsAny<HumanTaskDescriptor>(),
                It.IsAny<HumanTaskCreationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HumanTaskAssigneeResolution
            {
                AssigneeRoleId = "resolved-role"
            });
        var (runtime, store, _, _) = CreateRuntime(registry, resolverMock: resolverMock);

        var instance = await runtime.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01"
        });

        instance.AssigneeRoleId.Should().Be("resolved-role");
        instance.Status.Should().Be(HumanTaskInstanceStatus.Assigned);
    }

    [Fact]
    public async Task HumanTaskRuntime_CreateAsync_WithCandidates_StatusCreated()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1));
        var resolverMock = new Mock<IHumanTaskAssigneeResolver>();
        resolverMock
            .Setup(r => r.ResolveAsync(
                It.IsAny<HumanTaskDescriptor>(),
                It.IsAny<HumanTaskCreationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HumanTaskAssigneeResolution
            {
                CandidateUserIds = new[] { "candidate-1", "candidate-2" }
            });
        var (runtime, store, _, _) = CreateRuntime(registry, resolverMock: resolverMock);

        var instance = await runtime.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01"
        });

        instance.CandidateUserIds.Should().BeEquivalentTo(new[] { "candidate-1", "candidate-2" });
        instance.Status.Should().Be(HumanTaskInstanceStatus.Created);
    }

    [Fact]
    public async Task HumanTaskRuntime_CreateAsync_Stores_OrganizationUnit_And_Position()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1));
        var resolverMock = new Mock<IHumanTaskAssigneeResolver>();
        resolverMock
            .Setup(r => r.ResolveAsync(
                It.IsAny<HumanTaskDescriptor>(),
                It.IsAny<HumanTaskCreationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HumanTaskAssigneeResolution
            {
                OrganizationUnitId = "org-dept-1",
                PositionId = "pos-manager",
                AssigneeResolutionReason = "context-based assignment"
            });
        var (runtime, store, _, _) = CreateRuntime(registry, resolverMock: resolverMock);

        var instance = await runtime.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01"
        });

        instance.OrganizationUnitId.Should().Be("org-dept-1");
        instance.PositionId.Should().Be("pos-manager");
        instance.AssigneeResolutionReason.Should().Be("context-based assignment");
    }

    [Fact]
    public async Task HumanTaskRuntime_CreateAsync_ResolverException_Propagates_AndDoesNotSave()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1));
        var resolverMock = new Mock<IHumanTaskAssigneeResolver>();
        resolverMock
            .Setup(r => r.ResolveAsync(
                It.IsAny<HumanTaskDescriptor>(),
                It.IsAny<HumanTaskCreationRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("resolver failure"));

        var store = new InMemoryHumanTaskInstanceStore();
        var eventBus = new Mock<ILocalEventBus>();
        var runtime = new DefaultHumanTaskRuntime(
            registry, store, eventBus.Object, resolverMock.Object);

        await runtime.Invoking(r => r.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01",
            WorkflowInstanceId = "wf-1"
        })).Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("resolver failure");

        var allPending = await store.GetPendingByWorkflowAsync("wf-1");
        allPending.Should().BeEmpty();
    }

    [Fact]
    public async Task HumanTaskRuntime_CreateAsync_ExplicitAssignment_Works_WithoutOrganizationServices()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1));
        var resolver = new DefaultHumanTaskAssigneeResolver();
        var store = new InMemoryHumanTaskInstanceStore();
        var eventBus = new Mock<ILocalEventBus>();
        var runtime = new DefaultHumanTaskRuntime(
            registry, store, eventBus.Object, resolver);

        var instance = await runtime.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01",
            AssigneeUserId = "user-1",
            RequestedOrganizationUnitId = "org-dept-1",
            RequestedPositionId = "pos-manager"
        });

        instance.AssigneeUserId.Should().Be("user-1");
        instance.OrganizationUnitId.Should().Be("org-dept-1");
        instance.PositionId.Should().Be("pos-manager");
        instance.Status.Should().Be(HumanTaskInstanceStatus.Assigned);
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

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateUserAsync(
        string userId, CancellationToken ct = default)
        => _inner.GetPendingByCandidateUserAsync(userId, ct);

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateRoleAsync(
        string roleId, CancellationToken ct = default)
        => _inner.GetPendingByCandidateRoleAsync(roleId, ct);

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByOrganizationAsync(
        string organizationUnitId, CancellationToken ct = default)
        => _inner.GetPendingByOrganizationAsync(organizationUnitId, ct);

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByPositionAsync(
        string positionId, CancellationToken ct = default)
        => _inner.GetPendingByPositionAsync(positionId, ct);
}
