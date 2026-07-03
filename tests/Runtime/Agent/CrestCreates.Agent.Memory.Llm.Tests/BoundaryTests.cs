using CrestCreates.Agent.Memory;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Llm.Tests;

public sealed class BoundaryTests
{
    [Fact]
    public void AgentMemoryRuntime_DoesNotReference_MemoryLlm()
    {
        typeof(AgentMemoryServiceCollectionExtensions).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .Should()
            .NotContain("CrestCreates.Agent.Memory.Llm");
    }

    [Fact]
    public void AgentMemoryLlm_DoesNotReference_ForbiddenAgentSurfaces()
    {
        typeof(AgentMemoryLlmAdapterOptions).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .Should()
            .NotContain(new[]
            {
                "CrestCreates.Agent.ControlPlane",
                "CrestCreates.Agent.ControlPlane.Abstractions",
                "CrestCreates.Agent.Authoring.Http",
                "CrestCreates.Platform.Agent",
                "CrestCreates.AspNetCore",
                "CrestCreates.DynamicApi"
            });
    }
}
