using CrestCreates.OpenApi;

namespace CrestCreates.AspNetCore.Authentication.OpenIddict;

/// <summary>
/// Contributes source-generated JSON metadata for OpenIddict DTO types
/// to the shared OpenAPI <see cref="JsonSerializerOptions"/>.
/// </summary>
public sealed class OpenIddictJsonTypeInfoContributor
    : JsonTypeInfoContributor<OpenIddictJsonContext>;