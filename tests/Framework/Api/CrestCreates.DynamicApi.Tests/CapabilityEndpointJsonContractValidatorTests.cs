using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CrestCreates.DynamicApi.Tests;

public class CapabilityEndpointJsonContractValidatorTests : IAsyncDisposable
{
    public CapabilityEndpointJsonContractValidatorTests()
    {
        // Reset registry before each test
        CapabilityEndpointJsonContractRegistry.Reset();
    }

    [Fact]
    public void Validate_MissingType_ThrowsInvalidOperationException()
    {
        // Register a type that has NO JsonTypeInfo in the application's JsonSerializerContext
        CapabilityEndpointJsonContractRegistry.RegisterBodyType(typeof(MissingRequest));

        // Create a minimal resolver that only knows about basic types, not MissingRequest
        var services = new ServiceCollection();
        services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.TypeInfoResolver = new BasicTypesOnlyResolver();
        });
        var serviceProvider = services.BuildServiceProvider();

        var validator = new CapabilityEndpointJsonContractValidator(serviceProvider);

        var act = () => validator.Validate();

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("MissingRequest");
    }

    [Fact]
    public void Validate_AllTypesPresent_Succeeds()
    {
        // Register a type that IS available via DefaultJsonTypeInfoResolver
        CapabilityEndpointJsonContractRegistry.RegisterBodyType(typeof(string));

        var services = new ServiceCollection();
        services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
        });
        var serviceProvider = services.BuildServiceProvider();

        var validator = new CapabilityEndpointJsonContractValidator(serviceProvider);

        // Should not throw — string is always resolvable
        var act = () => validator.Validate();
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_NoRegisteredTypes_Succeeds()
    {
        // No types registered — validator should pass silently
        var services = new ServiceCollection();
        services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
        });
        var serviceProvider = services.BuildServiceProvider();

        var validator = new CapabilityEndpointJsonContractValidator(serviceProvider);

        var act = () => validator.Validate();
        act.Should().NotThrow();
    }

    public ValueTask DisposeAsync()
    {
        // Reset registry after each test
        CapabilityEndpointJsonContractRegistry.Reset();
        return ValueTask.CompletedTask;
    }

    // A type that our restricted resolver won't produce JsonTypeInfo for.
    // Used to test validation failure.
    private record MissingRequest(string Name);

    /// <summary>
    /// A minimal IJsonTypeInfoResolver that only resolves basic BCL types.
    /// Returns null for any custom type, simulating a source-generated context
    /// that doesn't include the requested type.
    /// </summary>
    private sealed class BasicTypesOnlyResolver : IJsonTypeInfoResolver
    {
        private static readonly HashSet<Type> SupportedTypes =
        [
            typeof(string), typeof(int), typeof(bool), typeof(long),
            typeof(double), typeof(decimal), typeof(Guid), typeof(DateTime),
            typeof(DateTimeOffset), typeof(byte[]), typeof(object)
        ];

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            if (SupportedTypes.Contains(type))
                return new DefaultJsonTypeInfoResolver().GetTypeInfo(type, options);
            return null;
        }
    }
}
