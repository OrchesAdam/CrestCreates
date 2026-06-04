using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CrestCreates.OpenApi;

/// <summary>
/// Marker interface for contributing <see cref="IJsonTypeInfoResolver"/> instances
/// to the shared <see cref="JsonSerializerOptions"/> used by OpenAPI schema generation.
/// Each module that defines source-generated <see cref="JsonSerializerContext"/> types
/// should register an implementation of this interface.
/// </summary>
public interface IOpenApiJsonTypeInfoContributor
{
    IJsonTypeInfoResolver Resolver { get; }
}

/// <summary>
/// Generic <see cref="IOpenApiJsonTypeInfoContributor"/> that wraps a <see cref="JsonSerializerContext"/>.
/// </summary>
/// <typeparam name="TContext">The <see cref="JsonSerializerContext"/> type to contribute.</typeparam>
public class JsonTypeInfoContributor<TContext> : IOpenApiJsonTypeInfoContributor
    where TContext : JsonSerializerContext, new()
{
    public IJsonTypeInfoResolver Resolver { get; } = new TContext();
}
