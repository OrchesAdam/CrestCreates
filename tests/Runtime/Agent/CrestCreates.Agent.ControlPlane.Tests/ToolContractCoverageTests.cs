using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.ContextPack.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.ControlPlane.Tests;

public class ToolContractCoverageTests
{
    private const string ToolContextName = "Tool";
    private const string ActivationContextName = "Activation";

    public static TheoryData<string> ContextNames => new()
    {
        ToolContextName,
        ActivationContextName
    };

    private static (Type ContextType, JsonSerializerContext Context) GetContext(string contextName)
    {
        return contextName switch
        {
            ToolContextName => (
                typeof(AgentControlPlaneToolJsonSerializerContext),
                AgentControlPlaneToolJsonSerializerContext.Default),
            ActivationContextName => (
                typeof(AgentControlPlaneActivationJsonSerializerContext),
                AgentControlPlaneActivationJsonSerializerContext.Default),
            _ => throw new ArgumentOutOfRangeException(nameof(contextName))
        };
    }

    private static Type GetManifestType(Type contextType)
    {
        var manifestType = contextType.GetNestedType(
            $"{contextType.Name}RootManifest",
            BindingFlags.NonPublic | BindingFlags.Public);
        manifestType.Should().NotBeNull("generated root manifest must exist");
        return manifestType!;
    }

    private static IReadOnlySet<Type> GetManifestRootTypes(Type contextType, string propertyName)
    {
        var manifestType = GetManifestType(contextType);
        var prop = manifestType.GetProperty(propertyName, BindingFlags.NonPublic | BindingFlags.Static);
        prop.Should().NotBeNull($"manifest must have {propertyName} property");
        return (IReadOnlySet<Type>)prop!.GetValue(null)!;
    }

    private static HashSet<Type> GetAllJsonSerializableTypes(Type contextType)
    {
        return contextType.GetCustomAttributesData()
            .Where(d => d.AttributeType == typeof(JsonSerializableAttribute))
            .Select(d => (Type)d.ConstructorArguments[0].Value!)
            .ToHashSet();
    }

    private static HashSet<Type> GetAllJsonTypeInfoTypes(Type contextType)
    {
        return contextType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.PropertyType.IsGenericType
                        && p.PropertyType.GetGenericTypeDefinition() == typeof(JsonTypeInfo<>))
            .Select(p => p.PropertyType.GetGenericArguments()[0])
            .ToHashSet();
    }

    [Theory]
    [MemberData(nameof(ContextNames))]
    public void EveryGeneratedRoot_HasJsonTypeInfo(string contextName)
    {
        var (contextType, context) = GetContext(contextName);
        var allDirectRoots = GetManifestRootTypes(contextType, "AllDirectRootTypes");

        foreach (var rootType in allDirectRoots)
        {
            context.GetTypeInfo(rootType).Should().NotBeNull(
                $"every AllDirectRootTypes entry must have JsonTypeInfo: {rootType.FullName}");
        }
    }

    [Theory]
    [MemberData(nameof(ContextNames))]
    public void GeneratedSurfaceRoots_AreResolvedByContext(string contextName)
    {
        var (contextType, context) = GetContext(contextName);
        var surfaceRoots = GetManifestRootTypes(contextType, "SurfaceRootTypes");

        foreach (var rootType in surfaceRoots)
        {
            context.GetTypeInfo(rootType).Should().NotBeNull(
                $"every SurfaceRootTypes entry must be resolved: {rootType.FullName}");
        }
    }

    [Theory]
    [MemberData(nameof(ContextNames))]
    public void ExplicitExtras_HaveJsonTypeInfo(string contextName)
    {
        var (contextType, context) = GetContext(contextName);
        var explicitRoots = GetManifestRootTypes(contextType, "ExplicitRootTypes");

        foreach (var rootType in explicitRoots)
        {
            context.GetTypeInfo(rootType).Should().NotBeNull(
                $"every ExplicitRootTypes entry must have JsonTypeInfo: {rootType.FullName}");
        }
    }

    [Theory]
    [MemberData(nameof(ContextNames))]
    public void ExplicitExtras_AreResolvedByContext(string contextName)
    {
        var (contextType, _) = GetContext(contextName);
        var explicitRoots = GetManifestRootTypes(contextType, "ExplicitRootTypes");
        var jsonTypeInfoTypes = GetAllJsonTypeInfoTypes(contextType);

        explicitRoots.Except(jsonTypeInfoTypes).Should().BeEmpty(
            "every explicit extra must have a JsonTypeInfo property");
    }

    [Theory]
    [MemberData(nameof(ContextNames))]
    public void AllDirectRoots_EqualSurfaceUnionExplicit(string contextName)
    {
        var (contextType, _) = GetContext(contextName);
        var surfaceRoots = GetManifestRootTypes(contextType, "SurfaceRootTypes");
        var explicitRoots = GetManifestRootTypes(contextType, "ExplicitRootTypes");
        var allDirectRoots = GetManifestRootTypes(contextType, "AllDirectRootTypes");

        surfaceRoots.Union(explicitRoots).ToHashSet().SetEquals(allDirectRoots).Should().BeTrue(
            "AllDirectRootTypes must equal SurfaceRootTypes ∪ ExplicitRootTypes");
    }

    [Theory]
    [MemberData(nameof(ContextNames))]
    public void ControlPlaneRootManifest_IsInternal(string contextName)
    {
        var (contextType, _) = GetContext(contextName);
        GetManifestType(contextType).IsVisible.Should().BeFalse("root manifest must be internal");
    }

    [Theory]
    [MemberData(nameof(ContextNames))]
    public void ManifestToolNames_Match_ContractRegistrations(string contextName)
    {
        var (contextType, _) = GetContext(contextName);
        var provider = new StaticAgentToolManifestProvider();
        var manifestToolNames = provider.GetAllTools().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var surfaceRoots = GetManifestRootTypes(contextType, "SurfaceRootTypes");
        var serializableTypes = GetAllJsonSerializableTypes(contextType);

        serializableTypes.Should().Contain(surfaceRoots,
            "every surface root type must be registered in [JsonSerializable]");
        manifestToolNames.Should().NotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(ContextNames))]
    public void ContractRegistrations_Match_JsonTypeInfoSet(string contextName)
    {
        var (contextType, _) = GetContext(contextName);
        var serializableTypes = GetAllJsonSerializableTypes(contextType);
        var jsonTypeInfoTypes = GetAllJsonTypeInfoTypes(contextType);

        serializableTypes.Except(jsonTypeInfoTypes).Should().BeEmpty(
            "every [JsonSerializable] type must have a corresponding JsonTypeInfo<T> property");
    }

    [Fact]
    public void SerializerOptions_ContainOnlyGeneratedResolverChain_AndMatchContextPolicies()
    {
        var contexts = new (JsonSerializerOptions Options, JsonSerializerContext Context)[]
        {
            (AgentControlPlaneToolJsonSerializerOptions.CreateDefault(),
                AgentControlPlaneToolJsonSerializerContext.Default),
            (AgentControlPlaneActivationJsonSerializerOptions.CreateDefault(),
                AgentControlPlaneActivationJsonSerializerContext.Default)
        };

        foreach (var (serializerOptions, context) in contexts)
        {
            serializerOptions.TypeInfoResolver.Should().NotBeNull("options must have a type info resolver");
            serializerOptions.TypeInfoResolver.Should().NotBeOfType<DefaultJsonTypeInfoResolver>(
                "resolver must be source-generated, not DefaultJsonTypeInfoResolver");
            serializerOptions.TypeInfoResolverChain.Should().NotContain(
                resolver => resolver is DefaultJsonTypeInfoResolver,
                "DefaultJsonTypeInfoResolver must not appear in the resolver chain");
            serializerOptions.PropertyNamingPolicy.Should().BeSameAs(context.Options.PropertyNamingPolicy);
            serializerOptions.DefaultIgnoreCondition.Should().Be(context.Options.DefaultIgnoreCondition);
            serializerOptions.PropertyNameCaseInsensitive.Should().Be(context.Options.PropertyNameCaseInsensitive);
            serializerOptions.NumberHandling.Should().Be(context.Options.NumberHandling);
        }
    }

    [Fact]
    public void ActivationSerializerOptions_AndContext_UseTheSameWireContract()
    {
        var request = new SubmitActivationRequestRequest
        {
            DraftId = "draft-json",
            Rationale = null,
            BindingSnapshot = new ActivationBindingSnapshot
            {
                TenantId = "tenant-json",
                DraftId = "draft-json",
                DraftVersion = 7,
                ReviewResultId = "review-json",
                ReportId = null,
                PackagePreviewId = "package-json",
                EvidencePreviewId = "evidence-json",
                CorrelationId = null,
                CreatedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
                Hashes = new BindingHashes
                {
                    SourceReviewHash = TestHash("source"),
                    ReviewManifestHash = TestHash("review"),
                    PackageManifestHash = TestHash("manifest"),
                    PackageEvidenceHash = TestHash("evidence"),
                    PackageEvidenceEnvelopeHash = TestHash("envelope"),
                    ContractHash = TestHash("contract"),
                    DefinitionHash = TestHash("definition")
                }
            }
        };
        var context = AgentControlPlaneActivationJsonSerializerContext.Default;
        var options = AgentControlPlaneActivationJsonSerializerOptions.CreateDefault();

        var contextJson = JsonSerializer.Serialize(request, context.SubmitActivationRequestRequest);
        var optionsJson = JsonSerializer.Serialize(request, options);

        contextJson.Should().Be(optionsJson);
        contextJson.Should().Contain("\"draftId\"")
            .And.NotContain("\"Rationale\"")
            .And.NotContain("\"rationale\":null");
        JsonSerializer.Deserialize(contextJson, context.SubmitActivationRequestRequest)
            .Should().BeEquivalentTo(request);
        JsonSerializer.Deserialize<SubmitActivationRequestRequest>(optionsJson, options)
            .Should().BeEquivalentTo(request);
    }

    private static CanonicalHash TestHash(string value) => new()
    {
        Value = value,
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = "Descriptor",
        Scope = "InternalFull",
        Purpose = "Contract",
        ContractVersion = "canonical-hash-v1",
        CanonicalShapeVersion = "schema-contract-hash-v1"
    };

    [Fact]
    public void SerializerOptions_AndContexts_PreserveRequiredNullableMembers()
    {
        var contexts = new (JsonSerializerOptions Options, JsonSerializerContext Context)[]
        {
            (AgentControlPlaneToolJsonSerializerOptions.CreateDefault(),
                AgentControlPlaneToolJsonSerializerContext.Default),
            (AgentControlPlaneActivationJsonSerializerOptions.CreateDefault(),
                AgentControlPlaneActivationJsonSerializerContext.Default)
        };
        var summary = new MetadataContextPackSummary
        {
            TotalDescriptorCount = 0,
            DescriptorCountsByKind = new Dictionary<DescriptorKind, int>(),
            TotalRelationshipCount = 0,
            RelationshipCountsByKind = new Dictionary<RelationshipKind, int>(),
            FocusRefs = [],
            WasTruncated = false,
            TruncatedAtCount = null,
            TraversalDepthReached = 0
        };

        foreach (var (options, context) in contexts)
        {
            var contextJson = JsonSerializer.Serialize(summary, typeof(MetadataContextPackSummary), context);
            var optionsJson = JsonSerializer.Serialize(summary, options);

            optionsJson.Should().Be(contextJson);
            JsonSerializer.Deserialize(contextJson, typeof(MetadataContextPackSummary), context)
                .Should().BeEquivalentTo(summary);
            JsonSerializer.Deserialize<MetadataContextPackSummary>(optionsJson, options)
                .Should().BeEquivalentTo(summary);
        }
    }

    [Fact]
    public void ToolContext_DoesNotContainActivationContracts()
    {
        var (contextType, context) = GetContext(ToolContextName);
        var activationAssembly = typeof(ActivationRequest).Assembly;
        var allRoots = GetManifestRootTypes(contextType, "AllDirectRootTypes");
        var serializableTypes = GetAllJsonSerializableTypes(contextType);

        allRoots.Should().NotContain(type => type.Assembly == activationAssembly);
        serializableTypes.Should().NotContain(type => type.Assembly == activationAssembly);
        context.GetTypeInfo(typeof(ActivationRequest)).Should().BeNull(
            "read/draft consumers must not acquire activation metadata from the root context");
    }

    [Fact]
    public void ActivationContext_ContainsCompleteToolAndActivationSurface()
    {
        var (contextType, context) = GetContext(ActivationContextName);
        var allRoots = GetManifestRootTypes(contextType, "AllDirectRootTypes");

        allRoots.Should().Contain(typeof(DescriptorSearchRequest));
        allRoots.Should().Contain(typeof(CreateDescriptorDraftRequest));
        allRoots.Should().Contain(typeof(SubmitActivationRequestRequest));
        allRoots.Should().Contain(typeof(AgentToolResult<ActivationRequest>));
        allRoots.Should().Contain(typeof(AgentToolResult<ActivationReadinessPreview>));
        allRoots.Should().Contain(typeof(DescriptorActivationReviewDecision));
        context.GetTypeInfo(typeof(ActivationRequest)).Should().NotBeNull();
    }

    [Fact]
    public void NoAssemblyWidePublicRecordScanOrKnownExclusionList()
    {
        typeof(ToolContractCoverageTests).Assembly
            .GetType("CrestCreates.Agent.ControlPlane.Tests.ToolContractCoverageRootDiscovery")
            .Should().BeNull("legacy root discovery helper class must not exist");
    }
}
