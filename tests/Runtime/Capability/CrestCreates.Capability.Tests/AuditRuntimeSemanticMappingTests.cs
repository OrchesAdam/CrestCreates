using System.Reflection;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public sealed class AuditRuntimeSemanticMappingTests
{
    private static readonly IReadOnlyDictionary<InvocationSource, string> Expected =
        new Dictionary<InvocationSource, string>
        {
            [InvocationSource.Http] = "http",
            [InvocationSource.Workflow] = "workflow",
            [InvocationSource.HumanTask] = "human-task",
            [InvocationSource.Agent] = "agent",
            [InvocationSource.Mcp] = "mcp",
            [InvocationSource.Event] = "integration",
            [InvocationSource.BackgroundJob] = "system",
            [InvocationSource.Internal] = "system"
        };

    [Fact]
    public void InvocationSourceUsesStableExplicitMapping()
    {
        foreach (var (source, expected) in Expected)
            Map(source).Should().Be(expected);
    }

    [Fact]
    public void InvocationSourceMappingIsExhaustive()
    {
        Enum.GetValues<InvocationSource>().Should().BeEquivalentTo(Expected.Keys);
        Enum.GetValues<InvocationSource>().Should().OnlyContain(source =>
            !string.IsNullOrWhiteSpace(Map(source)));
    }

    [Fact]
    public void ProducerIncludesStableServiceAndApplicationReferencesWhenKnown()
    {
        var runtime = new AuditRuntimeContext
        {
            InvocationSource = "http",
            References =
            [
                new AuditRuntimeReference("application", "procurement"),
                new AuditRuntimeReference("service", "procurement-application")
            ]
        };

        runtime.References.Should().Contain(new AuditRuntimeReference("application", "procurement"));
        runtime.References.Should().Contain(new AuditRuntimeReference("service", "procurement-application"));
    }

    private static string Map(InvocationSource source)
    {
        var method = typeof(AuditMiddleware).GetMethod(
            "MapSource",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Capability source mapper was not found.");
        return (string)method.Invoke(null, [source])!;
    }
}
