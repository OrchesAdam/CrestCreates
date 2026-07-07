using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.DynamicApi.Tests;

public class CapabilityEndpointJsonRuntimeTests
{
    [Fact]
    public async Task ReadBodyAsync_Required_ReturnsDeserializedObject()
    {
        // Arrange
        var json = """{"name":"Clean Architecture","price":29.99}""";
        var context = CreateContext(json);
        var body2 = new MemoryStream(Encoding.UTF8.GetBytes(json));
        context.Request.Body = body2;
        context.Request.ContentLength = body2.Length;

        // Act
        var result = await CapabilityEndpointJsonRuntime.ReadBodyAsync<BookDto>(context, optional: false);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Clean Architecture");
        result.Price.Should().Be(29.99m);
    }

    [Fact]
    public async Task ReadBodyAsync_RequiredNullBody_ThrowsBadHttpRequestException()
    {
        // Arrange
        var json = """null""";
        var body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var context = CreateContext(json);
        context.Request.Body = body;
        context.Request.ContentLength = body.Length;

        // Act
        var act = async () => await CapabilityEndpointJsonRuntime.ReadBodyAsync<BookDto>(context, optional: false);

        // Assert
        await act.Should().ThrowAsync<BadHttpRequestException>()
            .WithMessage("*BookDto*");
    }

    [Fact]
    public async Task ReadBodyAsync_OptionalNullBody_ReturnsDefault()
    {
        // Arrange
        var json = """null""";
        var body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var context = CreateContext(json);
        context.Request.Body = body;
        context.Request.ContentLength = body.Length;

        // Act
        var result = await CapabilityEndpointJsonRuntime.ReadBodyAsync<BookDto>(context, optional: true);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ReadBodyAsync_InvalidJson_ThrowsBadHttpRequestException()
    {
        // Arrange
        var json = """{invalid json}""";
        var body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var context = CreateContext(json);
        context.Request.Body = body;
        context.Request.ContentLength = body.Length;

        // Act
        var act = async () => await CapabilityEndpointJsonRuntime.ReadBodyAsync<BookDto>(context, optional: false);

        // Assert
        await act.Should().ThrowAsync<BadHttpRequestException>()
            .WithMessage("*BookDto*");
    }

    [Fact]
    public async Task ReadBodyAsync_ContentLengthNull_OptionalTrue_StillReadsBody()
    {
        // Arrange
        var json = """{"name":"Chunked Transfer","price":15.50}""";
        var body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var context = CreateContext(json);
        context.Request.Body = body;
        context.Request.ContentLength = null;

        // Act
        var result = await CapabilityEndpointJsonRuntime.ReadBodyAsync<BookDto>(context, optional: true);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Chunked Transfer");
        result.Price.Should().Be(15.50m);
    }

    [Fact]
    public async Task ReadBodyAsync_ContentLengthZero_Optional_ReturnsDefault()
    {
        // Arrange
        var json = string.Empty;
        var body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var context = CreateContext(json);
        context.Request.Body = body;
        context.Request.ContentLength = 0;

        // Act
        var result = await CapabilityEndpointJsonRuntime.ReadBodyAsync<BookDto>(context, optional: true);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ReadBodyAsync_WithJsonTypeInfo_Required_ReturnsDeserializedObject()
    {
        // Arrange
        var json = """{"name":"AoT Safe","price":42.00}""";
        var body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var context = CreateContext(json);
        context.Request.Body = body;
        context.Request.ContentLength = body.Length;
        var jsonTypeInfo = BookDtoContext.Default.BookDto;

        // Act
        var result = await CapabilityEndpointJsonRuntime.ReadBodyAsync(context, jsonTypeInfo, optional: false);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("AoT Safe");
        result.Price.Should().Be(42.00m);
    }

    [Fact]
    public async Task ReadBodyAsync_WithJsonTypeInfo_InvalidJson_ThrowsBadHttpRequestException()
    {
        // Arrange
        var json = """not valid json""";
        var body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var context = CreateContext(json);
        context.Request.Body = body;
        context.Request.ContentLength = body.Length;
        var jsonTypeInfo = BookDtoContext.Default.BookDto;

        // Act
        var act = async () => await CapabilityEndpointJsonRuntime.ReadBodyAsync(context, jsonTypeInfo, optional: false);

        // Assert
        await act.Should().ThrowAsync<BadHttpRequestException>()
            .WithMessage("*BookDto*");
    }

    [Fact]
    public async Task ReadBodyAsync_WithJsonTypeInfo_RequiredNullBody_ThrowsBadHttpRequestException()
    {
        // Arrange
        var json = """null""";
        var body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var context = CreateContext(json);
        context.Request.Body = body;
        context.Request.ContentLength = body.Length;
        var jsonTypeInfo = BookDtoContext.Default.BookDto;

        // Act
        var act = async () => await CapabilityEndpointJsonRuntime.ReadBodyAsync(context, jsonTypeInfo, optional: false);

        // Assert
        await act.Should().ThrowAsync<BadHttpRequestException>()
            .WithMessage("*BookDto*");
    }

    [Fact]
    public async Task ReadBodyAsync_WithJsonTypeInfo_OptionalNullBody_ReturnsDefault()
    {
        // Arrange
        var json = """null""";
        var body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var context = CreateContext(json);
        context.Request.Body = body;
        context.Request.ContentLength = body.Length;
        var jsonTypeInfo = BookDtoContext.Default.BookDto;

        // Act
        var result = await CapabilityEndpointJsonRuntime.ReadBodyAsync(context, jsonTypeInfo, optional: true);

        // Assert
        result.Should().BeNull();
    }

    private static DefaultHttpContext CreateContext(string? bodyPayload = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();

        return new DefaultHttpContext
        {
            RequestServices = sp,
            Request =
            {
                ContentType = "application/json"
            }
        };
    }
}

public sealed class BookDto
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

[JsonSerializable(typeof(BookDto))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
public sealed partial class BookDtoContext : JsonSerializerContext
{
}
