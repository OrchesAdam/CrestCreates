using Microsoft.CodeAnalysis;

namespace CrestCreates.JsonContracts.BuildTasks.Semantic;

public static class JsonContractRootNormalizer
{
    public static ITypeSymbol Normalize(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol namedType && namedType.IsValueType)
        {
            if (namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                && namedType.Arity == 1)
            {
                return namedType;
            }
        }

        if (type.NullableAnnotation == NullableAnnotation.Annotated
            && type is INamedTypeSymbol { IsValueType: false })
        {
            return type.WithNullableAnnotation(NullableAnnotation.None);
        }

        return type;
    }
}
