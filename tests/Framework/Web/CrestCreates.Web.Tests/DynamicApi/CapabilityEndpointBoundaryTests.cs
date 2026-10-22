using System.Linq;
using System.Reflection;
using CrestCreates.DynamicApi;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Web.Tests.DynamicApi;

public class CapabilityEndpointBoundaryTests
{
    [Fact]
    public void DynamicApi_Abstractions_DoesNotReference_DynamicApi_Implementation()
    {
        var refs = typeof(CapabilityEndpointDescriptor)
            .Assembly
            .GetReferencedAssemblies()
            .Select(x => x.Name)
            .ToArray();

        var forbiddenRefs = new[] { "CrestCreates.DynamicApi" };

        foreach (var forbidden in forbiddenRefs)
        {
            refs.Should().NotContain(forbidden,
                because: "Abstractions must not reference implementation");
        }
    }

    [Fact]
    public void DynamicApi_Abstractions_Csproj_DoesNotReference_DynamicApi_Csproj()
    {
        var abstractionsCsproj = FindProjectFile("CrestCreates.DynamicApi.Abstractions.csproj");
        var content = File.ReadAllText(abstractionsCsproj);

        content.Should().NotContain("CrestCreates.DynamicApi.csproj",
            because: "Abstractions project must not reference implementation project");
    }

    [Fact]
    public void DynamicApi_Abstractions_DoesNotDefine_Legacy_Runtime_Types()
    {
        var assembly = typeof(CapabilityEndpointDescriptor).Assembly;
        var typeNames = assembly.GetTypes().Select(t => t.FullName).ToArray();

        var forbiddenTypes = new[]
        {
            "CrestCreates.DynamicApi.DynamicApiGeneratedRegistryStore",
            "CrestCreates.DynamicApi.DynamicApiGeneratedRuntime",
            "CrestCreates.DynamicApi.IDynamicApiGeneratedProvider"
        };

        foreach (var forbiddenType in forbiddenTypes)
        {
            typeNames.Should().NotContain(forbiddenType,
                because: "Abstractions assembly must not define legacy runtime types");
        }
    }

    [Fact]
    public void CapabilityEndpoint_Mapping_DoesNotReference_Legacy_AppService_Concepts()
    {
        var files = new[]
        {
            "CapabilityEndpointExtensions.cs",
            "CapabilityEndpointDescriptorValidator.cs",
            "CapabilityEndpointCapabilityResolver.cs",
            "CapabilityEndpointMapper.cs"
        };

        var forbidden = new[]
        {
            "DynamicApiGeneratedRegistryStore",
            "DynamicApiGeneratedRuntime",
            "IDynamicApiGeneratedProvider",
            "DynamicApiEndpointDescriptor",
            "DynamicApiServiceDescriptor",
            "DynamicApiActionDescriptor"
        };

        foreach (var file in files)
        {
            var path = Path.Combine(FindRepoRoot(), "src/Framework/Api/CrestCreates.DynamicApi", file);
            if (!File.Exists(path))
                continue; // File may not exist in all configurations

            var content = File.ReadAllText(path);

            foreach (var symbol in forbidden)
            {
                content.Should().NotContain(symbol,
                    because: $"CapabilityEndpoint mapping file {file} must not reference legacy AppService concept {symbol}");
            }
        }
    }

    [Fact]
    public void Legacy_projects_are_absent_from_the_build_graph_and_solutions()
    {
        var repoRoot = FindRepoRoot();
        var legacyNames = new[]
        {
            "CrestCreates.DynamicApi.Legacy",
            "CrestCreates.CodeGenerator.Legacy",
            "CrestCreates.Web.Legacy",
        };

        foreach (var legacyName in legacyNames)
        {
            Directory.Exists(Path.Combine(repoRoot, "src/Framework/Api", legacyName)).Should().BeFalse();
            Directory.Exists(Path.Combine(repoRoot, "src/Platform", legacyName)).Should().BeFalse();
            Directory.Exists(Path.Combine(repoRoot, "src/Tooling", legacyName)).Should().BeFalse();
        }

        var projectFiles = Directory.EnumerateFiles(repoRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}99_RecycleBin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        foreach (var projectFile in projectFiles)
        {
            var name = Path.GetFileNameWithoutExtension(projectFile);
            legacyNames.Should().NotContain(name,
                because: $"archived legacy project {name} must not remain in the build graph");
            var content = File.ReadAllText(projectFile);
            foreach (var legacyName in legacyNames)
            {
                content.Should().NotContain(legacyName,
                    because: $"{Path.GetRelativePath(repoRoot, projectFile)} must not reference the archived legacy package");
            }
        }

        var solutions = Directory.EnumerateFiles(repoRoot, "*.slnx", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}99_RecycleBin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();
        solutions.Should().NotBeEmpty();
        foreach (var solution in solutions)
        {
            var content = File.ReadAllText(solution);
            foreach (var legacyName in legacyNames)
            {
                content.Should().NotContain(legacyName,
                    because: $"{Path.GetRelativePath(repoRoot, solution)} must not include the archived legacy project");
            }
        }
    }

    [Fact]
    public void Formal_assemblies_do_not_carry_or_reference_legacy_types()
    {
        var formal = typeof(DynamicApiResponse).Assembly;
        formal.GetName().Name.Should().Be("CrestCreates.DynamicApi");

        foreach (var legacyType in new[]
                 {
                     "CrestCreates.DynamicApi.DynamicApiGeneratedRuntime",
                     "CrestCreates.DynamicApi.DynamicApiGeneratedRegistryStore",
                     "CrestCreates.DynamicApi.DynamicApiRegistry",
                     "CrestCreates.DynamicApi.IDynamicApiGeneratedProvider",
                     "CrestCreates.DynamicApi.DynamicApiOptions",
                     "CrestCreates.DynamicApi.CompatibilityBodyReader",
                 })
        {
            formal.GetType(legacyType, throwOnError: false).Should().BeNull(
                $"{legacyType} belongs to the archived legacy runtime");
        }

        var web = typeof(CrestCreates.Web.CrestWebOptions).Assembly;
        var references = web.GetReferencedAssemblies().Select(name => name.Name).ToArray();
        references.Should().NotContain("CrestCreates.DynamicApi.Legacy");
        references.Should().NotContain("CrestCreates.Web.Legacy");
    }

    [Fact]
    public void CapabilityEndpoint_Emitter_DoesNotEmit_Legacy_Symbols()
    {
        var emitterFiles = new[]
        {
            "CapabilityEndpointBindingEmitter.cs",
            "CapabilityEndpointProviderEmitter.cs"
        };

        var forbiddenPatterns = new[]
        {
            "DynamicApiEndpointDescriptor",
            "DynamicApiServiceDescriptor",
            "DynamicApiActionDescriptor",
            "IDynamicApiGeneratedProvider",
            "ServiceType =",
            ".ServiceType",
            "ActionName =",
            ".ActionName"
        };

        foreach (var file in emitterFiles)
        {
            var path = Path.Combine(FindRepoRoot(), "src/Tooling/CrestCreates.CodeGenerator/CapabilityEndpointGenerator", file);
            File.Exists(path).Should().BeTrue($"expected emitter file {file} to exist");
            var content = File.ReadAllText(path);

            foreach (var pattern in forbiddenPatterns)
            {
                content.Should().NotContain(pattern,
                    because: $"CapabilityEndpoint emitter {file} must not emit legacy symbol {pattern}");
            }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            // A worktree has a .git file (gitdir pointer), an exported snapshot may have no .git at
            // all; the checked-in solution marker is the stable repository root either way.
            if (File.Exists(Path.Combine(dir, "CrestCreates.slnx"))
                || Directory.Exists(Path.Combine(dir, ".git"))
                || File.Exists(Path.Combine(dir, ".git")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find repository root (.git or CrestCreates.slnx)");
    }

    private static string FindProjectFile(string projectFileName)
    {
        var repoRoot = FindRepoRoot();
        var matches = Directory.GetFiles(repoRoot, projectFileName, SearchOption.AllDirectories);
        matches.Should().NotBeEmpty($"expected to find {projectFileName}");
        return matches.First();
    }
}
