using System.Text.Json;
using System.Text.Json.Serialization;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.CapabilityEndpoint.TrimmingFixture;

/// <summary>
/// Application-owned JsonSerializerContext for AOT-safe JSON serialization.
/// The application declares which body types need JSON metadata here.
/// STJ source generator processes this and generates Default/property accessors.
/// </summary>
[JsonSerializable(typeof(GreetingRequest))]
[JsonSerializable(typeof(GreetingResponse))]
internal sealed partial class ApplicationApiJsonContext : JsonSerializerContext;
