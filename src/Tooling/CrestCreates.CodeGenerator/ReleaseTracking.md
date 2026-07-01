## Release 1.0.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CCHASH001 | CanonicalHash | Warning | UnclassifiedProperty
CCHASH002 | CanonicalHash | Error | PropertyNotFound
CCHASH003 | CanonicalHash | Error | CollectionRequiresOrderMode
CCHASH004 | CanonicalHash | Error | ComplexFieldRequiresProfile
CCHASH007 | CanonicalHash | Error | MissingRequiredProfileProps
CCHASH008 | CanonicalHash | Error | DuplicateOrder
CCHASH009 | CanonicalHash | Error | TargetTypeDescriptorKindMismatch
CCHASH010 | CanonicalHash | Warning | ReservedArtifactKind
CCHASH011 | CanonicalHash | Error | OrdinalByPropertyRequiresOrderBy
CCHASH012 | CanonicalHash | Error | OrderedKeyValueOnlyForDictionaries
CCHASH013 | CanonicalHash | Error | ElementProfileTypeMismatch
CCHASH014 | CanonicalHash | Error | MultipleFieldMethods
CCHASH015 | CanonicalHash | Error | UnionProfileMissingRequiredProps
CCHASH016 | CanonicalHash | Error | UnionCaseTypeNotAssignable
CCHASH017 | CanonicalHash | Error | UnionCaseMissingValueProfile
CCHASH018 | CanonicalHash | Error | DuplicateUnionDiscriminator
CCHASH019 | CanonicalHash | Error | DuplicateUnionCaseType
CCHASH020 | CanonicalHash | Error | UnionCaseTypeMustBeSealed
CCHASH021 | CanonicalHash | Error | UnionCaseMissingKnownSubtype
CCHASH022 | CanonicalHash | Error | UnionCaseValueProfileTargetMismatch
CCHASH023 | CanonicalHash | Error | CustomWriterUnsupported
CCHASH024 | CanonicalHash | Error | FilterOnlyForCollection
CCHASH025 | CanonicalHash | Error | InvalidFilterSignature
CCHASH026 | CanonicalHash | Error | FilterElementTypeMismatch
CCHASH027 | CanonicalHash | Error | FilterNotSupportedOnDictionary
CCHASH028 | CanonicalHash | Error | UnsupportedScalarType
ADP001 | AgentDraftContract | Error | NoSpecForDescriptor
ADP002 | AgentDraftContract | Error | NoClassification
ADP003 | AgentDraftContract | Error | MultipleClassifications
ADP004 | AgentDraftContract | Error | MissingReason
ADP005 | AgentDraftContract | Error | MissingCreateStrategy
ADP006 | AgentDraftContract | Error | InvalidRequiredOnCreate
ADP007 | AgentDraftContract | Error | NullableConflict
ADP008 | AgentDraftContract | Error | InvalidContractName
ADP009 | AgentDraftContract | Error | UnstableContract
ADP010 | AgentDraftContract | Error | UnsupportedReference
OM100 | ObjectMapping | Error | SourceTypeNotFound
OM101 | ObjectMapping | Error | TargetTypeNotFound
OM002 | ObjectMapping | Error | SourcePropertyNotFound
OM001 | ObjectMapping | Error | TargetPropertyNotMapped
OM004 | ObjectMapping | Error | TypeIncompatibility
OM006 | ObjectMapping | Error | ReadOnlyTarget
OM003 | ObjectMapping | Error | AmbiguousMapping
OM007 | ObjectMapping | Error | MissingElementMapping
OM009 | ObjectMapping | Error | ProtectedInputFieldWriteSkipped
OM005 | ObjectMapping | Error | NullabilityMismatch
OM008 | ObjectMapping | Error | NavigationPathInvalid
OM012 | ObjectMapping | Error | InvalidConverterType
