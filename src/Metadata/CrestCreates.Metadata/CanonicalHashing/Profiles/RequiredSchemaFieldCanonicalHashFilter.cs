using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

internal static class RequiredSchemaFieldCanonicalHashFilter
{
    public static bool Include(SchemaFieldDescriptor field) => field.IsRequired;
}
