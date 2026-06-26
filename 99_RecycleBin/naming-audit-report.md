# Directory / File Naming Audit Report

Generated: 2026-06-24
Scope: src/ (1387 .cs files)

## Summary

- Total .cs files in src/: **1387**
- Files with type/file mismatch: **56**
- Files with multiple top-level types: **126**
- Namespace/path mismatch: **64**
- Type name collisions across projects: **27** (excluding AssemblyMarker)
- Placement issues (interface in non-abstractions, impl in abstractions, InMemory in wrong project): **126**

- Recommended P0 changes: **~13**
- Recommended P1 changes: **~115**
- Recommended P2 changes: **~247**

## P0 Fixes

Clear file-name/type-name mismatches where the file name directly misleads a reader or LLM.

| Current Path | Main Type | Problem | Proposed Path | NS Change | Split | Refs | Reason |
|---|---|---|---|---|---|---|---|
| src/Framework/Ddd/CrestCreates.Application/Tenants/TenantDeletionManager.cs | ITenantDeletionManager | File named TenantDeletionManager.cs, but primary public type is ITenantDeletionManager | ITenantDeletionManager.cs | No | Yes (move interface to own file) | All usings of this interface | Interface is primary public type but hidden behind wrong file name |
| src/Framework/Ddd/CrestCreates.Domain/DomainEvents/DomainEvent.cs | IDomainEvent | File named DomainEvent.cs, but primary public type is IDomainEvent | IDomainEvent.cs | No | Yes (move interface to own file) | All usings of this interface | Interface is primary public type but hidden behind wrong file name |
| src/Framework/Infrastructure/CrestCreates.Authorization.Abstractions/PermissionDefinition.cs | IPermissionDefinitionProvider | File named PermissionDefinition.cs, but primary public type is IPermissionDefinitionProvider | IPermissionDefinitionProvider.cs | No | Yes (move interface to own file) | All usings of this interface | Interface is primary public type but hidden behind wrong file name |
| src/Framework/Infrastructure/CrestCreates.Infrastructure/Localization/ResourceManagerLocalizationProvider.cs | ILocalizationProvider | File named ResourceManagerLocalizationProvider.cs, but primary public type is ILocalizationProvider | ILocalizationProvider.cs | No | Yes (move interface to own file) | All usings of this interface | Interface is primary public type but hidden behind wrong file name |
| src/Framework/Infrastructure/CrestCreates.MultiTenancy/ConnectionStringProtector.cs | IConnectionStringProtector | File named ConnectionStringProtector.cs, but primary public type is IConnectionStringProtector | IConnectionStringProtector.cs | No | Yes (move interface to own file) | All usings of this interface | Interface is primary public type but hidden behind wrong file name |
| src/Framework/Modules/CrestCreates.Scheduling.Abstractions/Jobs/JobRecord.cs | IJobRecord | File named JobRecord.cs, but primary public type is IJobRecord | IJobRecord.cs | No | Yes (move interface to own file) | All usings of this interface | Interface is primary public type but hidden behind wrong file name |
| src/Framework/Web/CrestCreates.AspNetCore.Authentication.OpenIddict/Handlers/PasswordGrantHandler.cs | IPasswordGrantHandler | File named PasswordGrantHandler.cs, but primary public type is IPasswordGrantHandler | IPasswordGrantHandler.cs | No | Yes (move interface to own file) | All usings of this interface | Interface is primary public type but hidden behind wrong file name |
| src/Framework/Web/CrestCreates.AspNetCore.Authentication.OpenIddict/Handlers/RefreshTokenGrantHandler.cs | IRefreshTokenGrantHandler | File named RefreshTokenGrantHandler.cs, but primary public type is IRefreshTokenGrantHandler | IRefreshTokenGrantHandler.cs | No | Yes (move interface to own file) | All usings of this interface | Interface is primary public type but hidden behind wrong file name |
| src/Integrations/CrestCreates.PluginSystem/Services/PluginManager.cs | IPluginManager | File named PluginManager.cs, but primary public type is IPluginManager | IPluginManager.cs | No | Yes (move interface to own file) | All usings of this interface | Interface is primary public type but hidden behind wrong file name |
| src/Runtime/Eventing/CrestCreates.EventBus.Abstractions/ILocalDeadLetterManager.cs | DeadLetterStats | File named ILocalDeadLetterManager.cs suggests interface, but contains class DeadLetterStats | DeadLetterStats.cs | No | No | All usings of this type | File name directly misleads: looks like interface file but contains implementation class |
| src/Framework/Modules/CrestCreates.Scheduling.Abstractions/Services/DefaultJobExecutionHandler.cs | DefaultJobExecutionHandler | Implementation class DefaultJobExecutionHandler in Abstractions project | Move to corresponding non-abstractions project | Yes | No | DI registration, direct references | Default implementations should not live in Abstractions projects; they belong in Runtime/Implementation or the non-Abstractions sibling |
| src/Framework/Modules/CrestCreates.Scheduling.Abstractions/Services/IJobFailureHandler.cs | DefaultJobFailureHandler | Implementation class DefaultJobFailureHandler in Abstractions project | Move to corresponding non-abstractions project | Yes | No | DI registration, direct references | Default implementations should not live in Abstractions projects; they belong in Runtime/Implementation or the non-Abstractions sibling |
| src/Persistence/CrestCreates.Data.Abstractions/Abstractions/ICurrentUserProvider.cs | DefaultCurrentUserProvider | Implementation class DefaultCurrentUserProvider in Abstractions project | Move to corresponding non-abstractions project | Yes | No | DI registration, direct references | Default implementations should not live in Abstractions projects; they belong in Runtime/Implementation or the non-Abstractions sibling |

## P1 Fixes

File-name/type-name mismatches that are less misleading but still create confusion.

| Current Path | Main Type | Problem | Proposed Path | NS Change | Split | Refs | Reason |
|---|---|---|---|---|---|---|---|
| src/Framework/Api/CrestCreates.DynamicApi/DynamicApiDescriptors.cs | DynamicApiParameterSource | Filename 'DynamicApiDescriptors.cs' ≠ type 'DynamicApiParameterSource' (file has 9 types) | DynamicApiParameterSource.cs | No | Yes (split into individual files) | Referencing files | File contains 9 types (DynamicApiParameterSource, DynamicApiServiceDescriptor, DynamicApiActionDescriptor...); filename suggests only one |
| src/Framework/Api/CrestCreates.DynamicApi/GeneratedDynamicApiRuntime.cs | DynamicApiGeneratedRuntime | Filename 'GeneratedDynamicApiRuntime.cs' ≠ type 'DynamicApiGeneratedRuntime' | DynamicApiGeneratedRuntime.cs | No | No | Referencing files | Filename 'GeneratedDynamicApiRuntime' does not match primary type 'DynamicApiGeneratedRuntime'; confusing for navigation |
| src/Framework/Ddd/CrestCreates.Application.Contracts/DTOs/AuditLog/CleanupAuditLogsDto.cs | CleanupAuditLogsRequestDto | Filename 'CleanupAuditLogsDto.cs' ≠ type 'CleanupAuditLogsRequestDto' (file has 2 types) | CleanupAuditLogsRequestDto.cs | No | Yes (split into individual files) | Referencing files | File contains 2 types (CleanupAuditLogsRequestDto, CleanupAuditLogsResultDto...); filename suggests only one |
| src/Framework/Ddd/CrestCreates.Application.Contracts/DTOs/ProductDtos.cs | ProductDto | Filename 'ProductDtos.cs' ≠ type 'ProductDto' (file has 5 types) | ProductDto.cs | No | Yes (split into individual files) | Referencing files | File contains 5 types (ProductDto, CreateProductDto, UpdateProductDto...); filename suggests only one |
| src/Framework/Ddd/CrestCreates.Domain.Shared/Attributes/DynamicApiAttributes.cs | DynamicApiIgnoreAttribute | Filename 'DynamicApiAttributes.cs' ≠ type 'DynamicApiIgnoreAttribute' (file has 2 types) | DynamicApiIgnoreAttribute.cs | No | Yes (split into individual files) | Referencing files | File contains 2 types (DynamicApiIgnoreAttribute, DynamicApiRouteAttribute...); filename suggests only one |
| src/Framework/Ddd/CrestCreates.Domain/Permission/TenantInitializationRecord.Serialization.cs | TenantInitializationRecordJsonContext | Filename 'TenantInitializationRecord.Serialization.cs' ≠ type 'TenantInitializationRecordJsonContext' | TenantInitializationRecordJsonContext.cs | No | No | Referencing files | Filename 'TenantInitializationRecord.Serialization' does not match primary type 'TenantInitializationRecordJsonContext'; confusing for navigation |
| src/Framework/Ddd/CrestCreates.Domain/Repositories/CrestRepositoryBase.cs | TenantFilter | Filename 'CrestRepositoryBase.cs' ≠ type 'TenantFilter' (file has 2 types) | TenantFilter.cs | No | Yes (split into individual files) | Referencing files | File contains 2 types (TenantFilter, CrestRepositoryBase...); filename suggests only one |
| src/Framework/Infrastructure/CrestCreates.Infrastructure/Authorization/AuthorizationAttributes.cs | AuthorizePermissionAttribute | Filename 'AuthorizationAttributes.cs' ≠ type 'AuthorizePermissionAttribute' (file has 5 types) | AuthorizePermissionAttribute.cs | No | Yes (split into individual files) | Referencing files | File contains 5 types (AuthorizePermissionAttribute, AuthorizeRolesAttribute, PermissionAuthorizationRequirement...); filename suggests only one |
| src/Framework/Infrastructure/CrestCreates.Infrastructure/Authorization/AuthorizationExtensions.cs | AuthorizationOptions | Filename 'AuthorizationExtensions.cs' ≠ type 'AuthorizationOptions' (file has 3 types) | AuthorizationOptions.cs | Possibly | Yes (split into individual files) | Referencing files | File contains 3 types (AuthorizationOptions, AuthorizationServiceCollectionExtensions, PermissionDefinitionExtensions...); filename suggests only one |
| src/Framework/Infrastructure/CrestCreates.Infrastructure/Authorization/IIdentityClaimsBuilder.cs | IdentityClaimsContext | Filename 'IIdentityClaimsBuilder.cs' ≠ type 'IdentityClaimsContext' (file has 2 types) | IdentityClaimsContext.cs | No | Yes (split into individual files) | Referencing files | File contains 2 types (IdentityClaimsContext, IIdentityClaimsBuilder...); filename suggests only one |
| src/Framework/Infrastructure/CrestCreates.Infrastructure/Permission/PermissionModule.cs | PermissionServiceCollectionExtensions | Filename 'PermissionModule.cs' ≠ type 'PermissionServiceCollectionExtensions' | PermissionServiceCollectionExtensions.cs | Possibly | No | Referencing files | Filename 'PermissionModule' does not match primary type 'PermissionServiceCollectionExtensions'; confusing for navigation |
| src/Framework/Infrastructure/CrestCreates.Infrastructure/UnitOfWork/UnitOfWorkFactory.cs | OrmOptions | Filename 'UnitOfWorkFactory.cs' ≠ type 'OrmOptions' (file has 6 types) | OrmOptions.cs | Possibly | Yes (split into individual files) | Referencing files | File contains 6 types (OrmOptions, IUnitOfWorkFactory, UnitOfWorkFactory...); filename suggests only one |
| src/Framework/Infrastructure/CrestCreates.MultiTenancy/MultiTenancyExtensions.cs | MultiTenancyServiceCollectionExtensions | Filename 'MultiTenancyExtensions.cs' ≠ type 'MultiTenancyServiceCollectionExtensions' (file has 2 types) | MultiTenancyServiceCollectionExtensions.cs | Possibly | Yes (split into individual files) | Referencing files | File contains 2 types (MultiTenancyServiceCollectionExtensions, MultiTenancyApplicationBuilderExtensions...); filename suggests only one |
| src/Framework/Infrastructure/CrestCreates.MultiTenancy/Resolvers/OtherTenantResolvers.cs | QueryStringTenantResolver | Filename 'OtherTenantResolvers.cs' ≠ type 'QueryStringTenantResolver' (file has 3 types) | QueryStringTenantResolver.cs | No | Yes (split into individual files) | Referencing files | File contains 3 types (QueryStringTenantResolver, CookieTenantResolver, RouteTenantResolver...); filename suggests only one |
| src/Framework/Infrastructure/CrestCreates.MultiTenancy/TenantResolutionResult.cs | TenantResolutionResultExtensions | Filename 'TenantResolutionResult.cs' ≠ type 'TenantResolutionResultExtensions' | TenantResolutionResultExtensions.cs | No | No | Referencing files | Filename 'TenantResolutionResult' does not match primary type 'TenantResolutionResultExtensions'; confusing for navigation |
| src/Framework/Infrastructure/CrestCreates.Validation/Localization/ValidationLocalization.cs | ValidationErrorCodes | Filename 'ValidationLocalization.cs' ≠ type 'ValidationErrorCodes' (file has 2 types) | ValidationErrorCodes.cs | No | Yes (split into individual files) | Referencing files | File contains 2 types (ValidationErrorCodes, ValidationLocalizationExtensions...); filename suggests only one |
| src/Framework/Modularity/CrestCreates.Modularity/ModuleBase.cs | ModuleDescriptor | Filename 'ModuleBase.cs' ≠ type 'ModuleDescriptor' (file has 2 types) | ModuleDescriptor.cs | Possibly | Yes (split into individual files) | Referencing files | File contains 2 types (ModuleDescriptor, ModuleBase...); filename suggests only one |
| src/Framework/Modules/CrestCreates.ModuleDiagnostics/Modules/ModuleDiagnosticsModule.cs | ModuleDiagnosticsServiceCollectionExtensions | Filename 'ModuleDiagnosticsModule.cs' ≠ type 'ModuleDiagnosticsServiceCollectionExtensions' | ModuleDiagnosticsServiceCollectionExtensions.cs | Possibly | No | Referencing files | Filename 'ModuleDiagnosticsModule' does not match primary type 'ModuleDiagnosticsServiceCollectionExtensions'; confusing for navigation |
| src/Framework/Modules/CrestCreates.ModuleDiagnostics/Stores/ModulePhaseDiagnostic.cs | ModulePhaseStatus | Filename 'ModulePhaseDiagnostic.cs' ≠ type 'ModulePhaseStatus' (file has 2 types) | ModulePhaseStatus.cs | Possibly | Yes (split into individual files) | Referencing files | File contains 2 types (ModulePhaseStatus, ModulePhaseDiagnostic...); filename suggests only one |
| src/Framework/Web/CrestCreates.AspNetCore.Authentication.OpenIddict/Services/IdentitySecurityLogService.cs | IdentitySecurityLogServiceImpl | Filename 'IdentitySecurityLogService.cs' ≠ type 'IdentitySecurityLogServiceImpl' | IdentitySecurityLogServiceImpl.cs | No | No | Referencing files | Filename 'IdentitySecurityLogService' does not match primary type 'IdentitySecurityLogServiceImpl'; confusing for navigation |
| src/Framework/Web/CrestCreates.AspNetCore/AspNetCoreModuleExtensions.cs | AspNetCoreServiceExtensions | Filename 'AspNetCoreModuleExtensions.cs' ≠ type 'AspNetCoreServiceExtensions' | AspNetCoreServiceExtensions.cs | Possibly | No | Referencing files | Filename 'AspNetCoreModuleExtensions' does not match primary type 'AspNetCoreServiceExtensions'; confusing for navigation |
| src/Framework/Web/CrestCreates.HealthCheck.AspNetCore/Serialization/HealthReportJsonContext.cs | HealthReportData | Filename 'HealthReportJsonContext.cs' ≠ type 'HealthReportData' (file has 5 types) | HealthReportData.cs | No | Yes (split into individual files) | Referencing files | File contains 5 types (HealthReportData, HealthReportDataConverter, HealthReportJsonContext...); filename suggests only one |
| src/Framework/Web/CrestCreates.HealthCheck.Mvc/HealthChecks/CommonHealthChecks.cs | MemoryHealthCheck | Filename 'CommonHealthChecks.cs' ≠ type 'MemoryHealthCheck' (file has 3 types) | MemoryHealthCheck.cs | No | Yes (split into individual files) | Referencing files | File contains 3 types (MemoryHealthCheck, DatabaseHealthCheck, RedisHealthCheck...); filename suggests only one |
| src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorRelationship.cs | DescriptorRef | Filename 'DescriptorRelationship.cs' ≠ type 'DescriptorRef' (file has 3 types) | DescriptorRef.cs | No | Yes (split into individual files) | Referencing files | File contains 3 types (DescriptorRef, DescriptorRelationship, RelationshipKind...); filename suggests only one |
| src/Metadata/CrestCreates.Metadata.Abstractions/ValidationIssue.cs | ValidationSeverity | Filename 'ValidationIssue.cs' ≠ type 'ValidationSeverity' (file has 2 types) | ValidationSeverity.cs | No | Yes (split into individual files) | Referencing files | File contains 2 types (ValidationSeverity, ValidationIssue...); filename suggests only one |
| src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/DescriptorDraftDiagnostic.cs | DescriptorDraftDiagnosticSeverity | Filename 'DescriptorDraftDiagnostic.cs' ≠ type 'DescriptorDraftDiagnosticSeverity' (file has 2 types) | DescriptorDraftDiagnosticSeverity.cs | No | Yes (split into individual files) | Referencing files | File contains 2 types (DescriptorDraftDiagnosticSeverity, DescriptorDraftDiagnostic...); filename suggests only one |
| src/Persistence/CrestCreates.Data.Abstractions/Abstractions/IEntity.cs | ISoftDelete | Filename 'IEntity.cs' ≠ type 'ISoftDelete' | ISoftDelete.cs | No | No | Referencing files | Filename 'IEntity' does not match primary type 'ISoftDelete'; confusing for navigation |
| src/Persistence/CrestCreates.Data.EFCore/MultiTenancy/MultiTenancyDiscriminator.cs | MultiTenancyDiscriminatorExtensions | Filename 'MultiTenancyDiscriminator.cs' ≠ type 'MultiTenancyDiscriminatorExtensions' (file has 6 types) | MultiTenancyDiscriminatorExtensions.cs | No | Yes (split into individual files) | Referencing files | File contains 6 types (MultiTenancyDiscriminatorExtensions, IMultiTenant, MultiTenantEntity...); filename suggests only one |
| src/Persistence/CrestCreates.Data.FreeSql/Repositories/FreeSqlRepositoryBase.cs | FreeSqlRepository | Filename 'FreeSqlRepositoryBase.cs' ≠ type 'FreeSqlRepository' | FreeSqlRepository.cs | No | No | Referencing files | Filename 'FreeSqlRepositoryBase' does not match primary type 'FreeSqlRepository'; confusing for navigation |
| src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/ControlPlaneAgentToolDescriptor.cs | AgentToolDescriptor | Filename 'ControlPlaneAgentToolDescriptor.cs' ≠ type 'AgentToolDescriptor' | AgentToolDescriptor.cs | No | No | Referencing files | Filename 'ControlPlaneAgentToolDescriptor' does not match primary type 'AgentToolDescriptor'; confusing for navigation |
| src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentControlPlaneArtifactEntries.cs | ReviewResourceSnapshot | Filename 'AgentControlPlaneArtifactEntries.cs' ≠ type 'ReviewResourceSnapshot' (file has 8 types) | ReviewResourceSnapshot.cs | No | Yes (split into individual files) | Referencing files | File contains 8 types (ReviewResourceSnapshot, FixProposalResourceSnapshot, PackagePreviewEntry...); filename suggests only one |
| src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentControlPlaneResourceResolver.cs | DescriptorResourceSnapshot | Filename 'AgentControlPlaneResourceResolver.cs' ≠ type 'DescriptorResourceSnapshot' (file has 5 types) | DescriptorResourceSnapshot.cs | No | Yes (split into individual files) | Referencing files | File contains 5 types (DescriptorResourceSnapshot, DraftResourceSnapshot, ResourceResolutionStatus...); filename suggests only one |
| src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentDescriptorKindPolicyEvaluator.cs | AgentDescriptorKindDecision | Filename 'AgentDescriptorKindPolicyEvaluator.cs' ≠ type 'AgentDescriptorKindDecision' (file has 2 types) | AgentDescriptorKindDecision.cs | No | Yes (split into individual files) | Referencing files | File contains 2 types (AgentDescriptorKindDecision, AgentDescriptorKindPolicyEvaluator...); filename suggests only one |
| src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentDiagnosticExplanationPolicy.cs | DiagnosticExplanationTemplate | Filename 'AgentDiagnosticExplanationPolicy.cs' ≠ type 'DiagnosticExplanationTemplate' (file has 2 types) | DiagnosticExplanationTemplate.cs | No | Yes (split into individual files) | Referencing files | File contains 2 types (DiagnosticExplanationTemplate, AgentDiagnosticExplanationPolicy...); filename suggests only one |
| src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentToolVisibilityCoverage.cs | AgentToolResourceShape | Filename 'AgentToolVisibilityCoverage.cs' ≠ type 'AgentToolResourceShape' (file has 3 types) | AgentToolResourceShape.cs | No | Yes (split into individual files) | Referencing files | File contains 3 types (AgentToolResourceShape, AgentToolVisibilityEntry, AgentToolVisibilityCoverage...); filename suggests only one |
| src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentVisibleDescriptorUniverse.cs | UniverseCreationResult | Filename 'AgentVisibleDescriptorUniverse.cs' ≠ type 'UniverseCreationResult' (file has 2 types) | UniverseCreationResult.cs | No | Yes (split into individual files) | Referencing files | File contains 2 types (UniverseCreationResult, AgentVisibleDescriptorUniverse...); filename suggests only one |
| src/Runtime/Agent/CrestCreates.Agent.DraftContracts/Specs/AgentDraftPreserveAttribute.cs | PreserveCreateStrategy | Filename 'AgentDraftPreserveAttribute.cs' ≠ type 'PreserveCreateStrategy' (file has 2 types) | PreserveCreateStrategy.cs | No | Yes (split into individual files) | Referencing files | File contains 2 types (AgentDraftPreserveAttribute, PreserveCreateStrategy...); filename suggests only one |
| src/Runtime/Capability/CrestCreates.Capability/SystemEventDescriptors.cs | SystemEventDescriptorProvider | Filename 'SystemEventDescriptors.cs' ≠ type 'SystemEventDescriptorProvider' | SystemEventDescriptorProvider.cs | No | No | Referencing files | Filename 'SystemEventDescriptors' does not match primary type 'SystemEventDescriptorProvider'; confusing for navigation |
| src/Runtime/Eventing/CrestCreates.EventBus.Abstractions/DeadLetterMessage.cs | DeadLetterStatus | Filename 'DeadLetterMessage.cs' ≠ type 'DeadLetterStatus' (file has 2 types) | DeadLetterStatus.cs | No | Yes (split into individual files) | Referencing files | File contains 2 types (DeadLetterStatus, DeadLetterMessage...); filename suggests only one |
| src/Tooling/CrestCreates.CodeGenerator/AgentDraftContractGenerator/AgentDraftContractModel.cs | FieldClassification | Filename 'AgentDraftContractModel.cs' ≠ type 'FieldClassification' (file has 5 types) | FieldClassification.cs | No | Yes (split into individual files) | Referencing files | File contains 5 types (FieldClassification, PreserveStrategy, ContractFieldSpec...); filename suggests only one |
| src/Tooling/CrestCreates.CodeGenerator/AgentDraftContractGenerator/ContractModelBuilder.cs | SpecClassInfo | Filename 'ContractModelBuilder.cs' ≠ type 'SpecClassInfo' (file has 2 types) | SpecClassInfo.cs | No | Yes (split into individual files) | Referencing files | File contains 2 types (SpecClassInfo, ContractModelBuilder...); filename suggests only one |
| src/Tooling/CrestCreates.CodeGenerator/Authorization/AuthorizationAttributeGenerator.cs | AuthorizationConfig | Filename 'AuthorizationAttributeGenerator.cs' ≠ type 'AuthorizationConfig' (file has 4 types) | AuthorizationConfig.cs | No | Yes (split into individual files) | Referencing files | File contains 4 types (AuthorizationConfig, AuthorizationAttributeGenerator, RoslynAuthorizationInjector...); filename suggests only one |
| src/Tooling/CrestCreates.CodeGenerator/CanonicalHashGenerator/CanonicalHashModel.cs | ProfileFieldModel | Filename 'CanonicalHashModel.cs' ≠ type 'ProfileFieldModel' (file has 6 types) | ProfileFieldModel.cs | No | Yes (split into individual files) | Referencing files | File contains 6 types (ProfileFieldModel, ProfileReferenceModel, FieldFilterModel...); filename suggests only one |
| src/Tooling/CrestCreates.CodeGenerator/CanonicalHashGenerator/CanonicalHashModelBuilder.cs | ProfileClassInfo | Filename 'CanonicalHashModelBuilder.cs' ≠ type 'ProfileClassInfo' (file has 2 types) | ProfileClassInfo.cs | No | Yes (split into individual files) | Referencing files | File contains 2 types (ProfileClassInfo, CanonicalHashModelBuilder...); filename suggests only one |
| src/Tooling/CrestCreates.CodeGenerator/KafkaGenerator/KafkaSubscriptionModel.cs | KafkaSubscriptionInfo | Filename 'KafkaSubscriptionModel.cs' ≠ type 'KafkaSubscriptionInfo' (file has 2 types) | KafkaSubscriptionInfo.cs | No | Yes (split into individual files) | Referencing files | File contains 2 types (KafkaSubscriptionInfo, KafkaSubscriptionModel...); filename suggests only one |
| src/Tooling/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingModel.cs | MappingDeclaration | Filename 'ObjectMappingModel.cs' ≠ type 'MappingDeclaration' (file has 5 types) | MappingDeclaration.cs | No | Yes (split into individual files) | Referencing files | File contains 5 types (MappingDeclaration, PropertyMapping, ObjectMappingModel...); filename suggests only one |

## P2 Fixes

Namespace/path mismatches, interface-in-non-abstractions, and InMemory placement issues.

### P2a: Namespace vs Path Mismatches (selected high-impact)

| Current Path | Namespace | Expected Namespace | Reason |
|---|---|---|---|
| src/Framework/Api/CrestCreates.DynamicApi/GeneratedApi/ApiOverrideAttribute.cs | CrestCreates.DynamicApi | CrestCreates.DynamicApi.GeneratedApi | Subdirectory 'GeneratedApi' not reflected in namespace |
| src/Framework/Api/CrestCreates.DynamicApi/GeneratedApi/CrestApiController.cs | CrestCreates.DynamicApi | CrestCreates.DynamicApi.GeneratedApi | Subdirectory 'GeneratedApi' not reflected in namespace |
| src/Framework/Api/CrestCreates.DynamicApi/GeneratedApi/CrudAction.cs | CrestCreates.DynamicApi | CrestCreates.DynamicApi.GeneratedApi | Subdirectory 'GeneratedApi' not reflected in namespace |
| src/Framework/Api/CrestCreates.DynamicApi/GeneratedApi/DynamicApiEndpointConventionContext.cs | CrestCreates.DynamicApi | CrestCreates.DynamicApi.GeneratedApi | Subdirectory 'GeneratedApi' not reflected in namespace |
| src/Framework/Api/CrestCreates.DynamicApi/GeneratedApi/DynamicApiEndpointConventionRunner.cs | CrestCreates.DynamicApi | CrestCreates.DynamicApi.GeneratedApi | Subdirectory 'GeneratedApi' not reflected in namespace |
| src/Framework/Api/CrestCreates.DynamicApi/GeneratedApi/DynamicApiEndpointDescriptor.cs | CrestCreates.DynamicApi | CrestCreates.DynamicApi.GeneratedApi | Subdirectory 'GeneratedApi' not reflected in namespace |
| src/Framework/Api/CrestCreates.DynamicApi/GeneratedApi/GeneratedApiControllerAttribute.cs | CrestCreates.DynamicApi | CrestCreates.DynamicApi.GeneratedApi | Subdirectory 'GeneratedApi' not reflected in namespace |
| src/Framework/Api/CrestCreates.DynamicApi/GeneratedApi/IDynamicApiEndpointConvention.cs | CrestCreates.DynamicApi | CrestCreates.DynamicApi.GeneratedApi | Subdirectory 'GeneratedApi' not reflected in namespace |
| src/Framework/Ddd/CrestCreates.Application.Contracts/DTOs/ProductDtos.cs | CrestCreates.Application.Contracts.Examples.DTOs | CrestCreates.Application.Contracts.DTOs | Subdirectory 'DTOs' not reflected in namespace |
| src/Framework/Ddd/CrestCreates.Application.Contracts/Interfaces/IProductService.cs | CrestCreates.Application.Contracts.Examples.Interfaces | CrestCreates.Application.Contracts.Interfaces | Subdirectory 'Interfaces' not reflected in namespace |
| src/Framework/Infrastructure/CrestCreates.Caching.Abstractions/ICrestCacheService.cs | CrestCreates.Caching | CrestCreates.Caching.Abstractions | Subdirectory '' not reflected in namespace |
| src/Framework/Infrastructure/CrestCreates.VirtualFileSystem/Models/VirtualDirectoryInfo.cs | CrestCreates.VirtualFileSystem.Providers | CrestCreates.VirtualFileSystem.Models | Subdirectory 'Models' not reflected in namespace |
| src/Framework/Infrastructure/CrestCreates.VirtualFileSystem/Providers/CodeGeneratorResourceProvider.cs | CrestCreates.CodeGenerator | CrestCreates.VirtualFileSystem.Providers | Subdirectory 'Providers' not reflected in namespace |
| src/Framework/Modules/CrestCreates.Scheduling.Abstractions/Jobs/IJob.cs | CrestCreates.Scheduling.Jobs | CrestCreates.Scheduling.Abstractions.Jobs | Subdirectory 'Jobs' not reflected in namespace |
| src/Framework/Modules/CrestCreates.Scheduling.Abstractions/Jobs/IJobArgs.cs | CrestCreates.Scheduling.Jobs | CrestCreates.Scheduling.Abstractions.Jobs | Subdirectory 'Jobs' not reflected in namespace |
| src/Framework/Modules/CrestCreates.Scheduling.Abstractions/Jobs/JobExecutionContext.cs | CrestCreates.Scheduling.Jobs | CrestCreates.Scheduling.Abstractions.Jobs | Subdirectory 'Jobs' not reflected in namespace |
| src/Framework/Modules/CrestCreates.Scheduling.Abstractions/Jobs/JobId.cs | CrestCreates.Scheduling.Jobs | CrestCreates.Scheduling.Abstractions.Jobs | Subdirectory 'Jobs' not reflected in namespace |
| src/Framework/Modules/CrestCreates.Scheduling.Abstractions/Jobs/JobRecord.cs | CrestCreates.Scheduling.Jobs | CrestCreates.Scheduling.Abstractions.Jobs | Subdirectory 'Jobs' not reflected in namespace |
| src/Framework/Modules/CrestCreates.Scheduling.Abstractions/Modules/SchedulingModule.cs | CrestCreates.Scheduling.Modules | CrestCreates.Scheduling.Abstractions.Modules | Subdirectory 'Modules' not reflected in namespace |
| src/Framework/Modules/CrestCreates.Scheduling.Abstractions/Services/DefaultJobExecutionHandler.cs | CrestCreates.Scheduling.Services | CrestCreates.Scheduling.Abstractions.Services | Subdirectory 'Services' not reflected in namespace |
| src/Framework/Modules/CrestCreates.Scheduling.Abstractions/Services/ExponentialBackoffRetryPolicy.cs | CrestCreates.Scheduling.Services | CrestCreates.Scheduling.Abstractions.Services | Subdirectory 'Services' not reflected in namespace |
| src/Framework/Modules/CrestCreates.Scheduling.Abstractions/Services/FixedDelayRetryPolicy.cs | CrestCreates.Scheduling.Services | CrestCreates.Scheduling.Abstractions.Services | Subdirectory 'Services' not reflected in namespace |
| src/Framework/Modules/CrestCreates.Scheduling.Abstractions/Services/IBackgroundJobRetryPolicy.cs | CrestCreates.Scheduling.Services | CrestCreates.Scheduling.Abstractions.Services | Subdirectory 'Services' not reflected in namespace |
| src/Framework/Modules/CrestCreates.Scheduling.Abstractions/Services/IJobExecutionHandler.cs | CrestCreates.Scheduling.Services | CrestCreates.Scheduling.Abstractions.Services | Subdirectory 'Services' not reflected in namespace |
| src/Framework/Modules/CrestCreates.Scheduling.Abstractions/Services/IJobFailureHandler.cs | CrestCreates.Scheduling.Services | CrestCreates.Scheduling.Abstractions.Services | Subdirectory 'Services' not reflected in namespace |
| src/Framework/Modules/CrestCreates.Scheduling.Abstractions/Services/IJobHistoryRepository.cs | CrestCreates.Scheduling.Services | CrestCreates.Scheduling.Abstractions.Services | Subdirectory 'Services' not reflected in namespace |
| src/Framework/Modules/CrestCreates.Scheduling.Abstractions/Services/ISchedulerService.cs | CrestCreates.Scheduling.Services | CrestCreates.Scheduling.Abstractions.Services | Subdirectory 'Services' not reflected in namespace |
| src/Framework/Modules/CrestCreates.Scheduling.Abstractions/Services/NoRetryPolicy.cs | CrestCreates.Scheduling.Services | CrestCreates.Scheduling.Abstractions.Services | Subdirectory 'Services' not reflected in namespace |
| src/Framework/Web/CrestCreates.AspNetCore.Authentication.OpenIddict/Data/OpenIddictDbContext.cs | CrestCreates.AspNetCore.Authentication.OpenIddict | CrestCreates.AspNetCore.Authentication.OpenIddict.Data | Subdirectory 'Data' not reflected in namespace |
| src/Persistence/CrestCreates.Data.Abstractions/Abstractions/ICurrentUserProvider.cs | CrestCreates.Data.Abstractions | CrestCreates.Data.Abstractions.Abstractions | Subdirectory 'Abstractions' not reflected in namespace |

### P2b: InMemory Implementations in Non-Infrastructure Projects

| Current Path | Type | Project | Suggested Home |
|---|---|---|---|
| src/Framework/Infrastructure/CrestCreates.Authorization/InMemoryPermissionStore.cs | InMemoryPermissionStore | CrestCreates.Authorization | Stores/InMemory/ or Infrastructure/InMemory/ |
| src/Framework/Infrastructure/CrestCreates.MultiTenancy/Providers/InMemoryTenantProvider.cs | InMemoryTenantProvider | CrestCreates.MultiTenancy | Stores/InMemory/ or Infrastructure/InMemory/ |
| src/Framework/Modules/CrestCreates.FileManagement/Repositories/InMemoryFileRepository.cs | InMemoryFileRepository | CrestCreates.FileManagement | Stores/InMemory/ or Infrastructure/InMemory/ |
| src/Framework/Modules/CrestCreates.Organization/InMemoryDataPermissionScopeRuleStore.cs | InMemoryDataPermissionScopeRuleStore | CrestCreates.Organization | Stores/InMemory/ or Infrastructure/InMemory/ |
| src/Framework/Modules/CrestCreates.Organization/InMemoryOrganizationStore.cs | InMemoryOrganizationStore | CrestCreates.Organization | Stores/InMemory/ or Infrastructure/InMemory/ |
| src/Metadata/Draft/CrestCreates.DescriptorDraft/InMemoryDescriptorDraftStore.cs | InMemoryDescriptorDraftStore | CrestCreates.DescriptorDraft | Stores/InMemory/ or Infrastructure/InMemory/ |
| src/Metadata/Draft/CrestCreates.Draft/InMemoryDraftStore.cs | InMemoryDraftStore | CrestCreates.Draft | Stores/InMemory/ or Infrastructure/InMemory/ |
| src/Runtime/Agent/CrestCreates.Agent.ControlPlane/InMemoryAgentToolInvocationAuditor.cs | InMemoryAgentToolInvocationAuditor | CrestCreates.Agent.ControlPlane | Stores/InMemory/ or Infrastructure/InMemory/ |
| src/Runtime/Capability/CrestCreates.Capability/InMemoryCapabilityAuditStore.cs | InMemoryCapabilityAuditStore | CrestCreates.Capability | Stores/InMemory/ or Infrastructure/InMemory/ |
| src/Runtime/Capability/CrestCreates.Capability/InMemoryIdempotenceStore.cs | InMemoryIdempotenceStore | CrestCreates.Capability | Stores/InMemory/ or Infrastructure/InMemory/ |
| src/Runtime/Capability/CrestCreates.Capability/InMemoryPipelineMetrics.cs | InMemoryPipelineMetrics | CrestCreates.Capability | Stores/InMemory/ or Infrastructure/InMemory/ |
| src/Runtime/Capability/CrestCreates.Capability/InMemoryRateLimitStore.cs | InMemoryRateLimitStore | CrestCreates.Capability | Stores/InMemory/ or Infrastructure/InMemory/ |
| src/Runtime/Eventing/CrestCreates.EventBus.EventStore/InMemoryEventRetryStore.cs | InMemoryEventRetryStore | CrestCreates.EventBus.EventStore | Stores/InMemory/ or Infrastructure/InMemory/ |
| src/Runtime/Eventing/CrestCreates.EventBus.Local/InMemoryDeadLetterStore.cs | InMemoryDeadLetterStore | CrestCreates.EventBus.Local | Stores/InMemory/ or Infrastructure/InMemory/ |
| src/Runtime/Eventing/CrestCreates.EventBus.Local/InMemoryEventIdempotencyStore.cs | InMemoryEventIdempotencyStore | CrestCreates.EventBus.Local | Stores/InMemory/ or Infrastructure/InMemory/ |
| src/Runtime/HumanTask/CrestCreates.HumanTask/InMemoryHumanTaskInstanceStore.cs | InMemoryHumanTaskInstanceStore | CrestCreates.HumanTask | Stores/InMemory/ or Infrastructure/InMemory/ |
| src/Runtime/Workflow/CrestCreates.Workflow/InMemoryWorkflowInstanceStore.cs | InMemoryWorkflowInstanceStore | CrestCreates.Workflow | Stores/InMemory/ or Infrastructure/InMemory/ |

### P2c: Interface-in-Non-Abstractions (DDD acceptable, but noted)

Many interfaces live in Domain, Application, and Infrastructure projects (not in *.Abstractions).
This is common in DDD (domain interfaces in Domain layer, repository interfaces in Domain).
These are LOW priority — only flag if they cause cross-layer dependency issues.

Total: 106 interfaces

## Split Candidates

Files with multiple top-level public types that should be split:

| File | Types | Count | Priority |
|---|---|---|---|
| src/Framework/Api/CrestCreates.DynamicApi/DynamicApiDescriptors.cs | DynamicApiParameterSource, DynamicApiServiceDescriptor, DynamicApiActionDescriptor, DynamicApiParameterDescriptor, DynamicApiReturnDescriptor +4 more | 9 | P1 |
| src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentControlPlaneArtifactEntries.cs | ReviewResourceSnapshot, FixProposalResourceSnapshot, PackagePreviewEntry, PackagePreviewResourceSnapshot, EvidencePreviewEntry +3 more | 8 | P1 |
| src/Framework/Ddd/CrestCreates.Application.Contracts/DTOs/Tenants/TenantDiagnosticsDto.cs | TenantDiagnosticsDto, TenantHealthStatus, TenantStatusDetails, ConnectionStringSummary, AdminSummary +1 more | 6 | P1 |
| src/Framework/Infrastructure/CrestCreates.Infrastructure/UnitOfWork/UnitOfWorkFactory.cs | OrmOptions, IUnitOfWorkFactory, UnitOfWorkFactory, IUnitOfWorkManager, UnitOfWorkManager +1 more | 6 | P1 |
| src/Persistence/CrestCreates.Data.EFCore/MultiTenancy/MultiTenancyDiscriminator.cs | MultiTenancyDiscriminatorExtensions, IMultiTenant, MultiTenantEntity, MultiTenantEntity, TenantFilterRegistryStore +1 more | 6 | P1 |
| src/Tooling/CrestCreates.CodeGenerator/CanonicalHashGenerator/CanonicalHashModel.cs | ProfileFieldModel, ProfileReferenceModel, FieldFilterModel, UnionCaseModel, UnionProfileModel +1 more | 6 | P1 |
| src/Framework/Ddd/CrestCreates.Application.Contracts/DTOs/ProductDtos.cs | ProductDto, CreateProductDto, UpdateProductDto, UpdateProductPriceDto, UpdateProductStockDto | 5 | P1 |
| src/Framework/Infrastructure/CrestCreates.Authorization.Abstractions/PermissionDefinition.cs | IPermissionDefinitionProvider, IPermissionDefinitionContext, IPermissionDefinitionManager, PermissionDefinition, PermissionGroupDefinition | 5 | P1 |
| src/Framework/Infrastructure/CrestCreates.Infrastructure/Authorization/AuthorizationAttributes.cs | AuthorizePermissionAttribute, AuthorizeRolesAttribute, PermissionAuthorizationRequirement, PermissionAuthorizationHandler, PermissionPolicies | 5 | P1 |
| src/Framework/Modules/CrestCreates.FileManagement/Configuration/FileManagementOptions.cs | FileManagementOptions, StorageProviderType, LocalFileSystemOptions, FileValidationOptions, FileUrlOptions | 5 | P1 |
| src/Framework/Modules/CrestCreates.Scheduling.Abstractions/Services/IJobExecutionHandler.cs | IJobExecutionHandler, JobScheduledContext, JobStartedContext, JobSucceededContext, JobCancelledContext | 5 | P1 |
| src/Framework/Web/CrestCreates.HealthCheck.AspNetCore/Serialization/HealthReportJsonContext.cs | HealthReportData, HealthReportDataConverter, HealthReportJsonContext, HealthReportResponse, HealthCheckEntryResponse | 5 | P1 |
| src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentControlPlaneResourceResolver.cs | DescriptorResourceSnapshot, DraftResourceSnapshot, ResourceResolutionStatus, ResourceResolution, AgentControlPlaneResourceResolver | 5 | P1 |
| src/Tooling/CrestCreates.CodeGenerator/AgentDraftContractGenerator/AgentDraftContractModel.cs | FieldClassification, PreserveStrategy, ContractFieldSpec, ContractKindSpec, ContractModel | 5 | P1 |
| src/Tooling/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingModel.cs | MappingDeclaration, PropertyMapping, ObjectMappingModel, MapDirection, ObjectMappingConversionKind | 5 | P1 |
| src/Framework/Modules/CrestCreates.Scheduling.Abstractions/Services/IJobFailureHandler.cs | IJobFailureHandler, JobFailureContext, JobRetryOptions, DefaultJobFailureHandler | 4 | P1 |
| src/Framework/Modules/CrestCreates.Scheduling.Abstractions/Services/ISchedulerService.cs | ISchedulerService, JobStatus, JobInfo, JobMetadata | 4 | P1 |
| src/Framework/Infrastructure/CrestCreates.Security/Configuration/SecurityOptions.cs | SecurityOptions, CsrfOptions, HstsOptions, SecurityHeadersOptions | 4 | P2 |
| src/Framework/Infrastructure/CrestCreates.Validation/Validators/ValidatorBase.cs | ValidatorBase, ValidationResult, ValidationErrorDetail, ValidationExtensions | 4 | P2 |
| src/Integrations/CrestCreates.PluginSystem/Models/PluginManifest.cs | PluginManifest, PluginLoadResult, PluginState, PluginInfo | 4 | P2 |
| src/Persistence/CrestCreates.Data.Abstractions/Abstractions/IRepository.cs | IRepository, IRepository, IReadOnlyRepository, IReadOnlyRepository | 4 | P2 |
| src/Runtime/Workflow/CrestCreates.Workflow.Abstractions/InteractionTarget.cs | InteractionTarget, CapabilityTarget, HumanTaskTarget, SubWorkflowTarget | 4 | P2 |
| src/Tooling/CrestCreates.CodeGenerator/Authorization/AuthorizationAttributeGenerator.cs | AuthorizationConfig, AuthorizationAttributeGenerator, RoslynAuthorizationInjector, BatchAuthorizationGenerator | 4 | P2 |
| src/Framework/Infrastructure/CrestCreates.Validation/Modules/ValidationModule.cs | ValidationModule, IValidationService, ValidationService | 3 | P1 |
| src/Framework/Modules/CrestCreates.Scheduling.Abstractions/Jobs/IJob.cs | IJob, IJob, NoArgs | 3 | P1 |
| src/Framework/Modules/CrestCreates.Scheduling.Abstractions/Jobs/JobRecord.cs | IJobRecord, JobExecutionResult, JobRecord | 3 | P1 |
| src/Framework/Web/CrestCreates.AspNetCore.Authentication.OpenIddict/Handlers/PasswordGrantHandler.cs | IPasswordGrantHandler, PasswordGrantResult, PasswordGrantHandlerImpl | 3 | P1 |
| src/Framework/Web/CrestCreates.AspNetCore.Authentication.OpenIddict/Handlers/RefreshTokenGrantHandler.cs | IRefreshTokenGrantHandler, RefreshTokenGrantResult, RefreshTokenGrantHandlerImpl | 3 | P1 |
| src/Integrations/CrestCreates.PluginSystem/Services/PluginManager.cs | IPluginManager, PluginManager, PluginAssemblyLoadContext | 3 | P1 |
| src/Persistence/CrestCreates.Data.EFCore/MultiTenancy/TenantConnectionStringResolver.cs | TenantConnectionStringResolver, ITenantConnectionStringResolver, TenantDbContextFactory | 3 | P1 |
| src/Runtime/Capability/CrestCreates.Capability.Abstractions/IPipelineMetrics.cs | IPipelineMetrics, PipelineMetricsSnapshot, PerCapabilityMetrics | 3 | P1 |
| src/Runtime/DistributedTransaction/CrestCreates.DistributedTransaction/Extensions/UnitOfWorkIntegrationExtensions.cs | UnitOfWorkIntegrationExtensions, ITransactionParticipantRegistry, UnitOfWorkTransactionParticipant | 3 | P1 |
| src/Runtime/Eventing/CrestCreates.Event.Abstractions/IEventValidator.cs | IEventValidator, ValidationResult, EventValidationError | 3 | P1 |
| src/Runtime/Eventing/CrestCreates.EventBus.Abstract/TenantEventContext.cs | TenantEventContext, ITenantEventContextAccessor, TenantEventContextAccessor | 3 | P1 |
| src/Runtime/Eventing/CrestCreates.EventBus.Abstractions/ILocalDeadLetterManager.cs | DeadLetterStats, DeadLetterRetryResult, ILocalDeadLetterManager | 3 | P1 |
| src/Framework/Ddd/CrestCreates.Application.Contracts/DTOs/Tenants/TenantConnectionStringDto.cs | TenantConnectionStringDto, CreateTenantConnectionStringDto, UpdateTenantConnectionStringDto | 3 | P2 |
| src/Framework/Ddd/CrestCreates.Application/Tenants/TenantDomainAppService.cs | TenantDomainAppService, TenantDomainMappingDto, CreateTenantDomainMappingDto | 3 | P2 |
| src/Framework/Infrastructure/CrestCreates.Infrastructure/Authorization/AuthorizationExtensions.cs | AuthorizationOptions, AuthorizationServiceCollectionExtensions, PermissionDefinitionExtensions | 3 | P2 |
| src/Framework/Infrastructure/CrestCreates.MultiTenancy/Middleware/MultiTenancyMiddleware.cs | MultiTenancyMiddleware, CompositeTenantResolver, TenantBoundaryMiddleware | 3 | P2 |
| src/Framework/Infrastructure/CrestCreates.MultiTenancy/MultiTenancyOptions.cs | MultiTenancyOptions, TenantResolutionStrategy, TenantIsolationStrategy | 3 | P2 |

## Type Name Collisions Across Projects

These are public types with the same name in different projects, which can confuse LLM tooling.
AssemblyMarker is excluded (intentional per-project markers).

| Type Name | Locations |
|---|---|
| AuthorizationServiceCollectionExtensions | `CrestCreates.Authorization: src/Framework/Infrastructure/CrestCreates.Authorization/AuthorizationServiceCollectionExtensions.cs`; `CrestCreates.Infrastructure: src/Framework/Infrastructure/CrestCreates.Infrastructure/Authorization/AuthorizationExtensions.cs` |
| CacheOptions | `CrestCreates.Aop.Abstractions: src/Framework/Infrastructure/CrestCreates.Aop.Abstractions/Options/CacheOptions.cs`; `CrestCreates.Caching.Abstractions: src/Framework/Infrastructure/CrestCreates.Caching.Abstractions/CacheOptions.cs` |
| CrestPermissionException | `CrestCreates.Domain: src/Framework/Ddd/CrestCreates.Domain/Exceptions/CrestPermissionException.cs`; `CrestCreates.Authorization.Abstractions: src/Framework/Infrastructure/CrestCreates.Authorization.Abstractions/CrestPermissionException.cs` |
| DataPermissionFilter | `CrestCreates.Infrastructure: src/Framework/Infrastructure/CrestCreates.Infrastructure/DataFilter/DataPermissionFilter.cs`; `CrestCreates.Organization.Abstractions: src/Framework/Modules/CrestCreates.Organization.Abstractions/DataPermissionFilter.cs` |
| DraftQuery | `CrestCreates.DescriptorDraft.Abstractions: src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/DraftQuery.cs`; `CrestCreates.Draft.Abstractions: src/Metadata/Draft/CrestCreates.Draft.Abstractions/DraftQuery.cs` |
| IMultiTenant | `CrestCreates.DataFilter: src/Framework/Infrastructure/CrestCreates.DataFilter/Entities/IMultiTenant.cs`; `CrestCreates.Data.EFCore: src/Persistence/CrestCreates.Data.EFCore/MultiTenancy/MultiTenancyDiscriminator.cs` |
| IOrganizationHierarchyService | `CrestCreates.Infrastructure: src/Framework/Infrastructure/CrestCreates.Infrastructure/Authorization/IOrganizationHierarchyService.cs`; `CrestCreates.Organization.Abstractions: src/Framework/Modules/CrestCreates.Organization.Abstractions/IOrganizationHierarchyService.cs` |
| IRepository | `CrestCreates.Domain: src/Framework/Ddd/CrestCreates.Domain/Repositories/IRepository.cs`; `CrestCreates.Data.Abstractions: src/Persistence/CrestCreates.Data.Abstractions/Abstractions/IRepository.cs`; `CrestCreates.Data.Abstractions: src/Persistence/CrestCreates.Data.Abstractions/Abstractions/IRepository.cs` |
| ISoftDelete | `CrestCreates.Domain.Shared: src/Framework/Ddd/CrestCreates.Domain.Shared/Entities/Auditing/ISoftDelete.cs`; `CrestCreates.Data.Abstractions: src/Persistence/CrestCreates.Data.Abstractions/Abstractions/IEntity.cs` |
| IUnitOfWorkFactory | `CrestCreates.Infrastructure: src/Framework/Infrastructure/CrestCreates.Infrastructure/UnitOfWork/UnitOfWorkFactory.cs`; `CrestCreates.Data.Abstractions: src/Persistence/CrestCreates.Data.Abstractions/Abstractions/IUnitOfWorkFactory.cs` |
| IUnitOfWorkManager | `CrestCreates.Infrastructure: src/Framework/Infrastructure/CrestCreates.Infrastructure/UnitOfWork/UnitOfWorkFactory.cs`; `CrestCreates.Data.Abstractions: src/Persistence/CrestCreates.Data.Abstractions/Abstractions/IUnitOfWorkManager.cs` |
| MultiTenantEntity | `CrestCreates.DataFilter: src/Framework/Infrastructure/CrestCreates.DataFilter/Entities/MultiTenantEntity.cs`; `CrestCreates.Data.EFCore: src/Persistence/CrestCreates.Data.EFCore/MultiTenancy/MultiTenancyDiscriminator.cs`; `CrestCreates.Data.EFCore: src/Persistence/CrestCreates.Data.EFCore/MultiTenancy/MultiTenancyDiscriminator.cs` |
| OrmProvider | `CrestCreates.Domain.Shared: src/Framework/Ddd/CrestCreates.Domain.Shared/Enums/OrmProvider.cs`; `CrestCreates.Data.Abstractions: src/Persistence/CrestCreates.Data.Abstractions/OrmProvider.cs` |
| PagedResult | `CrestCreates.Domain.Shared: src/Framework/Ddd/CrestCreates.Domain.Shared/DTOs/PagedResult.cs`; `CrestCreates.DbContextProvider.Abstract: src/Persistence/CrestCreates.DbContextProvider.Abstract/IQueryableBuilder.cs` |
| RegistryState | `CrestCreates.Metadata.Abstractions: src/Metadata/CrestCreates.Metadata.Abstractions/RegistryState.cs`; `CrestCreates.Event.Abstractions: src/Runtime/Eventing/CrestCreates.Event.Abstractions/RegistryState.cs` |
| ServiceCollectionExtensions | `CrestCreates.Aop: src/Framework/Infrastructure/CrestCreates.Aop/Extensions/ServiceCollectionExtensions.cs`; `CrestCreates.Scheduling.Quartz: src/Framework/Modules/CrestCreates.Scheduling.Quartz/Services/ServiceCollectionExtensions.cs` |
| TenantInitializationResult | `CrestCreates.Application.Contracts: src/Framework/Ddd/CrestCreates.Application.Contracts/DTOs/Tenants/TenantInitializationResult.cs`; `CrestCreates.MultiTenancy.Abstract: src/Framework/Infrastructure/CrestCreates.MultiTenancy.Abstract/TenantInitializationResult.cs` |
| TenantInitializationStep | `CrestCreates.Application.Contracts: src/Framework/Ddd/CrestCreates.Application.Contracts/DTOs/Tenants/TenantInitializationResult.cs`; `CrestCreates.MultiTenancy.Abstract: src/Framework/Infrastructure/CrestCreates.MultiTenancy.Abstract/TenantInitializationStep.cs` |
| TenantInitializationStepStatus | `CrestCreates.Domain.Shared: src/Framework/Ddd/CrestCreates.Domain.Shared/TenantInitializationStepStatus.cs`; `CrestCreates.MultiTenancy.Abstract: src/Framework/Infrastructure/CrestCreates.MultiTenancy.Abstract/TenantInitializationStep.cs` |
| TenantManagementServiceCollectionExtensions | `CrestCreates.Application: src/Framework/Ddd/CrestCreates.Application/Tenants/TenantManagementServiceCollectionExtensions.cs`; `CrestCreates.MultiTenancy: src/Framework/Infrastructure/CrestCreates.MultiTenancy/TenantManagementServiceCollectionExtensions.cs` |
| TransactionPropagation | `CrestCreates.Data.FreeSql: src/Persistence/CrestCreates.Data.FreeSql/UnitOfWork/FreeSqlUnitOfWorkManager.cs`; `CrestCreates.DbContextProvider.Abstract: src/Persistence/CrestCreates.DbContextProvider.Abstract/TransactionPropagation.cs` |
| UnitOfWorkFactory | `CrestCreates.Infrastructure: src/Framework/Infrastructure/CrestCreates.Infrastructure/UnitOfWork/UnitOfWorkFactory.cs`; `CrestCreates.Data.Abstractions: src/Persistence/CrestCreates.Data.Abstractions/UnitOfWorkBase/UnitOfWorkFactory.cs` |
| UnitOfWorkManager | `CrestCreates.Infrastructure: src/Framework/Infrastructure/CrestCreates.Infrastructure/UnitOfWork/UnitOfWorkFactory.cs`; `CrestCreates.Data.Abstractions: src/Persistence/CrestCreates.Data.Abstractions/UnitOfWorkBase/UnitOfWorkManager.cs` |
| UnitOfWorkOptions | `CrestCreates.Aop.Abstractions: src/Framework/Infrastructure/CrestCreates.Aop.Abstractions/Options/UnitOfWorkOptions.cs`; `CrestCreates.Data.Abstractions: src/Persistence/CrestCreates.Data.Abstractions/Abstractions/IUnitOfWorkEnhanced.cs` |
| ValidationResult | `CrestCreates.Validation: src/Framework/Infrastructure/CrestCreates.Validation/Validators/ValidatorBase.cs`; `CrestCreates.Event.Abstractions: src/Runtime/Eventing/CrestCreates.Event.Abstractions/IEventValidator.cs` |
| WorkflowRelationshipExtractor | `CrestCreates.Metadata: src/Metadata/CrestCreates.Metadata/WorkflowRelationshipExtractor.cs`; `CrestCreates.Workflow: src/Runtime/Workflow/CrestCreates.Workflow/WorkflowRelationshipExtractor.cs` |

## Move/Rename Execution Plan for Claude Code

This plan is organized into batches by risk level. Each batch should be committed separately.
After each batch, run `dotnet build` to verify no compilation errors.

### Batch 1: P0 — Extract Interface from Mixed File (file named as class, contains interface first)

These files have the interface as primary public type but the file is named after a different thing.
Action: Create new file for the interface, update the original file to only contain the class.

```bash
# Example: PasswordGrantHandler.cs contains IPasswordGrantHandler + PasswordGrantResult + PasswordGrantHandlerImpl
# 1. Create IPasswordGrantHandler.cs with interface + result type
# 2. Keep PasswordGrantHandler.cs with only the implementation
# 3. Update usings if namespace changes
```

Files to split:
1. `src/Framework/Web/.../Handlers/PasswordGrantHandler.cs` → extract `IPasswordGrantHandler` + `PasswordGrantResult` to `IPasswordGrantHandler.cs`
2. `src/Framework/Web/.../Handlers/RefreshTokenGrantHandler.cs` → extract `IRefreshTokenGrantHandler` + `RefreshTokenGrantResult` to `IRefreshTokenGrantHandler.cs`
3. `src/Framework/Infrastructure/.../Authorization/IIdentityClaimsBuilder.cs` → rename to `IdentityClaimsContext.cs` or split interface out
4. `src/Framework/Infrastructure/.../ConnectionStringProtector.cs` → extract `IConnectionStringProtector` to `IConnectionStringProtector.cs`
5. `src/Framework/Infrastructure/.../Localization/ResourceManagerLocalizationProvider.cs` → extract `ILocalizationProvider` to `ILocalizationProvider.cs`
6. `src/Framework/Modules/.../Jobs/JobRecord.cs` → extract `IJobRecord` to `IJobRecord.cs`
7. `src/Integrations/.../Services/PluginManager.cs` → extract `IPluginManager` to `IPluginManager.cs`
8. `src/Runtime/Eventing/.../ILocalDeadLetterManager.cs` → extract `DeadLetterStats` + `DeadLetterRetryResult` to own files

### Batch 2: P0 — Move Default Implementations Out of Abstractions Projects

```bash
# DefaultJobExecutionHandler is in CrestCreates.Scheduling.Abstractions
# Move to CrestCreates.Scheduling (non-abstractions sibling)
git mv src/Framework/Modules/CrestCreates.Scheduling.Abstractions/Services/DefaultJobExecutionHandler.cs \
       src/Framework/Modules/CrestCreates.Scheduling/Services/DefaultJobExecutionHandler.cs
# Update namespace: CrestCreates.Scheduling.Services (remove .Abstractions from project)

# DefaultJobFailureHandler is co-located in IJobFailureHandler.cs
# Extract to own file in non-abstractions project

# DefaultCurrentUserProvider is in CrestCreates.Data.Abstractions/Abstractions/ICurrentUserProvider.cs
# Extract to CrestCreates.Data.Core/ or similar implementation project
```

Verify: `dotnet build`

### Batch 3: P1 — Fix File Name / Type Name Mismatches (no namespace change)

Pure renames where file name should match the primary public type:

```bash
# Rename file only, no namespace change needed
git mv "src/Framework/Api/CrestCreates.DynamicApi/DynamicApiDescriptors.cs" \
       "src/Framework/Api/CrestCreates.DynamicApi/DynamicApiServiceDescriptor.cs"
# WARNING: DynamicApiDescriptors.cs has 9 types — this needs splitting first

git mv "src/Framework/Api/CrestCreates.DynamicApi/GeneratedDynamicApiRuntime.cs" \
       "src/Framework/Api/CrestCreates.DynamicApi/DynamicApiGeneratedRuntime.cs"

git mv "src/Framework/Ddd/CrestCreates.Application.Contracts/DTOs/AuditLog/CleanupAuditLogsDto.cs" \
       "src/Framework/Ddd/CrestCreates.Application.Contracts/DTOs/AuditLog/CleanupAuditLogsRequestDto.cs"
# WARNING: Contains 2 types — split first

git mv "src/Framework/Infrastructure/CrestCreates.MultiTenancy/TenantResolutionResult.cs" \
       "src/Framework/Infrastructure/CrestCreates.MultiTenancy/TenantResolutionResultExtensions.cs"
# WARNING: Contains interface + extension class — split first

git mv "src/Framework/Infrastructure/CrestCreates.Validation/Localization/ValidationLocalization.cs" \
       "src/Framework/Infrastructure/CrestCreates.Validation/Localization/ValidationErrorCodes.cs"
# WARNING: Contains 2 types — split first

git mv "src/Metadata/CrestCreates.Metadata.Abstractions/ValidationIssue.cs" \
       "src/Metadata/CrestCreates.Metadata.Abstractions/ValidationSeverity.cs"
# WARNING: Contains enum + record — split first

git mv "src/Persistence/CrestCreates.Data.Abstractions/Abstractions/IEntity.cs" \
       "src/Persistence/CrestCreates.Data.Abstractions/Abstractions/ISoftDelete.cs"
# WARNING: File is IEntity.cs but primary type is ISoftDelete — verify this is correct

# Simple renames (single type, just wrong name):
git mv "src/Framework/Infrastructure/CrestCreates.MultiTenancy/ConnectionStringProtector.cs" \
       "src/Framework/Infrastructure/CrestCreates.MultiTenancy/IConnectionStringProtector.cs"
# Only after extracting interface (Batch 1)
```

After each rename: search for file references in .csproj (if explicitly listed) and update.
Run `dotnet build` after each batch.

### Batch 4: P1 — Split Multi-Type Files

Files with 3+ types or mixed interface+class that should be split:

High priority splits:
1. `DynamicApiDescriptors.cs` (9 types) → individual files per type
2. `ProductDtos.cs` (5 DTOs) → split or keep as group (DTO groups are acceptable)
3. `TenantDiagnosticsDto.cs` (6 types) → split supporting types out
4. `AuthorizationAttributes.cs` (5 types) → split per attribute/requirement
5. `UnitOfWorkFactory.cs` (6 types!) → split into UnitOfWorkFactory.cs, UnitOfWorkManager.cs, OrmOptions.cs
6. `MultiTenancyDiscriminator.cs` (6 types) → split into MultiTenantEntity.cs, TenantFilterRegistryStore.cs, etc.
7. `HealthReportJsonContext.cs` (5 types) → split per type
8. `CommonHealthChecks.cs` (3 health checks) → one file per health check
9. `ScanEntityPermissions.cs` (3 types) → split manifest types out
10. `AuthorizationAttributeGenerator.cs` (4 types) → split config/batch generator
11. `CanonicalHashModel.cs` (6 records) → split into individual model files
12. `ObjectMappingModel.cs` (5 types) → split
13. `AgentControlPlaneArtifactEntries.cs` (8 records!) → split per snapshot type

After splitting: run `dotnet build` and `dotnet test`.

### Batch 5: P2 — Namespace/Path Alignment (high-value only)

Only fix the most confusing namespace mismatches:

1. `CrestCreates.DynamicApi.GeneratedApi/` — files use namespace `CrestCreates.DynamicApi` instead of `CrestCreates.DynamicApi.GeneratedApi`. Consider adding `.GeneratedApi` to namespace.
2. `CrestCreates.Scheduling.Abstractions/Jobs/` — files use `CrestCreates.Scheduling.Jobs` instead of `CrestCreates.Scheduling.Abstractions.Jobs`. Pick one convention.
3. `CrestCreates.Scheduling.Abstractions/Services/` — same issue, `CrestCreates.Scheduling.Services` vs `.Abstractions.Services`.
4. `CrestCreates.Data.Abstractions/Abstractions/` — double-Abstractions path, namespace is just `CrestCreates.Data.Abstractions`. Consider moving files up one level.
5. `CrestCreates.Agent.ControlPlane.Abstractions/ToolDtos/` — files use root namespace, should be `.ToolDtos`.

These require namespace change + global using update. Do one at a time, build, verify.

### Batch 6: P2 — InMemory Implementation Relocation (optional)

Move InMemory implementations to a `Stores/InMemory/` subdirectory within each project:

```bash
# Example for Capability project:
mkdir -p src/Runtime/Capability/CrestCreates.Capability/Stores/InMemory/
git mv src/Runtime/Capability/CrestCreates.Capability/InMemoryCapabilityAuditStore.cs \
       src/Runtime/Capability/CrestCreates.Capability/Stores/InMemory/
git mv src/Runtime/Capability/CrestCreates.Capability/InMemoryIdempotenceStore.cs \
       src/Runtime/Capability/CrestCreates.Capability/Stores/InMemory/
git mv src/Runtime/Capability/CrestCreates.Capability/InMemoryPipelineMetrics.cs \
       src/Runtime/Capability/CrestCreates.Capability/Stores/InMemory/
git mv src/Runtime/Capability/CrestCreates.Capability/InMemoryRateLimitStore.cs \
       src/Runtime/Capability/CrestCreates.Capability/Stores/InMemory/
# Update namespaces to CrestCreates.Capability.Stores.InMemory
```

Repeat for: HumanTask, Workflow, EventBus.Local, EventBus.EventStore, Agent.ControlPlane, DescriptorDraft, Draft.

## Do Not Touch

1. **AssemblyMarker.cs files** — Intentional per-project assembly markers. Same name across projects is by design.
2. **IsExternalInit.cs** (CodeGenerator) — .NET polyfill for netstandard2.0. Must stay as-is.
3. **DDD Layer interfaces** (Domain/, Domain.Shared/) — Interfaces like `IRepository`, `ISoftDelete`, `IAggregateRoot` in Domain projects are standard DDD practice, NOT misplaced abstractions.
4. **Application.Contracts/DTOs/ProductDtos.cs** — Example/sample DTOs, acceptable grouping.
5. **Generated code directories** (GeneratedApi/, Generated/) — These contain code-generator output patterns. Namespace flattening is intentional for generated API surface.
6. **Test files** — Not in scope of this audit.
7. **sample/ projects** — Not in scope.
8. **Type collisions that are intentional re-exports** — e.g., `IRepository` in Domain and Data.Abstractions may be intentional layering (Domain defines contract, Data defines persistence contract). Verify before renaming.
9. **CacheOptions in both Aop.Abstractions and Caching.Abstractions** — May be intentional: AOP cache vs application cache are different concepts.
10. **UnitOfWorkFactory/Manager in both Infrastructure and Data.Abstractions** — Intentional: Infrastructure has runtime implementation, Data.Abstractions has the abstract contract.
11. **ServiceCollectionExtensions** with same name in different projects — This is a standard .NET naming pattern for DI extension methods. Do not rename.
12. **TenantInitializationResult/Step** duplicates — May be intentional: Application.Contracts has the DTO version, MultiTenancy.Abstract has the domain version. Verify before merging.
13. **ValidationResult in both Validation and Event.Abstractions** — Different domains (input validation vs event validation). Do not merge.