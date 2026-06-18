using System.Text.Json;
using CrestCreates.OpenApi;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace CrestCreates.OpenApi;

/// <summary>
/// Post-configures <see cref="JsonOptions"/> by aggregating all registered
/// <see cref="IOpenApiJsonTypeInfoContributor"/> resolvers into the
/// <see cref="JsonSerializerOptions.TypeInfoResolverChain"/>.
/// This ensures AoT/trimming compatibility for OpenAPI schema generation.
/// </summary>
internal sealed class OpenApiJsonTypeInfoPostConfigureOptions : IPostConfigureOptions<JsonOptions>
{
    private readonly IEnumerable<IOpenApiJsonTypeInfoContributor> _contributors;

    public OpenApiJsonTypeInfoPostConfigureOptions(
        IEnumerable<IOpenApiJsonTypeInfoContributor> contributors)
    {
        _contributors = contributors;
    }

    public void PostConfigure(string? name, JsonOptions options)
    {
        foreach (var contributor in _contributors)
        {
            options.SerializerOptions.TypeInfoResolverChain.Add(contributor.Resolver);
        }
    }
}
