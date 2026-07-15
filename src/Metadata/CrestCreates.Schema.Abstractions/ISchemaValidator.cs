using System.Text.Json;

namespace CrestCreates.Schema.Abstractions;

public interface ISchemaValidator
{
    SchemaValidationResult Validate(
        SchemaDescriptor schema,
        object? payload,
        bool rejectUnknownProperties = false);

    SchemaValidationResult Validate(
        SchemaDescriptor schema,
        JsonElement payload,
        bool rejectUnknownProperties = false);
}
