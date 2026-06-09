using CrestCreates.Metadata.Abstractions;
using CrestCreates.Event.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class EventValidatorTests
{
    [Fact]
    public void VersionChainValidator_fails_when_no_active_version()
    {
        var validator = new EventVersionChainValidator();
        var descriptors = new List<GeneratedEventDescriptor>
        {
            Create("test", 1, DescriptorState.Deprecated)
        };
        var report = validator.Validate(descriptors);
        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("no Active version"));
    }

    [Fact]
    public void VersionChainValidator_fails_when_multiple_active()
    {
        var validator = new EventVersionChainValidator();
        var descriptors = new List<GeneratedEventDescriptor>
        {
            Create("test", 1, DescriptorState.Active),
            Create("test", 2, DescriptorState.Active)
        };
        var report = validator.Validate(descriptors);
        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("Active versions"));
    }

    [Fact]
    public void VersionChainValidator_fails_when_highest_not_active()
    {
        var validator = new EventVersionChainValidator();
        var descriptors = new List<GeneratedEventDescriptor>
        {
            Create("test", 1, DescriptorState.Active),
            Create("test", 2, DescriptorState.Deprecated)
        };
        var report = validator.Validate(descriptors);
        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("highest version"));
    }

    [Fact]
    public void VersionChainValidator_passes_with_single_active_highest()
    {
        var validator = new EventVersionChainValidator();
        var descriptors = new List<GeneratedEventDescriptor>
        {
            Create("test", 1, DescriptorState.Deprecated),
            Create("test", 2, DescriptorState.Active)
        };
        var report = validator.Validate(descriptors);
        report.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void DuplicateNameVersionValidator_finds_duplicates()
    {
        var validator = new DuplicateNameVersionValidator();
        var descriptors = new List<GeneratedEventDescriptor>
        {
            Create("test", 1, DescriptorState.Active),
            Create("test", 1, DescriptorState.Active)
        };
        var report = validator.Validate(descriptors);
        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("Duplicate"));
    }

    [Fact]
    public void UniquePayloadTypeValidator_finds_conflicts()
    {
        var validator = new UniquePayloadTypeValidator();
        var descriptors = new List<GeneratedEventDescriptor>
        {
            Create("test.a", 1, DescriptorState.Active, typeof(string)),
            Create("test.b", 1, DescriptorState.Active, typeof(string))
        };
        var report = validator.Validate(descriptors);
        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("PayloadType"));
    }

    private static GeneratedEventDescriptor Create(string name, int version, DescriptorState state, Type? payloadType = null)
        => new()
        {
            Id = GeneratedEventDescriptor.GenerateId(name),
            Name = name,
            Version = version,
            State = state,
            PayloadType = payloadType ?? typeof(object),
            Scope = EventScope.Local,
            Reliability = EventReliability.AtLeastOnce,
            Importance = EventImportance.Business
        };
}
