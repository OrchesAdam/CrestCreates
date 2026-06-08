using System.Text.Json;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

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
        result.Errors[0].FieldName.Should().Be("Name");
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
        result.Errors.Should().HaveCount(2);
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
}
