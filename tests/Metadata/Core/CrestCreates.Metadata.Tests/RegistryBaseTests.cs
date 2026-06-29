using System.Collections.Frozen;
using System.Collections.Immutable;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Metadata.Tests;

public class RegistryBaseTests
{
    private class TestDescriptor : IDescriptor
    {
        public string Namespace { get; init; } = "test";
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public DescriptorKind Kind { get; init; }
        public DescriptorState State { get; init; } = DescriptorState.Active;
        public string ContractHash { get; init; } = string.Empty;
        public string DefinitionHash { get; init; } = string.Empty;
        public string? SupersededById { get; init; }
    }

    private class TestProvider : IDescriptorProvider<TestDescriptor>
    {
        private readonly List<TestDescriptor> _descriptors;
        public TestProvider(List<TestDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<TestDescriptor> GetDescriptors() => _descriptors;
    }

    private class TestRegistry : RegistryBase<TestDescriptor>
    {
        protected override string RegistryNamespace => "test";

        public TestRegistry(IRegistryValidationEngine<TestDescriptor> engine) : base(engine) { }

        protected override RegistrySnapshot<TestDescriptor> BuildSnapshot(List<TestDescriptor> descriptors)
        {
            var byId = descriptors.ToFrozenDictionary(d => d.Id, d => d);
            var byName = descriptors.GroupBy(d => d.Name)
                .ToFrozenDictionary(g => g.Key, g => g.ToImmutableArray());
            var byVersion = descriptors
                .ToFrozenDictionary(d => new DescriptorKey(d.Namespace, d.Id, 1));
            return new RegistrySnapshot<TestDescriptor>(byId, byName,
                byVersion,
                descriptors.ToImmutableArray(),
                ImmutableDictionary<Type, IRegistryIndex>.Empty);
        }
    }

    [Fact]
    public void Build_sets_state_to_Built()
    {
        var engine = new RegistryValidationEngine<TestDescriptor>(Array.Empty<IRegistryValidator<TestDescriptor>>());
        var registry = new TestRegistry(engine);
        var provider = new TestProvider([new TestDescriptor { Id = "a", Name = "A" }]);
        registry.Build([provider]);
        registry.State.Should().Be(RegistryState.Built);
    }

    [Fact]
    public void Build_is_idempotent()
    {
        var engine = new RegistryValidationEngine<TestDescriptor>(Array.Empty<IRegistryValidator<TestDescriptor>>());
        var registry = new TestRegistry(engine);
        var provider = new TestProvider([new TestDescriptor { Id = "a", Name = "A" }]);
        registry.Build([provider]);
        registry.Build([provider]);
        registry.State.Should().Be(RegistryState.Built);
    }

    [Fact]
    public void GetById_returns_descriptor()
    {
        var engine = new RegistryValidationEngine<TestDescriptor>(Array.Empty<IRegistryValidator<TestDescriptor>>());
        var registry = new TestRegistry(engine);
        var provider = new TestProvider([new TestDescriptor { Id = "a", Name = "A" }]);
        registry.Build([provider]);
        registry.GetById("a")!.Name.Should().Be("A");
    }

    [Fact]
    public void GetById_returns_null_for_unknown()
    {
        var engine = new RegistryValidationEngine<TestDescriptor>(Array.Empty<IRegistryValidator<TestDescriptor>>());
        var registry = new TestRegistry(engine);
        var provider = new TestProvider([new TestDescriptor { Id = "a", Name = "A" }]);
        registry.Build([provider]);
        registry.GetById("unknown").Should().BeNull();
    }

    [Fact]
    public void GetByName_returns_all_versions()
    {
        var engine = new RegistryValidationEngine<TestDescriptor>(Array.Empty<IRegistryValidator<TestDescriptor>>());
        var registry = new TestRegistry(engine);
        var provider = new TestProvider([
            new TestDescriptor { Id = "a1", Name = "A" },
            new TestDescriptor { Id = "a2", Name = "A" }
        ]);
        registry.Build([provider]);
        registry.GetByName("A").Should().HaveCount(2);
    }

    [Fact]
    public void GetAll_returns_all_descriptors()
    {
        var engine = new RegistryValidationEngine<TestDescriptor>(Array.Empty<IRegistryValidator<TestDescriptor>>());
        var registry = new TestRegistry(engine);
        var provider = new TestProvider([
            new TestDescriptor { Id = "a", Name = "A" },
            new TestDescriptor { Id = "b", Name = "B" }
        ]);
        registry.Build([provider]);
        registry.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public void Build_with_failing_validator_sets_Failed_state()
    {
        var validator = new FailingValidator();
        var engine = new RegistryValidationEngine<TestDescriptor>([validator]);
        var registry = new TestRegistry(engine);
        var provider = new TestProvider([new TestDescriptor { Id = "a", Name = "A" }]);
        var act = () => registry.Build([provider]);
        act.Should().Throw<RegistryValidationException>();
        registry.State.Should().Be(RegistryState.Failed);
    }

    [Fact]
    public void Build_after_Failed_throws_InvalidOperationException()
    {
        var validator = new FailingValidator();
        var engine = new RegistryValidationEngine<TestDescriptor>([validator]);
        var registry = new TestRegistry(engine);
        var provider = new TestProvider([new TestDescriptor { Id = "a", Name = "A" }]);
        try { registry.Build([provider]); } catch { }
        var act = () => registry.Build([provider]);
        act.Should().Throw<InvalidOperationException>().WithMessage("*previously failed*");
    }

    [Fact]
    public void GetByVersion_returns_specific_version()
    {
        var engine = new RegistryValidationEngine<TestDescriptor>(Array.Empty<IRegistryValidator<TestDescriptor>>());
        var registry = new TestRegistry(engine);
        var provider = new TestProvider([
            new TestDescriptor { Id = "a", Name = "A" }
        ]);
        registry.Build([provider]);
        registry.GetByVersion("a", 1).Should().NotBeNull();
    }

    [Fact]
    public void Build_collects_all_errors_not_just_first()
    {
        var validators = new List<IRegistryValidator<TestDescriptor>>
        {
            new ErrorValidator("Error 1"),
            new ErrorValidator("Error 2"),
            new ErrorValidator("Error 3")
        };
        var engine = new RegistryValidationEngine<TestDescriptor>(validators);
        var registry = new TestRegistry(engine);
        var provider = new TestProvider([new TestDescriptor { Id = "a", Name = "A" }]);
        var act = () => registry.Build([provider]);
        act.Should().Throw<RegistryValidationException>().Which.Issues.Should().HaveCount(3);
    }

    private class FailingValidator : IRegistryValidator<TestDescriptor>
    {
        public int Order => 0;
        public ValidationReport Validate(IReadOnlyList<TestDescriptor> descriptors)
            => ValidationReport.FromIssues(new ValidationIssue(SeverityLevel.Error, "Always fails"));
    }

    private class ErrorValidator : IRegistryValidator<TestDescriptor>
    {
        private readonly string _message;
        public ErrorValidator(string message) => _message = message;
        public int Order => 0;
        public ValidationReport Validate(IReadOnlyList<TestDescriptor> descriptors)
            => ValidationReport.FromIssues(new ValidationIssue(SeverityLevel.Error, _message));
    }
}
