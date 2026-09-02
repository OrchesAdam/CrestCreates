using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using CrestCreates.Agent.ControlPlane;
using CrestCreates.Localization.Services;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using Microsoft.Extensions.Logging.Abstractions;

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

            var reflectionFallbackDisabled = options.TypeInfoResolver is not DefaultJsonTypeInfoResolver
                && !options.TypeInfoResolverChain.Any(resolver => resolver is DefaultJsonTypeInfoResolver);

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

            var localizedCatalog = new DefaultDescriptorReviewMessageTemplateCatalog(
                new KeyReturningLocalizationService("zh-CN"),
                NullLogger<DefaultDescriptorReviewMessageTemplateCatalog>.Instance);
            var localizedMessage = localizedCatalog.Format(
                DescriptorActivationMessageTemplateIds.ActivationBlocked,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["BlockingReasons"] = "策略检查"
                });
            var localizedMessagePassed = string.Equals(
                localizedMessage,
                "草稿不符合激活条件：策略检查。",
                StringComparison.Ordinal);
            allPassed &= localizedMessagePassed;
            if (localizedMessagePassed)
                Console.WriteLine("CONTROL_PLANE_LOCALIZED_MESSAGE_NATIVEAOT_OK");
            else
                Console.Error.WriteLine($"FAIL: localized descriptor-governance message was '{localizedMessage}'.");

            bool unregisteredTypeRejected = false;
            try
            {
                var unregisteredTypeInfo = context.GetTypeInfo(typeof(System.Net.Http.HttpClient));
                unregisteredTypeRejected = unregisteredTypeInfo is null;
            }
            catch (NotSupportedException)
            {
                unregisteredTypeRejected = true;
            }

            if (!unregisteredTypeRejected)
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

    private sealed class KeyReturningLocalizationService(string currentCulture) : ILocalizationService
    {
        public string CurrentCulture { get; } = currentCulture;
        public string GetString(string key) => key;
        public string GetString(string key, params object[] arguments) => key;
        public string GetString(string key, string cultureName) => key;
        public string GetString(string key, string cultureName, params object[] arguments) => key;
        public Task<string?> GetStringAsync(string key) => Task.FromResult<string?>(key);
        public Task<string?> GetStringAsync(string key, params object[] arguments) => Task.FromResult<string?>(key);
        public Task<string?> GetStringAsync(string key, string cultureName) => Task.FromResult<string?>(key);
        public Task<string?> GetStringAsync(string key, string cultureName, params object[] arguments) => Task.FromResult<string?>(key);
        public IDisposable ChangeCulture(string cultureName) => throw new NotSupportedException();
        public Task<IDisposable> ChangeCultureAsync(string cultureName) => throw new NotSupportedException();
    }
}
