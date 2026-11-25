using CrestCreates.Capability.Abstractions;
using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Identity;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Metadata.Registry;
using CrestCreates.Runtime.Persistence;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Accountability;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public sealed class WorkflowConditionPolicyTests
{
    [Fact]
    public void PublicTokens_AreCanonicalAndExact()
    {
        WorkflowConditionTokens.PreviousHumanTaskApproved.Should().Be("previous-human-task-approved");
        WorkflowConditionTokens.PreviousHumanTaskRejected.Should().Be("previous-human-task-rejected");
        WorkflowConditionTokens.AllPriorHumanTasksApproved.Should().Be("all-prior-human-tasks-approved");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Previous-Human-Task-Approved")]
    [InlineData("workflow.input == true")]
    public void CompatibilityValidation_RejectsUnsupportedCondition(string condition)
    {
        var descriptor = Descriptor(
            new WorkflowStep
            {
                Id = "conditional",
                Target = HumanTaskTarget("final"),
                Condition = condition
            });
        var validator = new WorkflowCompatibilityValidator(
            new Mock<ICapabilityRegistry>().Object,
            HumanTaskRegistry("initial", "final"));

        var act = () => validator.Validate(descriptor);

        act.Should().Throw<WorkflowValidationException>()
            .WithMessage("*unsupported condition*");
    }

    [Fact]
    public void CompatibilityValidation_RejectsFirstStepCondition()
    {
        var descriptor = Descriptor(new WorkflowStep
        {
            Id = "first",
            Target = HumanTaskTarget("initial"),
            Condition = WorkflowConditionTokens.PreviousHumanTaskApproved
        });
        var validator = new WorkflowCompatibilityValidator(
            new Mock<ICapabilityRegistry>().Object,
            HumanTaskRegistry("initial"));

        var act = () => validator.Validate(descriptor);

        act.Should().Throw<WorkflowValidationException>()
            .WithMessage("*immediately preceding step*HumanTask*");
    }

    [Fact]
    public void CompatibilityValidation_RejectsConditionAfterConditionalPredecessor()
    {
        var descriptor = Descriptor(
            new WorkflowStep { Id = "initial", Target = HumanTaskTarget("initial") },
            new WorkflowStep
            {
                Id = "conditional-h2",
                Target = HumanTaskTarget("mid"),
                Condition = WorkflowConditionTokens.PreviousHumanTaskApproved
            },
            new WorkflowStep
            {
                Id = "conditional-h3",
                Target = HumanTaskTarget("final"),
                Condition = WorkflowConditionTokens.PreviousHumanTaskRejected
            });
        var validator = new WorkflowCompatibilityValidator(
            new Mock<ICapabilityRegistry>().Object,
            HumanTaskRegistry("initial", "mid", "final"));

        var act = () => validator.Validate(descriptor);

        act.Should().Throw<WorkflowValidationException>()
            .WithMessage("*unconditional*conditional predecessor*skipped*");
    }

    [Fact]
    public void BindingStatus_RejectsConditionAfterCapabilityStep()
    {
        var wfRegistry = new Mock<IWorkflowRegistry>();
        var schemaRegistry = new Mock<ISchemaRegistry>();
        var capabilities = new Mock<ICapabilityRegistry>();
        capabilities.Setup(registry => registry.GetByVersion("cap", 1))
            .Returns(new CapabilityDescriptor { Id = "cap", Name = "cap", Version = 1 });
        var tasks = new Mock<IHumanTaskRegistry>();
        tasks.Setup(registry => registry.GetByVersion("final", 1))
            .Returns(new HumanTaskDescriptor { Id = "final", Name = "final", Version = 1 });
        var contributor = new WorkflowBindingStatusContributor(
            wfRegistry.Object, schemaRegistry.Object, capabilities.Object, tasks.Object);
        var descriptor = Descriptor(
            new WorkflowStep
            {
                Id = "capability",
                Target = new CapabilityTarget
                {
                    Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap", 1)
                }
            },
            new WorkflowStep
            {
                Id = "conditional",
                Target = HumanTaskTarget("final"),
                Condition = WorkflowConditionTokens.PreviousHumanTaskApproved
            });

        var report = contributor.Evaluate(descriptor);

        report.Status.Should().Be(DescriptorBindingStatus.Invalid);
        report.Issues.Should().Contain(issue => issue.Code == "INVALID_CONDITION");
    }

    [Fact]
    public void Evaluation_DoesNotTreatSeededOutcomeAsCompletedPredecessor()
    {
        var descriptor = Descriptor(
            new WorkflowStep { Id = "initial", Target = HumanTaskTarget("initial") },
            new WorkflowStep
            {
                Id = "conditional",
                Target = HumanTaskTarget("final"),
                Condition = WorkflowConditionTokens.PreviousHumanTaskApproved
            });
        var instance = new WorkflowInstance { StepIndex = 1 };
        instance.Variables["lastStepOutcome"] = StateRegistry().Capture("Approve");

        var act = () => WorkflowConditionPolicy.Evaluate(descriptor, instance, 1, StateRegistry());

        act.Should().Throw<WorkflowValidationException>()
            .WithMessage("*completed result*immediately preceding HumanTask*");
    }

    [Fact]
    public void Evaluation_RejectsMalformedPersistedOutcome()
    {
        var descriptor = Descriptor(
            new WorkflowStep { Id = "initial", Target = HumanTaskTarget("initial") },
            new WorkflowStep
            {
                Id = "conditional",
                Target = HumanTaskTarget("final"),
                Condition = WorkflowConditionTokens.PreviousHumanTaskApproved
            });
        var stateRegistry = StateRegistry();
        var instance = new WorkflowInstance { StepIndex = 1 };
        instance.StepResults.Add(new WorkflowStepResult
        {
            StepId = "initial",
            Status = StepExecutionStatus.Completed
        });
        instance.Variables["lastStepOutcome"] = stateRegistry.Capture(42);

        var act = () => WorkflowConditionPolicy.Evaluate(descriptor, instance, 1, stateRegistry);

        act.Should().Throw<WorkflowValidationException>()
            .WithMessage("*malformed persisted lastStepOutcome*");
    }

    [Theory]
    [InlineData(WorkflowConditionTokens.PreviousHumanTaskApproved, "Approve", true)]
    [InlineData(WorkflowConditionTokens.PreviousHumanTaskApproved, "Reject", false)]
    [InlineData(WorkflowConditionTokens.PreviousHumanTaskRejected, "Approve", false)]
    [InlineData(WorkflowConditionTokens.PreviousHumanTaskRejected, "Reject", true)]
    public void Evaluation_UsesCanonicalApprovedAndRejectedOutcomes(
        string condition, string outcome, bool expected)
    {
        var descriptor = Descriptor(
            new WorkflowStep { Id = "initial", Target = HumanTaskTarget("initial") },
            new WorkflowStep
            {
                Id = "conditional",
                Target = HumanTaskTarget("final"),
                Condition = condition
            });
        var stateRegistry = StateRegistry();
        var instance = new WorkflowInstance { StepIndex = 1 };
        instance.StepResults.Add(new WorkflowStepResult
        {
            StepId = "initial",
            Status = StepExecutionStatus.Completed
        });
        instance.Variables["lastStepOutcome"] = stateRegistry.Capture(outcome);

        WorkflowConditionPolicy.Evaluate(descriptor, instance, 1, stateRegistry)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(null, typeof(WorkflowValidationException))]
    [InlineData(1, typeof(WorkflowValidationException))]
    [InlineData(2, typeof(InvalidOperationException))]
    public async Task Engine_RejectsInvalidConditionOrMissingVersionBeforeAnyEffects(
        int? requestedVersion, Type expectedException)
    {
        var descriptor = Descriptor(
            new WorkflowStep { Id = "side-effecting", Target = HumanTaskTarget("initial") },
            new WorkflowStep
            {
                Id = "invalid-later",
                Target = HumanTaskTarget("final"),
                Condition = " "
            });
        var descriptorProvider = new Mock<IDescriptorProvider<WorkflowDescriptor>>();
        descriptorProvider.Setup(provider => provider.GetDescriptors()).Returns([descriptor]);
        var workflowRegistry = new WorkflowRegistry(new RegistryValidationEngine<WorkflowDescriptor>([]));
        workflowRegistry.Build([descriptorProvider.Object]);
        var store = new Mock<IWorkflowInstanceStore>();
        var transactions = new Mock<IRuntimeTransactionCoordinator>();
        var runner = new RecordingRunner();
        var eventPublisher = new RecordingLifecyclePublisher();
        var contexts = new CrestCreates.Accountability.Context.AuditOperationContextAccessor();
        var pinResolver = new ThrowingPinResolver();
        var engine = new WorkflowEngine(
            workflowRegistry,
            store.Object,
            runner,
            eventPublisher,
            contexts,
            new WorkflowLifecycleEventFactory(
                new GuidAuditIdentityGenerator(),
                new DescriptorStableHashBuilder(new DefaultCanonicalHashComputer())),
            pinResolver,
            StateRegistry(),
            transactions.Object,
            new WorkflowAccountabilityOutboxAppender(null, null, null));

        var act = () => engine.ExecuteAsync(new WorkflowExecutionRequest
        {
            WorkflowId = descriptor.Id,
            WorkflowVersion = requestedVersion,
            TenantId = "tenant"
        });

        var error = await Assert.ThrowsAsync(expectedException, act);
        if (requestedVersion == 2)
            error.Message.Should().Be($"Workflow '{descriptor.Id}' version 2 not found.");
        store.VerifyNoOtherCalls();
        transactions.VerifyNoOtherCalls();
        runner.Calls.Should().Be(0);
        eventPublisher.Calls.Should().Be(0);
    }

    [Fact]
    public void AllPriorValidation_RequiresPredecessorAndOnlyHumanTaskPredecessors()
    {
        var first = Descriptor(new WorkflowStep
        {
            Id = "first", Target = HumanTaskTarget("first"),
            Condition = WorkflowConditionTokens.AllPriorHumanTasksApproved
        });
        var nonHumanPrior = Descriptor(
            new WorkflowStep
            {
                Id = "machine", Target = new CapabilityTarget
                {
                    Capability = new VersionedDescriptorRef<IVersionedDescriptor>("machine", 1)
                }
            },
            new WorkflowStep
            {
                Id = "quality", Target = HumanTaskTarget("quality"),
                Condition = WorkflowConditionTokens.AllPriorHumanTasksApproved
            });

        WorkflowConditionPolicy.GetValidationError(first, 0).Should().Contain("at least one preceding HumanTask");
        WorkflowConditionPolicy.GetValidationError(nonHumanPrior, 1)
            .Should().Contain("every preceding step").And.Contain("'machine'");
    }

    [Theory]
    [InlineData(WorkflowConditionTokens.PreviousHumanTaskApproved)]
    [InlineData(WorkflowConditionTokens.AllPriorHumanTasksApproved)]
    public void AllPriorValidation_AcceptsApprovalGatedPredecessors(string predecessorCondition)
    {
        var descriptor = Descriptor(
            new WorkflowStep { Id = "manager", Target = HumanTaskTarget("manager") },
            new WorkflowStep
            {
                Id = "machine-test", Target = HumanTaskTarget("machine-test"),
                Condition = predecessorCondition
            },
            new WorkflowStep
            {
                Id = "quality", Target = HumanTaskTarget("quality"),
                Condition = WorkflowConditionTokens.AllPriorHumanTasksApproved
            });

        WorkflowConditionPolicy.ValidateDescriptor(descriptor).Should().BeEmpty();
    }

    [Fact]
    public void AllPriorValidation_RejectsRejectionGatedPredecessor()
    {
        var descriptor = Descriptor(
            new WorkflowStep { Id = "manager", Target = HumanTaskTarget("manager") },
            new WorkflowStep
            {
                Id = "rework", Target = HumanTaskTarget("rework"),
                Condition = WorkflowConditionTokens.PreviousHumanTaskRejected
            },
            new WorkflowStep
            {
                Id = "quality", Target = HumanTaskTarget("quality"),
                Condition = WorkflowConditionTokens.AllPriorHumanTasksApproved
            });

        WorkflowConditionPolicy.GetValidationError(descriptor, 2)
            .Should().Contain("only approval-gated conditional predecessors").And.Contain("'rework'");
    }

    [Fact]
    public void AllPriorEvaluation_UsesPerStepProofAndShortCircuitsAtFirstRejection()
    {
        var descriptor = ThreeGates();
        var instance = new WorkflowInstance { StepIndex = 2 };
        instance.StepResults.Add(CompletedHumanTask("manager", CompletionCondition.Reject));
        instance.StepResults.Add(new WorkflowStepResult
        {
            StepId = "machine-test", Status = StepExecutionStatus.Skipped
        });

        WorkflowConditionPolicy.Evaluate(descriptor, instance, 2, StateRegistry()).Should().BeFalse();
    }

    [Fact]
    public void AllPriorEvaluation_RequiresEveryApprovalProof()
    {
        var descriptor = ThreeGates();
        var instance = new WorkflowInstance { StepIndex = 2 };
        instance.StepResults.Add(CompletedHumanTask("manager", CompletionCondition.Approve));
        instance.StepResults.Add(CompletedHumanTask("machine-test", CompletionCondition.Approve));
        instance.Variables["lastStepOutcome"] = StateRegistry().Capture("Approve");

        WorkflowConditionPolicy.Evaluate(descriptor, instance, 2, StateRegistry()).Should().BeTrue();

        instance.StepResults[1] = new WorkflowStepResult
        {
            StepId = "machine-test", Status = StepExecutionStatus.Completed
        };
        var act = () => WorkflowConditionPolicy.Evaluate(descriptor, instance, 2, StateRegistry());
        act.Should().Throw<WorkflowValidationException>().WithMessage("*persisted HumanTask outcome evidence*machine-test*");
    }

    [Fact]
    public void AllPriorEvaluation_FailsOnMissingOrMalformedProofBeforeLaterRejection()
    {
        var descriptor = ThreeGates();
        var instance = new WorkflowInstance { StepIndex = 2 };
        instance.StepResults.Add(new WorkflowStepResult
        {
            StepId = "manager", Status = StepExecutionStatus.Completed
        });
        instance.StepResults.Add(CompletedHumanTask("machine-test", CompletionCondition.Reject));

        var act = () => WorkflowConditionPolicy.Evaluate(descriptor, instance, 2, StateRegistry());
        act.Should().Throw<WorkflowValidationException>().WithMessage("*outcome evidence*manager*");

        instance.StepResults[0] = CompletedHumanTask("manager", (CompletionCondition)99);
        act.Should().Throw<WorkflowValidationException>().WithMessage("*malformed persisted HumanTask outcome*manager*");
    }

    [Fact]
    public void Snapshot_PreservesTypedHumanTaskProofWithoutSharingResultList()
    {
        var instance = new WorkflowInstance();
        instance.StepResults.Add(CompletedHumanTask("manager", CompletionCondition.Approve));

        var snapshot = instance.Snapshot();
        snapshot.StepResults.Should().NotBeSameAs(instance.StepResults);
        snapshot.StepResults[0].HumanTaskOutcome.Should().Be(CompletionCondition.Approve);
        snapshot.StepResults.Clear();
        instance.StepResults.Should().ContainSingle();
    }

    private static WorkflowDescriptor ThreeGates() => Descriptor(
        new WorkflowStep { Id = "manager", Target = HumanTaskTarget("manager") },
        new WorkflowStep
        {
            Id = "machine-test", Target = HumanTaskTarget("machine-test"),
            Condition = WorkflowConditionTokens.PreviousHumanTaskApproved
        },
        new WorkflowStep
        {
            Id = "quality", Target = HumanTaskTarget("quality"),
            Condition = WorkflowConditionTokens.AllPriorHumanTasksApproved
        });

    private static WorkflowStepResult CompletedHumanTask(string id, CompletionCondition outcome) => new()
    {
        StepId = id, Status = StepExecutionStatus.Completed, HumanTaskOutcome = outcome
    };

    private static WorkflowDescriptor Descriptor(params WorkflowStep[] steps) => new()
    {
        Id = "workflow",
        Name = "workflow",
        Version = 1,
        Steps = steps
    };

    private static HumanTaskTarget HumanTaskTarget(string id) => new()
    {
        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>(id, 1)
    };

    private static IHumanTaskRegistry HumanTaskRegistry(params string[] ids)
    {
        var registry = new Mock<IHumanTaskRegistry>();
        registry.Setup(value => value.GetById(It.IsAny<string>()))
            .Returns((string id) => ids.Contains(id, StringComparer.Ordinal)
                ? new HumanTaskDescriptor { Id = id, Name = id, Version = 1 }
                : null);
        return registry.Object;
    }

    private static IRuntimeStateContractRegistry StateRegistry()
    {
        var services = new ServiceCollection();
        services.AddRuntimePersistence();
        return services.BuildServiceProvider().GetRequiredService<IRuntimeStateContractRegistry>();
    }

    private sealed class RecordingRunner : IWorkflowExecutionRunner
    {
        public int Calls { get; private set; }

        public Task<WorkflowInstance> RunAsync(
            WorkflowInstance instance,
            string workflowRunOperationId,
            string? enclosingAuditId,
            CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(instance);
        }
    }

    private sealed class RecordingLifecyclePublisher : IWorkflowLifecycleEventPublisher
    {
        public int Calls { get; private set; }

        public Task PublishAsync(WorkflowLifecycleEvent lifecycleEvent, CancellationToken ct)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingPinResolver : IRuntimeDescriptorPinResolver<WorkflowDescriptor>
    {
        public ResolvedRuntimeDescriptor<WorkflowDescriptor> Capture(WorkflowDescriptor descriptor)
            => throw new InvalidOperationException("The pin resolver must not be reached after condition validation.");

        public ResolvedRuntimeDescriptor<WorkflowDescriptor> Resolve(RuntimeDescriptorPin pin)
            => throw new InvalidOperationException("The pin resolver must not be reached after condition validation.");
    }
}
