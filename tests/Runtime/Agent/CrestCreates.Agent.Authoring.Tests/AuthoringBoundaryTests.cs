using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Authoring.Tests;

public sealed class AuthoringBoundaryTests
{
    [Fact]
    public void AuthoringAbstractions_DoNotReference_ControlPlane()
    {
        typeof(IDescriptorAuthoringAgent).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .Should()
            .NotContain(new[]
            {
                "CrestCreates.Agent.ControlPlane",
                "CrestCreates.Agent.ControlPlane.Abstractions"
            });
    }

    [Fact]
    public void AuthoringAbstractions_DoNotReference_Http_Or_ProviderSdk()
    {
        typeof(IDescriptorAuthoringAgent).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .Should()
            .NotContain(new[]
            {
                "CrestCreates.Agent.Authoring.Http",
                "OpenAI",
                "Azure.AI.OpenAI",
                "Anthropic"
            });
    }

    [Fact]
    public void AuthoringRuntime_DoNotReference_ControlPlane()
    {
        typeof(AgentAuthoringServiceCollectionExtensions).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .Should()
            .NotContain(new[]
            {
                "CrestCreates.Agent.ControlPlane",
                "CrestCreates.Agent.ControlPlane.Abstractions",
                "CrestCreates.Agent.DraftContracts"
            });
    }

    [Fact]
    public void AuthoringRuntime_DoNotReference_Http_Or_ProviderSdk()
    {
        typeof(AgentAuthoringServiceCollectionExtensions).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .Should()
            .NotContain(new[]
            {
                "CrestCreates.Agent.Authoring.Http",
                "OpenAI",
                "Azure.AI.OpenAI"
            });
    }
}
