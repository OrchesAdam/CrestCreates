namespace CrestCreates.CodeGenerator.CanonicalHashGenerator;

public static class CanonicalHashDiagnosticCodes
{
    public const string UnclassifiedPropertyValue = "CCHASH001";
    public const string PropertyNotFoundValue = "CCHASH002";
    public const string CollectionRequiresOrderModeValue = "CCHASH003";
    public const string ComplexFieldRequiresProfileValue = "CCHASH004";
    // CCHASH005 and CCHASH006 removed (see CanonicalHashDiagnostics.cs comment)
    public const string MissingRequiredProfilePropsValue = "CCHASH007";
    public const string DuplicateOrderValue = "CCHASH008";
    public const string TargetTypeDescriptorKindMismatchValue = "CCHASH009";
    public const string ReservedArtifactKindValue = "CCHASH010";
    public const string OrdinalByPropertyRequiresOrderByValue = "CCHASH011";
    public const string OrderedKeyValueOnlyForDictionariesValue = "CCHASH012";
    public const string ElementProfileTypeMismatchValue = "CCHASH013";
    public const string MultipleFieldMethodsValue = "CCHASH014";
    public const string UnionProfileMissingRequiredPropsValue = "CCHASH015";
    public const string UnionCaseTypeNotAssignableValue = "CCHASH016";
    public const string UnionCaseMissingValueProfileValue = "CCHASH017";
    public const string DuplicateUnionDiscriminatorValue = "CCHASH018";
    public const string DuplicateUnionCaseTypeValue = "CCHASH019";
    public const string UnionCaseTypeMustBeSealedValue = "CCHASH020";
    public const string UnionCaseMissingKnownSubtypeValue = "CCHASH021";
    public const string UnionCaseValueProfileTargetMismatchValue = "CCHASH022";
    public const string CustomWriterUnsupportedValue = "CCHASH023";
    public const string FilterOnlyForCollectionValue = "CCHASH024";
    public const string InvalidFilterSignatureValue = "CCHASH025";
    public const string FilterElementTypeMismatchValue = "CCHASH026";
    public const string FilterNotSupportedOnDictionaryValue = "CCHASH027";
    public const string UnsupportedScalarTypeValue = "CCHASH028";
}
