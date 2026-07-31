using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Json;
using CrestCreates.Accountability.Abstractions.Semantics;
using CrestCreates.Accountability.Abstractions.Sinks;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Accountability.Abstractions.Tests.Contract;

public sealed class AuditEnvelopeContractTests
{
    [Fact]
    public void AuditTagMapEmptyUsesOrdinalComparer()
    {
        AuditTagMap.Empty.KeyComparer.Should().BeSameAs(StringComparer.Ordinal);
    }

    [Fact]
    public void EnvelopeDefaultsToOrdinalAuditTagMap()
    {
        var envelope = CreateEnvelope();

        envelope.Tags.KeyComparer.Should().BeSameAs(StringComparer.Ordinal);
        envelope.Evidence.IsDefault.Should().BeFalse();
        envelope.Runtime.References.IsDefault.Should().BeFalse();
        envelope.Descriptors.Items.IsDefault.Should().BeFalse();
    }

    [Fact]
    public void UsesImmutableCollectionsAndClonedJsonElements()
    {
        var envelope = CreateEnvelope() with
        {
            Payload = new AuditPayload
            {
                Kind = "test.payload",
                Version = 1,
                Data = JsonDocument.Parse("{\"value\":1}").RootElement.Clone()
            },
            Evidence = [new AuditEvidenceReference { Kind = "test", Id = "evidence-1" }]
        };

        envelope.Evidence.Should().BeOfType<ImmutableArray<AuditEvidenceReference>>();
        envelope.Payload!.Data.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public void PreservesUnknownStableExtensionKinds()
    {
        var actor = new AuditActor { Kind = "extension.actor", Id = "a-1" };
        var action = new AuditAction { Kind = "extension.action", Name = "do" };
        var target = new AuditTarget { Kind = "extension.target", Id = "t-1" };

        actor.Kind.Should().Be("extension.actor");
        action.Kind.Should().Be("extension.action");
        target.Kind.Should().Be("extension.target");
    }

    [Fact]
    public void RoundTripsWithGeneratedJsonTypeInfo()
    {
        var envelope = CreateEnvelope();
        var json = JsonSerializer.Serialize(envelope, AccountabilityJsonSerializerContext.Default.AuditEnvelope);
        var restored = JsonSerializer.Deserialize(json, AccountabilityJsonSerializerContext.Default.AuditEnvelope);

        restored.Should().NotBeNull();
        restored!.AuditId.Should().Be(envelope.AuditId);
        restored.CorrelationId.Should().Be(envelope.CorrelationId);
        restored.Actor.Kind.Should().Be(envelope.Actor.Kind);
        restored.Tags.Should().BeEquivalentTo(envelope.Tags);
    }

    [Fact]
    public void AccountabilityJsonContextHasNoHandwrittenTransitiveRootLedger()
    {
        AccountabilityJsonSerializerContext.AccountabilityJsonSerializerContextRootManifest
            .ExplicitRootTypes.Should().BeEmpty();
    }

    [Fact]
    public void GeneratedManifestOwnsAuditSinkDirectRoots()
    {
        AccountabilityJsonSerializerContext.AccountabilityJsonSerializerContextRootManifest
            .SurfaceRootTypes.Should().BeEquivalentTo([typeof(AuditEnvelope), typeof(AuditSinkWriteResult)]);
    }

    [Fact]
    public void CancellationTokenIsExcludedFromContractSurface()
    {
        AccountabilityJsonSerializerContext.AccountabilityJsonSerializerContextRootManifest
            .AllDirectRootTypes.Should().NotContain(typeof(CancellationToken));
    }

    [Fact]
    public void TagOrderingIsCultureIndependent()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            var values = new[] { "en-US", "tr-TR", "zh-CN" }
                .Select(name =>
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                    return AuditTagMap.Empty
                        .Add("I", "one")
                        .Add("i", "two")
                        .Keys
                        .ToArray();
                })
                .ToArray();

            values.Should().AllBeEquivalentTo(values[0]);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static AuditEnvelope CreateEnvelope()
        => new()
        {
            AuditId = "audit-1",
            OccurredAt = DateTimeOffset.Parse("2026-07-29T00:00:00Z", CultureInfo.InvariantCulture),
            CorrelationId = "correlation-1",
            Actor = new AuditActor { Kind = AuditActorKinds.User, Id = "user-1" },
            Action = new AuditAction { Kind = AuditActionKinds.HttpRequest, Name = "GET /items" },
            Target = new AuditTarget { Kind = "http-route", Id = "GET /items" },
            Outcome = new AuditOutcome { Status = AuditOutcomeStatuses.Succeeded },
            Tags = AuditTagMap.Empty.Add("z", "last").Add("a", "first")
        };
}
