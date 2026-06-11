# Phase 5d Design: Capability Authorization Bridge

**Date**: 2026-06-11
**Status**: Draft
**Scope**: Bridge Capability Runtime to existing Authorization main chain via `IPermissionChecker`.

---

## 1. Motivation

The Capability Runtime currently has no authorization. `AuthorizationMiddleware` exists in the pipeline but its `_authService` is always `null` because `ICapabilityAuthorizationService` is never registered by `AddCapabilityRuntime`. Even if a consumer manually registers an implementation, the current `ICapabilityAuthorizationService.AuthorizeAsync(string, string?, CancellationToken)` receives only `capabilityName` and `userId` — no access to the permissions declared on the `CapabilityDescriptor`.

This phase bridges that gap by:

1. Making `CapabilityDescriptor.Permissions` flow through the pipeline into the authorization service.
2. Providing a default `ICapabilityAuthorizationService` implementation that delegates to the existing `IPermissionChecker`.
3. Registering it automatically so authorization is not silently skipped in production.

**Key principle**: This phase does NOT create a second permission system. It reuses the existing RBAC chain (`IPermissionChecker` → `PermissionGrantManager` → `PermissionGrantStore`) exactly as-is.

---

## 2. Design Decisions

### 2.1 Permission source: `CapabilityDescriptor.Permissions`

`CapabilityDescriptor` already has `IReadOnlyList<string> Permissions`. This is the sole authorization source. No new fields are added to the descriptor. No `RequiredPermissions`, `AuthorizationPolicy`, or `Metadata["permission"]` patterns.

### 2.2 Context propagation: `RequiredPermissions` on `CapabilityExecutionContext`

The pipeline needs to carry permissions from the descriptor to the auth service. Two options considered:

| Option | Pro | Con |
|--------|-----|-----|
| `CapabilityDescriptor? Descriptor` property | Full context | Drags `CrestCreates.Metadata` into `Capability.Abstractions` |
| `IReadOnlyList<string> RequiredPermissions` property | Minimal, type-safe, no new dep | — |

**Chosen**: `IReadOnlyList<string> RequiredPermissions`. The auth service only needs permission names; carrying the entire descriptor into the abstractions layer would violate the dependency direction (`Metadata` → `Abstractions`, not the reverse).

### 2.3 Ordering guarantee: `RequiredPermissions` set AFTER `configureContext`

```csharp
// CapabilityPipeline.ExecuteAsync
var context = new CapabilityExecutionContext { ... };
configureContext?.Invoke(context);                         // caller runs first
context.RequiredPermissions = descriptor.Permissions;      // THEN framework sets
```

This prevents a `configureContext` callback from clearing permissions to bypass authorization. Locked by test.

### 2.4 Empty permissions = allow

When `descriptor.Permissions` is empty, authorization is a no-op (`return true`). This matches the existing behavior (no auth service → middleware skips) so capabilities that don't declare permissions continue to work without changes.

### 2.5 Non-empty permissions = ALL must be granted

`IPermissionChecker.IsGrantedAsync(string[])` returns `MultiplePermissionGrantResult` with an `AllGranted` property. We use this directly — if any required permission is denied, the entire capability is rejected.

### 2.6 Authorization service lifetime: Scoped

`IPermissionChecker` is registered as Scoped. `PermissionCapabilityAuthorizationService` depends on it, so it must also be Scoped. Singleton would cause captive dependency or resolution failure.

### 2.7 Runtime chain lifetime: Scoped (preexisting fix)

`ICapabilityPipeline` and `ICapabilityDispatcher` were registered as Singleton, but both depend on or resolve scoped services (`ITenantContext`, `ICurrentUser`, and now `ICapabilityAuthorizationService`). Under the Singleton registration:

- `TenantMiddleware` always gets `null` `ITenantContext` (Scoped service resolved from root provider).
- `CapabilityDispatcher` always gets `null` `ITenantContext` and `ICurrentUser` (same reason).
- `AuthorizationMiddleware` would never receive a real `ICapabilityAuthorizationService`.

**Fix**: Change both `ICapabilityPipeline` and `ICapabilityDispatcher` to `TryAddScoped`. This is safe because:

- `CapabilityStepExecutor` (Workflow, the only external pipeline consumer) is already Scoped.
- `ICapabilityDispatcher` has zero external consumers outside the Capability project.
- All middleware are Transient and resolved lazily at execution time from the pipeline's `IServiceProvider` — no captive dependency.
- Test code uses explicit `AddSingleton` which takes precedence over `TryAddScoped`.

### 2.8 What is NOT changed

- `IPermissionChecker` / `PermissionChecker` / `PermissionGrantManager` / `PermissionGrantStore` — consumed, not modified.
- `PermissionGrantProviderType.User` / `Role` — untouched.
- `CapabilityDescriptor` — `Permissions` field already exists, used as-is.
- Token / claims / `PermissionGrantProviderType` — no changes.
- Organization role (Phase 5c `UserOrganizationRoleAssignment`) — not wired to RBAC. It remains an organization-scoped identity fact only.
- `userId` parameter — kept for diagnostics; `IPermissionChecker` determines identity from ambient principal/current tenant, not from the passed `userId`.

---

## 3. Architecture

```
CapabilityPipeline.ExecuteAsync()
  │
  ├─ Resolves CapabilityDescriptor from registry
  ├─ Sets context.RequiredPermissions = descriptor.Permissions
  │
  └─ Middleware chain
       │
        ├─ TenantMiddleware (scoped lifetime fix enables ITenantContext when host registers it)
       ├─ AuthorizationMiddleware
       │    │
       │    └─ ICapabilityAuthorizationService.AuthorizeAsync(name, userId, permissions, ct)
       │         │
       │         └─ PermissionCapabilityAuthorizationService (default)
       │              │
       │              ├─ permissions empty? → return true
       │              └─ permissions non-empty?
       │                   └─ IPermissionChecker.IsGrantedAsync(permissions)
       │                        │
       │                        ├─ AllGranted → return true
       │                        └─ any denied → return false
       │                             └─ AuthorizationMiddleware → UNAUTHORIZED result
       │
       ├─ ValidationMiddleware
       └─ Handler
```

### Project dependency map

```
Capability.Abstractions          ← no new deps
  └─ CapabilityExecutionContext  [+RequiredPermissions]

Capability                       ← already refs Authorization.Abstractions
  ├─ PermissionCapabilityAuthorizationService  [NEW]
  ├─ CapabilityPipeline          [+1 line: populate RequiredPermissions]
  ├─ AuthorizationMiddleware     [pass RequiredPermissions]
  └─ CapabilityServiceCollectionExtensions  [Scoped lifetimes + register auth service]
```

No new project references needed.

---

## 4. Interface Changes

### 4.1 `CapabilityExecutionContext`

```csharp
// Addition (after existing properties)
public IReadOnlyList<string> RequiredPermissions { get; set; } = Array.Empty<string>();
```

### 4.2 `ICapabilityAuthorizationService`

```csharp
// Before:
Task<bool> AuthorizeAsync(string capabilityName, string? userId, CancellationToken ct);

// After:
Task<bool> AuthorizeAsync(
    string capabilityName,
    string? userId,
    IReadOnlyList<string> requiredPermissions,
    CancellationToken ct);
```

---

## 5. New Implementation

### 5.1 `PermissionCapabilityAuthorizationService`

Location: `framework/src/CrestCreates.Capability/PermissionCapabilityAuthorizationService.cs`

```csharp
namespace CrestCreates.Capability;

public sealed class PermissionCapabilityAuthorizationService : ICapabilityAuthorizationService
{
    private readonly IPermissionChecker _permissionChecker;

    public PermissionCapabilityAuthorizationService(IPermissionChecker permissionChecker)
    {
        _permissionChecker = permissionChecker;
    }

    public async Task<bool> AuthorizeAsync(
        string capabilityName,
        string? userId,
        IReadOnlyList<string> requiredPermissions,
        CancellationToken ct)
    {
        if (requiredPermissions.Count == 0)
            return true;

        var result = await _permissionChecker.IsGrantedAsync(requiredPermissions.ToArray());
        return result.AllGranted;
    }
}
```

### 5.2 DI Registration

**Registration point**: `AddCapabilityPipeline()`. This is where `AuthorizationMiddleware` is added to the default middleware chain (line 25 of the current `CapabilityServiceCollectionExtensions.cs`). Registering the auth service here ensures every consumer — whether calling `AddCapabilityPipeline()` directly or `AddCapabilityRuntime()` (which calls `AddCapabilityPipeline()`) — gets the default auth service. Do NOT register only in `AddCapabilityRuntime()`; that would leave direct `AddCapabilityPipeline()` consumers with a null auth service.

```csharp
// In AddCapabilityPipeline():
services.TryAddScoped<ICapabilityAuthorizationService, PermissionCapabilityAuthorizationService>();
```

And the lifetime changes:

```csharp
// Before:
services.TryAddSingleton<ICapabilityPipeline, CapabilityPipeline>();
services.TryAddSingleton<ICapabilityDispatcher>(sp => new CapabilityDispatcher(...));

// After:
services.TryAddScoped<ICapabilityPipeline, CapabilityPipeline>();
services.TryAddScoped<ICapabilityDispatcher>(sp => new CapabilityDispatcher(...));
```

Alternatively, simplify `CapabilityDispatcher` to use constructor injection directly (since it's now Scoped, the factory delegate's `sp.GetService<ITenantContext>()` / `sp.GetService<ICurrentUser>()` pattern is no longer needed for lazy resolution).

---

## 6. Files Changed

| File | Change |
|------|--------|
| `Capability.Abstractions/CapabilityExecutionContext.cs` | Add `RequiredPermissions` property |
| `Capability.Abstractions/ICapabilityAuthorizationService.cs` | Update method signature |
| `Capability/PermissionCapabilityAuthorizationService.cs` | **NEW** — implementation |
| `Capability/CapabilityPipeline.cs` | Set `context.RequiredPermissions` after `configureContext` |
| `Capability/Middleware/AuthorizationMiddleware.cs` | Pass `context.RequiredPermissions` to auth service |
| `Capability/CapabilityServiceCollectionExtensions.cs` | Scoped lifetimes + register default auth service |

---

## 7. Test Plan

All new tests in `framework/test/CrestCreates.Capability.Tests/`. New file: `PermissionCapabilityAuthorizationServiceTests.cs`.

| # | Test | Verification |
|---|------|-------------|
| T1 | `Authorize_EmptyPermissions_AllowsExecution` | `requiredPermissions = []` → `true`; `IPermissionChecker` not called |
| T2 | `Authorize_AllPermissionsGranted_AllowsExecution` | Mock `IPermissionChecker` returns all-true → `true` |
| T3 | `Authorize_AnyPermissionDenied_ReturnsUnauthorized` | Mock returns mixed (one false) → `false` |
| T4 | `AuthorizationMiddleware_UsesDescriptorPermissions_NotCapabilityName` | Verify middleware calls `AuthorizeAsync` with `context.RequiredPermissions`, not `capabilityName` as permission |
| T5 | `Pipeline_SetsRequiredPermissions_AfterConfigureContext` | `configureContext` clears `RequiredPermissions` → final value is `descriptor.Permissions` (not empty) |
| T6 | `Pipeline_WithDescriptorPermissions_AndGrantedPermission_InvokesHandler` | Full pipeline via `AddCapabilityPipeline()` with descriptor declaring `["perm.read"]` and mock `IPermissionChecker` granting it → handler invoked, result is Success |
| T7 | `Pipeline_WithDescriptorPermissions_AndDeniedPermission_ReturnsUnauthorized` | Full pipeline via `AddCapabilityPipeline()` with descriptor declaring `["perm.write"]` and mock `IPermissionChecker` denying it → result Status is Failed, ErrorCode is `UNAUTHORIZED` |
| T8 | `AddCapabilityPipeline_RegistersDefaultAuthorizationService` | `IServiceCollection` → `AddCapabilityPipeline()` → resolve `ICapabilityAuthorizationService` → not null, is `PermissionCapabilityAuthorizationService` |
| T9 | `AddCapabilityRuntime_RegistersDefaultAuthorizationService` | `IServiceCollection` → `AddCapabilityRuntime()` → resolve `ICapabilityAuthorizationService` → not null, is `PermissionCapabilityAuthorizationService` (verifies inheritance) |

T6 and T7 are pipeline-level integration tests that exercise the full DI registration chain — they catch DI misconfiguration, middleware ordering issues, and missing permission propagation that unit tests on the service alone would miss. All tests using `AddCapabilityPipeline()` or `AddCapabilityRuntime()` must include the middleware chain (do NOT use an empty builder), and must register a mock `IPermissionChecker` with the DI container.

### 7.1 Existing test adjustments

- `CapabilityPipelineTests`: No changes needed (manual `AddSingleton`, no middleware chain, no auth service registered).
- `CapabilityEndToEndTests`: Must register a mock `IPermissionChecker` in DI, or use descriptors with empty `Permissions`. Do NOT remove `AuthorizationMiddleware` from the pipeline builder and do NOT use an empty builder — the middleware chain must remain intact to verify the auth bridge doesn't break existing pipeline behavior.
- `CapabilityDispatcherTests`: May need mock `ICapabilityAuthorizationService` if they go through the full pipeline chain.
- Authorization tests (`CrestCreates.Application.Tests` filtered by `Permission`): Must not regress. No changes to those tests expected.

---

## 8. Non-Goals (Explicitly Out of Scope)

- **No new permission definitions.** `CapabilityDescriptor.Permissions` is the only permission source.
- **No new grant store / permission checker.** The existing `IPermissionChecker` chain is the sole authorization path.
- **No Organization role → RBAC wiring.** Phase 5c's `UserOrganizationRoleAssignment` is an organization-scoped identity fact; it does not participate in this phase's authorization decisions.
- **No `userId`-based custom checking.** `IPermissionChecker` relies on ambient principal/current tenant. The `userId` parameter in `AuthorizeAsync` is diagnostic only.
- **No identity impersonation via `CapabilityExecutionContext`.** Setting `context.UserId` or `context.TenantId` alone does not establish an ambient security context. `IPermissionChecker` resolves identity from `ICurrentPrincipalAccessor` / `ICurrentTenant` (the HTTP request or ambient scope), not from the capability context. Workflow or background invocations that set only `context.UserId` without an ambient principal will be denied for any non-empty `Permissions`. Service-principal / system-permission support for non-HTTP callers is a future concern.
- **No changes to token/claims/PermissionGrantProviderType.**
- **No AOT violations.** No reflection, no dynamic expressions, no runtime fallback paths.

---

## 9. Acceptance Criteria

```bash
# Main test suite
dotnet test framework/test/CrestCreates.Capability.Tests/

# Authorization regression check
dotnet test framework/test/CrestCreates.Application.Tests/ --filter "FullyQualifiedName~Permission"

# Build verification (at minimum these projects)
dotnet build framework/src/CrestCreates.Capability.Abstractions/
dotnet build framework/src/CrestCreates.Capability/
dotnet build framework/test/CrestCreates.Capability.Tests/
```

---

## 10. Relationship to Organization Identity Kernel (Phase 5c)

The Organization Identity Kernel (Phase 5c) provides organization-scoped identity models (`OrganizationUnit`, `Position`, `UserOrganizationMembership`, `UserOrganizationRoleAssignment`). These are identity facts about which organization and position a user holds.

**They are not wired to RBAC in this phase.** The `OrganizationRole` in Phase 5c is an organization-scoped role (e.g., "Department Manager"), distinct from the RBAC auth role (e.g., "Admin"). The `UserOrganizationRoleAssignment` stores which org role a user has; it does not participate in `IPermissionChecker` permission resolution.

Future phases may bridge organization identity to data-scoped permissions (e.g., "User can see books in their own organization"), but that is out of scope for Phase 5d.
