using CrestCreates.Capability.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public class WorkflowCompatibilityValidatorTests
{
    private static WorkflowDescriptor CreateDescriptorWithStep(InteractionTarget target,
        StepErrorBehavior onError = StepErrorBehavior.Fail,
        IReadOnlyList<string>? transitions = null)
    {
        return new WorkflowDescriptor
        {
            Id = "wf_test", Name = "test.wf", Version = 1,
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    Id = "step_01", Name = "Test Step",
                    Target = target,
                    OnError = onError,
                    Transitions = transitions ?? Array.Empty<string>()
                }
            }
        };
    }

    private static WorkflowCompatibilityValidator CreateValidator(
        string[]? capabilityIds = null,
        string[]? humanTaskIds = null)
    {
        var capRegistry = new InMemoryCapabilityRegistry(capabilityIds ?? Array.Empty<string>());
        var htRegistry = new InMemoryHumanTaskRegistry(humanTaskIds ?? Array.Empty<string>());
        return new WorkflowCompatibilityValidator(capRegistry, htRegistry);
    }

    private sealed class InMemoryCapabilityRegistry : ICapabilityRegistry
    {
        private readonly HashSet<string> _ids;
        public InMemoryCapabilityRegistry(string[] ids) => _ids = new HashSet<string>(ids);

        public CapabilityDescriptor? GetById(string id)
            => _ids.Contains(id) ? new CapabilityDescriptor { Id = id, Name = id, Version = 1 } : null;
        public CapabilityDescriptor? GetByName(string name) => GetById(name);
        public IReadOnlyList<CapabilityDescriptor> GetAll()
            => _ids.Select(id => new CapabilityDescriptor { Id = id, Name = id, Version = 1 }).ToList();
        public CapabilityDescriptor? GetByNameAndVersion(string name, int version) => null;
        public CapabilityDescriptor? GetByVersion(string id, int version) => GetById(id);
        public IReadOnlyList<CapabilityDescriptor> GetAllByName(string name) => Array.Empty<CapabilityDescriptor>();
        public CapabilityDescriptor? GetActiveVersion(string name) => GetById(name);
        public CapabilityDescriptor? GetLatestVersion(string name) => GetById(name);
        public IReadOnlyList<CapabilityDescriptor> GetDeprecatedVersions(string name) => Array.Empty<CapabilityDescriptor>();
        public IReadOnlyList<CapabilityDescriptor> GetByKind(CapabilityKind kind) => Array.Empty<CapabilityDescriptor>();
        public IReadOnlyList<CapabilityDescriptor> GetByTag(string tag) => Array.Empty<CapabilityDescriptor>();
        void IDescriptorRegistry<CapabilityDescriptor>.Build(IEnumerable<IDescriptorProvider<CapabilityDescriptor>> providers) { }
    }

    private sealed class InMemoryHumanTaskRegistry : IHumanTaskRegistry
    {
        private readonly HashSet<string> _ids;
        public InMemoryHumanTaskRegistry(string[] ids) => _ids = new HashSet<string>(ids);

        public HumanTaskDescriptor? GetById(string id)
            => _ids.Contains(id) ? new HumanTaskDescriptor { Id = id, Name = id, Version = 1 } : null;
        public HumanTaskDescriptor? GetByName(string name) => GetById(name);
        public IReadOnlyList<HumanTaskDescriptor> GetAll()
            => _ids.Select(id => new HumanTaskDescriptor { Id = id, Name = id, Version = 1 }).ToList();
        public HumanTaskDescriptor? GetByNameAndVersion(string name, int version) => null;
        public HumanTaskDescriptor? GetByVersion(string id, int version) => GetById(id);
        public IReadOnlyList<HumanTaskDescriptor> GetAllByName(string name) => Array.Empty<HumanTaskDescriptor>();
        public HumanTaskDescriptor? GetActiveVersion(string name) => GetById(name);
        public HumanTaskDescriptor? GetLatestVersion(string name) => GetById(name);
        public IReadOnlyList<HumanTaskDescriptor> GetDeprecatedVersions(string name) => Array.Empty<HumanTaskDescriptor>();
        void IDescriptorRegistry<HumanTaskDescriptor>.Build(IEnumerable<IDescriptorProvider<HumanTaskDescriptor>> providers) { }
    }

    [Fact]
    public void Validate_SubWorkflowTarget_Throws()
    {
        var descriptor = CreateDescriptorWithStep(
            new SubWorkflowTarget
            {
                SubWorkflow = new VersionedDescriptorRef<WorkflowDescriptor>("wf_sub", 1)
            });

        var validator = CreateValidator();
        var act = () => validator.Validate(descriptor);

        act.Should().Throw<WorkflowValidationException>()
            .WithMessage("*SubWorkflowTarget*");
    }

    [Fact]
    public void Validate_RetryErrorBehavior_Throws()
    {
        var descriptor = CreateDescriptorWithStep(
            new HumanTaskTarget
            {
                HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
            },
            onError: StepErrorBehavior.Retry);

        var validator = CreateValidator(humanTaskIds: ["ht_01"]);
        var act = () => validator.Validate(descriptor);

        act.Should().Throw<WorkflowValidationException>()
            .WithMessage("*StepErrorBehavior.Retry*");
    }

    [Fact]
    public void Validate_CompensateErrorBehavior_Throws()
    {
        var descriptor = CreateDescriptorWithStep(
            new HumanTaskTarget
            {
                HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
            },
            onError: StepErrorBehavior.Compensate);

        var validator = CreateValidator(humanTaskIds: ["ht_01"]);
        var act = () => validator.Validate(descriptor);

        act.Should().Throw<WorkflowValidationException>()
            .WithMessage("*StepErrorBehavior.Compensate*");
    }

    [Fact]
    public void Validate_Transitions_Throws()
    {
        var descriptor = CreateDescriptorWithStep(
            new HumanTaskTarget
            {
                HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
            },
            transitions: new List<string> { "step_02" });

        var validator = CreateValidator(humanTaskIds: ["ht_01"]);
        var act = () => validator.Validate(descriptor);

        act.Should().Throw<WorkflowValidationException>()
            .WithMessage("*transition*");
    }

    [Fact]
    public void Validate_ValidDescriptor_DoesNotThrow()
    {
        var descriptor = CreateDescriptorWithStep(
            new CapabilityTarget
            {
                Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_01", 1)
            },
            onError: StepErrorBehavior.Skip);

        var validator = CreateValidator(capabilityIds: ["cap_01"]);
        var act = () => validator.Validate(descriptor);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_FailErrorBehavior_DoesNotThrow()
    {
        var descriptor = CreateDescriptorWithStep(
            new CapabilityTarget
            {
                Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_01", 1)
            },
            onError: StepErrorBehavior.Fail);

        var validator = CreateValidator(capabilityIds: ["cap_01"]);
        var act = () => validator.Validate(descriptor);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_CapabilityTarget_MissingReference_Throws()
    {
        var descriptor = CreateDescriptorWithStep(
            new CapabilityTarget
            {
                Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_missing", 1)
            });

        var validator = CreateValidator(capabilityIds: ["cap_01", "cap_02"]);
        var act = () => validator.Validate(descriptor);

        act.Should().Throw<WorkflowValidationException>()
            .WithMessage("*cap_missing*");
    }

    [Fact]
    public void Validate_HumanTaskTarget_MissingReference_Throws()
    {
        var descriptor = CreateDescriptorWithStep(
            new HumanTaskTarget
            {
                HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_missing", 1)
            });

        var validator = CreateValidator(humanTaskIds: ["ht_01", "ht_02"]);
        var act = () => validator.Validate(descriptor);

        act.Should().Throw<WorkflowValidationException>()
            .WithMessage("*ht_missing*");
    }

    [Fact]
    public void Validate_CapabilityTarget_ExistingReference_DoesNotThrow()
    {
        var descriptor = CreateDescriptorWithStep(
            new CapabilityTarget
            {
                Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_01", 1)
            });

        var validator = CreateValidator(capabilityIds: ["cap_01"]);
        var act = () => validator.Validate(descriptor);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_HumanTaskTarget_ExistingReference_DoesNotThrow()
    {
        var descriptor = CreateDescriptorWithStep(
            new HumanTaskTarget
            {
                HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
            });

        var validator = CreateValidator(humanTaskIds: ["ht_01"]);
        var act = () => validator.Validate(descriptor);

        act.Should().NotThrow();
    }
}
