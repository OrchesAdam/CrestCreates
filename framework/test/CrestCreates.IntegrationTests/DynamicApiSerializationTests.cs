using System.Text.Json;
using FluentAssertions;
using LibraryManagement.Application.Contracts.DTOs;
using Xunit;

namespace CrestCreates.IntegrationTests;

public class DynamicApiSerializationTests
{
    [Fact]
    public void Deserialize_CreateCategoryDto_WithCamelCaseJson_BindsExpectedProperties()
    {
        var json = """
        {
          "name": "integration-category",
          "description": "Integration category",
          "parentId": null,
          "parent": null,
          "children": [],
          "books": []
        }
        """;

        var result = JsonSerializer.Deserialize<CreateCategoryDto>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        result.Should().NotBeNull();
        result!.Name.Should().Be("integration-category");
        result.Description.Should().Be("Integration category");
        result.ParentId.Should().BeNull();
    }
}
