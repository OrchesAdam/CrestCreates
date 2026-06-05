# Auth Chain Consolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create `CrestCreates.Security.Abstractions` project, merge duplicate password hashing into it, relocate `IIdentitySecurityLogWriter`, and remove dead code (ISecurityService, OAuth stub).

**Architecture:** New `CrestCreates.Security.Abstractions` project with `IPasswordHasher`, `ITokenGenerator`, `IIdentitySecurityLogWriter`. `CrestCreates.Security` implements `PasswordHasher` (PBKDF2) and `TokenGenerator`; deletes old `SecurityService`/`ISecurityService`. All consumers migrated from `CrestCreates.Domain.Authorization` → `CrestCreates.Security.Abstractions`.

**Tech Stack:** .NET 10, PBKDF2-SHA256, Microsoft.NET.Sdk (no external deps for Abstractions)

---

## File Map

| Action | File Path |
|--------|-----------|
| Create | `framework/src/CrestCreates.Security.Abstractions/CrestCreates.Security.Abstractions.csproj` |
| Create | `framework/src/CrestCreates.Security.Abstractions/IPasswordHasher.cs` |
| Create | `framework/src/CrestCreates.Security.Abstractions/ITokenGenerator.cs` |
| Create | `framework/src/CrestCreates.Security.Abstractions/IIdentitySecurityLogWriter.cs` |
| Create | `framework/src/CrestCreates.Security/Services/PasswordHasher.cs` |
| Create | `framework/src/CrestCreates.Security/Services/TokenGenerator.cs` |
| Modify | `framework/src/CrestCreates.Security/CrestCreates.Security.csproj` |
| Modify | `framework/src/CrestCreates.Security/Modules/SecurityModule.cs` |
| Modify | `framework/src/CrestCreates.Domain/CrestCreates.Domain.csproj` |
| Delete | `framework/src/CrestCreates.Domain/Authorization/IPasswordHasher.cs` |
| Modify | `framework/src/CrestCreates.Domain/Permission/IdentitySecurityLog.cs` |
| Modify | `framework/src/CrestCreates.Infrastructure/CrestCreates.Infrastructure.csproj` |
| Delete | `framework/src/CrestCreates.Infrastructure/Authorization/PasswordHasher.cs` |
| Modify | `framework/src/CrestCreates.Infrastructure/Authorization/PasswordPolicyValidator.cs` |
| Modify | `framework/src/CrestCreates.Infrastructure/Authorization/IdentityAuthenticationServiceCollectionExtensions.cs` |
| Modify | `framework/src/CrestCreates.AspNetCore.Authentication.OpenIddict/CrestCreates.AspNetCore.Authentication.OpenIddict.csproj` |
| Modify | `framework/src/CrestCreates.AspNetCore.Authentication.OpenIddict/OpenIddictServiceCollectionExtensions.cs` |
| Modify | `framework/src/CrestCreates.AspNetCore.Authentication.OpenIddict/Services/IdentitySecurityLogService.cs` |
| Modify | `framework/src/CrestCreates.AspNetCore.Authentication.OpenIddict/Handlers/PasswordGrantHandler.cs` |
| Modify | `framework/src/CrestCreates.AspNetCore.Authentication.OpenIddict/Handlers/RefreshTokenGrantHandler.cs` |
| Modify | `framework/src/CrestCreates.Application/Identity/UserAppService.cs` |
| Modify | `framework/src/CrestCreates.Application/Identity/RoleAppService.cs` |
| Modify | `framework/src/CrestCreates.Application/Tenants/TenantBootstrapper.cs` |
| Modify | `framework/src/CrestCreates.Data.EFCore/DataSeed/HostIdentityDataSeeder.cs` |
| Delete | `framework/src/CrestCreates.Security/Services/ISecurityService.cs` |
| Delete | `framework/src/CrestCreates.Security/Services/SecurityService.cs` |
| Delete | `framework/src/CrestCreates.AspNetCore.Authentication.OAuth/CrestCreates.AspNetCore.Authentication.OAuth.csproj` |
| Delete | `framework/src/CrestCreates.AspNetCore.Authentication.OAuth/Class1.cs` |
| Modify | 5 test files (see Task 7) |
| Modify | `CrestCreates.slnx` |
| Modify | `Directory.Packages.props` (if needed — verify Microsoft.AspNetCore.Identity removal) |

---

### Task 1: Create CrestCreates.Security.Abstractions project

**Files:**
- Create: `framework/src/CrestCreates.Security.Abstractions/CrestCreates.Security.Abstractions.csproj`
- Create: `framework/src/CrestCreates.Security.Abstractions/IPasswordHasher.cs`
- Create: `framework/src/CrestCreates.Security.Abstractions/ITokenGenerator.cs`
- Create: `framework/src/CrestCreates.Security.Abstractions/IIdentitySecurityLogWriter.cs`

- [ ] **Step 1: Create the .csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Security.Abstractions</RootNamespace>
  </PropertyGroup>

</Project>
```

- [ ] **Step 2: Create IPasswordHasher.cs**

```csharp
namespace CrestCreates.Security.Abstractions;

public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string hashedPassword, string providedPassword);
}
```

- [ ] **Step 3: Create ITokenGenerator.cs**

```csharp
namespace CrestCreates.Security.Abstractions;

public interface ITokenGenerator
{
    string GenerateRandomToken(int length = 32);
    bool ValidateToken(string token, string expectedToken);
}
```

- [ ] **Step 4: Create IIdentitySecurityLogWriter.cs**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Security.Abstractions;

public interface IIdentitySecurityLogWriter
{
    Task WriteAsync(
        Guid? userId,
        string? userName,
        string? tenantId,
        string action,
        bool isSucceeded,
        string? detail = null,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Add project to CrestCreates.slnx**

Add the new project path to the solution file following the existing pattern.

- [ ] **Step 6: Build to verify**

Run: `dotnet build framework/src/CrestCreates.Security.Abstractions/CrestCreates.Security.Abstractions.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add framework/src/CrestCreates.Security.Abstractions/
git add CrestCreates.slnx
git commit -m "feat: add CrestCreates.Security.Abstractions project with IPasswordHasher, ITokenGenerator, IIdentitySecurityLogWriter"
```

---

### Task 2: Implement PasswordHasher and TokenGenerator in CrestCreates.Security

**Files:**
- Create: `framework/src/CrestCreates.Security/Services/PasswordHasher.cs`
- Create: `framework/src/CrestCreates.Security/Services/TokenGenerator.cs`
- Modify: `framework/src/CrestCreates.Security/CrestCreates.Security.csproj`
- Modify: `framework/src/CrestCreates.Security/Modules/SecurityModule.cs`
- Delete: `framework/src/CrestCreates.Security/Services/ISecurityService.cs`
- Delete: `framework/src/CrestCreates.Security/Services/SecurityService.cs`

- [ ] **Step 1: Update CrestCreates.Security.csproj**

Add ProjectReference to Security.Abstractions, remove Microsoft.AspNetCore.Identity PackageReference:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.ViewFeatures" />
    <PackageReference Include="Microsoft.AspNetCore.Antiforgery" />
    <PackageReference Include="Microsoft.AspNetCore.HttpsPolicy" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Security.Abstractions\CrestCreates.Security.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Domain.Shared\CrestCreates.Domain.Shared.csproj" />
    <ProjectReference Include="..\CrestCreates.Modularity\CrestCreates.Modularity.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create PasswordHasher.cs**

Move PBKDF2 implementation from `CrestCreates.Infrastructure/Authorization/PasswordHasher.cs`, change namespace + internal:

```csharp
using System;
using System.Security.Cryptography;
using CrestCreates.Security.Abstractions;

namespace CrestCreates.Security.Services;

internal class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public string HashPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentNullException(nameof(password));
        }

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, HashSize);

        byte[] hashBytes = new byte[SaltSize + HashSize];
        Array.Copy(salt, 0, hashBytes, 0, SaltSize);
        Array.Copy(hash, 0, hashBytes, SaltSize, HashSize);

        return Convert.ToBase64String(hashBytes);
    }

    public bool VerifyPassword(string hashedPassword, string providedPassword)
    {
        if (string.IsNullOrEmpty(hashedPassword))
        {
            return false;
        }

        if (string.IsNullOrEmpty(providedPassword))
        {
            return false;
        }

        byte[] hashBytes;
        try
        {
            hashBytes = Convert.FromBase64String(hashedPassword);
        }
        catch
        {
            return false;
        }

        if (hashBytes.Length != SaltSize + HashSize)
        {
            return false;
        }

        byte[] salt = new byte[SaltSize];
        Array.Copy(hashBytes, 0, salt, 0, SaltSize);

        byte[] storedHash = new byte[HashSize];
        Array.Copy(hashBytes, SaltSize, storedHash, 0, HashSize);

        byte[] computedHash = Rfc2898DeriveBytes.Pbkdf2(providedPassword, salt, Iterations, Algorithm, HashSize);

        return CryptographicOperations.FixedTimeEquals(storedHash, computedHash);
    }
}
```

- [ ] **Step 3: Create TokenGenerator.cs**

Extract token methods from SecurityService:

```csharp
using System;
using System.Security.Cryptography;
using CrestCreates.Security.Abstractions;

namespace CrestCreates.Security.Services;

internal class TokenGenerator : ITokenGenerator
{
    public string GenerateRandomToken(int length = 32)
    {
        var bytes = new byte[length];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public bool ValidateToken(string token, string expectedToken)
    {
        return string.Equals(token, expectedToken, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 4: Update SecurityModule.cs OnConfigureServices**

Replace `services.AddSingleton<ISecurityService, SecurityService>();` with:

```csharp
services.TryAddScoped<IPasswordHasher, PasswordHasher>();
services.TryAddSingleton<ITokenGenerator, TokenGenerator>();
```

Also remove the `using CrestCreates.Security.Services;` import — the new classes are `internal`, accessed only via DI.

Import the new namespace: add `using CrestCreates.Security.Abstractions;`.

- [ ] **Step 5: Delete ISecurityService.cs and SecurityService.cs**

Move to `99_RecycleBin/` following the project file deletion rule.

```bash
mkdir -p /home/orches/workspace/99_RecycleBin
mv framework/src/CrestCreates.Security/Services/ISecurityService.cs /home/orches/workspace/99_RecycleBin/
mv framework/src/CrestCreates.Security/Services/SecurityService.cs /home/orches/workspace/99_RecycleBin/
```

- [ ] **Step 6: Build CrestCreates.Security**

Run: `dotnet build framework/src/CrestCreates.Security/CrestCreates.Security.csproj`
Expected: 0 errors.

- [ ] **Step 7: Commit**

```bash
git add framework/src/CrestCreates.Security/
git commit -m "refactor: replace ISecurityService with IPasswordHasher + ITokenGenerator in Security module"
```

---

### Task 3: Migrate IPasswordHasher out of CrestCreates.Domain

**Files:**
- Delete: `framework/src/CrestCreates.Domain/Authorization/IPasswordHasher.cs`
- Modify: `framework/src/CrestCreates.Domain/CrestCreates.Domain.csproj`
- Modify: `framework/src/CrestCreates.Domain/Permission/IdentitySecurityLog.cs`

- [ ] **Step 1: Update CrestCreates.Domain.csproj**

Add ProjectReference to Security.Abstractions:

```xml
<ProjectReference Include="..\CrestCreates.Security.Abstractions\CrestCreates.Security.Abstractions.csproj" />
```

- [ ] **Step 2: Remove IIdentitySecurityLogWriter from IdentitySecurityLog.cs**

Read `framework/src/CrestCreates.Domain/Permission/IdentitySecurityLog.cs` and remove the `IIdentitySecurityLogWriter` interface definition (lines 35-46). Keep the `IdentitySecurityLog` entity class (lines 1-33).

The file after edit should contain only the `IdentitySecurityLog` entity class with its `using System; using System.Threading; using System.Threading.Tasks; using CrestCreates.Domain.Entities;` header.

- [ ] **Step 3: Delete old IPasswordHasher.cs**

```bash
mv framework/src/CrestCreates.Domain/Authorization/IPasswordHasher.cs /home/orches/workspace/99_RecycleBin/
```

- [ ] **Step 4: Build CrestCreates.Domain**

Run: `dotnet build framework/src/CrestCreates.Domain/CrestCreates.Domain.csproj`
Expected: errors from files that still reference `CrestCreates.Domain.Authorization.IPasswordHasher` — expected, will be fixed in Task 4.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Domain/
git commit -m "refactor: remove IPasswordHasher and IIdentitySecurityLogWriter from Domain layer"
```

---

### Task 4: Migrate consumers to CrestCreates.Security.Abstractions

**Files:**
- Modify: `framework/src/CrestCreates.Infrastructure/CrestCreates.Infrastructure.csproj`
- Modify: `framework/src/CrestCreates.Infrastructure/Authorization/PasswordPolicyValidator.cs`
- Modify: `framework/src/CrestCreates.Infrastructure/Authorization/IdentityAuthenticationServiceCollectionExtensions.cs`
- Delete: `framework/src/CrestCreates.Infrastructure/Authorization/PasswordHasher.cs`
- Modify: `framework/src/CrestCreates.AspNetCore.Authentication.OpenIddict/CrestCreates.AspNetCore.Authentication.OpenIddict.csproj`
- Modify: `framework/src/CrestCreates.AspNetCore.Authentication.OpenIddict/OpenIddictServiceCollectionExtensions.cs`
- Modify: `framework/src/CrestCreates.AspNetCore.Authentication.OpenIddict/Services/IdentitySecurityLogService.cs`
- Modify: `framework/src/CrestCreates.AspNetCore.Authentication.OpenIddict/Handlers/PasswordGrantHandler.cs`
- Modify: `framework/src/CrestCreates.AspNetCore.Authentication.OpenIddict/Handlers/RefreshTokenGrantHandler.cs`
- Modify: `framework/src/CrestCreates.Application/Identity/UserAppService.cs`
- Modify: `framework/src/CrestCreates.Application/Identity/RoleAppService.cs`
- Modify: `framework/src/CrestCreates.Application/Tenants/TenantBootstrapper.cs`
- Modify: `framework/src/CrestCreates.Data.EFCore/DataSeed/HostIdentityDataSeeder.cs`

- [ ] **Step 1: Update CrestCreates.Infrastructure.csproj**

Add ProjectReference to Security.Abstractions:
```xml
<ProjectReference Include="..\CrestCreates.Security.Abstractions\CrestCreates.Security.Abstractions.csproj" />
```

- [ ] **Step 2: Delete old Infrastructure PasswordHasher.cs**

```bash
mv framework/src/CrestCreates.Infrastructure/Authorization/PasswordHasher.cs /home/orches/workspace/99_RecycleBin/
```

- [ ] **Step 3: Update PasswordPolicyValidator.cs**

Change `using CrestCreates.Domain.Authorization;` to `using CrestCreates.Security.Abstractions;`. The `IPasswordPolicyValidator` interface definition also lives in the old file — we need to keep it alive during transition. Since `IPasswordPolicyValidator` is defined in the SAME file as `IPasswordHasher` (which we deleted), we need to extract it.

Actually, re-read: `IPasswordPolicyValidator` is in `IPasswordHasher.cs` (the Domain Authorization file). We moved that file to RecycleBin. So we need to recreate it.

No wait — let's handle this more cleanly. `IPasswordPolicyValidator` has 1 consumer (`UserAppService`). We should either:
- Keep `IPasswordPolicyValidator` in `CrestCreates.Security.Abstractions`
- Move it inline to `UserAppService` or `Infrastructure`

Following the design principle, add `IPasswordPolicyValidator` to `CrestCreates.Security.Abstractions`:

Create: `framework/src/CrestCreates.Security.Abstractions/IPasswordPolicyValidator.cs`

```csharp
namespace CrestCreates.Security.Abstractions;

public interface IPasswordPolicyValidator
{
    void Validate(string password);
}
```

Then update `PasswordPolicyValidator.cs`:
- `using CrestCreates.Domain.Authorization;` → `using CrestCreates.Security.Abstractions;`

- [ ] **Step 4: Update IdentityAuthenticationServiceCollectionExtensions.cs**

Replace `using CrestCreates.Domain.Authorization;` with `using CrestCreates.Security.Abstractions;`.

IMPORTANT: `services.TryAddScoped<IPasswordHasher, PasswordHasher>();` — `PasswordHasher` no longer exists in Infrastructure (moved to Security). Remove this line. `IPasswordHasher` is now registered in `SecurityModule`.

Keep `services.TryAddScoped<IPasswordPolicyValidator, PasswordPolicyValidator>();` and `services.TryAddScoped<IIdentityClaimsBuilder, IdentityClaimsBuilder>();`.

- [ ] **Step 5: Update OpenIddict project and all its files**

Add to `CrestCreates.AspNetCore.Authentication.OpenIddict.csproj`:
```xml
<ProjectReference Include="..\CrestCreates.Security.Abstractions\CrestCreates.Security.Abstractions.csproj" />
```

Update `OpenIddictServiceCollectionExtensions.cs`:
- `using CrestCreates.Domain.Permission;` → `using CrestCreates.Security.Abstractions;`

Update `IdentitySecurityLogService.cs`:
- `using CrestCreates.Domain.Permission;` → `using CrestCreates.Security.Abstractions;`

Update `PasswordGrantHandler.cs`:
- `using CrestCreates.Domain.Authorization;` → `using CrestCreates.Security.Abstractions;`

Update `RefreshTokenGrantHandler.cs`:
- `using CrestCreates.Domain.Authorization;` → `using CrestCreates.Security.Abstractions;`

- [ ] **Step 6: Update Application layer consumers**

Update `UserAppService.cs`:
- `using CrestCreates.Domain.Authorization;` → `using CrestCreates.Security.Abstractions;`

Update `RoleAppService.cs`:
- No change needed for authorization namespace — it only uses `IIdentitySecurityLogWriter`. Check if it imports `CrestCreates.Domain.Permission` and change to `CrestCreates.Security.Abstractions`.

Update `TenantBootstrapper.cs`:
- `using CrestCreates.Domain.Authorization;` → `using CrestCreates.Security.Abstractions;`

Update `HostIdentityDataSeeder.cs`:
- `using CrestCreates.Domain.Authorization;` → `using CrestCreates.Security.Abstractions;`

- [ ] **Step 7: Build to verify all migrations work**

Run: `dotnet build`
Expected: 0 errors, reduced warnings.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "refactor: migrate all consumers to CrestCreates.Security.Abstractions"
```

---

### Task 5: Delete OAuth empty stub project

**Files:**
- Delete: `framework/src/CrestCreates.AspNetCore.Authentication.OAuth/CrestCreates.AspNetCore.Authentication.OAuth.csproj`
- Delete: `framework/src/CrestCreates.AspNetCore.Authentication.OAuth/Class1.cs`
- Modify: `CrestCreates.slnx`

- [ ] **Step 1: Move OAuth project to RecycleBin**

```bash
mv framework/src/CrestCreates.AspNetCore.Authentication.OAuth/ /home/orches/workspace/99_RecycleBin/
```

- [ ] **Step 2: Remove from solution file**

Remove the OAuth project entry from `CrestCreates.slnx`.

- [ ] **Step 3: Build to verify**

Run: `dotnet build`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "chore: remove empty OAuth stub project"
```

---

### Task 6: Add IPasswordPolicyValidator to Security.Abstractions & cleanup

**Files:**
- Create: `framework/src/CrestCreates.Security.Abstractions/IPasswordPolicyValidator.cs`
- Modify: `framework/src/CrestCreates.Infrastructure/Authorization/PasswordPolicyValidator.cs`

- [ ] **Step 1: Create IPasswordPolicyValidator.cs in Security.Abstractions**

```csharp
namespace CrestCreates.Security.Abstractions;

public interface IPasswordPolicyValidator
{
    void Validate(string password);
}
```

- [ ] **Step 2: Update PasswordPolicyValidator.cs**

```csharp
// Change: using CrestCreates.Domain.Authorization; → using CrestCreates.Security.Abstractions;
```

- [ ] **Step 3: Build and commit**

```bash
dotnet build
git add -A
git commit -m "refactor: relocate IPasswordPolicyValidator to Security.Abstractions"
```

---

### Task 7: Update test files

**Files:**
- Modify: `framework/test/CrestCreates.Application.Tests/Identity/UserAppServiceTests.cs`
- Modify: `framework/test/CrestCreates.Application.Tests/Identity/RoleAppServiceTests.cs`
- Modify: `framework/test/CrestCreates.Application.Tests/Identity/PasswordGrantHandlerTests.cs`
- Modify: `framework/test/CrestCreates.Application.Tests/Identity/RefreshTokenGrantHandlerTests.cs`
- Modify: `framework/test/CrestCreates.Application.Tests/Tenants/TenantBootstrapperTests.cs`
- Modify: `framework/test/CrestCreates.IntegrationTests/WebApplicationFactory.cs`
- Modify: `samples/SaaSHelpdesk/SaaSHelpdesk.Tests/Fixtures/HelpdeskWebApplicationFactory.cs`
- Modify: `samples/SaaSHelpdesk/SaaSHelpdesk.Application/Services/AgentAppService.cs`

- [ ] **Step 1: Update tests — using namespace changes**

For each test file, change `using CrestCreates.Domain.Authorization;` → `using CrestCreates.Security.Abstractions;`.
Also change any `using CrestCreates.Domain.Permission;` → `using CrestCreates.Security.Abstractions;` where it was only used for `IIdentitySecurityLogWriter`.

Files to update:
1. `framework/test/CrestCreates.Application.Tests/Identity/UserAppServiceTests.cs` — line 7
2. `framework/test/CrestCreates.Application.Tests/Identity/RoleAppServiceTests.cs` — check for Permission namespace
3. `framework/test/CrestCreates.Application.Tests/Identity/PasswordGrantHandlerTests.cs` — line 6
4. `framework/test/CrestCreates.Application.Tests/Identity/RefreshTokenGrantHandlerTests.cs` — check for Authorization namespace
5. `framework/test/CrestCreates.Application.Tests/Tenants/TenantBootstrapperTests.cs` — line 7
6. `framework/test/CrestCreates.IntegrationTests/WebApplicationFactory.cs` — line 17
7. `samples/SaaSHelpdesk/SaaSHelpdesk.Tests/Fixtures/HelpdeskWebApplicationFactory.cs` — line 16
8. `samples/SaaSHelpdesk/SaaSHelpdesk.Application/Services/AgentAppService.cs` — line 6

- [ ] **Step 2: Run tests**

Run: `dotnet test`
Expected: Same pass/fail as before changes (no regressions).

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "test: update test files to use CrestCreates.Security.Abstractions"
```

---

### Task 8: Final verification and cleanup

- [ ] **Step 1: Full build with no errors**

Run: `dotnet build`
Expected: 0 errors, warnings count reduced.

- [ ] **Step 2: Run full test suite**

Run: `dotnet test`
Expected: All previously-passing tests still pass, no new failures.

- [ ] **Step 3: Verify no stale references**

```bash
grep -rn "CrestCreates.Domain.Authorization" framework/src --include="*.cs" | grep -v obj | grep -v bin
```
Expected: No output (all migrated).

```bash
grep -rn "ISecurityService\|SecurityService" framework/src --include="*.cs" | grep -v obj | grep -v bin
```
Expected: No output (all dead code removed).

```bash
grep -rn "IIdentitySecurityLogWriter" framework/src --include="*.cs" | grep -v obj | grep -v bin
```
Expected: Only shows usage from `CrestCreates.Security.Abstractions.IIdentitySecurityLogWriter`.

- [ ] **Step 4: Final commit if any cleanup needed, or mark complete**

```bash
git status
```
