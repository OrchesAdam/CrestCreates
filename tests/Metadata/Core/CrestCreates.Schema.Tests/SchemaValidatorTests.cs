using System.Text.Json;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

// semantic-string-guard: allow

namespace CrestCreates.Schema.Tests;

public class SchemaValidatorTests
{
    [Fact]
    public void Validate_NullPayload_WithRequiredFields_ReturnsFailure()
    {
        var schema = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1,
            Fields = new List<SchemaFieldDescriptor>
            {
                new() { Name = "Name", FieldType = "string", IsRequired = true }
            }
        };
        var result = new SchemaValidator().Validate(schema, null);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorCode == "FIELD_REQUIRED");
    }

    [Fact]
    public void Validate_MissingRequiredField_ReturnsError()
    {
        var schema = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1,
            Fields = new List<SchemaFieldDescriptor>
            {
                new() { Name = "Name", FieldType = "string", IsRequired = true }
            }
        };
        var result = new SchemaValidator().Validate(schema, "{\"Other\":\"value\"}");
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.FieldName == "Name" && error.ErrorCode == SchemaValidationErrorCodes.FieldRequired);
    }

    [Fact]
    public void Validate_TypeMismatch_ReturnsError()
    {
        var schema = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1,
            Fields = new List<SchemaFieldDescriptor>
            {
                new() { Name = "Age", FieldType = "int", IsRequired = true }
            }
        };
        var result = new SchemaValidator().Validate(schema, "{\"Age\":\"x\"}");
        result.IsValid.Should().BeFalse();
        result.Errors[0].ErrorCode.Should().Be("TYPE_MISMATCH");
    }

    [Fact]
    public void Validate_StringTooLong_ReturnsError()
    {
        var schema = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1,
            Fields = new List<SchemaFieldDescriptor>
            {
                new() { Name = "Code", FieldType = "string", MaxLength = 5 }
            }
        };
        var result = new SchemaValidator().Validate(schema, "{\"Code\":\"123456\"}");
        result.Errors[0].ErrorCode.Should().Be("MAX_LENGTH_EXCEEDED");
    }

    [Fact]
    public void Validate_PatternMismatch_ReturnsError()
    {
        var schema = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1,
            Fields = new List<SchemaFieldDescriptor>
            {
                new() { Name = "Email", FieldType = "string", Pattern = @"^[^@]+@[^@]+$" }
            }
        };
        var result = new SchemaValidator().Validate(schema, "{\"Email\":\"bad\"}");
        result.Errors[0].ErrorCode.Should().Be("PATTERN_MISMATCH");
    }

    [Fact]
    public void Validate_ValidPayload_ReturnsSuccess()
    {
        var schema = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1,
            Fields = new List<SchemaFieldDescriptor>
            {
                new() { Name = "Name", FieldType = "string", IsRequired = true, MaxLength = 50 },
                new() { Name = "Age", FieldType = "int", IsRequired = true }
            }
        };
        var result = new SchemaValidator().Validate(schema, "{\"Name\":\"John\",\"Age\":30}");
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_NullValueOnNonNullable_ReturnsError()
    {
        var schema = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1,
            Fields = new List<SchemaFieldDescriptor>
            {
                new() { Name = "Name", FieldType = "string", IsNullable = false }
            }
        };
        var result = new SchemaValidator().Validate(schema, "{\"Name\":null}");
        result.Errors[0].ErrorCode.Should().Be("NULL_NOT_ALLOWED");
    }

    [Fact]
    public void Validate_MultipleFields_ReturnsAllErrors()
    {
        var schema = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1,
            Fields = new List<SchemaFieldDescriptor>
            {
                new() { Name = "Name", FieldType = "string", IsRequired = true },
                new() { Name = "Age", FieldType = "int", IsRequired = true }
            }
        };
        var result = new SchemaValidator().Validate(schema, "{\"Other\":\"x\"}");
        result.Errors.Should().Contain(error => error.FieldName == "Name" && error.ErrorCode == SchemaValidationErrorCodes.FieldRequired);
        result.Errors.Should().Contain(error => error.FieldName == "Age" && error.ErrorCode == SchemaValidationErrorCodes.FieldRequired);
        result.Errors.Should().NotContain(error => error.ErrorCode == SchemaValidationErrorCodes.UnknownProperty);
    }

    [Fact]
    public void Validate_OptionalFieldMissing_Passes()
    {
        var schema = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1,
            Fields = new List<SchemaFieldDescriptor>
            {
                new() { Name = "Name", FieldType = "string", IsRequired = false }
            }
        };
        var result = new SchemaValidator().Validate(schema, "{}");
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_NumericConstraints()
    {
        var schema = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1,
            Fields = new List<SchemaFieldDescriptor>
            {
                new() { Name = "Score", FieldType = "int", MinValue = 0, MaxValue = 100 }
            }
        };
        var tooLow = new SchemaValidator().Validate(schema, "{\"Score\":-1}");
        tooLow.Errors[0].ErrorCode.Should().Be("MIN_VALUE_NOT_MET");

        var tooHigh = new SchemaValidator().Validate(schema, "{\"Score\":101}");
        tooHigh.Errors[0].ErrorCode.Should().Be("MAX_VALUE_EXCEEDED");
    }

    [Fact]
    public void Validate_JsonElement_requires_object_root()
    {
        using var json = JsonDocument.Parse("[]");

        var result = new SchemaValidator().Validate(new SchemaDescriptor(), json.RootElement);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.ErrorCode == SchemaValidationErrorCodes.InvalidRoot);
    }

    [Fact]
    public void Validate_JsonElement_rejects_duplicate_properties_ordinally()
    {
        var schema = SchemaWith(new SchemaFieldDescriptor { Name = "name", FieldType = "string" });
        using var json = JsonDocument.Parse("{\"name\":\"first\",\"name\":\"second\"}");

        var result = new SchemaValidator().Validate(schema, json.RootElement);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_JsonElement_can_reject_properties_not_declared_by_schema()
    {
        var schema = SchemaWith(new SchemaFieldDescriptor { Name = "name", FieldType = "string" });
        using var json = JsonDocument.Parse("{\"name\":\"valid\",\"undeclared\":true}");

        var result = new SchemaValidator().Validate(schema, json.RootElement, rejectUnknownProperties: true);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.FieldName == "undeclared"
            && error.ErrorCode == SchemaValidationErrorCodes.UnknownProperty);
    }

    [Fact]
    public void Validate_DateTimeOffset_token_uses_the_same_rfc3339_contract_as_datetime()
    {
        var schema = SchemaWith(new SchemaFieldDescriptor { Name = "createdAt", FieldType = "DateTimeOffset" });
        using var valid = JsonDocument.Parse("{\"createdAt\":\"2026-07-15T10:30:00+08:00\"}");

        new SchemaValidator().Validate(schema, valid.RootElement).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_large_finite_double_without_constraints_does_not_require_decimal_conversion()
    {
        var schema = SchemaWith(new SchemaFieldDescriptor { Name = "value", FieldType = "double" });
        using var json = JsonDocument.Parse("{\"value\":1e300}");

        var action = () => new SchemaValidator().Validate(schema, json.RootElement);

        action.Should().NotThrow();
        action().IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_large_finite_double_constraint_does_not_require_decimal_conversion()
    {
        var schema = SchemaWith(new SchemaFieldDescriptor
        {
            Name = "value",
            FieldType = "double",
            MaxValue = 1e300
        });
        using var json = JsonDocument.Parse("{\"value\":1}");

        var action = () => new SchemaValidator().Validate(schema, json.RootElement);

        action.Should().NotThrow();
        action().IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_integer_rejects_fraction_and_int32_overflow()
    {
        var schema = SchemaWith(new SchemaFieldDescriptor { Name = "value", FieldType = "int" });
        using var fractional = JsonDocument.Parse("{\"value\":1.5}");
        using var overflow = JsonDocument.Parse("{\"value\":2147483648}");

        new SchemaValidator().Validate(schema, fractional.RootElement).IsValid.Should().BeFalse();
        new SchemaValidator().Validate(schema, overflow.RootElement).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_collection_uses_collection_element_type()
    {
        var schema = SchemaWith(new SchemaFieldDescriptor
        {
            Name = "ids",
            FieldType = "IList<Guid>",
            IsCollection = true,
            CollectionElementType = "guid"
        });
        using var json = JsonDocument.Parse("{\"ids\":[\"not-a-guid\"]}");

        var result = new SchemaValidator().Validate(schema, json.RootElement);

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("550e8400-e29b-41d4-a716-446655440000", true)]
    [InlineData("550E8400-E29B-41D4-A716-446655440000", true)]
    [InlineData("{550e8400-e29b-41d4-a716-446655440000}", false)]
    public void Validate_guid_requires_canonical_D_form(string value, bool expected)
    {
        var schema = SchemaWith(new SchemaFieldDescriptor { Name = "id", FieldType = "guid" });
        using var json = JsonDocument.Parse($"{{\"id\":\"{value}\"}}");

        new SchemaValidator().Validate(schema, json.RootElement).IsValid.Should().Be(expected);
    }

    private static SchemaDescriptor SchemaWith(SchemaFieldDescriptor field) => new()
    {
        Id = "schema.test",
        Name = "Test",
        Version = 1,
        Fields = [field]
    };
}
