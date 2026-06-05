# Auth Chain Consolidation — Design Spec

**Date:** 2026-06-05
**Status:** Approved

## Goal

Close the authentication chain gaps identified in the Major TODO audit:
1. Merge duplicate password hashing (IPasswordHasher vs ISecurityService)
2. Remove dead code (ISecurityService/SecurityService with zero consumers)
3. Remove OAuth empty stub project
4. Relocate IIdentitySecurityLogWriter from Domain/Permission to Security.Abstractions

## Architecture

### New Project: CrestCreates.Security.Abstractions

```
framework/src/CrestCreates.Security.Abstractions/
  IPasswordHasher.cs
  ITokenGenerator.cs
  IIdentitySecurityLogWriter.cs
  CrestCreates.Security.Abstractions.csproj
```

- `IPasswordHasher` — migrated from `CrestCreates.Domain.Authorization.IPasswordHasher` (new namespace: `CrestCreates.Security.Abstractions`)
- `ITokenGenerator` — extracted from `ISecurityService` (GenerateRandomToken, ValidateToken)
- `IIdentitySecurityLogWriter` — migrated from `CrestCreates.Domain.Permission.IdentitySecurityLog` (与 Permission 无关，属于安全认证日志)
- No external dependencies. Plain `Microsoft.NET.Sdk`.

### Deleted Projects

- `framework/src/CrestCreates.AspNetCore.Authentication.OAuth/` → `99_RecycleBin/` (empty stub, Class1.cs only)

### Modified Projects

| Project | Change |
|---------|--------|
| `CrestCreates.Security` | Add ref to `Security.Abstractions`; add `PasswordHasher` (PBKDF2 impl); add `TokenGenerator`; delete `SecurityService` + `ISecurityService`; remove `Microsoft.AspNetCore.Identity` package |
| `CrestCreates.Domain` | Add ref to `Security.Abstractions`; delete `Authorization/IPasswordHasher.cs`; delete `IIdentitySecurityLogWriter` from `Permission/IdentitySecurityLog.cs` (keep entity class) |
| `CrestCreates.Infrastructure` | Add ref to `Security.Abstractions`; delete `Authorization/PasswordHasher.cs`; remove DI registration in `IdentityAuthenticationServiceCollectionExtensions` |
| `CrestCreates.AspNetCore.Authentication.OpenIddict` | Add ref to `Security.Abstractions`; `IdentitySecurityLogServiceImpl` uses new namespace; update DI registration for `IIdentitySecurityLogWriter` |
| `CrestCreates.Application` | Add ref to `Security.Abstractions` (if not already transitive) |
| `CrestCreates.Data.EFCore` | Add ref to `Security.Abstractions` (if not already transitive) |

## Implementation Details

### PasswordHasher (moved to CrestCreates.Security)

```csharp
namespace CrestCreates.Security.Services;

internal class PasswordHasher : IPasswordHasher
{
    // PBKDF2-SHA256, 16-byte salt, 32-byte hash, 100,000 iterations
    // Same algorithm as former CrestCreates.Infrastructure.Authorization.PasswordHasher
    public string HashPassword(string password) { ... }
    public bool VerifyPassword(string hashedPassword, string providedPassword) { ... }
}
```

### TokenGenerator (new, extracted from SecurityService)

```csharp
namespace CrestCreates.Security.Services;

internal class TokenGenerator : ITokenGenerator
{
    public string GenerateRandomToken(int length = 32) { ... }
    public bool ValidateToken(string token, string expectedToken) { ... }
}
```

### DI Registration (SecurityModule.cs)

```csharp
services.TryAddScoped<IPasswordHasher, PasswordHasher>();
services.TryAddSingleton<ITokenGenerator, TokenGenerator>();
```

### Deleted Types

| Old Type | Reason |
|----------|--------|
| `CrestCreates.Domain.Authorization.IPasswordHasher` | Migrated to Security.Abstractions |
| `CrestCreates.Domain.Authorization.IPasswordPolicyValidator` | Check if consumed; if not, delete |
| `CrestCreates.Domain.Permission.IIdentitySecurityLogWriter` | Migrated to Security.Abstractions |
| `CrestCreates.Infrastructure.Authorization.PasswordHasher` | Moved to Security |
| `CrestCreates.Security.Services.ISecurityService` | Dead code, zero consumers |
| `CrestCreates.Security.Services.SecurityService` | Dead code, zero consumers |
| `CrestCreates.AspNetCore.Authentication.OAuth.Class1` | Empty stub |

### Consumer Migration

**IPasswordHasher** consumers change:
- `using CrestCreates.Domain.Authorization;` → `using CrestCreates.Security.Abstractions;`

**IIdentitySecurityLogWriter** consumers change:
- `using CrestCreates.Domain.Permission;` → `using CrestCreates.Security.Abstractions;`

Affected files (14 production + 5 test):
- PasswordGrantHandler, RefreshTokenGrantHandler, UserAppService, RoleAppService, TenantBootstrapper
- HostIdentityDataSeeder, IdentityAuthenticationServiceCollectionExtensions, IdentitySecurityLogServiceImpl
- OpenIddictServiceCollectionExtensions, EfCoreOrmModule, WebApplicationFactory
- 5 test files

## Risk Assessment

- **Low risk**: Pure refactoring. PBKDF2 algorithm unchanged — existing password hashes remain valid.
- **No circular dependency**: `Domain` → `Security.Abstractions` (new), `Security.Abstractions` has no project refs.
- **No breaking API change**: Interface namespace change is a compile-time fix; interface methods unchanged.

## Test Strategy

1. Run existing `PasswordHasher` tests to verify algorithm compatibility
2. Run `PasswordGrantHandlerTests` to verify auth flow
3. Run `UserAppServiceTests` to verify user management
4. Run full test suite to catch any missed using references