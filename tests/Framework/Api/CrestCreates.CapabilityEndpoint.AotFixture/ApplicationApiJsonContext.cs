using System.Text.Json;
using System.Text.Json.Serialization;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.DynamicApi;

namespace CrestCreates.CapabilityEndpoint.AotFixture;

/// <summary>
/// Application-owned JsonSerializerContext for AOT-safe JSON serialization.
/// The application declares which body types need JSON metadata here.
/// STJ source generator processes this and generates Default/property accessors.
/// </summary>
[JsonSerializable(typeof(GreetingRequest))]
[JsonSerializable(typeof(GreetingResponse))]
[JsonSerializable(typeof(List<GreetingResponse>))]
// Compatibility projection result envelopes are serialized with the runtime
// output type erased to object, so the app must declare this closed root
// (same pattern as the Procurement/Asset golden hosts) or native requests fail.
[JsonSerializable(typeof(DynamicApiResponse<object>))]
public sealed partial class ApplicationApiJsonContext : JsonSerializerContext;
