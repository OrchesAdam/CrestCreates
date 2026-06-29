using CrestCreates.Agent.Memory.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Tests;

public sealed class BoundaryTests
{
    [Fact]
    public void AgentMemoryAbstractionsAssembly_DoesNotReference_ControlPlaneAbstractions()
    {
        typeof(AgentMemoryDiagnostic).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .Should()
            .NotContain("CrestCreates.Agent.ControlPlane.Abstractions");
    }

    [Fact]
    public void AgentMemoryRuntimeAssembly_DoesNotReference_ControlPlane()
    {
        typeof(AgentMemoryServiceCollectionExtensions).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .Should()
            .NotContain(new[]
            {
                "CrestCreates.Agent.ControlPlane",
                "CrestCreates.Agent.ControlPlane.Abstractions"
            });
    }
}
