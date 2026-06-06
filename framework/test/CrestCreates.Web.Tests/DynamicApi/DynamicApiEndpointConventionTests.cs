using System;
using CrestCreates.DynamicApi;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Web.Tests.DynamicApi;

public class DynamicApiEndpointConventionTests
{
    [Fact]
    public void DynamicApiOptions_ShouldRegisterEndpointConvention()
    {
        var options = new DynamicApiOptions();

        options.AddEndpointConvention<TestEndpointConvention>();

        options.EndpointConventionTypes.Should().Contain(typeof(TestEndpointConvention));
    }

    [Fact]
    public void DynamicApiEndpointConventionRunner_ShouldApplyRegisteredConventions()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestEndpointConvention>();
        using var provider = services.BuildServiceProvider();

        var descriptor = new DynamicApiEndpointDescriptor(
            "Book",
            "GetList",
            "GET",
            "/api/books",
            typeof(object),
            typeof(object),
            typeof(string),
            Array.Empty<string>(),
            false);

        var builder = new RouteHandlerBuilder(Array.Empty<IEndpointConventionBuilder>());
        var context = new DynamicApiEndpointConventionContext(descriptor, builder);
        var options = new DynamicApiOptions();
        options.AddEndpointConvention<TestEndpointConvention>();

        DynamicApiEndpointConventionRunner.Apply(provider, options, context);

        var convention = provider.GetRequiredService<TestEndpointConvention>();
        convention.AppliedActionName.Should().Be("GetList");
    }

    [Fact]
    public void AddEndpointConvention_ShouldNotDuplicate()
    {
        var options = new DynamicApiOptions();
        options.AddEndpointConvention<TestEndpointConvention>();
        options.AddEndpointConvention<TestEndpointConvention>();

        options.EndpointConventionTypes.Should().ContainSingle()
            .Which.Should().Be(typeof(TestEndpointConvention));
    }

    private sealed class TestEndpointConvention : IDynamicApiEndpointConvention
    {
        public string? AppliedActionName { get; private set; }

        public void Apply(DynamicApiEndpointConventionContext context)
        {
            AppliedActionName = context.Descriptor.ActionName;
        }
    }
}