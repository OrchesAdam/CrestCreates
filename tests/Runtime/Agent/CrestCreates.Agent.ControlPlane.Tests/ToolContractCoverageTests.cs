using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.ControlPlane.Tests;

public class ToolContractCoverageTests
{
    private static readonly Type ContextType = typeof(AgentControlPlaneToolJsonSerializerContext);
    private static readonly Type ManifestType = ContextType.GetNestedType(
        "AgentControlPlaneToolJsonSerializerContextRootManifest",
        BindingFlags.NonPublic | BindingFlags.Public);

    private static IReadOnlySet<Type> GetManifestRootTypes(string propertyName)
    {
        ManifestType.Should().NotBeNull("generated root manifest must exist");
        var prop = ManifestType!.GetProperty(propertyName, BindingFlags.NonPublic | BindingFlags.Static);
        prop.Should().NotBeNull($"manifest must have {propertyName} property");
        return (IReadOnlySet<Type>)prop!.GetValue(null)!;
    }

    private static HashSet<Type> GetAllJsonSerializableTypes()
    {
        return ContextType.GetCustomAttributesData()
            .Where(d => d.AttributeType == typeof(System.Text.Json.Serialization.JsonSerializableAttribute))
            .Select(d => (Type)d.ConstructorArguments[0].Value!)
            .ToHashSet();
    }

    private static HashSet<Type> GetAllJsonTypeInfoTypes()
    {
        return ContextType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.PropertyType.IsGenericType
                        && p.PropertyType.GetGenericTypeDefinition() == typeof(JsonTypeInfo<>))
            .Select(p => p.PropertyType.GetGenericArguments()[0])
            .ToHashSet();
    }

    [Fact]
    public void EveryGeneratedRoot_HasJsonTypeInfo()
    {
        var allDirectRoots = GetManifestRootTypes("AllDirectRootTypes");

        foreach (var rootType in allDirectRoots)
        {
            var typeInfo = AgentControlPlaneToolJsonSerializerContext.Default.GetTypeInfo(rootType);
            typeInfo.Should().NotBeNull(
                $"every AllDirectRootTypes entry must have JsonTypeInfo: {rootType.FullName}");
        }
    }

    [Fact]
    public void GeneratedSurfaceRoots_AreResolvedByContext()
    {
        var surfaceRoots = GetManifestRootTypes("SurfaceRootTypes");

        foreach (var rootType in surfaceRoots)
        {
            var typeInfo = AgentControlPlaneToolJsonSerializerContext.Default.GetTypeInfo(rootType);
            typeInfo.Should().NotBeNull(
                $"every SurfaceRootTypes entry must be resolved: {rootType.FullName}");
        }
    }

    [Fact]
    public void ExplicitExtras_HaveJsonTypeInfo()
    {
        var explicitRoots = GetManifestRootTypes("ExplicitRootTypes");

        foreach (var rootType in explicitRoots)
        {
            var typeInfo = AgentControlPlaneToolJsonSerializerContext.Default.GetTypeInfo(rootType);
            typeInfo.Should().NotBeNull(
                $"every ExplicitRootTypes entry must have JsonTypeInfo: {rootType.FullName}");
        }
    }

    [Fact]
    public void ExplicitExtras_AreResolvedByContext()
    {
        var explicitRoots = GetManifestRootTypes("ExplicitRootTypes");
        var jsonTypeInfoTypes = GetAllJsonTypeInfoTypes();

        var missing = explicitRoots.Except(jsonTypeInfoTypes).ToList();
        missing.Should().BeEmpty(
            "every explicit extra must have a JsonTypeInfo property. Missing: " +
            string.Join(", ", missing.Select(t => t.Name)));
    }

    [Fact]
    public void AllDirectRoots_EqualSurfaceUnionExplicit()
    {
        var surfaceRoots = GetManifestRootTypes("SurfaceRootTypes");
        var explicitRoots = GetManifestRootTypes("ExplicitRootTypes");
        var allDirectRoots = GetManifestRootTypes("AllDirectRootTypes");

        var expected = surfaceRoots.Union(explicitRoots).ToHashSet();
        expected.SetEquals(allDirectRoots).Should().BeTrue(
            "AllDirectRootTypes must equal SurfaceRootTypes ∪ ExplicitRootTypes");
    }

    [Fact]
    public void ControlPlaneRootManifest_IsInternal()
    {
        ManifestType.Should().NotBeNull();
        ManifestType!.IsVisible.Should().BeFalse("root manifest must be internal");
    }

    [Fact]
    public void ManifestToolNames_Match_ContractRegistrations()
    {
        var provider = new StaticAgentToolManifestProvider();
        var manifestToolNames = provider.GetAllTools().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        var surfaceRoots = GetManifestRootTypes("SurfaceRootTypes");
        var serializableTypes = GetAllJsonSerializableTypes();

        serializableTypes.Should().Contain(surfaceRoots,
            "every surface root type must be registered in [JsonSerializable]");
    }

    [Fact]
    public void ContractRegistrations_Match_JsonTypeInfoSet()
    {
        var serializableTypes = GetAllJsonSerializableTypes();
        var jsonTypeInfoTypes = GetAllJsonTypeInfoTypes();

        var missingFromJsonTypeInfo = serializableTypes.Except(jsonTypeInfoTypes).ToList();

        missingFromJsonTypeInfo.Should().BeEmpty(
            "every [JsonSerializable] type must have a corresponding JsonTypeInfo<T> property. " +
            $"Missing JsonTypeInfo for: {string.Join(", ", missingFromJsonTypeInfo.Select(t => t.Name))}");
    }

    [Fact]
    public void SerializerOptions_ContainOnlyGeneratedResolverChain()
    {
        var options = AgentControlPlaneToolJsonSerializerOptions.CreateDefault();

        options.TypeInfoResolver.Should().NotBeNull("options must have a type info resolver");
        options.TypeInfoResolver.Should().NotBeOfType<DefaultJsonTypeInfoResolver>(
            "resolver must be source-generated, not DefaultJsonTypeInfoResolver");
    }

    [Fact]
    public void NoAssemblyWideJsonSerializableFallbackRemains()
    {
        var options = AgentControlPlaneToolJsonSerializerOptions.CreateDefault();

        var resolver = options.TypeInfoResolver;
        resolver.Should().NotBeOfType<DefaultJsonTypeInfoResolver>(
            "DefaultJsonTypeInfoResolver must not appear in the resolver chain");
    }

    [Fact]
    public void NoAssemblyWidePublicRecordScanOrKnownExclusionList()
    {
        var thisAssembly = typeof(ToolContractCoverageTests).Assembly;

        thisAssembly.GetType("CrestCreates.Agent.ControlPlane.Tests.ToolContractCoverageRootDiscovery")
            .Should().BeNull("legacy root discovery helper class must not exist");
    }
}
