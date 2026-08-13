using System.Reflection;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Promotion;
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

    [Fact]
    public void PromotionService_HasNoLegacyFallbackBranches()
    {
        var methods = typeof(DefaultAgentMemoryPromotionService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToArray();

        methods.Should().NotContain(name => name.Contains("Legacy", StringComparison.Ordinal));
        methods.Should().NotContain(name => name.Contains("Fallback", StringComparison.Ordinal));

        // Every convenience overload must route through hash projection + the
        // conditional transition; there is no Get/Save Archive path.
        var archive = typeof(IAgentMemoryPromotionService).GetMethod("ArchiveAsync");
        archive.Should().NotBeNull();

        // Hash projector is a required, non-nullable constructor dependency.
        var ctor = typeof(DefaultAgentMemoryPromotionService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single();
        var hashesParam = ctor.GetParameters()
            .Single(p => p.Name == "hashes");
        hashesParam.ParameterType.Should().Be(typeof(CrestCreates.Agent.Memory.CanonicalHashing.AgentMemoryCanonicalHashProjector));
        hashesParam.IsOptional.Should().BeFalse();
    }

    [Fact]
    public void ConditionalCurationStore_SurfacesAtomicArchive()
    {
        var archive = typeof(IAgentMemoryConditionalCurationStore).GetMethod("ArchiveAsync");
        archive.Should().NotBeNull();
        archive!.ReturnType.Should().Be(typeof(ValueTask<AgentMemoryItem>));
    }
}
