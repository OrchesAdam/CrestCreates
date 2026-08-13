using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Abstractions.Json;
using CrestCreates.Agent.Memory.Accountability;
using CrestCreates.Agent.Memory.Identity;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Agent.Memory.Tests;

public sealed class AccountabilityContractTests
{
    // ---- Operation identity ----

    [Fact]
    public void OperationIdentity_Should_HaveExactlyOperationIdAndOccurredAt()
    {
        var properties = GetInstanceProperties(typeof(AgentMemoryOperationIdentity));

        properties.Select(p => p.Name)
            .Should()
            .BeEquivalentTo(new[] { nameof(AgentMemoryOperationIdentity.OperationId), nameof(AgentMemoryOperationIdentity.OccurredAt) });
        properties.Single(p => p.Name == nameof(AgentMemoryOperationIdentity.OperationId)).PropertyType
            .Should().Be(typeof(string));
        properties.Single(p => p.Name == nameof(AgentMemoryOperationIdentity.OccurredAt)).PropertyType
            .Should().Be(typeof(DateTimeOffset));
    }

    [Fact]
    public void OperationIdentity_Should_NotCarryCorrelationOrOriginData()
    {
        var forbidden = new[]
        {
            "TenantId", "ActorId", "ActorKind", "AgentId", "SessionId", "CorrelationId",
            "CausationId", "InvocationSource", "Origin", "ArtifactId", "InvocationId",
            "RequestId", "CapabilityId", "ResourceId", "Kind", "Source", "Provider",
            "Ordinal", "Content", "DescriptorRef", "SourceRef"
        };

        GetInstanceProperties(typeof(AgentMemoryOperationIdentity))
            .Select(p => p.Name)
            .Should().NotContain(forbidden);
    }

    [Fact]
    public void OperationIdentity_Equality_Should_RequireBothFields()
    {
        var a = new AgentMemoryOperationIdentity
        {
            OperationId = "op-1",
            OccurredAt = new DateTimeOffset(2026, 8, 11, 1, 2, 3, TimeSpan.Zero)
        };
        var b = a with { OccurredAt = a.OccurredAt.AddMinutes(1) };
        var c = a with { };

        a.Should().NotBe(b);
        a.Should().Be(c);
        (a == c).Should().BeTrue();
        (a == b).Should().BeFalse();
        a.GetHashCode().Should().Be(c.GetHashCode());
    }

    [Fact]
    public void OperationRequest_Should_ReplaceTimestampWithRequiredIdentity()
    {
        typeof(AgentMemoryOperationRequest).GetProperty("Timestamp").Should().BeNull();

        var identityProperty = typeof(AgentMemoryOperationRequest).GetProperty("Identity");
        identityProperty.Should().NotBeNull();
        identityProperty!.PropertyType.Should().Be(typeof(AgentMemoryOperationIdentity));
        identityProperty.GetCustomAttribute<RequiredMemberAttribute>().Should().NotBeNull();
    }

    [Fact]
    public void OperationRequest_Snapshot_Should_PreserveIdentity()
    {
        var request = new AgentMemoryOperationRequest
        {
            Identity = new AgentMemoryOperationIdentity
            {
                OperationId = "op-snap",
                OccurredAt = new DateTimeOffset(2026, 8, 11, 1, 0, 0, TimeSpan.Zero)
            },
            TenantId = "tenant-1",
            InvocationContext = new AgentMemoryInvocationContext
            {
                TenantId = "tenant-1",
                ActorId = "actor-1",
                ActorKind = "Agent"
            },
            Reason = "snapshot"
        };

        var snapshot = request.Snapshot();

        snapshot.Should().NotBeSameAs(request);
        snapshot.Identity.Should().Be(request.Identity);
        snapshot.TenantId.Should().Be(request.TenantId);
        snapshot.Reason.Should().Be(request.Reason);
        snapshot.Explanation.Should().Be(request.Explanation);
        snapshot.InvocationContext.Should().BeEquivalentTo(request.InvocationContext);
        snapshot.SourceRefs.Should().BeEquivalentTo(request.SourceRefs);
    }

    // ---- Payload roots ----

    [Fact]
    public void PayloadRoots_Should_HaveExactPropertySets()
    {
        AssertExactProperties<AgentMemoryRecallAccountabilityPayload>(new[]
        {
            "OperationId", "Result", "StableFailureCode", "EffectivePackHash", "ReturnedCount",
            "WasTruncated", "DiagnosticCodes", "RequestedKinds", "MaximumCount",
            "CharacterBudget", "MinimumConfidence"
        });
        AssertExactProperties<AgentMemoryCurationAccountabilityPayload>(new[]
        {
            "OperationId", "Operation", "CandidateId", "MemoryId", "ReplacementCandidateId",
            "NewMemoryId", "ExpectedCandidateStateHash", "ExpectedMemoryStateHash",
            "ExpectedReplacementStateHash", "ExpectedContentHash", "PreviousState",
            "ResultingState", "Result", "StableFailureCode", "Sanitization"
        });
        AssertExactProperties<AgentMemorySourceExpansionAccountabilityPayload>(new[]
        {
            "OperationId", "SourceKind", "SourceId", "RangeStart", "RangeEnd", "Status",
            "EffectiveVisibleContentHash", "MaximumCharacters", "WasTruncated",
            "Sanitization", "DiagnosticCodes"
        });
        AssertExactProperties<AgentMemoryAccountabilitySanitizationSummary>(new[]
        {
            "State", "RedactionCodes", "DiagnosticCodes"
        });
    }

    [Fact]
    public void Payloads_Should_NotExposeRawOrProvenanceData()
    {
        var forbidden = new[]
        {
            "Content", "SourceRefs", "DescriptorRefs", "MemoryIds", "Handles", "RuleSet",
            "RuleSetVersion", "TraceAttributes", "Reason", "Explanation", "GrantId",
            "IntentText", "Tags", "ScopeFingerprint", "VisibleMemorySetHash",
            "CanonicalPackHash", "SanitizedContent", "Message", "MatchCount", "Rule"
        };
        var payloadTypes = new[]
        {
            typeof(AgentMemoryRecallAccountabilityPayload),
            typeof(AgentMemoryCurationAccountabilityPayload),
            typeof(AgentMemorySourceExpansionAccountabilityPayload),
            typeof(AgentMemoryAccountabilitySanitizationSummary)
        };

        foreach (var type in payloadTypes)
        {
            GetInstanceProperties(type).Select(p => p.Name)
                .Should().NotContain(forbidden, $"for {type.Name}");
        }
    }

    [Fact]
    public void Payloads_Should_UseReadOnlyCollections()
    {
        var collectionProperties = new[]
        {
            typeof(AgentMemoryRecallAccountabilityPayload).GetProperty("DiagnosticCodes")!,
            typeof(AgentMemoryRecallAccountabilityPayload).GetProperty("RequestedKinds")!,
            typeof(AgentMemorySourceExpansionAccountabilityPayload).GetProperty("DiagnosticCodes")!,
            typeof(AgentMemoryAccountabilitySanitizationSummary).GetProperty("RedactionCodes")!,
            typeof(AgentMemoryAccountabilitySanitizationSummary).GetProperty("DiagnosticCodes")!
        };

        foreach (var property in collectionProperties)
        {
            property.PropertyType.Should().Be(typeof(IReadOnlyList<string>), property.Name);
        }
    }

    [Fact]
    public void PayloadNullability_Should_FollowV1Matrix()
    {
        AssertNullability<AgentMemoryRecallAccountabilityPayload>(new()
        {
            ["OperationId"] = false, ["Result"] = false, ["StableFailureCode"] = true,
            ["EffectivePackHash"] = true, ["ReturnedCount"] = false, ["WasTruncated"] = false,
            ["DiagnosticCodes"] = false, ["RequestedKinds"] = false, ["MaximumCount"] = false,
            ["CharacterBudget"] = false, ["MinimumConfidence"] = false
        });
        AssertNullability<AgentMemoryCurationAccountabilityPayload>(new()
        {
            ["OperationId"] = false, ["Operation"] = false, ["CandidateId"] = true,
            ["MemoryId"] = true, ["ReplacementCandidateId"] = true, ["NewMemoryId"] = true,
            ["ExpectedCandidateStateHash"] = true, ["ExpectedMemoryStateHash"] = true,
            ["ExpectedReplacementStateHash"] = true, ["ExpectedContentHash"] = true,
            ["PreviousState"] = true, ["ResultingState"] = true, ["Result"] = false,
            ["StableFailureCode"] = true, ["Sanitization"] = true
        });
        AssertNullability<AgentMemorySourceExpansionAccountabilityPayload>(new()
        {
            ["OperationId"] = false, ["SourceKind"] = false, ["SourceId"] = false,
            ["RangeStart"] = true, ["RangeEnd"] = true, ["Status"] = false,
            ["EffectiveVisibleContentHash"] = true, ["MaximumCharacters"] = false,
            ["WasTruncated"] = false, ["Sanitization"] = false, ["DiagnosticCodes"] = false
        });
        AssertNullability<AgentMemoryAccountabilitySanitizationSummary>(new()
        {
            ["State"] = false, ["RedactionCodes"] = false, ["DiagnosticCodes"] = false
        });
    }

    [Fact]
    public void PayloadRequiredMembers_Should_MatchV1Spec()
    {
        AssertRequiredMembers<AgentMemoryRecallAccountabilityPayload>(new[]
        {
            "OperationId", "Result", "ReturnedCount", "WasTruncated", "MaximumCount",
            "CharacterBudget", "MinimumConfidence"
        });
        AssertRequiredMembers<AgentMemoryCurationAccountabilityPayload>(new[]
        {
            "OperationId", "Operation", "Result"
        });
        AssertRequiredMembers<AgentMemorySourceExpansionAccountabilityPayload>(new[]
        {
            "OperationId", "SourceKind", "SourceId", "Status", "MaximumCharacters",
            "WasTruncated", "Sanitization"
        });
        AssertRequiredMembers<AgentMemoryAccountabilitySanitizationSummary>(new[]
        {
            "State"
        });
    }

    // ---- JSON context ----

    [Fact]
    public void JsonContext_Should_RoundTripRecallPayload_CamelCaseWithoutNulls()
    {
        var payload = new AgentMemoryRecallAccountabilityPayload
        {
            OperationId = "op-json",
            Result = "completed",
            EffectivePackHash = TestCanonicalHash("abc"),
            ReturnedCount = 2,
            WasTruncated = false,
            DiagnosticCodes = new[] { AgentMemoryDiagnosticCodes.BudgetTruncated.RequireValue() },
            RequestedKinds = new[] { "preference", "project-fact" },
            MaximumCount = 10,
            CharacterBudget = 4096,
            MinimumConfidence = "medium"
        };

        var json = JsonSerializer.Serialize(
            payload,
            AgentMemoryAccountabilityJsonSerializerContext.Default.AgentMemoryRecallAccountabilityPayload);

        json.Should().Contain("\"operationId\"");
        json.Should().NotContain("\"OperationId\"");
        json.Should().Contain("\"effectivePackHash\"");
        json.Should().NotContain("\"stableFailureCode\"");
        json.Should().NotContain("\"stableFailureCode\": null");

        var roundTripped = JsonSerializer.Deserialize(
            json,
            AgentMemoryAccountabilityJsonSerializerContext.Default.AgentMemoryRecallAccountabilityPayload);

        roundTripped.Should().BeEquivalentTo(payload);
    }

    [Fact]
    public void JsonContext_Should_RejectUnknownMembers()
    {
        const string json = """
            { "operationId": "op-1", "result": "completed", "unknown": 1 }
            """;

        var act = () => JsonSerializer.Deserialize(
            json,
            AgentMemoryAccountabilityJsonSerializerContext.Default.AgentMemoryRecallAccountabilityPayload);

        act.Should().Throw<JsonException>();
    }

    // ---- Producer ----

    [Fact]
    public void Producer_Should_AcceptIdentityContextAndPayloadOnly()
    {
        var methods = typeof(IAgentMemoryAccountabilityProducer).GetMethods(BindingFlags.Instance | BindingFlags.Public);
        methods.Should().HaveCount(3);

        foreach (var method in methods)
        {
            method.ReturnType.Should().Be(typeof(ValueTask));
            var parameters = method.GetParameters();
            parameters.Should().HaveCount(3);
            parameters[0].ParameterType.Should().Be(typeof(AgentMemoryOperationIdentity));
            parameters[1].ParameterType.Should().Be(typeof(AgentMemoryInvocationContext));
            parameters.Select(p => p.ParameterType).Should().NotContain(typeof(CancellationToken));
            parameters.Select(p => p.Name).Should().NotContain("timestamp");
        }
    }

    [Fact]
    public void Producer_Should_MapEachMethodToItsPayload()
    {
        var payloadByMethod = new Dictionary<string, Type>
        {
            ["PublishRecallAsync"] = typeof(AgentMemoryRecallAccountabilityPayload),
            ["PublishCurationAsync"] = typeof(AgentMemoryCurationAccountabilityPayload),
            ["PublishSourceExpansionAsync"] = typeof(AgentMemorySourceExpansionAccountabilityPayload)
        };

        var methods = typeof(IAgentMemoryAccountabilityProducer).GetMethods(BindingFlags.Instance | BindingFlags.Public);
        methods.Should().HaveCount(3);
        foreach (var method in methods)
        {
            payloadByMethod.Keys.Should().Contain(method.Name);
            method.GetParameters()[2].ParameterType.Should().Be(payloadByMethod[method.Name]);
        }
    }

    [Fact]
    public async Task NullProducer_Should_CompleteWithoutEffect()
    {
        var producer = new NullAgentMemoryAccountabilityProducer();
        var identity = new AgentMemoryOperationIdentity
        {
            OperationId = "op-null",
            OccurredAt = new DateTimeOffset(2026, 8, 11, 1, 0, 0, TimeSpan.Zero)
        };
        var context = CreateContext();

        var recall = producer.PublishRecallAsync(
            identity, context, new AgentMemoryRecallAccountabilityPayload
            {
                OperationId = identity.OperationId,
                Result = "completed",
                ReturnedCount = 0,
                WasTruncated = false,
                MaximumCount = 10,
                CharacterBudget = 4096,
                MinimumConfidence = "low"
            });
        recall.IsCompletedSuccessfully.Should().BeTrue();
        await recall;

        var curation = producer.PublishCurationAsync(
            identity, context, new AgentMemoryCurationAccountabilityPayload
            {
                OperationId = identity.OperationId,
                Operation = "reject",
                Result = "committed"
            });
        curation.IsCompletedSuccessfully.Should().BeTrue();

        var expansion = producer.PublishSourceExpansionAsync(
            identity, context, new AgentMemorySourceExpansionAccountabilityPayload
            {
                OperationId = identity.OperationId,
                SourceKind = "TaskRecord",
                SourceId = "src-1",
                Status = "redacted",
                MaximumCharacters = 4096,
                WasTruncated = false,
                Sanitization = new AgentMemoryAccountabilitySanitizationSummary { State = "redacted" }
            });
        expansion.IsCompletedSuccessfully.Should().BeTrue();
    }

    // ---- Identity factory ----

    [Fact]
    public void IdentityFactory_Should_AllocateNonDefaultStablePair()
    {
        var now = new DateTimeOffset(2026, 8, 11, 5, 0, 0, TimeSpan.Zero);
        var time = new FixedTimeProvider(now);
        var factory = new DefaultAgentMemoryOperationIdentityFactory(time);

        var first = factory.Create();
        var second = factory.Create();

        first.OperationId.Should().NotBeNullOrWhiteSpace();
        first.OccurredAt.Should().Be(now);
        first.OperationId.Should().NotBe(second.OperationId);
        second.OccurredAt.Should().Be(now);
    }

    // ---- Standalone runtime ----

    [Fact]
    public void StandaloneRuntime_Should_ResolveNullProducerAndOneIdentityFactory()
    {
        var services = new ServiceCollection();
        services.AddAgentMemoryReadRuntime();

        using var provider = services.BuildServiceProvider();
        var producer = provider.GetRequiredService<IAgentMemoryAccountabilityProducer>();
        var factory = provider.GetRequiredService<IAgentMemoryOperationIdentityFactory>();

        producer.Should().BeOfType<NullAgentMemoryAccountabilityProducer>();
        factory.Should().BeOfType<DefaultAgentMemoryOperationIdentityFactory>();

        provider.GetRequiredService<IAgentMemoryAccountabilityProducer>().Should().BeSameAs(producer);
        provider.GetRequiredService<IAgentMemoryOperationIdentityFactory>().Should().BeSameAs(factory);
    }

    // ---- Helpers ----

    private static PropertyInfo[] GetInstanceProperties(Type type) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetIndexParameters().Length == 0)
            .ToArray();

    private static void AssertExactProperties<T>(IEnumerable<string> expected)
    {
        GetInstanceProperties(typeof(T)).Select(p => p.Name)
            .Should().BeEquivalentTo(expected);
    }

    private static void AssertNullability<T>(Dictionary<string, bool> expected)
    {
        var actual = GetInstanceProperties(typeof(T))
            .ToDictionary(p => p.Name, IsNullable);
        actual.Should().BeEquivalentTo(expected);
    }

    private static void AssertRequiredMembers<T>(IEnumerable<string> expected)
    {
        var actual = GetInstanceProperties(typeof(T))
            .Where(p => p.GetCustomAttribute<RequiredMemberAttribute>() is not null)
            .Select(p => p.Name);
        actual.Should().BeEquivalentTo(expected);
    }

    private static bool IsNullable(PropertyInfo property)
    {
        if (property.PropertyType.IsValueType)
        {
            return Nullable.GetUnderlyingType(property.PropertyType) is not null;
        }

        return new NullabilityInfoContext().Create(property).ReadState == NullabilityState.Nullable;
    }

    private static AgentMemoryInvocationContext CreateContext() => new()
    {
        TenantId = "tenant-1",
        ActorId = "actor-1",
        ActorKind = "Agent"
    };

    private static CanonicalHash TestCanonicalHash(string value) => new()
    {
        Value = value,
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = "AgentMemoryAccountabilityEffectivePack",
        Scope = "TenantVisible",
        Purpose = "AuditEvidence",
        ContractVersion = "agent-memory-accountability-effective-pack-v1",
        CanonicalShapeVersion = "agent-memory-accountability-effective-pack-v1"
    };

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
