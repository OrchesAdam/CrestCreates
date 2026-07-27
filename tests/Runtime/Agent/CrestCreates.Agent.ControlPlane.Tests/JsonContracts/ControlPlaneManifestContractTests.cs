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
    private static readonly Type ContextType = typeof(AgentControlPlaneToolJsonSerializerContext);
    private static readonly Type ManifestType = ContextType.GetNestedType("AgentControlPlaneToolJsonSerializerContextRootManifest", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;

    private static IReadOnlySet<Type> GetSurfaceRootTypes()
    {
        var prop = ManifestType.GetProperty("SurfaceRootTypes", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        return (IReadOnlySet<Type>)prop.GetValue(null)!;
    }

    private static IReadOnlySet<Type> GetExplicitRootTypes()
    {
        var prop = ManifestType.GetProperty("ExplicitRootTypes", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        return (IReadOnlySet<Type>)prop.GetValue(null)!;
    }

    private static IReadOnlySet<Type> GetAllDirectRootTypes()
    {
        var prop = ManifestType.GetProperty("AllDirectRootTypes", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        return (IReadOnlySet<Type>)prop.GetValue(null)!;
    }

    private static HashSet<Type> GetJsonSerializableTypes()
    {
        return ContextType.GetCustomAttributesData()
            .Where(d => d.AttributeType == typeof(JsonSerializableAttribute))
            .Select(d => (Type)d.ConstructorArguments[0].Value!)
            .ToHashSet();
    }

    private static HashSet<Type> GetJsonTypeInfoTypes()
    {
        return ContextType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.PropertyType.IsGenericType
                        && p.PropertyType.GetGenericTypeDefinition() == typeof(JsonTypeInfo<>))
            .Select(p => p.PropertyType.GetGenericArguments()[0])
            .ToHashSet();
    }

    [Fact]
    public void GeneratedSurfaceRoots_MatchExpectedToolAndManifestSurfaces()
    {
        var surfaceRoots = GetSurfaceRootTypes();

        surfaceRoots.Should().Contain(typeof(AgentToolDescriptor), "ListAgentTools returns IReadOnlyList<AgentToolDescriptor>");
        surfaceRoots.Should().Contain(typeof(AgentToolResult<string>), "GetAgentToolDescriptor returns string tool name");
        surfaceRoots.Should().Contain(typeof(DescriptorRef), "GetAgentToolDescriptor takes DescriptorRef parameter");
        surfaceRoots.Should().Contain(typeof(ApplyFixProposalRequest), "ApplyFixProposal takes ApplyFixProposalRequest");
        surfaceRoots.Should().Contain(typeof(SubmitActivationRequestRequest), "SubmitActivationRequest takes SubmitActivationRequestRequest");
        surfaceRoots.Should().Contain(typeof(DescriptorSearchRequest), "SearchDescriptors takes DescriptorSearchRequest");
        surfaceRoots.Should().Contain(typeof(ExplainDiagnosticsRequest), "ExplainDiagnostics takes ExplainDiagnosticsRequest");
        surfaceRoots.Should().Contain(typeof(CreateDescriptorDraftRequest), "CreateDescriptorDraft takes CreateDescriptorDraftRequest");
        surfaceRoots.Should().Contain(typeof(UpdateDescriptorDraftRequest), "UpdateDescriptorDraft takes UpdateDescriptorDraftRequest");
    }

    [Fact]
    public void GeneratedSurfaceRoots_AreResolvedByContext()
    {
        var surfaceRoots = GetSurfaceRootTypes();
        var jsonTypeInfoTypes = GetJsonTypeInfoTypes();

        var unresolved = surfaceRoots.Except(jsonTypeInfoTypes).ToList();
        unresolved.Should().BeEmpty(
            "all surface root types must have corresponding JsonTypeInfo<T> properties. " +
            "Unresolved: {0}", string.Join(", ", unresolved.Select(t => t.FullName)));
    }

    [Fact]
    public void ExplicitExtras_AreResolvedByContext()
    {
        var explicitRoots = GetExplicitRootTypes();
        var jsonTypeInfoTypes = GetJsonTypeInfoTypes();

        var unresolved = explicitRoots.Except(jsonTypeInfoTypes).ToList();
        unresolved.Should().BeEmpty(
            "all explicit extra root types must have corresponding JsonTypeInfo<T> properties. " +
            "Unresolved: {0}", string.Join(", ", unresolved.Select(t => t.FullName)));
    }

    [Fact]
    public void AllDirectRoots_EqualSurfaceUnionExplicit()
    {
        var surfaceRoots = GetSurfaceRootTypes();
        var explicitRoots = GetExplicitRootTypes();
        var allDirectRoots = GetAllDirectRootTypes();

        var expected = new HashSet<Type>(surfaceRoots);
        expected.UnionWith(explicitRoots);

        allDirectRoots.Should().BeEquivalentTo(expected,
            "AllDirectRootTypes must equal SurfaceRootTypes ∪ ExplicitRootTypes");
    }

    [Fact]
    public void CanonicalHashParserRoot_IsExplicit()
    {
        var explicitRoots = GetExplicitRootTypes();

        explicitRoots.Should().BeEquivalentTo(
            [typeof(DescriptorActivationReviewDecision), typeof(CanonicalHash)],
            "the repository-wide direct-use ledger proves exactly these two non-surface roots");
    }

    [Fact]
    public void Manifest_ContainsImportantBoundaryTypes()
    {
        var allRoots = GetAllDirectRootTypes();

        allRoots.Should().Contain(typeof(DescriptorSearchRequest));
        allRoots.Should().Contain(typeof(DescriptorReviewReportDto));
        allRoots.Should().Contain(typeof(DescriptorReviewReportFormat));
        allRoots.Should().Contain(typeof(string));
        allRoots.Should().Contain(typeof(AgentToolResult<string>));
        allRoots.Should().Contain(typeof(AgentToolDescriptor));
    }

    [Fact]
    public void Manifest_ExcludesExcludedParameterTypes()
    {
        var surfaceRoots = GetSurfaceRootTypes();

        surfaceRoots.Should().NotContain(typeof(AgentToolInvocationContext),
            "AgentToolInvocationContext is excluded via ExcludedParameterTypes");
        surfaceRoots.Should().NotContain(typeof(System.Threading.CancellationToken),
            "CancellationToken is a framework type, not a serialization root");
    }

    [Fact]
    public void Manifest_ExcludesMemberOnlyDtos_AsSurfaceRoots()
    {
        var surfaceRoots = GetSurfaceRootTypes();

        surfaceRoots.Should().NotContain(typeof(DescriptorActivationReviewDecision),
            "DescriptorActivationReviewDecision is a member-only DTO, not a surface root");
        surfaceRoots.Should().NotContain(typeof(DescriptorStableHashes),
            "DescriptorStableHashes is a member-only DTO, not a surface root");

        GetExplicitRootTypes().Should().NotContain(typeof(DescriptorStableHashes),
            "DescriptorStableHashes is transitive metadata, not a direct serialization root");
    }

    [Fact]
    public void ControlPlaneMigration_DoesNotAddJsonContributor()
    {
        var abstractionsAssembly = typeof(AgentControlPlaneToolJsonSerializerContext).Assembly;

        var contributorTypes = abstractionsAssembly.GetTypes()
            .Where(t => t.IsInterface
                        && t.Name.EndsWith("JsonContextContributor"))
            .ToList();

        contributorTypes.Should().BeEmpty(
            "ControlPlane.Abstractions migration must not add IAgentToolJsonContextContributor " +
            "or IMcpToolJsonContextContributor interfaces");
    }

    [Fact]
    public void JsonSerializableAttributes_CoverAllDirectRoots()
    {
        var allDirectRoots = GetAllDirectRootTypes();
        var jsonSerializableTypes = GetJsonSerializableTypes();

        var uncovered = allDirectRoots.Except(jsonSerializableTypes).ToList();
        uncovered.Should().BeEmpty(
            "all direct root types must have [JsonSerializable] attributes. " +
            "Uncovered: {0}", string.Join(", ", uncovered.Select(t => t.FullName)));
    }
}
