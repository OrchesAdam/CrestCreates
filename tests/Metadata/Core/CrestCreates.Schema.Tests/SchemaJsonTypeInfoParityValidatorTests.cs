using System.Text.Json.Serialization;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Schema.Tests;

public sealed class SchemaJsonTypeInfoParityValidatorTests
{
    private readonly SchemaJsonTypeInfoParityValidator _validator = new();

    [Fact]
    public void Directionally_matching_input_and_output_contracts_pass()
    {
        var schema = Schema(new SchemaFieldDescriptor
        {
            Name = "name",
            FieldType = "string",
            IsNullable = true
        });

        _validator.Invoking(instance => instance.ValidateInput(
                schema,
                SchemaContractTestJsonContext.Default.SchemaContractMutableDto))
            .Should().NotThrow();
        _validator.Invoking(instance => instance.ValidateOutput(
                schema,
                SchemaContractTestJsonContext.Default.SchemaContractMutableDto))
            .Should().NotThrow();
    }

    [Fact]
    public void Input_required_metadata_is_rejected_so_schema_owns_presence()
    {
        var schema = Schema(new SchemaFieldDescriptor
        {
            Name = "name",
            FieldType = "string",
            IsRequired = true
        });

        var action = () => _validator.ValidateInput(
            schema,
            SchemaContractTestJsonContext.Default.SchemaContractRequiredDto);

        action.Should().Throw<SchemaJsonContractException>()
            .Which.Violation.Should().Be(SchemaJsonContractViolation.RequirednessMismatch);
    }

    [Fact]
    public void Directional_property_set_and_get_are_enforced()
    {
        var schema = Schema(new SchemaFieldDescriptor
        {
            Name = "name",
            FieldType = "string",
            IsNullable = true
        });

        var action = () => _validator.ValidateInput(
            schema,
            SchemaContractTestJsonContext.Default.SchemaContractReadOnlyDto);

        action.Should().Throw<SchemaJsonContractException>()
            .Which.Violation.Should().Be(SchemaJsonContractViolation.JsonPropertyMismatch);
    }

    [Fact]
    public void Collection_element_type_must_match()
    {
        var schema = Schema(new SchemaFieldDescriptor
        {
            Name = "values",
            FieldType = "IList<string>",
            IsCollection = true,
            CollectionElementType = "int"
        });

        var action = () => _validator.ValidateOutput(
            schema,
            SchemaContractTestJsonContext.Default.SchemaContractStringCollectionDto);

        action.Should().Throw<SchemaJsonContractException>()
            .Which.Violation.Should().Be(SchemaJsonContractViolation.PropertyTypeMismatch);
    }

    [Fact]
    public void Nested_object_contract_is_validated_recursively()
    {
        var child = Schema("child", new SchemaFieldDescriptor
        {
            Name = "city", FieldType = "string", IsRequired = true
        });
        var root = Schema("root", new SchemaFieldDescriptor
        {
            Name = "address", FieldType = "object", IsRequired = true,
            ObjectSchema = new VersionedDescriptorRef<SchemaDescriptor>("child", 1)
        });

        _validator.ValidateOutput(
            root,
            SchemaContractTestJsonContext.Default.SchemaNestedRootDto,
            [child]);
    }

    [Fact]
    public void Missing_nested_schema_is_rejected_closed_world()
    {
        var root = Schema("root", new SchemaFieldDescriptor
        {
            Name = "address", FieldType = "object", IsRequired = true,
            ObjectSchema = new VersionedDescriptorRef<SchemaDescriptor>("missing", 1)
        });

        var action = () => _validator.ValidateOutput(
            root,
            SchemaContractTestJsonContext.Default.SchemaNestedRootDto,
            Array.Empty<SchemaDescriptor>());

        action.Should().Throw<SchemaJsonContractException>()
            .Which.Violation.Should().Be(SchemaJsonContractViolation.NestedSchemaNotFound);
    }

    private static SchemaDescriptor Schema(params SchemaFieldDescriptor[] fields)
        => Schema("schema.test", fields);

    private static SchemaDescriptor Schema(string id, params SchemaFieldDescriptor[] fields) => new()
    {
        Id = id,
        Name = id,
        Version = 1,
        Fields = fields
    };
}

public sealed class SchemaContractMutableDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class SchemaContractReadOnlyDto
{
    [JsonPropertyName("name")]
    public string? Name { get; }
}

public sealed class SchemaContractRequiredDto
{
    [JsonPropertyName("name")]
    [JsonRequired]
    public string Name { get; set; } = string.Empty;
}

public sealed class SchemaContractStringCollectionDto
{
    [JsonPropertyName("values")]
    public List<string> Values { get; init; } = [];
}

public sealed class SchemaNestedRootDto
{
    [JsonPropertyName("address")]
    public required SchemaNestedChildDto Address { get; init; }
}

public sealed class SchemaNestedChildDto
{
    [JsonPropertyName("city")]
    public required string City { get; init; }
}

[JsonSerializable(typeof(SchemaContractMutableDto))]
[JsonSerializable(typeof(SchemaContractReadOnlyDto))]
[JsonSerializable(typeof(SchemaContractRequiredDto))]
[JsonSerializable(typeof(SchemaContractStringCollectionDto))]
[JsonSerializable(typeof(SchemaNestedRootDto))]
[JsonSerializable(typeof(SchemaNestedChildDto))]
internal partial class SchemaContractTestJsonContext : JsonSerializerContext;
