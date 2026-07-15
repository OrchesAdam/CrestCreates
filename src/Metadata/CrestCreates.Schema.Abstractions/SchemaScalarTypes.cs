namespace CrestCreates.Schema.Abstractions;

public enum SchemaScalarKind
{
    String,
    Boolean,
    Int32,
    Int64,
    Decimal,
    Double,
    Guid,
    Date,
    DateTime
}

public static class SchemaScalarTypes
{
    public static bool TryResolve(string? token, out SchemaScalarKind kind)
    {
        kind = token switch
        {
            "string" => SchemaScalarKind.String,
            "bool" => SchemaScalarKind.Boolean,
            "int" => SchemaScalarKind.Int32,
            "long" => SchemaScalarKind.Int64,
            "decimal" => SchemaScalarKind.Decimal,
            "double" => SchemaScalarKind.Double,
            "guid" or "Guid" => SchemaScalarKind.Guid,
            "date" or "DateOnly" => SchemaScalarKind.Date,
            "datetime" or "DateTime" or "DateTimeOffset" => SchemaScalarKind.DateTime,
            _ => default
        };
        return token is "string" or "bool" or "int" or "long" or "decimal" or "double"
            or "guid" or "Guid" or "date" or "DateOnly" or "datetime" or "DateTime" or "DateTimeOffset";
    }
}
