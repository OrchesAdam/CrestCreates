using System.Text.Json;
using System.Text.Json.Serialization;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.DynamicApi;

namespace CrestCreates.CapabilityEndpoint.AotFixture;

/// <summary>
/// Application-owned JsonSerializerContext for AOT-safe JSON serialization.
/// The application declares which body types need JSON metadata here.
/// STJ source generator processes this and generates Default/property accessors.
/// The compatibility result contracts serialize through the typed, no-fallback overloads
/// (source-generated metadata only), so the closed envelope roots must be declared here —
/// a native host has no reflection resolver to fill a missing typed root.
/// </summary>
[JsonSerializable(typeof(GreetingRequest))]
[JsonSerializable(typeof(GreetingResponse))]
[JsonSerializable(typeof(List<GreetingResponse>))]
[JsonSerializable(typeof(DynamicApiResponse))]
[JsonSerializable(typeof(DynamicApiResponse<GreetingResponse>))]
[JsonSerializable(typeof(DynamicApiResponse<List<GreetingResponse>>))]
public sealed partial class ApplicationApiJsonContext : JsonSerializerContext;
