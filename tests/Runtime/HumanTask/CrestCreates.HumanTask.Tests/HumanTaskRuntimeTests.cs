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
            Mock<IHumanTaskAssigneeResolver>? resolverMock = null,
            IHumanTaskCompletionFailurePolicy? completionFailurePolicy = null)
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

        var runtime = new DefaultHumanTaskRuntime(
            registry,
            store,
            eventBus.Object,
            resolver.Object,
            completionFailurePolicy);
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
    public async Task CompletionFailureState_IsExplicitlyRecoverable()
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

        var failed = await store.GetByIdAsync(instance.Id);
        failed!.Status.Should().Be(HumanTaskInstanceStatus.CompletionDispatchFailed);
        failed.Outcome.Should().Be("Approve");
        failed.CompletedAt.Should().NotBeNull();
        failed.CompletionDispatchError.Should().Contain("decision failed");
        failed.CompletionDispatchFailedAt.Should().NotBeNull();
        failed.CompletionDispatchAttemptCount.Should().Be(1);

        await runtime.Invoking(value => value.CompleteAsync(request))
            .Should().ThrowAsync<HumanTaskCompletionRecoveryRequiredException>();
        eventBus.Verify(bus => bus.PublishAsync(
            It.IsAny<HumanTaskCompletedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SecondSubscriberFailure_DoesNotReplayFirstSubscriberSideEffect()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1,
            CompletionCondition.Approve));
        var bus = new CheckpointingCompletionBus(failWithCancellation: false);
        var store = new InMemoryHumanTaskInstanceStore();
        var resolver = new DefaultHumanTaskAssigneeResolver();
        var runtime = new DefaultHumanTaskRuntime(
            registry,
            store,
            bus,
            resolver,
            new CheckpointingCompletionFailurePolicy(bus));
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
            .WithMessage("second subscriber failed");

        var completed = await runtime.CompleteAsync(request);

        completed.Status.Should().Be(HumanTaskInstanceStatus.Completed);
        bus.FirstSubscriberSideEffectCount.Should().Be(1);
        bus.SecondSubscriberAttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task Retry_DoesNotRepublishAlreadyCommittedHandlers()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1,
            CompletionCondition.Approve));
        var bus = new CheckpointingCompletionBus(failWithCancellation: false);
        var store = new InMemoryHumanTaskInstanceStore();
        var runtime = new DefaultHumanTaskRuntime(
            registry,
            store,
            bus,
            new DefaultHumanTaskAssigneeResolver(),
            new CheckpointingCompletionFailurePolicy(bus));
        var task = await runtime.CreateAsync(new HumanTaskCreationRequest { HumanTaskId = "ht_01" });
        var request = new HumanTaskCompletionRequest
        {
            HumanTaskInstanceId = task.Id,
            Outcome = "Approve"
        };

        await runtime.Invoking(value => value.CompleteAsync(request))
            .Should().ThrowAsync<InvalidOperationException>();
        await runtime.CompleteAsync(request);

        bus.ExecutedHandlerIndexes.Should().Equal(0, 1);
    }

    [Fact]
    public async Task CancellationAfterPartialDispatch_DoesNotResetTaskToCreated()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1,
            CompletionCondition.Approve));
        var bus = new CheckpointingCompletionBus(failWithCancellation: true);
        var store = new InMemoryHumanTaskInstanceStore();
        var runtime = new DefaultHumanTaskRuntime(
            registry,
            store,
            bus,
            new DefaultHumanTaskAssigneeResolver());
        var task = await runtime.CreateAsync(new HumanTaskCreationRequest { HumanTaskId = "ht_01" });

        await runtime.Invoking(value => value.CompleteAsync(new HumanTaskCompletionRequest
            {
                HumanTaskInstanceId = task.Id,
                Outcome = "Approve"
            }))
            .Should().ThrowAsync<OperationCanceledException>();

        var failed = await store.GetByIdAsync(task.Id);
        failed!.Status.Should().Be(HumanTaskInstanceStatus.CompletionDispatchFailed);
        failed.Outcome.Should().Be("Approve");
        bus.FirstSubscriberSideEffectCount.Should().Be(1);
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

sealed class CheckpointingCompletionFailurePolicy(CheckpointingCompletionBus bus)
    : IHumanTaskCompletionFailurePolicy
{
    public Task RecoverAsync(
        HumanTaskInstance instance,
        HumanTaskCompletedEvent completion,
        CancellationToken cancellationToken = default)
        => bus.PublishAsync(completion, cancellationToken);
}

sealed class CheckpointingCompletionBus(bool failWithCancellation) : ILocalEventBus
{
    int _nextHandlerIndex;
    bool _hasFailed;

    public int FirstSubscriberSideEffectCount { get; private set; }
    public int SecondSubscriberAttemptCount { get; private set; }
    public List<int> ExecutedHandlerIndexes { get; } = [];

    public Task PublishAsync(ILocalEvent @event, CancellationToken cancellationToken = default)
        => PublishAsync((HumanTaskCompletedEvent)@event, cancellationToken);

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : ILocalEvent
    {
        if (_nextHandlerIndex == 0)
        {
            FirstSubscriberSideEffectCount++;
            ExecutedHandlerIndexes.Add(0);
            _nextHandlerIndex = 1;
        }

        SecondSubscriberAttemptCount++;
        if (!_hasFailed)
        {
            _hasFailed = true;
            if (failWithCancellation)
                throw new OperationCanceledException("second subscriber cancelled");
            throw new InvalidOperationException("second subscriber failed");
        }

        ExecutedHandlerIndexes.Add(1);
        _nextHandlerIndex = 2;
        return Task.CompletedTask;
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
