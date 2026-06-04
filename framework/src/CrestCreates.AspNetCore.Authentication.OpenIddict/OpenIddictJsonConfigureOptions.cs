using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace CrestCreates.AspNetCore.Authentication.OpenIddict;

/// <summary>
/// Post-configures <see cref="JsonOptions"/> to include source-generated JSON metadata
/// for OpenIddict DTO types, ensuring AoT/trimming compatibility when OpenAPI
/// generates schemas for the /connect/token endpoint.
/// Uses <see cref="IPostConfigureOptions{T}"/> to ensure it runs after all other
/// <see cref="IConfigureOptions{T}"/> registrations.
/// </summary>
public sealed class OpenIddictJsonPostConfigureOptions : IPostConfigureOptions<JsonOptions>
{
    public void PostConfigure(string? name, JsonOptions options)
    {
        options.SerializerOptions.TypeInfoResolverChain.Add(OpenIddictJsonContext.Default);
    }
}
