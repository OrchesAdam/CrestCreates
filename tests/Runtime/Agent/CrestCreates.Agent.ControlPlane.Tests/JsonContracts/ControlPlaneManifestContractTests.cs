using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using CrestCreates.Core.Abstractions.Serialization;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.ControlPlane.Tests.JsonContracts;

public class ControlPlaneManifestContractTests
{
    private const string ToolContextName = "Tool";
    private const string ActivationContextName = "Activation";

    public static TheoryData<string> ContextNames => new()
    {
        ToolContextName,
        ActivationContextName
    };

    private static Type GetContextType(string contextName)
    {
        return contextName switch
        {
            ToolContextName => typeof(AgentControlPlaneToolJsonSerializerContext),
            ActivationContextName => typeof(AgentControlPlaneActivationJsonSerializerContext),
            _ => throw new ArgumentOutOfRangeException(nameof(contextName))
        };
    }

    private static Type GetManifestType(string contextName)
    {
        var contextType = GetContextType(contextName);
        var manifestType = contextType.GetNestedType(
            $"{contextType.Name}RootManifest",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        manifestType.Should().NotBeNull("generated root manifest must exist");
        return manifestType!;
    }

    private static IReadOnlySet<Type> GetRootTypes(string contextName, string propertyName)
    {
        var prop = GetManifestType(contextName).GetProperty(
            propertyName,
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        prop.Should().NotBeNull($"manifest must have {propertyName} property");
        return (IReadOnlySet<Type>)prop!.GetValue(null)!;
    }

    private static HashSet<Type> GetJsonSerializableTypes(string contextName)
    {
        return GetContextType(contextName).GetCustomAttributesData()
            .Where(d => d.AttributeType == typeof(JsonSerializableAttribute))
            .Select(d => (Type)d.ConstructorArguments[0].Value!)
            .ToHashSet();
    }

    private static HashSet<Type> GetJsonTypeInfoTypes(string contextName)
    {
        return GetContextType(contextName)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.PropertyType.IsGenericType
                        && p.PropertyType.GetGenericTypeDefinition() == typeof(JsonTypeInfo<>))
            .Select(p => p.PropertyType.GetGenericArguments()[0])
            .ToHashSet();
    }

    [Theory]
    [MemberData(nameof(ContextNames))]
    public void GeneratedSurfaceRoots_MatchExpectedToolAndManifestSurfaces(string contextName)
    {
        var surfaceRoots = GetRootTypes(contextName, "SurfaceRootTypes");

        surfaceRoots.Should().Contain(typeof(AgentToolDescriptor), "ListAgentTools returns IReadOnlyList<AgentToolDescriptor>");
        surfaceRoots.Should().Contain(typeof(AgentToolResult<string>), "GetAgentToolDescriptor returns string tool name");
        surfaceRoots.Should().Contain(typeof(DescriptorRef), "GetAgentToolDescriptor takes DescriptorRef parameter");
        surfaceRoots.Should().Contain(typeof(ApplyFixProposalRequest), "ApplyFixProposal takes ApplyFixProposalRequest");
        surfaceRoots.Should().Contain(typeof(DescriptorSearchRequest), "SearchDescriptors takes DescriptorSearchRequest");
        surfaceRoots.Should().Contain(typeof(ExplainDiagnosticsRequest), "ExplainDiagnostics takes ExplainDiagnosticsRequest");
        surfaceRoots.Should().Contain(typeof(CreateDescriptorDraftRequest), "CreateDescriptorDraft takes CreateDescriptorDraftRequest");
        surfaceRoots.Should().Contain(typeof(UpdateDescriptorDraftRequest), "UpdateDescriptorDraft takes UpdateDescriptorDraftRequest");

        if (contextName == ActivationContextName)
        {
            surfaceRoots.Should().Contain(typeof(SubmitActivationRequestRequest));
            surfaceRoots.Should().Contain(typeof(AgentToolResult<ActivationRequest>));
            surfaceRoots.Should().Contain(typeof(AgentToolResult<ActivationReadinessPreview>));
        }
        else
        {
            surfaceRoots.Should().NotContain(typeof(SubmitActivationRequestRequest));
            surfaceRoots.Should().NotContain(typeof(AgentToolResult<ActivationRequest>));
            surfaceRoots.Should().NotContain(typeof(AgentToolResult<ActivationReadinessPreview>));
        }
    }

    [Theory]
    [MemberData(nameof(ContextNames))]
    public void GeneratedSurfaceRoots_AreResolvedByContext(string contextName)
    {
        var surfaceRoots = GetRootTypes(contextName, "SurfaceRootTypes");
        var jsonTypeInfoTypes = GetJsonTypeInfoTypes(contextName);

        surfaceRoots.Except(jsonTypeInfoTypes).Should().BeEmpty(
            "all surface root types must have corresponding JsonTypeInfo<T> properties");
    }

    [Theory]
    [MemberData(nameof(ContextNames))]
    public void ExplicitExtras_AreResolvedByContext(string contextName)
    {
        var explicitRoots = GetRootTypes(contextName, "ExplicitRootTypes");
        var jsonTypeInfoTypes = GetJsonTypeInfoTypes(contextName);

        explicitRoots.Except(jsonTypeInfoTypes).Should().BeEmpty(
            "all explicit extra root types must have corresponding JsonTypeInfo<T> properties");
    }

    [Theory]
    [MemberData(nameof(ContextNames))]
    public void AllDirectRoots_EqualSurfaceUnionExplicit(string contextName)
    {
        var surfaceRoots = GetRootTypes(contextName, "SurfaceRootTypes");
        var explicitRoots = GetRootTypes(contextName, "ExplicitRootTypes");
        var allDirectRoots = GetRootTypes(contextName, "AllDirectRootTypes");
        var expected = new HashSet<Type>(surfaceRoots);
        expected.UnionWith(explicitRoots);

        allDirectRoots.Should().BeEquivalentTo(expected,
            "AllDirectRootTypes must equal SurfaceRootTypes ∪ ExplicitRootTypes");
    }

    [Fact]
    public void CanonicalHashParserRoots_AreOwnedByActivationContext()
    {
        GetRootTypes(ToolContextName, "ExplicitRootTypes").Should().BeEmpty();
        GetRootTypes(ActivationContextName, "ExplicitRootTypes").Should().BeEquivalentTo(
            [typeof(DescriptorActivationReviewDecision), typeof(CanonicalHash)],
            "the activation direct-use ledger proves exactly these two non-surface roots");
    }

    [Theory]
    [MemberData(nameof(ContextNames))]
    public void Manifest_ContainsImportantBoundaryTypes(string contextName)
    {
        var allRoots = GetRootTypes(contextName, "AllDirectRootTypes");

        allRoots.Should().Contain(typeof(DescriptorSearchRequest));
        allRoots.Should().Contain(typeof(DescriptorReviewReportDto));
        allRoots.Should().Contain(typeof(DescriptorReviewReportFormat));
        allRoots.Should().Contain(typeof(string));
        allRoots.Should().Contain(typeof(AgentToolResult<string>));
        allRoots.Should().Contain(typeof(AgentToolDescriptor));
    }

    [Theory]
    [MemberData(nameof(ContextNames))]
    public void Manifest_ExcludesExcludedParameterTypes(string contextName)
    {
        var surfaceRoots = GetRootTypes(contextName, "SurfaceRootTypes");

        surfaceRoots.Should().NotContain(typeof(AgentToolInvocationContext),
            "AgentToolInvocationContext is excluded via ExcludedParameterTypes");
        surfaceRoots.Should().NotContain(typeof(CancellationToken),
            "CancellationToken is a framework type, not a serialization root");
    }

    [Theory]
    [MemberData(nameof(ContextNames))]
    public void Manifest_ExcludesMemberOnlyDtos_AsSurfaceRoots(string contextName)
    {
        var surfaceRoots = GetRootTypes(contextName, "SurfaceRootTypes");
        var explicitRoots = GetRootTypes(contextName, "ExplicitRootTypes");

        surfaceRoots.Should().NotContain(typeof(DescriptorActivationReviewDecision),
            "DescriptorActivationReviewDecision is an explicit activation root, not a surface root");
        surfaceRoots.Should().NotContain(typeof(DescriptorStableHashes),
            "DescriptorStableHashes is a member-only DTO, not a surface root");
        explicitRoots.Should().NotContain(typeof(DescriptorStableHashes),
            "DescriptorStableHashes is transitive metadata, not a direct serialization root");
    }

    [Theory]
    [MemberData(nameof(ContextNames))]
    public void ControlPlaneMigration_DoesNotAddJsonContributor(string contextName)
    {
        var abstractionsAssembly = GetContextType(contextName).Assembly;
        var contributorTypes = abstractionsAssembly.GetTypes()
            .Where(t => t.IsInterface && t.Name.EndsWith("JsonContextContributor"))
            .ToList();

        contributorTypes.Should().BeEmpty(
            "ControlPlane abstraction migration must not add JSON context contributor interfaces");
    }

    [Theory]
    [MemberData(nameof(ContextNames))]
    public void JsonSerializableAttributes_CoverAllDirectRoots(string contextName)
    {
        var allDirectRoots = GetRootTypes(contextName, "AllDirectRootTypes");
        var jsonSerializableTypes = GetJsonSerializableTypes(contextName);

        allDirectRoots.Except(jsonSerializableTypes).Should().BeEmpty(
            "all direct root types must have [JsonSerializable] attributes");
    }
}
