using System.Text.Json;
using System.Text.RegularExpressions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Schema;

public sealed class SchemaValidator : ISchemaValidator
{
    public SchemaValidationResult Validate(SchemaDescriptor schema, object? payload)
    {
        if (payload == null)
        {
            var requiredFields = schema.Fields.Where(f => f.IsRequired).ToList();
            if (requiredFields.Count > 0)
            {
                return SchemaValidationResult.Failure(
                    requiredFields.Select(f => new SchemaValidationError
                    {
                        FieldName = f.Name,
                        ErrorCode = SchemaValidationErrorCodes.FieldRequiredValue,
                        Message = $"Field '{f.Name}' is required but payload is null."
                    }).ToList());
            }
            return SchemaValidationResult.Success();
        }

        var errors = new List<SchemaValidationError>();
        var json = payload is string s ? s : JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        foreach (var field in schema.Fields)
            ValidateField(root, field, errors);

        return errors.Count == 0
            ? SchemaValidationResult.Success()
            : SchemaValidationResult.Failure(errors);
    }

    private static void ValidateField(JsonElement root, SchemaFieldDescriptor field, List<SchemaValidationError> errors)
    {
        if (!root.TryGetProperty(field.Name, out var element))
        {
            if (field.IsRequired)
                errors.Add(new SchemaValidationError
                {
                    FieldName = field.Name,
                    ErrorCode = SchemaValidationErrorCodes.FieldRequiredValue,
                    Message = $"Field '{field.Name}' is required."
                });
            return;
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            if (!field.IsNullable)
                errors.Add(new SchemaValidationError
                {
                    FieldName = field.Name,
                    ErrorCode = SchemaValidationErrorCodes.NullNotAllowedValue,
                    Message = $"Field '{field.Name}' does not allow null."
                });
            return;
        }

        ValidateType(field, element, errors);
        ValidateStringConstraints(field, element, errors);
        ValidateNumericConstraints(field, element, errors);
    }

    private static void ValidateType(SchemaFieldDescriptor field, JsonElement element, List<SchemaValidationError> errors)
    {
        var type = field.FieldType;
        var kind = element.ValueKind;

        var valid = type switch
        {
            "string" => kind == JsonValueKind.String,
            "int" or "long" or "decimal" or "double" => kind == JsonValueKind.Number,
            "bool" => kind == JsonValueKind.True || kind == JsonValueKind.False,
            _ => true
        };

        if (!valid)
            errors.Add(new SchemaValidationError
            {
                FieldName = field.Name,
                ErrorCode = SchemaValidationErrorCodes.TypeMismatchValue,
                Message = $"Field '{field.Name}' expected {type}, got {kind}."
            });
    }

    private static void ValidateStringConstraints(SchemaFieldDescriptor field, JsonElement element, List<SchemaValidationError> errors)
    {
        if (element.ValueKind != JsonValueKind.String) return;
        var value = element.GetString()!;

        if (field.MaxLength.HasValue && value.Length > field.MaxLength.Value)
            errors.Add(new SchemaValidationError
            {
                FieldName = field.Name,
                ErrorCode = SchemaValidationErrorCodes.MaxLengthExceededValue,
                Message = $"Field '{field.Name}' exceeds max length {field.MaxLength}."
            });

        if (field.MinLength.HasValue && value.Length < field.MinLength.Value)
            errors.Add(new SchemaValidationError
            {
                FieldName = field.Name,
                ErrorCode = SchemaValidationErrorCodes.MinLengthNotMetValue,
                Message = $"Field '{field.Name}' shorter than min length {field.MinLength}."
            });

        if (field.Pattern != null && !Regex.IsMatch(value, field.Pattern))
            errors.Add(new SchemaValidationError
            {
                FieldName = field.Name,
                ErrorCode = SchemaValidationErrorCodes.PatternMismatchValue,
                Message = $"Field '{field.Name}' does not match pattern '{field.Pattern}'."
            });
    }

    private static void ValidateNumericConstraints(SchemaFieldDescriptor field, JsonElement element, List<SchemaValidationError> errors)
    {
        if (element.ValueKind != JsonValueKind.Number) return;
        var value = element.GetDecimal();

        if (field.MaxValue.HasValue && value > (decimal)field.MaxValue.Value)
            errors.Add(new SchemaValidationError
            {
                FieldName = field.Name,
                ErrorCode = SchemaValidationErrorCodes.MaxValueExceededValue,
                Message = $"Field '{field.Name}' exceeds max value {field.MaxValue}."
            });

        if (field.MinValue.HasValue && value < (decimal)field.MinValue.Value)
            errors.Add(new SchemaValidationError
            {
                FieldName = field.Name,
                ErrorCode = SchemaValidationErrorCodes.MinValueNotMetValue,
                Message = $"Field '{field.Name}' below min value {field.MinValue}."
            });
    }
}
