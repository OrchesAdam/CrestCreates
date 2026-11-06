using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.DynamicApi.Tests;

public class CrestHttpJsonResolverExtensionsTests
{
    [Fact]
    public void Insertion_places_generated_resolver_before_the_untouched_default()
    {
        var services = new ServiceCollection();
        var defaultResolver = new DefaultJsonTypeInfoResolver();
        services.Configure<JsonOptions>(options =>
            options.SerializerOptions.TypeInfoResolverChain.Add(defaultResolver));
        var generated = new ProbeResolver();

        services.AddCrestHttpJsonResolver(generated);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<JsonOptions>>().Value;

        options.SerializerOptions.TypeInfoResolverChain.Should().ContainInOrder(generated, defaultResolver);
        options.SerializerOptions.UnmappedMemberHandling.Should().Be(JsonUnmappedMemberHandling.Disallow);
    }

    [Fact]
    public void Without_a_default_resolver_the_generated_resolver_is_appended()
    {
        var services = new ServiceCollection();
        services.Configure<JsonOptions>(options =>
            options.SerializerOptions.TypeInfoResolverChain.Clear());
        var generated = new ProbeResolver();

        services.AddCrestHttpJsonResolver(generated, disallowUnmappedMembers: false);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<JsonOptions>>().Value;

        options.SerializerOptions.TypeInfoResolverChain.Should().ContainSingle()
            .Which.Should().BeSameAs(generated);
        options.SerializerOptions.UnmappedMemberHandling.Should().Be(JsonUnmappedMemberHandling.Skip);
    }

    private sealed class ProbeResolver : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) => null;
    }
}
