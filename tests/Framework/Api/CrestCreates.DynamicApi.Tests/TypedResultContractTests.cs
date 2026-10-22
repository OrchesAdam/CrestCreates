using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using CrestCreates.DynamicApi;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.DynamicApi.Tests;

internal sealed class ResultProbeDto
{
    public string Name { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ResultProbeDto))]
[JsonSerializable(typeof(DynamicApiResponse<ResultProbeDto>))]
[JsonSerializable(typeof(DynamicApiResponse))]
internal partial class ResultProbeJsonContext : JsonSerializerContext
{
}

/// <summary>
/// #128 S3 typed result contract: the typed, no-fallback overloads must produce the envelope through
/// the caller's declared type and the host's source-generated metadata, and must fail closed when that
/// metadata is absent. The host below configures JsonOptions with the generated context only, so a
/// passing serialization cannot have come from a reflection resolver.
/// </summary>
public class TypedResultContractTests
{
    [Fact]
    public void Public_response_mapper_exposes_only_metadata_required_overloads()
    {
        var methods = typeof(CompatibilityHttpResultMapper)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name.StartsWith("Wrap", StringComparison.Ordinal))
            .ToArray();

        methods.Should().NotBeEmpty();
        foreach (var method in methods)
        {
            method.GetParameters().Should().NotContain(
                parameter => parameter.ParameterType == typeof(IServiceProvider),
                $"'{method.Name}' must not expose the archived reflection/IServiceProvider fallback");
            method.GetParameters().Should().Contain(
                parameter => parameter.ParameterType.IsGenericType
                             && parameter.ParameterType.GetGenericTypeDefinition() == typeof(JsonTypeInfo<>),
                $"'{method.Name}' must require source-generated JSON metadata");
        }

        typeof(CompatibilityHttpResultMapper)
            .GetMethod("MapAotJson", BindingFlags.NonPublic | BindingFlags.Static)
            .Should().BeNull("the reflection fallback helper was archived with the legacy runtime");
    }

    [Fact]
    public async Task Typed_result_must_serialize_through_the_generated_metadata()
    {
        var services = BuildServices();

        var result = CompatibilityHttpResultMapper.WrapResult<ResultProbeDto>(
            new ResultProbeDto { Name = "board" },
            CapabilityEndpointJsonTypeInfoResolver.Require<DynamicApiResponse<ResultProbeDto>>(services));

        var (status, body) = await ExecuteAsync(result, services);
        status.Should().Be(StatusCodes.Status200OK);
        body.Should().Contain("\"data\":{\"name\":\"board\"}");
        body.Should().Contain("\"code\":200");
    }

    [Fact]
    public async Task Typed_get_result_must_return_the_404_envelope_for_null()
    {
        var services = BuildServices();

        var result = CompatibilityHttpResultMapper.WrapGetResult<ResultProbeDto>(
            null,
            CapabilityEndpointJsonTypeInfoResolver.Require<DynamicApiResponse<ResultProbeDto>>(services),
            CapabilityEndpointJsonTypeInfoResolver.Require<DynamicApiResponse>(services));

        var (status, body) = await ExecuteAsync(result, services);
        status.Should().Be(StatusCodes.Status404NotFound);
        body.Should().Contain("\"code\":404");
        body.Should().NotContain("\"data\"");
    }

    [Fact]
    public async Task Typed_void_result_must_serialize_the_void_envelope()
    {
        var services = BuildServices();

        var result = CompatibilityHttpResultMapper.WrapVoidResult(
            CapabilityEndpointJsonTypeInfoResolver.Require<DynamicApiResponse>(services));

        var (status, body) = await ExecuteAsync(result, services);
        status.Should().Be(StatusCodes.Status200OK);
        body.Should().Contain("\"code\":200");
        body.Should().NotContain("\"data\"");
    }

    [Fact]
    public void Missing_metadata_must_fail_closed_instead_of_falling_back()
    {
        var services = BuildServices();

        // The host's context has no metadata for this marker type, and JsonOptions carries no reflection
        // resolver, so §7.3's rule is observable: the typed accessor refuses instead of guessing.
        var act = () => CapabilityEndpointJsonTypeInfoResolver.Require<TypedResultContractTests>(services);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not available in this host*");
    }

    [Fact]
    public void Typed_overloads_must_reject_missing_metadata_references()
    {
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<DynamicApiResponse<ResultProbeDto>>? missingEnvelope = null;
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<DynamicApiResponse>? missingVoid = null;

        Action wrap = () => CompatibilityHttpResultMapper.WrapResult(
            new ResultProbeDto(), missingEnvelope!);
        Action wrapVoid = () => CompatibilityHttpResultMapper.WrapVoidResult(missingVoid!);

        wrap.Should().Throw<ArgumentNullException>();
        wrapVoid.Should().Throw<ArgumentNullException>();
    }

    private static ServiceProvider BuildServices() =>
        new ServiceCollection()
            .AddLogging()
            .Configure<JsonOptions>(options =>
            {
                options.SerializerOptions.TypeInfoResolver = ResultProbeJsonContext.Default;
            })
            .BuildServiceProvider();

    private static async Task<(int Status, string Body)> ExecuteAsync(IResult result, IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context.Response.StatusCode, body);
    }
}
