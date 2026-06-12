using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class BindingStatusSynthesizerTests
{
    [Fact]
    public void SynthesizeStatus_EmptyIssues_ReturnsRuntimeReady()
    {
        var result = BindingStatusSynthesizer.SynthesizeStatus(Array.Empty<DescriptorBindingIssue>());
        result.Should().Be(DescriptorBindingStatus.RuntimeReady);
    }

    [Fact]
    public void SynthesizeStatus_RefError_ReturnsInvalid()
    {
        var issues = new[] { new DescriptorBindingIssue(ValidationSeverity.Error, "REF_MISSING_SCHEMA", "Schema missing") };
        var result = BindingStatusSynthesizer.SynthesizeStatus(issues);
        result.Should().Be(DescriptorBindingStatus.Invalid);
    }

    [Fact]
    public void SynthesizeStatus_BindError_ReturnsUnbound()
    {
        var issues = new[] { new DescriptorBindingIssue(ValidationSeverity.Error, "BIND_NO_HANDLER", "No handler") };
        var result = BindingStatusSynthesizer.SynthesizeStatus(issues);
        result.Should().Be(DescriptorBindingStatus.Unbound);
    }

    [Fact]
    public void SynthesizeStatus_UnsupportedError_ReturnsUnsupported()
    {
        var issues = new[] { new DescriptorBindingIssue(ValidationSeverity.Error, "UNSUPPORTED_RETRY", "Retry not supported") };
        var result = BindingStatusSynthesizer.SynthesizeStatus(issues);
        result.Should().Be(DescriptorBindingStatus.Unsupported);
    }

    [Fact]
    public void SynthesizeStatus_WarningOnly_ReturnsPartiallyBound()
    {
        var issues = new[] { new DescriptorBindingIssue(ValidationSeverity.Warning, "WARN_DEPRECATED", "Deprecated") };
        var result = BindingStatusSynthesizer.SynthesizeStatus(issues);
        result.Should().Be(DescriptorBindingStatus.PartiallyBound);
    }

    [Fact]
    public void SynthesizeStatus_MixedErrors_RefTakesPriority()
    {
        var issues = new[]
        {
            new DescriptorBindingIssue(ValidationSeverity.Error, "UNSUPPORTED_RETRY", "Retry unsupported"),
            new DescriptorBindingIssue(ValidationSeverity.Error, "REF_MISSING_SCHEMA", "Schema missing")
        };
        var result = BindingStatusSynthesizer.SynthesizeStatus(issues);
        result.Should().Be(DescriptorBindingStatus.Invalid);
    }
}
