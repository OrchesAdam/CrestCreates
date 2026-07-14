using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.DynamicApi;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;
using FluentAssertions;

namespace CrestCreates.DynamicApi.Tests;

/// <summary>
/// Tests for CapabilityEndpointBodyReader covering leading-whitespace JSON,
/// whitespace-only body, empty body, JSON null, and invalid JSON scenarios.
/// </summary>
public class CapabilityEndpointBodyReaderTests
{
    // Simple DTO for testing
    private record TestDto(string Name);

    private static JsonTypeInfo<TestDto> GetTypeInfo()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        return options.GetTypeInfo(typeof(TestDto)) as JsonTypeInfo<TestDto>
            ?? throw new InvalidOperationException("Failed to resolve JsonTypeInfo<TestDto>");
    }

    private static HttpContext CreateHttpContext(string? body, long? contentLength = null)
    {
        var httpContext = new DefaultHttpContext();
        var stream = body is not null
            ? new MemoryStream(Encoding.UTF8.GetBytes(body))
            : new MemoryStream();
        httpContext.Request.Body = stream;
        httpContext.Request.ContentLength = contentLength ?? stream.Length;

        // Provide IOptions<JsonOptions> via service provider
        var jsonOptions = new Microsoft.AspNetCore.Http.Json.JsonOptions();
        var optionsMock = new Mock<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>();
        optionsMock.Setup(o => o.Value).Returns(jsonOptions);
        var spMock = new Mock<IServiceProvider>();
        spMock.Setup(sp => sp.GetService(typeof(Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>)))
              .Returns(optionsMock.Object);
        httpContext.RequestServices = spMock.Object;

        return httpContext;
    }

    // === ReadNativeBodyAsync tests ===

    [Fact]
    public async Task Native_LeadingWhitespace_ValidJson_Deserializes()
    {
        var httpContext = CreateHttpContext("  { \"Name\": \"World\" }");
        var jsonTypeInfo = GetTypeInfo();

        var result = await CapabilityEndpointBodyReader.ReadNativeBodyAsync<TestDto>(
            httpContext, jsonTypeInfo, optional: false);

        result.Should().NotBeNull();
        result!.Name.Should().Be("World");
    }

    [Fact]
    public async Task Native_LeadingNewline_ValidJson_Deserializes()
    {
        var httpContext = CreateHttpContext("\n{\n  \"Name\": \"World\"\n}");
        var jsonTypeInfo = GetTypeInfo();

        var result = await CapabilityEndpointBodyReader.ReadNativeBodyAsync<TestDto>(
            httpContext, jsonTypeInfo, optional: false);

        result.Should().NotBeNull();
        result!.Name.Should().Be("World");
    }

    [Fact]
    public async Task Native_WhitespaceOnly_Required_Throws()
    {
        var httpContext = CreateHttpContext("   ");
        var jsonTypeInfo = GetTypeInfo();

        var act = async () => await CapabilityEndpointBodyReader.ReadNativeBodyAsync<TestDto>(
            httpContext, jsonTypeInfo, optional: false);

        await act.Should().ThrowAsync<BadHttpRequestException>();
    }

    [Fact]
    public async Task Native_WhitespaceOnly_Optional_ReturnsDefault()
    {
        var httpContext = CreateHttpContext("   ");
        var jsonTypeInfo = GetTypeInfo();

        var result = await CapabilityEndpointBodyReader.ReadNativeBodyAsync<TestDto>(
            httpContext, jsonTypeInfo, optional: true);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Native_EmptyBody_Required_Throws()
    {
        var httpContext = CreateHttpContext(null);
        httpContext.Request.ContentLength = 0;
        var jsonTypeInfo = GetTypeInfo();

        var act = async () => await CapabilityEndpointBodyReader.ReadNativeBodyAsync<TestDto>(
            httpContext, jsonTypeInfo, optional: false);

        await act.Should().ThrowAsync<BadHttpRequestException>();
    }

    [Fact]
    public async Task Native_EmptyBody_Optional_ReturnsDefault()
    {
        var httpContext = CreateHttpContext(null);
        httpContext.Request.ContentLength = 0;
        var jsonTypeInfo = GetTypeInfo();

        var result = await CapabilityEndpointBodyReader.ReadNativeBodyAsync<TestDto>(
            httpContext, jsonTypeInfo, optional: true);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Native_JsonNull_Required_Throws()
    {
        var httpContext = CreateHttpContext("null");
        var jsonTypeInfo = GetTypeInfo();

        var act = async () => await CapabilityEndpointBodyReader.ReadNativeBodyAsync<TestDto>(
            httpContext, jsonTypeInfo, optional: false);

        await act.Should().ThrowAsync<BadHttpRequestException>();
    }

    [Fact]
    public async Task Native_JsonNull_Optional_ReturnsDefault()
    {
        var httpContext = CreateHttpContext("null");
        var jsonTypeInfo = GetTypeInfo();

        var result = await CapabilityEndpointBodyReader.ReadNativeBodyAsync<TestDto>(
            httpContext, jsonTypeInfo, optional: true);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Native_InvalidJson_Required_Throws()
    {
        var httpContext = CreateHttpContext("{ invalid }");
        var jsonTypeInfo = GetTypeInfo();

        var act = async () => await CapabilityEndpointBodyReader.ReadNativeBodyAsync<TestDto>(
            httpContext, jsonTypeInfo, optional: false);

        await act.Should().ThrowAsync<BadHttpRequestException>();
    }

    [Fact]
    public async Task Native_InvalidJson_Optional_ReturnsDefault()
    {
        var httpContext = CreateHttpContext("{ invalid }");
        var jsonTypeInfo = GetTypeInfo();

        var result = await CapabilityEndpointBodyReader.ReadNativeBodyAsync<TestDto>(
            httpContext, jsonTypeInfo, optional: true);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Native_ValidJson_Required_ReturnsValue()
    {
        var httpContext = CreateHttpContext("{ \"Name\": \"Test\" }");
        var jsonTypeInfo = GetTypeInfo();

        var result = await CapabilityEndpointBodyReader.ReadNativeBodyAsync<TestDto>(
            httpContext, jsonTypeInfo, optional: false);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Test");
    }

    // === ReadCompatibilityBodyAsync tests ===

    [Fact]
    public async Task Compatibility_LeadingWhitespace_ValidJson_Deserializes()
    {
        var httpContext = CreateHttpContext("  { \"Name\": \"World\" }");
        var jsonTypeInfo = GetTypeInfo();

        var result = await CapabilityEndpointBodyReader.ReadCompatibilityBodyAsync<TestDto>(
            httpContext, jsonTypeInfo, static () => new TestDto("factory"), optional: false);

        result.Should().NotBeNull();
        result!.Name.Should().Be("World");
    }

    [Fact]
    public async Task Compatibility_LeadingNewline_ValidJson_Deserializes()
    {
        var httpContext = CreateHttpContext("\n{\n  \"Name\": \"World\"\n}");
        var jsonTypeInfo = GetTypeInfo();

        var result = await CapabilityEndpointBodyReader.ReadCompatibilityBodyAsync<TestDto>(
            httpContext, jsonTypeInfo, static () => new TestDto("factory"), optional: false);

        result.Should().NotBeNull();
        result!.Name.Should().Be("World");
    }

    [Fact]
    public async Task Compatibility_WhitespaceOnly_Required_CallsFactory()
    {
        var httpContext = CreateHttpContext("   ");
        var jsonTypeInfo = GetTypeInfo();

        var result = await CapabilityEndpointBodyReader.ReadCompatibilityBodyAsync<TestDto>(
            httpContext, jsonTypeInfo, static () => new TestDto("factory"), optional: false);

        result.Should().NotBeNull();
        result!.Name.Should().Be("factory");
    }

    [Fact]
    public async Task Compatibility_WhitespaceOnly_Optional_ReturnsDefault()
    {
        var httpContext = CreateHttpContext("   ");
        var jsonTypeInfo = GetTypeInfo();

        var result = await CapabilityEndpointBodyReader.ReadCompatibilityBodyAsync<TestDto>(
            httpContext, jsonTypeInfo, static () => new TestDto("factory"), optional: true);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Compatibility_EmptyBody_Required_CallsFactory()
    {
        var httpContext = CreateHttpContext(null);
        httpContext.Request.ContentLength = 0;
        var jsonTypeInfo = GetTypeInfo();

        var result = await CapabilityEndpointBodyReader.ReadCompatibilityBodyAsync<TestDto>(
            httpContext, jsonTypeInfo, static () => new TestDto("factory"), optional: false);

        result.Should().NotBeNull();
        result!.Name.Should().Be("factory");
    }

    [Fact]
    public async Task Compatibility_EmptyBody_Optional_ReturnsDefault()
    {
        var httpContext = CreateHttpContext(null);
        httpContext.Request.ContentLength = 0;
        var jsonTypeInfo = GetTypeInfo();

        var result = await CapabilityEndpointBodyReader.ReadCompatibilityBodyAsync<TestDto>(
            httpContext, jsonTypeInfo, static () => new TestDto("factory"), optional: true);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Compatibility_JsonNull_Required_CallsFactory()
    {
        var httpContext = CreateHttpContext("null");
        var jsonTypeInfo = GetTypeInfo();

        var result = await CapabilityEndpointBodyReader.ReadCompatibilityBodyAsync<TestDto>(
            httpContext, jsonTypeInfo, static () => new TestDto("factory"), optional: false);

        result.Should().NotBeNull();
        result!.Name.Should().Be("factory");
    }

    [Fact]
    public async Task Compatibility_JsonNull_Optional_ReturnsDefault()
    {
        var httpContext = CreateHttpContext("null");
        var jsonTypeInfo = GetTypeInfo();

        var result = await CapabilityEndpointBodyReader.ReadCompatibilityBodyAsync<TestDto>(
            httpContext, jsonTypeInfo, static () => new TestDto("factory"), optional: true);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Compatibility_InvalidJson_Required_Throws()
    {
        var httpContext = CreateHttpContext("{ invalid }");
        var jsonTypeInfo = GetTypeInfo();

        var act = async () => await CapabilityEndpointBodyReader.ReadCompatibilityBodyAsync<TestDto>(
            httpContext, jsonTypeInfo, static () => new TestDto("factory"), optional: false);

        await act.Should().ThrowAsync<BadHttpRequestException>();
    }

    [Fact]
    public async Task Compatibility_InvalidJson_Optional_ReturnsDefault()
    {
        var httpContext = CreateHttpContext("{ invalid }");
        var jsonTypeInfo = GetTypeInfo();

        var result = await CapabilityEndpointBodyReader.ReadCompatibilityBodyAsync<TestDto>(
            httpContext, jsonTypeInfo, static () => new TestDto("factory"), optional: true);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Compatibility_ValidJson_Required_ReturnsValue()
    {
        var httpContext = CreateHttpContext("{ \"Name\": \"Test\" }");
        var jsonTypeInfo = GetTypeInfo();

        var result = await CapabilityEndpointBodyReader.ReadCompatibilityBodyAsync<TestDto>(
            httpContext, jsonTypeInfo, static () => new TestDto("factory"), optional: false);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Test");
    }
}
