using System.Text.Json;
using System.Text.Json.Serialization;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

public static class ControlPlaneJsonContractFixtureRunner
{
    private static readonly CanonicalHash FixtureHash = new()
    {
        Value = "abc123",
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = "Descriptor",
        Scope = "InternalFull",
        Purpose = "Contract",
        ContractVersion = "canonical-hash-v1",
        CanonicalShapeVersion = "schema-contract-hash-v1"
    };

    public static int Main()
    {
        try
        {
            var context = AgentControlPlaneToolJsonSerializerContext.Default;
            var options = context.Options;

            bool reflectionFallbackDisabled = true;
            try
            {
                _ = Activator.CreateInstance(
                    Type.GetType("System.Text.Json.Metadata.DefaultJsonTypeInfoResolver, System.Text.Json")!);
                reflectionFallbackDisabled = false;
            }
            catch
            {
            }

            if (!reflectionFallbackDisabled)
            {
                Console.Error.WriteLine("FAIL: DefaultJsonTypeInfoResolver found in resolver chain.");
                return 1;
            }

            Console.WriteLine($"ReflectionFallback_IsDisabled:{(reflectionFallbackDisabled ? "PASS" : "FAIL")}");

            bool allPassed = true;

            allPassed &= RoundTrip<DescriptorSearchRequest>(
                new DescriptorSearchRequest { NameContains = "test-query", MaxResults = 10 },
                "DescriptorSearchRequest");

            allPassed &= RoundTrip<AgentToolResult<string>>(
                AgentToolResult<string>.Success("hello-aot"),
                "AgentToolResult<string>");

            allPassed &= RoundTrip<DescriptorActivationReviewDecision>(
                new DescriptorActivationReviewDecision
                {
                    ActivationRequestId = "act-1",
                    TenantId = "tenant-1",
                    CorrelationId = "corr-1",
                    Decision = DescriptorActivationReviewOutcome.Approved,
                    ActorKind = DescriptorActivationActorKind.Human,
                    ActorId = "reviewer-1",
                    Reason = "AOT fixture test",
                    DecidedAt = DateTimeOffset.UtcNow,
                    BoundEvidenceHash = FixtureHash,
                    BoundEnvelopeHash = FixtureHash
                },
                "DescriptorActivationReviewDecision");

            allPassed &= RoundTrip<CanonicalHash>(FixtureHash, "CanonicalHash");

            allPassed &= RoundTrip<DescriptorReviewReportFormat>(DescriptorReviewReportFormat.Markdown, "DescriptorReviewReportFormat");

            bool failClosed = false;
            try
            {
                var unregisteredTypeInfo = context.GetTypeInfo(typeof(System.Net.Http.HttpClient));
                if (unregisteredTypeInfo != null)
                    failClosed = true;
            }
            catch (InvalidOperationException)
            {
            }

            if (failClosed)
            {
                Console.Error.WriteLine("FAIL: Unregistered type resolved — fail-closed violated.");
                allPassed = false;
            }

            Console.WriteLine($"SerializeDeserialize_RepresentativeToolRoots:{(allPassed ? "PASS" : "FAIL")}");

            if (reflectionFallbackDisabled && allPassed)
            {
                Console.WriteLine("CONTROL_PLANE_JSON_CONTRACT_NATIVEAOT_OK");
                return 0;
            }

            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"UNEXPECTED: {ex.Message}");
            return 3;
        }
    }

    private static bool RoundTrip<T>(T value, string label) where T : notnull
    {
        try
        {
            var context = AgentControlPlaneToolJsonSerializerContext.Default;
            var json = JsonSerializer.Serialize(value, typeof(T), context);
            var deserialized = JsonSerializer.Deserialize(json, typeof(T), context);
            if (deserialized is null)
            {
                Console.Error.WriteLine($"FAIL [{label}]: deserialized null.");
                return false;
            }

            var reJson = JsonSerializer.Serialize(deserialized, typeof(T), context);
            if (json != reJson)
            {
                Console.Error.WriteLine($"FAIL [{label}]: round-trip mismatch.");
                Console.Error.WriteLine($"  Original:  {json}");
                Console.Error.WriteLine($"  RoundTrip: {reJson}");
                return false;
            }

            Console.WriteLine($"  OK [{label}]");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL [{label}]: {ex.Message}");
            return false;
        }
    }
}
