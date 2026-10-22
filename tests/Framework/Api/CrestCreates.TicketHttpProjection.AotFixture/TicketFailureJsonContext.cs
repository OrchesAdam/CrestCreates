using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace CrestCreates.TicketHttpProjection.AotFixture;

/// <summary>
/// The fixture's fallback failure body is ASP.NET Core's ProblemDetails. It must be answerable by a
/// source-generated context in this reflection-disabled/ native host.
/// </summary>
[JsonSerializable(typeof(ProblemDetails))]
internal partial class TicketFailureJsonContext : JsonSerializerContext;
