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
            "CapabilityEndpointCapabilityResolver.cs"
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
    public void Legacy_DynamicApi_Source_DoesNotReference_CapabilityEndpoint_Runtime()
    {
        var files = new[]
        {
            "DynamicApiExtensions.cs",
            "DynamicApiGeneratedRegistryStore.cs",
            "DynamicApiGeneratedRuntime.cs"
        };

        var forbidden = new[]
        {
            "ICapabilityDispatcher",
            "CapabilityEndpointMapper",
            "MapCrestCapabilityEndpoints",
            "CapabilityEndpointBindingRegistry"
        };

        foreach (var file in files)
        {
            var path = Path.Combine(FindRepoRoot(), "src/Framework/Api/CrestCreates.DynamicApi", file);
            File.Exists(path).Should().BeTrue($"expected legacy file {file} to exist");
            var content = File.ReadAllText(path);

            foreach (var symbol in forbidden)
            {
                content.Should().NotContain(symbol,
                    because: $"Legacy file {file} must not reference CapabilityEndpoint runtime symbol {symbol}");
            }
        }
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
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find repository root (.git directory)");
    }

    private static string FindProjectFile(string projectFileName)
    {
        var repoRoot = FindRepoRoot();
        var matches = Directory.GetFiles(repoRoot, projectFileName, SearchOption.AllDirectories);
        matches.Should().NotBeEmpty($"expected to find {projectFileName}");
        return matches.First();
    }
}
