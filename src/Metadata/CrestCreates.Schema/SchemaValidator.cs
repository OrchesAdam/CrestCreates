using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics.CodeAnalysis;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Schema;

public sealed class SchemaValidator : ISchemaValidator
{
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Legacy object validation serializes runtime-shaped inputs. Trimming-safe protocol paths must call the JsonElement overload.")]
    [UnconditionalSuppressMessage(
        "Aot",
        "IL3050",
        Justification = "Legacy object validation may require dynamic JSON code. NativeAOT-verified protocol paths must call the JsonElement overload.")]
    public SchemaValidationResult Validate(
        SchemaDescriptor schema,
        object? payload,
        bool rejectUnknownProperties = false)
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
                        ErrorCode = SchemaValidationErrorCodes.FieldRequired,
                        Message = $"Field '{f.Name}' is required but payload is null."
                    }).ToList());
            }
            return SchemaValidationResult.Success();
        }

        if (payload is JsonElement element)
            return Validate(schema, element, rejectUnknownProperties);

        var json = payload is string s ? s : JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        return Validate(schema, doc.RootElement, rejectUnknownProperties);
    }

    public SchemaValidationResult Validate(
        SchemaDescriptor schema,
        JsonElement payload,
        bool rejectUnknownProperties = false)
    {
        var errors = new List<SchemaValidationError>();

        if (payload.ValueKind != JsonValueKind.Object)
        {
            errors.Add(Error(string.Empty, SchemaValidationErrorCodes.InvalidRoot,
                "Schema payload root must be a JSON object."));
            return SchemaValidationResult.Failure(errors);
        }

        var propertyNames = new HashSet<string>(StringComparer.Ordinal);
        var fieldNames = schema.Fields.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var property in payload.EnumerateObject())
        {
            if (!propertyNames.Add(property.Name))
            {
                errors.Add(Error(property.Name, SchemaValidationErrorCodes.DuplicateProperty,
                    $"Property '{property.Name}' occurs more than once."));
            }
            else if (rejectUnknownProperties && !fieldNames.Contains(property.Name))
            {
                errors.Add(Error(property.Name, SchemaValidationErrorCodes.UnknownProperty,
                    $"Property '{property.Name}' is not declared by the schema."));
            }
        }

        foreach (var field in schema.Fields)
            ValidateField(payload, field, errors);

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
                    ErrorCode = SchemaValidationErrorCodes.FieldRequired,
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
                    ErrorCode = SchemaValidationErrorCodes.NullNotAllowed,
                    Message = $"Field '{field.Name}' does not allow null."
                });
            return;
        }

        if (field.IsCollection)
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                errors.Add(Error(field.Name, SchemaValidationErrorCodes.TypeMismatch,
                    $"Field '{field.Name}' expected array, got {element.ValueKind}."));
                return;
            }

            var elementType = field.CollectionElementType;
            if (string.IsNullOrWhiteSpace(elementType))
            {
                errors.Add(Error(field.Name, SchemaValidationErrorCodes.UnknownFieldType,
                    $"Collection field '{field.Name}' has no element type."));
                return;
            }

            foreach (var item in element.EnumerateArray())
                ValidateScalar(field, elementType, item, errors, allowNull: false);
            return;
        }

        ValidateScalar(field, field.FieldType, element, errors, allowNull: field.IsNullable);
    }

    private static void ValidateScalar(
        SchemaFieldDescriptor field,
        string type,
        JsonElement element,
        List<SchemaValidationError> errors,
        bool allowNull)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            if (!allowNull)
                errors.Add(Error(field.Name, SchemaValidationErrorCodes.NullNotAllowed,
                    $"Field '{field.Name}' does not allow null."));
            return;
        }

        var kind = element.ValueKind;

        var valid = SchemaScalarTypes.TryResolve(type, out var scalarKind) && scalarKind switch
        {
            SchemaScalarKind.String => kind == JsonValueKind.String,
            SchemaScalarKind.Int32 => kind == JsonValueKind.Number && element.TryGetInt32(out _),
            SchemaScalarKind.Int64 => kind == JsonValueKind.Number && element.TryGetInt64(out _),
            SchemaScalarKind.Decimal => kind == JsonValueKind.Number && element.TryGetDecimal(out _),
            SchemaScalarKind.Double => kind == JsonValueKind.Number && element.TryGetDouble(out var value) && double.IsFinite(value),
            SchemaScalarKind.Boolean => kind == JsonValueKind.True || kind == JsonValueKind.False,
            SchemaScalarKind.Guid => kind == JsonValueKind.String && Guid.TryParseExact(element.GetString(), "D", out _),
            SchemaScalarKind.Date => kind == JsonValueKind.String && DateOnly.TryParseExact(
                element.GetString(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _),
            SchemaScalarKind.DateTime => kind == JsonValueKind.String && IsRfc3339DateTime(element.GetString()),
            _ => false
        };

        if (!valid)
        {
            errors.Add(Error(
                field.Name,
                SchemaScalarTypes.TryResolve(type, out _) ? SchemaValidationErrorCodes.TypeMismatch : SchemaValidationErrorCodes.UnknownFieldType,
                $"Field '{field.Name}' expected {type}, got {kind}."));
            return;
        }

        ValidateStringConstraints(field, element, errors);
        ValidateNumericConstraints(field, element, errors);
    }

    private static void ValidateStringConstraints(SchemaFieldDescriptor field, JsonElement element, List<SchemaValidationError> errors)
    {
        if (element.ValueKind != JsonValueKind.String) return;
        var value = element.GetString()!;

        if (field.MaxLength.HasValue && value.Length > field.MaxLength.Value)
            errors.Add(new SchemaValidationError
            {
                FieldName = field.Name,
                ErrorCode = SchemaValidationErrorCodes.MaxLengthExceeded,
                Message = $"Field '{field.Name}' exceeds max length {field.MaxLength}."
            });

        if (field.MinLength.HasValue && value.Length < field.MinLength.Value)
            errors.Add(new SchemaValidationError
            {
                FieldName = field.Name,
                ErrorCode = SchemaValidationErrorCodes.MinLengthNotMet,
                Message = $"Field '{field.Name}' shorter than min length {field.MinLength}."
            });

        if (field.Pattern != null && !Regex.IsMatch(value, field.Pattern))
            errors.Add(new SchemaValidationError
            {
                FieldName = field.Name,
                ErrorCode = SchemaValidationErrorCodes.PatternMismatch,
                Message = $"Field '{field.Name}' does not match pattern '{field.Pattern}'."
            });
    }

    private static void ValidateNumericConstraints(SchemaFieldDescriptor field, JsonElement element, List<SchemaValidationError> errors)
    {
        if (element.ValueKind != JsonValueKind.Number
            || !field.MinValue.HasValue && !field.MaxValue.HasValue)
            return;

        var value = element.GetDouble();

        if (field.MaxValue.HasValue && value > field.MaxValue.Value)
            errors.Add(NumericError(field, SchemaValidationErrorCodes.MaxValueExceeded,
                $"Field '{field.Name}' exceeds max value {field.MaxValue}."));

        if (field.MinValue.HasValue && value < field.MinValue.Value)
            errors.Add(NumericError(field, SchemaValidationErrorCodes.MinValueNotMet,
                $"Field '{field.Name}' below min value {field.MinValue}."));
    }

    private static SchemaValidationError NumericError(
        SchemaFieldDescriptor field,
        CrestCreates.Core.Abstractions.Identity.DiagnosticCode code,
        string message) => new() { FieldName = field.Name, ErrorCode = code, Message = message };

    private static bool IsRfc3339DateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$",
                RegexOptions.CultureInvariant))
            return false;

        return DateTimeOffset.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out _);
    }

    private static SchemaValidationError Error(
        string fieldName,
        CrestCreates.Core.Abstractions.Identity.DiagnosticCode code,
        string message) => new()
    {
        FieldName = fieldName,
        ErrorCode = code,
        Message = message
    };
}
