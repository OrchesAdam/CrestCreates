namespace CrestCreates.DynamicApi;

public static class DynamicApiSwaggerSchemaIdHelper
{
    public static string GetSchemaId(Type type)
    {
        if (!type.IsGenericType)
        {
            return Normalize(type.FullName ?? type.Name);
        }

        var genericTypeName = type.GetGenericTypeDefinition().FullName ?? type.Name;
        var genericTypeArguments = string.Join(
            "_",
            type.GetGenericArguments().Select(GetSchemaId));

        return Normalize($"{genericTypeName}_{genericTypeArguments}");
    }

    private static string Normalize(string value)
    {
        return value
            .Replace('.', '_')
            .Replace('+', '_');
    }
}