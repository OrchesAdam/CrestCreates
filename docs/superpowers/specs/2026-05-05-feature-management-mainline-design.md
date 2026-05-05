# Feature Management Mainline — Design Spec

**Feature plan**: `docs/review/feature-plans/feature-management-mainline.xml`
**Date**: 2026-05-05
**Status**: draft

> This spec supersedes `docs/review/feature-plans/feature-management-mainline.xml` for implementation details. The XML feature plan describes goals and review checklist; this document defines the concrete architecture, data flow, security rules, error handling, audit requirements, and testing strategy.

## Decision Summary

| Decision | Choice |
|----------|--------|
| Storage | Keep independent `FeatureValue` storage. Do not store feature flags in `SettingValue`. |
| Scope | Support `Global` and `Tenant` only. No user-level override in this phase. |
| Resolution order | `Tenant override -> Global override -> FeatureDefinition default`. |
| Runtime read API | `IFeatureProvider` and `IFeatureChecker`. |
| Write API | `IFeatureManager` is the only write entry point. App services and seeders do not write repositories directly. |
| Dynamic API | Generated Dynamic API mainline only. Do not add runtime scanner/executor support. |
| Tenant identity | Use `CurrentTenant.Id` or explicit `tenantId`; never use `TenantName` as the key. |
| Cache | Cache by `scope + providerKey + tenantId + featureName`; writes must invalidate affected item and scope caches. |
| Audit | Feature writes and removals are mandatory audit events. |
| Advanced rollout | Out of scope: percentage rollout, rule engine, organization/user targeting, remote config center. |

## 1. Architecture & Components

### Main Components

| Component | Layer | Responsibility |
|-----------|-------|----------------|
| `FeatureDefinition` / `FeatureDefinitionGroup` | `Domain.Shared` | Immutable feature metadata: name, display text, default value, value type, supported scopes. |
| `IFeatureDefinitionProvider` | `Domain` | Module extension point for registering feature definitions. |
| `FeatureDefinitionManager` | `Domain` | Aggregates providers, validates duplicates, exposes definitions and groups. |
| `FeatureValue` | `Domain` | Persisted override value for one feature at one scope. |
| `IFeatureRepository` | `Domain` | Persistence abstraction for `FeatureValue`. |
| `FeatureStore` | `Application` | Cached read/write-adjacent store over repository data. |
| `FeatureValueResolver` | `Application` | Applies fallback order and returns resolved values with source metadata. |
| `FeatureProvider` | `Application` | Runtime value reader for current context. |
| `FeatureChecker` | `Application` | Boolean convenience reader for capability checks. |
| `FeatureManager` | `Application` | Validates and writes global/tenant overrides, removes overrides, invalidates cache, emits audit. |
| `FeatureAppService` | `Application` | Generated Dynamic API surface for reading and managing feature values. |
| `FeatureDefinitionAppService` | `Application` | Generated Dynamic API surface for definition/group queries. |
| `FeatureRepository` | `OrmProviders.EFCore` | EF Core implementation of `IFeatureRepository`. |
| `TenantFeatureDefaultsSeeder` | `Application` | Tenant initialization phase hook for feature defaults. |

### Boundaries

Feature Management and Setting Management are intentionally separate:

| Concern | Owner |
|---------|-------|
| Capability on/off, quotas, feature gates | Feature Management |
| Runtime configuration, display options, integration keys, encrypted values | Setting Management |
| Permissions and user authority | Permission/Authorization mainline |

`FeatureChecker` must not become a permission checker. A disabled feature can block a capability, but a feature being enabled does not grant permission by itself.

## 2. Definition Model

`FeatureDefinition` must expose:

| Field | Required | Meaning |
|-------|----------|---------|
| `Name` | Yes | Stable unique key, for example `Identity.UserCreationEnabled`. |
| `DisplayName` | Yes | Human-readable label. |
| `Description` | No | Optional explanation. |
| `GroupName` | Yes | Group key used by definition APIs. |
| `DefaultValue` | Yes | Value used when no override exists. |
| `ValueType` | Yes | `Bool`, `Int`, or `String`. |
| `Scopes` | Yes | Supported write scopes: `Global`, `Tenant`, or both. |

Rules:

- Duplicate feature names are invalid. `FeatureDefinitionManager` must fail fast rather than silently overriding a definition.
- `DefaultValue` must be normalized according to `ValueType`.
- A write to an unsupported scope must fail before touching storage.
- Feature names are case-insensitive for lookup, but original casing is preserved in definitions and persisted values.

## 3. Data Model

### `FeatureValue`

| Field | Rule |
|-------|------|
| `Name` | Must reference a defined feature. |
| `Value` | Normalized string value validated against the definition's `ValueType`. |
| `Scope` | `Global` or `Tenant`. |
| `ProviderKey` | Empty for global; tenant id for tenant scope. |
| `TenantId` | Null for global; tenant id for tenant scope. |
| `ConcurrencyStamp` | Required if the entity inherits concurrency support. |

EF Core mapping must enforce a unique key equivalent to:

```text
Name + Scope + ProviderKey + TenantId
```

This protects `SetTenantAsync` / `SetGlobalAsync` from duplicate rows under concurrency.

## 4. Data Flow

### 4.1 Runtime Read

```
Caller
  -> IFeatureChecker.IsEnabledAsync(name)
  -> IFeatureProvider.GetAsync<bool>(name)
  -> IFeatureValueResolver.ResolveAsync(name, CurrentTenant.Id)
       -> FeatureStore.GetListAsync(Global, "")
       -> FeatureStore.GetListAsync(Tenant, tenantId)  // only when tenant id exists
       -> pick Tenant override
       -> else pick Global override
       -> else use FeatureDefinition default
```

The resolved value includes `Scope`, `ProviderKey`, and `TenantId` so the caller can tell whether the value came from a tenant override, global override, or default.

### 4.2 Write Global Override

```
FeatureAppService.SetGlobalAsync(name, value)
  -> permission check: FeatureManagement.ManageGlobal
  -> FeatureManager.SetGlobalAsync(name, value)
       -> load definition
       -> validate scope supports Global
       -> normalize value
       -> find FeatureValue(Name, Global, "", null)
       -> insert or update
       -> invalidate global item and global scope cache
       -> write audit event
```

### 4.3 Write Tenant Override

```
FeatureAppService.SetTenantAsync(name, tenantId, value)
  -> permission check: FeatureManagement.ManageTenant
  -> host/cross-tenant guard
  -> FeatureManager.SetTenantAsync(name, tenantId, value)
       -> load definition
       -> validate scope supports Tenant
       -> normalize value
       -> find FeatureValue(Name, Tenant, tenantId, tenantId)
       -> insert or update
       -> invalidate that tenant's item and scope cache
       -> write audit event
```

### 4.4 Remove Override

`RemoveGlobalAsync` deletes only the global override. The next read falls back to the definition default unless a tenant override exists.

`RemoveTenantAsync` deletes only that tenant's override. The next tenant read falls back to global override, then definition default.

### 4.5 Resolve All

`ResolveAllAsync(groupName, tenantId)` returns all definitions in the group, not just persisted overrides. Every returned item must include:

- feature name
- resolved value
- source scope (`Tenant`, `Global`, or null for definition default)
- provider key
- tenant id

## 5. Generated Dynamic API Mainline

Feature APIs must be available through generated Dynamic API endpoints. The implementation must not add behavior to `DynamicApiScanner`, `DynamicApiEndpointExecutor`, or runtime reflection fallback.

Required app service surface:

| API | Purpose | Caller |
|-----|---------|--------|
| `FeatureDefinitionAppService.GetGroupsAsync` | List groups and definitions. | Host and tenant users with read permission. |
| `FeatureAppService.GetGlobalValuesAsync` | View global overrides. | Host only. |
| `FeatureAppService.GetTenantValuesAsync(tenantId)` | View one tenant's overrides. | Host only. |
| `FeatureAppService.GetCurrentTenantValuesAsync` | View current tenant resolved values. | Current tenant. |
| `FeatureAppService.SetGlobalAsync` | Write global override. | Host only. |
| `FeatureAppService.SetTenantAsync` | Write tenant override. | Host only. |
| `FeatureAppService.RemoveGlobalAsync` | Remove global override. | Host only. |
| `FeatureAppService.RemoveTenantAsync` | Remove tenant override. | Host only. |
| `FeatureAppService.IsEnabledAsync` | Check current context boolean feature. | Current caller. |
| `FeatureAppService.IsTenantEnabledAsync` | Check explicit tenant boolean feature. | Host only. |

Contracts must live where the source generator can discover them. Tests must verify generated endpoints work with runtime reflection disabled.

## 6. Security Rules

| Scenario | Rule |
|----------|------|
| Host sets global feature | Allowed with `FeatureManagement.ManageGlobal`. |
| Host sets tenant feature | Allowed with `FeatureManagement.ManageTenant`. |
| Tenant user reads current tenant values | Allowed with read permission and current tenant context. |
| Tenant user passes arbitrary `tenantId` | Forbidden. |
| Anonymous user writes feature values | Forbidden. |
| Feature enabled but permission missing | Still forbidden. Feature does not grant permission. |

The app service must not trust caller-provided `tenantId` unless the caller is host-level or explicitly authorized for tenant management.

## 7. Tenant Initialization

`TenantFeatureDefaultsSeeder` remains a phase in `TenantInitializationOrchestrator`.

Rules:

- The seeder resolves `IFeatureManager` from a scope where `CurrentTenant` is already set to the tenant being initialized.
- Default behavior is lazy fallback to `FeatureDefinition.DefaultValue`.
- The seeder writes tenant overrides only for definitions that intentionally require an explicit tenant value.
- The seeder is idempotent: retrying tenant initialization must not create duplicate `FeatureValue` rows.
- Seeder failure is recorded in `TenantInitializationRecord` as the `FeatureDefaults` step and marks the tenant initialization as failed.

This flow must work for shared-database and independent-database tenants.

## 8. Cache Behavior

Cache key inputs:

```text
scope
providerKey
tenantId
featureName
```

Invalidation rules:

| Operation | Invalidate |
|-----------|------------|
| `SetGlobalAsync` | Global item cache and global scope list cache. |
| `RemoveGlobalAsync` | Global item cache and global scope list cache. |
| `SetTenantAsync` | That tenant's item cache and tenant scope list cache. |
| `RemoveTenantAsync` | That tenant's item cache and tenant scope list cache. |

Changing tenant A must not clear tenant B's cache. Cross-instance cache synchronization is out of scope for this spec and belongs to the caching plan; this spec requires correct local cache invalidation.

## 9. Error Handling

Feature Management errors use the global exception handling mainline.

| Error | Expected behavior |
|-------|-------------------|
| Undefined feature | Business error with clear code/message. |
| Invalid value for `ValueType` | Business error, no write. |
| Unsupported scope | Business error, no write. |
| Cross-tenant management attempt | 403 forbidden. |
| Missing permission | 403 forbidden. |
| Repository concurrency conflict | 409 conflict if `FeatureValue` uses concurrency stamps. |

Public errors must not include stack traces, SQL, or connection strings.

## 10. Audit Requirements

Feature writes and removals are mandatory audit events.

Each audit record should include:

| Field | Meaning |
|-------|---------|
| `FeatureName` | Feature being changed. |
| `Scope` | `Global` or `Tenant`. |
| `TenantId` | Target tenant id for tenant scope. |
| `OldValue` | Previous override value, null if none. |
| `NewValue` | New override value, null for remove. |
| `Operation` | Set or Remove. |
| `OperatorUserId` | Current user id when available. |
| `CorrelationId` | Request correlation id when available. |

Use the existing AuditLogging mainline. Do not create a side-channel audit table for Feature Management.

## 11. Testing Strategy

### Unit Tests

| Test | Verifies |
|------|----------|
| `FeatureDefinitionManager_WithDuplicateName_ShouldFail` | Duplicate definitions are rejected. |
| `FeatureValueResolver_ShouldUseTenantThenGlobalThenDefault` | Fallback order is stable. |
| `RemoveTenantFeature_ShouldFallbackToGlobal` | Tenant removal fallback. |
| `RemoveGlobalFeature_ShouldFallbackToDefault` | Global removal fallback. |
| `SetTenantFeature_ShouldInvalidateOnlyThatTenantCache` | Tenant cache isolation. |
| `SetGlobalFeature_ShouldInvalidateGlobalCache` | Global cache invalidation. |
| `UnknownFeature_ShouldThrowBusinessException` | Undefined feature error. |
| `InvalidFeatureValue_ShouldBeRejected` | Value type validation. |
| `TenantFeatureDefaultsSeeder_ShouldBeIdempotent` | Tenant initialization retry safety. |

### Integration Tests

| Test | Verifies |
|------|----------|
| `FeatureDynamicApi_ShouldWorkOnGeneratedPath` | Generated Dynamic API endpoints work without runtime fallback. |
| `TenantUser_ShouldNotManageOtherTenantFeature` | Cross-tenant protection. |
| `FeatureControlsRealCapability_TenantOverridesGlobal` | Feature value actually gates a real capability. |
| `FeatureChange_ShouldWriteAuditLog` | Feature write/remove produces audit records. |
| `TenantInitialization_ShouldRunFeatureDefaultsStep` | Tenant initialization includes feature defaults and records failure. |

### Regression Tests

Add regression coverage for these previously fragile areas:

- Test projects must not accidentally generate duplicate Dynamic API providers for sample services.
- Tenant feature reads must use tenant id, not tenant name.
- Removing overrides must invalidate cached resolved values.

## 12. Acceptance Criteria

1. Feature resolution order is stable: tenant override, then global override, then definition default.
2. Undefined feature reads/writes return a clear business error.
3. Invalid Bool/Int values are rejected before persistence.
4. Deleting a tenant override falls back to global; deleting global falls back to default.
5. Feature writes invalidate the correct cache entries and do not affect unrelated tenants.
6. Host can manage global and tenant features with the correct permissions.
7. Tenant users cannot manage another tenant's feature values.
8. Generated Dynamic API exposes Feature APIs without runtime scanner/executor support.
9. Tenant initialization runs the `FeatureDefaults` phase and records failures.
10. Feature writes and removals are recorded by AuditLogging.

