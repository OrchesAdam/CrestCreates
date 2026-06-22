using Xunit;
using FluentAssertions;
using CrestCreates.Agent.ControlPlane.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Tests;

public class DescriptorReviewMessageTemplateCatalogTests
{
    private readonly IDescriptorReviewMessageTemplateCatalog _catalog = new DefaultDescriptorReviewMessageTemplateCatalog();

    [Fact]
    public void TemplateVersion_IsPresent()
    {
        _catalog.TemplateVersion.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Format_KnownTemplate_ReturnsFormattedMessage()
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DiagnosticCount"] = "5"
        };
        var result = _catalog.Format("report.summary.valid", parameters);

        result.Should().Be("Draft validation passed with 5 diagnostics.");
    }

    [Fact]
    public void Format_KnownTemplate_WithMissingParam_LeavesPlaceholder()
    {
        var result = _catalog.Format("report.summary.valid", new Dictionary<string, string>(StringComparer.Ordinal));

        result.Should().Contain("{DiagnosticCount}");
    }

    [Fact]
    public void Format_UnknownTemplate_ReturnsPlaceholder()
    {
        var result = _catalog.Format("unknown.template", new Dictionary<string, string>(StringComparer.Ordinal));

        result.Should().Be("[Unknown template: unknown.template]");
    }

    [Fact]
    public void Format_UnknownTemplate_ReturnsPlaceholder_RegardlessOfParameters()
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Key"] = "Value"
        };
        var result = _catalog.Format("non.existent.template", parameters);

        result.Should().Be("[Unknown template: non.existent.template]");
    }

    [Fact]
    public void Format_DeterministicAcrossCalls()
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ErrorCount"] = "2",
            ["BlockerCount"] = "1"
        };

        var result1 = _catalog.Format("report.summary.invalid", parameters);
        var result2 = _catalog.Format("report.summary.invalid", parameters);

        result1.Should().Be(result2);
        result1.Should().Be("Draft validation failed with 2 errors and 1 blockers.");
    }

    [Fact]
    public void Format_WithExtraParams_IgnoresThem()
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AffectedCount"] = "10",
            ["ExtraUnused"] = "ignored"
        };
        var result = _catalog.Format("report.impact.affected", parameters);

        result.Should().Be("Impact analysis found 10 affected descriptors.");
    }

    [Fact]
    public void Format_TemplateVersion_IsStable()
    {
        _catalog.TemplateVersion.Should().Be("7d.v1");
    }
}
