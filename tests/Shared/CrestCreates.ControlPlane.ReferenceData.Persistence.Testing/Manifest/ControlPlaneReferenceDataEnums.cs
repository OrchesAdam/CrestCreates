namespace CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;

public enum SaveSurface
{
    Draft,
    OrganizationUnit,
    Position,
    Membership,
    RoleAssignment,
    Rule
}

public enum OrganizationEntitySurface
{
    OrganizationUnit,
    Position,
    Membership,
    RoleAssignment
}

public enum OrganizationIdentitySurface
{
    OrganizationUnit,
    Position,
    Membership,
    RoleAssignment
}

public enum OrganizationQuerySurface
{
    Units,
    Positions,
    MembershipsByUser,
    MembershipsByUnit,
    RolesByUser
}

public enum OrganizationReadSurface
{
    UnitById,
    Units,
    PositionById,
    Positions,
    MembershipsByUser,
    MembershipsByUnit,
    RolesByUser
}

public enum PersistedSnapshotRowSurface
{
    Draft,
    OrganizationUnit,
    Position,
    Membership,
    RoleAssignment
}

public enum StoreMethodSurface
{
    DraftSave,
    DraftGet,
    DraftList,
    UnitSave,
    UnitGet,
    UnitList,
    PositionSave,
    PositionGet,
    PositionList,
    MembershipSave,
    MembershipsByUser,
    MembershipsByUnit,
    RoleSave,
    RolesByUser,
    RuleSave,
    RuleGet
}

public enum DescriptorPayloadVariant
{
    Schema,
    Form,
    Capability,
    HumanTask,
    Event,
    WorkflowCapabilityTarget,
    WorkflowHumanTaskTarget,
    WorkflowSubWorkflowTarget
}

public enum DraftQueryVariant
{
    DescriptorKind,
    Operation,
    AuthorKind,
    Status,
    CreatedFrom,
    CreatedTo,
    Combined
}

public enum DraftValidatorOwnedInvalidVariant
{
    DraftIdBlank,
    DescriptorIdBlank,
    AuthorIdBlank,
    SupportedPayloadKindMismatch,
    DefinedNonPayloadKindMismatch,
    PayloadIdMismatch,
    ProposedVersionMissing,
    ProposedVersionNotInteger,
    ProposedVersionMismatch,
    CreateBaseVersionPresent,
    UpdateBaseVersionMissing,
    DeprecateBaseVersionMissing,
    RemoveBaseVersionMissing
}

public enum IdentityValidationVector
{
    DraftNullInstance,
    DraftNullTenantId,
    DraftNullDraftId,
    DraftNullPayload,
    DraftGetNullTenantId,
    DraftGetNullDraftId,
    DraftListNullTenantId,
    UnitNullInstance,
    UnitInvalidId,
    UnitInvalidNonNullTenant,
    PositionNullInstance,
    PositionInvalidId,
    PositionInvalidNonNullTenant,
    MembershipNullInstance,
    MembershipInvalidId,
    MembershipInvalidNonNullTenant,
    MembershipInvalidUserId,
    MembershipInvalidOrganizationUnitId,
    MembershipInvalidPositionId,
    RoleAssignmentNullInstance,
    RoleAssignmentInvalidId,
    RoleAssignmentInvalidNonNullTenant,
    RoleAssignmentInvalidUserId,
    RoleAssignmentInvalidRoleId,
    RoleAssignmentInvalidOrganizationUnitId,
    UnitPointReadInvalidId,
    PositionPointReadInvalidId,
    MembershipByUserInvalidUserId,
    MembershipByUnitInvalidOrganizationUnitId,
    RoleByUserInvalidUserId,
    OrganizationQueryInvalidNonNullTenant,
    RuleNullInstance,
    RuleInvalidResource,
    RuleInvalidNonNullTenant
}

public enum PersistedEnumSurface
{
    DraftDescriptorKind,
    DraftOperation,
    DraftAuthorKind,
    DraftStatus,
    RuleScopeKind
}

public enum RuleSentinelField
{
    Action,
    Permission,
    TenantId
}

public enum RuleExactEmptyVariant
{
    ActionEmpty,
    PermissionEmpty,
    BothEmpty
}

public enum ScopedKeyCollisionVariant
{
    StoreTenantDelimiter,
    StoreIdDelimiter,
    HierarchyTenantDelimiter,
    HierarchyIdDelimiter
}

public enum MissingReferenceVariant
{
    MembershipOrganizationUnit,
    MembershipPosition,
    RoleAssignmentOrganizationUnit,
    RoleAssignmentRole
}

public enum OrganizationCreatedAtVariant
{
    UnitNonZeroOffset,
    PositionNonZeroOffset,
    MembershipNonZeroOffset,
    MembershipHundredNanosecondOrder,
    RoleAssignmentNonZeroOffset,
    RoleAssignmentHundredNanosecondOrder
}

public enum AotScenarioVariant
{
    WorkflowCapabilityTarget,
    WorkflowHumanTaskTarget,
    WorkflowSubWorkflowTarget,
    Organization,
    Rule
}

public enum PersistedRuleCorruptionVariant
{
    InvalidTenantScopeKind,
    TenantScopeTupleMismatch,
    InvalidActionMatchKind,
    ActionWildcardValueMismatch,
    InvalidPermissionMatchKind,
    PermissionWildcardValueMismatch,
    InvalidScopeKind
}

public enum PersistedSnapshotCorruptionVariant
{
    DraftInvalidJson,
    DraftUnsupportedStateContractVersion,
    DraftInvalidPayloadDiscriminator,
    DraftInvalidWorkflowTargetUnionShape,
    OrganizationUnitInvalidJson,
    OrganizationUnitUnsupportedStateContractVersion,
    PositionInvalidJson,
    PositionUnsupportedStateContractVersion,
    MembershipInvalidJson,
    MembershipUnsupportedStateContractVersion,
    RoleAssignmentInvalidJson,
    RoleAssignmentUnsupportedStateContractVersion
}

public enum PersistedStructuredFieldRowSurface
{
    Draft,
    OrganizationUnit,
    Position,
    Membership,
    RoleAssignment
}

public enum PersistedStructuredFieldVariant
{
    DraftTenantId,
    DraftDraftId,
    DraftPayloadDiscriminator,
    DraftDescriptorKind,
    DraftOperation,
    DraftAuthorKind,
    DraftStatus,
    DraftCreatedAtUtcTicks,
    DraftCreatedAtReadableProjection,
    OrganizationUnitTenantScope,
    OrganizationUnitId,
    OrganizationUnitParentId,
    OrganizationUnitSortOrder,
    OrganizationUnitIsActive,
    OrganizationUnitCreatedAtUtcTicks,
    OrganizationUnitCreatedAtReadableProjection,
    PositionTenantScope,
    PositionId,
    PositionIsActive,
    PositionCreatedAtUtcTicks,
    PositionCreatedAtReadableProjection,
    MembershipTenantScope,
    MembershipId,
    MembershipUserId,
    MembershipOrganizationUnitId,
    MembershipPositionId,
    MembershipIsPrimary,
    MembershipIsActive,
    MembershipCreatedAtUtcTicks,
    MembershipCreatedAtReadableProjection,
    RoleAssignmentTenantScope,
    RoleAssignmentId,
    RoleAssignmentUserId,
    RoleAssignmentRoleId,
    RoleAssignmentOrganizationUnitId,
    RoleAssignmentIsActive,
    RoleAssignmentCreatedAtUtcTicks,
    RoleAssignmentCreatedAtReadableProjection
}

public enum EvidenceVectorKey
{
    Default,
    Empty,
    Whitespace,
    Null,
    EmptyWhitespace,
    WorkflowHeaderSchemaPayload,
    Unknown,
    DynamicApiEndpoint,
    McpTool,
    AgentTool,
    Create,
    Update,
    Mismatch,
    JsonGlobalColumnsExact,
    JsonExactColumnsGlobal,
    JsonNullColumnNonNull,
    JsonNonNullColumnNull,
    SchemaReject,
    ProviderFailClosed
}

public enum RequiredRunner
{
    InMemory,
    PostgreSql,
    Architecture,
    Aot
}

public enum OwningSlice
{
    Slice1,
    Slice2,
    Slice3,
    Slice4,
    Slice5,
    Slice6,
    Slice7,
    Slice8,
    Slice9,
    Slice10,
    Slice11
}
