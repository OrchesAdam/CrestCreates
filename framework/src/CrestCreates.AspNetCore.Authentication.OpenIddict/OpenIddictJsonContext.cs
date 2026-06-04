using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestCreates.AspNetCore.Authentication.OpenIddict;

[JsonSerializable(typeof(OpenIddictEndpointRouteBuilderExtensions.OpenIddictTokenRequest))]
[JsonSerializable(typeof(OpenIddictEndpointRouteBuilderExtensions.OpenIddictTokenResponse))]
[JsonSerializable(typeof(OpenIddictEndpointRouteBuilderExtensions.OpenIddictErrorResponse))]
[JsonSerializable(typeof(OpenIddictEndpointRouteBuilderExtensions.OpenIddictLogoutResponse))]
public sealed partial class OpenIddictJsonContext : JsonSerializerContext;