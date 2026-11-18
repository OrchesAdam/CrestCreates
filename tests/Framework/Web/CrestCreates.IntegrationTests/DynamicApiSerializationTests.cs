using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using ErpManagement.Application.Contracts.DTOs;
using ErpManagement.Domain.Shared.Enums;
using FluentAssertions;
using Xunit;

namespace CrestCreates.IntegrationTests;

public class DynamicApiSerializationTests
{
    [Fact]
    public void Deserialize_CreateMaterialDto_WithCamelCaseJson_BindsExpectedProperties()
    {
        var unitId = Guid.NewGuid();
        var json = $$"""
        {
          "code": "IT-STEEL",
          "name": "Integration steel",
          "materialType": "Purchased",
          "baseUnitId": "{{unitId}}",
          "categoryId": null
        }
        """;

        var result = JsonSerializer.Deserialize<CreateMaterialDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter<MaterialType>() }
        });

        result.Should().NotBeNull();
        result!.Code.Should().Be("IT-STEEL");
        result.Name.Should().Be("Integration steel");
        result.MaterialType.Should().Be(MaterialType.Purchased);
        result.BaseUnitId.Should().Be(unitId);
        result.CategoryId.Should().BeNull();
    }
}
