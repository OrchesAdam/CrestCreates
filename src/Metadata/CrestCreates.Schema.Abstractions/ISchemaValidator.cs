namespace CrestCreates.Schema.Abstractions;

public interface ISchemaValidator
{
    SchemaValidationResult Validate(SchemaDescriptor schema, object? payload);
}
