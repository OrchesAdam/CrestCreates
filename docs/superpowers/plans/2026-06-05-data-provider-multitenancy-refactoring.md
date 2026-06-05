# Data Provider Architecture & Multi-Tenancy Refactoring — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor data provider project hierarchy to separate ORM base from DB-specific providers, consolidate multi-tenancy interfaces into `MultiTenancy.Abstract`, and clean up deprecated code.

**Architecture:** Follow ABP Framework's pattern: `Data.EFCore` (base, no DB provider) → `Data.EFCore.SqlServer/MySql/PostgreSql` (thin provider layers). Move tenant init interfaces from `Application.Contracts` to `MultiTenancy.Abstract`. Move `TenantInitializationOrchestrator` from `Application` to `MultiTenancy`. Remove deprecated interfaces and implementations.

**Tech Stack:** .NET 10, EF Core 10, Npgsql, MySql.EntityFrameworkCore, SqlSugarCore, FreeSql

**Spec:** `docs/superpowers/specs/2026-06-05-data-provider-multitenancy-refactoring-design.md`

---

## Phase 1: Create New Projects

### Task 1: Create `CrestCreates.Data.Core` project

**Files:**
- Create: `framework/src/CrestCreates.Data.Core/CrestCreates.Data.Core.csproj`
- Create: `framework/src/CrestCreates.Data.Core/DataCoreModule.cs`
- Modify: `CrestCreates.slnx`

- [ ] **Step 1: Create project directory and .csproj**

```bash
mkdir -p framework/src/CrestCreates.Data.Core
```

Write `framework/src/CrestCreates.Data.Core/CrestCreates.Data.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Data.Core</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Data.Abstractions\CrestCreates.Data.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.DataFilter\CrestCreates.DataFilter.csproj" />
    <ProjectReference Include="..\CrestCreates.MultiTenancy.Abstract\CrestCreates.MultiTenancy.Abstract.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create module class**

Write `framework/src/CrestCreates.Data.Core/DataCoreModule.cs`:

```csharp
using CrestCreates.Modularity;

namespace CrestCreates.Data.Core;

[CrestModule]
public class DataCoreModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        // Shared data infrastructure registrations go here.
        // Currently a placeholder — populated as common data concerns are extracted.
    }
}
```

- [ ] **Step 3: Add to solution file**

Edit `CrestCreates.slnx` — add to `/src/modules/Data/` folder:

```xml
<Project Path="framework/src/CrestCreates.Data.Core/CrestCreates.Data.Core.csproj" />
```

- [ ] **Step 4: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Data.Core/CrestCreates.Data.Core.csproj
```

Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Data.Core/ CrestCreates.slnx
git commit -m "feat: add CrestCreates.Data.Core project for shared data infrastructure"
```

---

### Task 2: Rename `CrestCreates.Data.FreeSqlProvider` → `CrestCreates.Data.FreeSql`

**Files:**
- Rename: `framework/src/CrestCreates.Data.FreeSqlProvider/` → `framework/src/CrestCreates.Data.FreeSql/`
- Modify: `CrestCreates.slnx`
- Modify: All .csproj files referencing `CrestCreates.Data.FreeSqlProvider`

- [ ] **Step 1: Move directory with git**

```bash
cd framework/src
git mv CrestCreates.Data.FreeSqlProvider CrestCreates.Data.FreeSql
```

- [ ] **Step 2: Update .csproj file name**

```bash
cd framework/src/CrestCreates.Data.FreeSql
git mv CrestCreates.Data.FreeSqlProvider.csproj CrestCreates.Data.FreeSql.csproj
```

- [ ] **Step 3: Update .csproj content**

Edit `framework/src/CrestCreates.Data.FreeSql/CrestCreates.Data.FreeSql.csproj` — replace:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Data.Core\CrestCreates.Data.Core.csproj" />
    <ProjectReference Include="..\CrestCreates.Data.Abstractions\CrestCreates.Data.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Domain\CrestCreates.Domain.csproj" />
    <ProjectReference Include="..\CrestCreates.Infrastructure\CrestCreates.Infrastructure.csproj" />
    <ProjectReference Include="..\CrestCreates.MultiTenancy.Abstract\CrestCreates.MultiTenancy.Abstract.csproj" />
    <ProjectReference Include="..\CrestCreates.DataFilter\CrestCreates.DataFilter.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="FreeSql" />
    <PackageReference Include="FreeSql.DbContext" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Data.FreeSql</RootNamespace>
  </PropertyGroup>

</Project>
```

- [ ] **Step 4: Update solution file**

Edit `CrestCreates.slnx` — change:

```xml
<!-- From -->
<Project Path="framework/src/CrestCreates.Data.FreeSqlProvider/CrestCreates.Data.FreeSqlProvider.csproj" />
<!-- To -->
<Project Path="framework/src/CrestCreates.Data.FreeSql/CrestCreates.Data.FreeSql.csproj" />
```

- [ ] **Step 5: Update all namespace references in source files**

```bash
cd framework/src/CrestCreates.Data.FreeSql
find . -name "*.cs" -exec sed -i 's/CrestCreates\.Data\.FreeSqlProvider/CrestCreates.Data.FreeSql/g' {} +
```

- [ ] **Step 6: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Data.FreeSql/CrestCreates.Data.FreeSql.csproj
```

Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add framework/src/CrestCreates.Data.FreeSql/ CrestCreates.slnx
git commit -m "refactor: rename CrestCreates.Data.FreeSqlProvider to CrestCreates.Data.FreeSql"
```

---

### Task 3: Rename `CrestCreates.Data.EFCore.PostgreSQL` → `CrestCreates.Data.EFCore.PostgreSql`

**Files:**
- Rename: `framework/src/CrestCreates.Data.EFCore.PostgreSQL/` → `framework/src/CrestCreates.Data.EFCore.PostgreSql/`
- Modify: `CrestCreates.slnx`
- Modify: All .csproj files referencing the old name
- Modify: All source files with old namespace

- [ ] **Step 1: Move directory with git**

```bash
cd framework/src
git mv CrestCreates.Data.EFCore.PostgreSQL CrestCreates.Data.EFCore.PostgreSql
```

- [ ] **Step 2: Update .csproj file name**

```bash
cd framework/src/CrestCreates.Data.EFCore.PostgreSql
git mv CrestCreates.Data.EFCore.PostgreSQL.csproj CrestCreates.Data.EFCore.PostgreSql.csproj
```

- [ ] **Step 3: Update .csproj content**

Edit `framework/src/CrestCreates.Data.EFCore.PostgreSql/CrestCreates.Data.EFCore.PostgreSql.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Data.EFCore.PostgreSql</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.MultiTenancy.Abstract\CrestCreates.MultiTenancy.Abstract.csproj" />
    <ProjectReference Include="..\CrestCreates.AspNetCore.Authentication.OpenIddict\CrestCreates.AspNetCore.Authentication.OpenIddict.csproj" />
    <ProjectReference Include="..\CrestCreates.Data.EFCore\CrestCreates.Data.EFCore.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
  </ItemGroup>

</Project>
```

Note: `Application.Contracts` reference removed — tenant init interfaces will be in `MultiTenancy.Abstract`.

- [ ] **Step 4: Update all namespace references in source files**

```bash
cd framework/src/CrestCreates.Data.EFCore.PostgreSql
find . -name "*.cs" -exec sed -i 's/CrestCreates\.Data\.EFCore\.PostgreSQL/CrestCreates.Data.EFCore.PostgreSql/g' {} +
```

- [ ] **Step 5: Update solution file**

Edit `CrestCreates.slnx` — change:

```xml
<!-- From -->
<Project Path="framework/src/CrestCreates.Data.EFCore.PostgreSQL/CrestCreates.Data.EFCore.PostgreSQL.csproj" />
<!-- To -->
<Project Path="framework/src/CrestCreates.Data.EFCore.PostgreSql/CrestCreates.Data.EFCore.PostgreSql.csproj" />
```

- [ ] **Step 6: Find and update all .csproj references to the old name**

```bash
grep -rl "CrestCreates.Data.EFCore.PostgreSQL" --include="*.csproj" --include="*.cs" --include="*.slnx" .
```

Expected: Should only find references to update in:
- `samples/SaaSHelpdesk/SaaSHelpdesk.EntityFrameworkCore/SaaSHelpdesk.EntityFrameworkCore.csproj`

Update that file — change the ProjectReference path from `CrestCreates.Data.EFCore.PostgreSQL` to `CrestCreates.Data.EFCore.PostgreSql`.

- [ ] **Step 7: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Data.EFCore.PostgreSql/CrestCreates.Data.EFCore.PostgreSql.csproj
```

Expected: Build succeeded.

- [ ] **Step 8: Commit**

```bash
git add framework/src/CrestCreates.Data.EFCore.PostgreSql/ CrestCreates.slnx samples/
git commit -m "refactor: rename CrestCreates.Data.EFCore.PostgreSQL to CrestCreates.Data.EFCore.PostgreSql"
```

---

### Task 4: Create `CrestCreates.Data.EFCore.SqlServer` project

**Files:**
- Create: `framework/src/CrestCreates.Data.EFCore.SqlServer/CrestCreates.Data.EFCore.SqlServer.csproj`
- Create: `framework/src/CrestCreates.Data.EFCore.SqlServer/DatabaseProviders/SqlServer/SqlServerTenantDatabaseProvisioner.cs`
- Create: `framework/src/CrestCreates.Data.EFCore.SqlServer/Configuration/SqlServerServiceCollectionExtensions.cs`
- Modify: `CrestCreates.slnx`

- [ ] **Step 1: Create project directory**

```bash
mkdir -p framework/src/CrestCreates.Data.EFCore.SqlServer/DatabaseProviders/SqlServer
mkdir -p framework/src/CrestCreates.Data.EFCore.SqlServer/Configuration
```

- [ ] **Step 2: Create .csproj**

Write `framework/src/CrestCreates.Data.EFCore.SqlServer/CrestCreates.Data.EFCore.SqlServer.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Data.EFCore.SqlServer</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Data.EFCore\CrestCreates.Data.EFCore.csproj" />
    <ProjectReference Include="..\CrestCreates.MultiTenancy.Abstract\CrestCreates.MultiTenancy.Abstract.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Create SqlServerTenantDatabaseProvisioner**

Write `framework/src/CrestCreates.Data.EFCore.SqlServer/DatabaseProviders/SqlServer/SqlServerTenantDatabaseProvisioner.cs`:

```csharp
using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Data.EFCore.SqlServer.DatabaseProviders.SqlServer;

public class SqlServerTenantDatabaseProvisioner : ITenantDatabaseProvisioner
{
    private readonly ILogger<SqlServerTenantDatabaseProvisioner> _logger;
    private static readonly Regex ValidDatabaseNameRegex = new(@"^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

    public SqlServerTenantDatabaseProvisioner(ILogger<SqlServerTenantDatabaseProvisioner> logger)
    {
        _logger = logger;
    }

    public async Task<TenantDatabaseInitializeResult> InitializeAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default)
    {
        var databaseName = $"tenant_{context.TenantId:N}";

        if (!ValidDatabaseNameRegex.IsMatch(databaseName))
        {
            return TenantDatabaseInitializeResult.Failed($"Invalid database name: {databaseName}");
        }

        var masterConnectionString = GetMasterConnectionString(context.ConnectionString!);

        try
        {
            await using var connection = new SqlConnection(masterConnectionString);
            await connection.OpenAsync(cancellationToken);

            var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = $"SELECT COUNT(*) FROM sys.databases WHERE name = @name";
            checkCommand.Parameters.AddWithValue("@name", databaseName);
            var exists = (int)(await checkCommand.ExecuteScalarAsync(cancellationToken))! > 0;

            if (!exists)
            {
                var escapedName = databaseName.Replace("]", "]]");
                var createCommand = connection.CreateCommand();
                createCommand.CommandText = $"CREATE DATABASE [{escapedName}]";
                await createCommand.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogInformation("Created database {DatabaseName} for tenant {TenantId}", databaseName, context.TenantId);
            }

            return TenantDatabaseInitializeResult.Succeeded();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize database for tenant {TenantId}", context.TenantId);
            return TenantDatabaseInitializeResult.Failed(ex.Message);
        }
    }

    private static string GetMasterConnectionString(string tenantConnectionString)
    {
        var builder = new SqlConnectionStringBuilder(tenantConnectionString);
        builder.InitialCatalog = "master";
        return builder.ConnectionString;
    }
}
```

- [ ] **Step 4: Create SqlServerServiceCollectionExtensions**

Write `framework/src/CrestCreates.Data.EFCore.SqlServer/Configuration/SqlServerServiceCollectionExtensions.cs`:

```csharp
using CrestCreates.Data.EFCore.SqlServer.DatabaseProviders.SqlServer;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Data.EFCore.SqlServer.Configuration;

public static class SqlServerServiceCollectionExtensions
{
    /// <summary>
    /// Registers SQL Server-specific EF Core services:
    /// <list type="bullet">
    ///   <item><see cref="ITenantDatabaseProvisioner"/> with SQL Server CREATE DATABASE support</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddCrestCreatesEfCoreSqlServer(this IServiceCollection services)
    {
        services.TryAddScoped<SqlServerTenantDatabaseProvisioner>();
        services.TryAddScoped<ITenantDatabaseProvisioner, SqlServerTenantDatabaseProvisioner>();

        return services;
    }
}
```

- [ ] **Step 5: Add to solution file**

Edit `CrestCreates.slnx` — add to `/src/modules/Data/` folder:

```xml
<Project Path="framework/src/CrestCreates.Data.EFCore.SqlServer/CrestCreates.Data.EFCore.SqlServer.csproj" />
```

- [ ] **Step 6: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Data.EFCore.SqlServer/CrestCreates.Data.EFCore.SqlServer.csproj
```

Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add framework/src/CrestCreates.Data.EFCore.SqlServer/ CrestCreates.slnx
git commit -m "feat: add CrestCreates.Data.EFCore.SqlServer project"
```

---

### Task 5: Create `CrestCreates.Data.EFCore.MySql` project

**Files:**
- Create: `framework/src/CrestCreates.Data.EFCore.MySql/CrestCreates.Data.EFCore.MySql.csproj`
- Create: `framework/src/CrestCreates.Data.EFCore.MySql/DatabaseProviders/MySql/MySqlTenantDatabaseProvisioner.cs`
- Create: `framework/src/CrestCreates.Data.EFCore.MySql/Configuration/MySqlServiceCollectionExtensions.cs`
- Modify: `CrestCreates.slnx`
- Modify: `Directory.Packages.props`

- [ ] **Step 1: Create project directory**

```bash
mkdir -p framework/src/CrestCreates.Data.EFCore.MySql/DatabaseProviders/MySql
mkdir -p framework/src/CrestCreates.Data.EFCore.MySql/Configuration
```

- [ ] **Step 2: Add MySql.EntityFrameworkCore package version**

Edit `Directory.Packages.props` — add after the Npgsql line:

```xml
<PackageVersion Include="MySql.EntityFrameworkCore" Version="10.0.7" />
```

- [ ] **Step 3: Create .csproj**

Write `framework/src/CrestCreates.Data.EFCore.MySql/CrestCreates.Data.EFCore.MySql.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Data.EFCore.MySql</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Data.EFCore\CrestCreates.Data.EFCore.csproj" />
    <ProjectReference Include="..\CrestCreates.MultiTenancy.Abstract\CrestCreates.MultiTenancy.Abstract.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="MySql.EntityFrameworkCore" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Create MySqlTenantDatabaseProvisioner**

Write `framework/src/CrestCreates.Data.EFCore.MySql/DatabaseProviders/MySql/MySqlTenantDatabaseProvisioner.cs`:

```csharp
using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace CrestCreates.Data.EFCore.MySql.DatabaseProviders.MySql;

public class MySqlTenantDatabaseProvisioner : ITenantDatabaseProvisioner
{
    private readonly ILogger<MySqlTenantDatabaseProvisioner> _logger;
    private static readonly Regex ValidDatabaseNameRegex = new(@"^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

    public MySqlTenantDatabaseProvisioner(ILogger<MySqlTenantDatabaseProvisioner> logger)
    {
        _logger = logger;
    }

    public async Task<TenantDatabaseInitializeResult> InitializeAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default)
    {
        var databaseName = $"tenant_{context.TenantId:N}";

        if (!ValidDatabaseNameRegex.IsMatch(databaseName))
        {
            return TenantDatabaseInitializeResult.Failed($"Invalid database name: {databaseName}");
        }

        var serverConnectionString = GetServerConnectionString(context.ConnectionString!);

        try
        {
            await using var connection = new MySqlConnection(serverConnectionString);
            await connection.OpenAsync(cancellationToken);

            var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = @name";
            checkCommand.Parameters.AddWithValue("@name", databaseName);
            var exists = Convert.ToInt64(await checkCommand.ExecuteScalarAsync(cancellationToken)) > 0;

            if (!exists)
            {
                var escapedName = MySqlHelper.EscapeString(databaseName);
                var createCommand = connection.CreateCommand();
                createCommand.CommandText = $"CREATE DATABASE `{escapedName}`";
                await createCommand.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogInformation("Created database {DatabaseName} for tenant {TenantId}", databaseName, context.TenantId);
            }

            return TenantDatabaseInitializeResult.Succeeded();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize database for tenant {TenantId}", context.TenantId);
            return TenantDatabaseInitializeResult.Failed(ex.Message);
        }
    }

    private static string GetServerConnectionString(string tenantConnectionString)
    {
        var builder = new MySqlConnectionStringBuilder(tenantConnectionString);
        builder.Database = string.Empty;
        return builder.ConnectionString;
    }
}
```

- [ ] **Step 5: Create MySqlServiceCollectionExtensions**

Write `framework/src/CrestCreates.Data.EFCore.MySql/Configuration/MySqlServiceCollectionExtensions.cs`:

```csharp
using CrestCreates.Data.EFCore.MySql.DatabaseProviders.MySql;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Data.EFCore.MySql.Configuration;

public static class MySqlServiceCollectionExtensions
{
    /// <summary>
    /// Registers MySQL-specific EF Core services:
    /// <list type="bullet">
    ///   <item><see cref="ITenantDatabaseProvisioner"/> with MySQL CREATE DATABASE support</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddCrestCreatesEfCoreMySql(this IServiceCollection services)
    {
        services.TryAddScoped<MySqlTenantDatabaseProvisioner>();
        services.TryAddScoped<ITenantDatabaseProvisioner, MySqlTenantDatabaseProvisioner>();

        return services;
    }
}
```

- [ ] **Step 6: Add to solution file**

Edit `CrestCreates.slnx` — add to `/src/modules/Data/` folder:

```xml
<Project Path="framework/src/CrestCreates.Data.EFCore.MySql/CrestCreates.Data.EFCore.MySql.csproj" />
```

- [ ] **Step 7: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Data.EFCore.MySql/CrestCreates.Data.EFCore.MySql.csproj
```

Expected: Build succeeded.

- [ ] **Step 8: Commit**

```bash
git add framework/src/CrestCreates.Data.EFCore.MySql/ CrestCreates.slnx Directory.Packages.props
git commit -m "feat: add CrestCreates.Data.EFCore.MySql project"
```

---

## Phase 2: Modify Existing Projects

### Task 6: Clean up `CrestCreates.Data.EFCore` — remove DB-specific packages and code

**Files:**
- Modify: `framework/src/CrestCreates.Data.EFCore/CrestCreates.Data.EFCore.csproj`
- Modify: `framework/src/CrestCreates.Data.EFCore/Configuration/EfCoreDbContextServiceCollectionExtensions.cs`
- Move to RecycleBin: `framework/src/CrestCreates.Data.EFCore/DatabaseProviders/SqlServer/SqlServerTenantDatabaseProvisioner.cs`
- Move to RecycleBin: `framework/src/CrestCreates.Data.EFCore/MultiTenancy/EfCoreTenantDatabaseInitializer.cs`
- Move to RecycleBin: `framework/src/CrestCreates.Data.EFCore/MultiTenancy/EfCoreTenantMigrationRunner.cs`

- [ ] **Step 1: Remove SqlServer and Sqlite PackageRefs from .csproj**

Edit `framework/src/CrestCreates.Data.EFCore/CrestCreates.Data.EFCore.csproj` — remove these lines:

```xml
<!-- REMOVE these -->
<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
```

The updated .csproj should be:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Authorization.Abstractions\CrestCreates.Authorization.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.MultiTenancy.Abstract\CrestCreates.MultiTenancy.Abstract.csproj" />
    <ProjectReference Include="..\CrestCreates.Data.Abstractions\CrestCreates.Data.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Domain\CrestCreates.Domain.csproj" />
    <ProjectReference Include="..\CrestCreates.Infrastructure\CrestCreates.Infrastructure.csproj" />
    <ProjectReference Include="..\CrestCreates.Application\CrestCreates.Application.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Tasks">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
```

- [ ] **Step 2: Move SqlServerTenantDatabaseProvisioner to RecycleBin**

```bash
mkdir -p 99_RecycleBin
mv framework/src/CrestCreates.Data.EFCore/DatabaseProviders/SqlServer/SqlServerTenantDatabaseProvisioner.cs 99_RecycleBin/
```

- [ ] **Step 3: Move deprecated files to RecycleBin**

```bash
mv framework/src/CrestCreates.Data.EFCore/MultiTenancy/EfCoreTenantDatabaseInitializer.cs 99_RecycleBin/
mv framework/src/CrestCreates.Data.EFCore/MultiTenancy/EfCoreTenantMigrationRunner.cs 99_RecycleBin/
```

- [ ] **Step 4: Update EfCoreDbContextServiceCollectionExtensions**

Edit `framework/src/CrestCreates.Data.EFCore/Configuration/EfCoreDbContextServiceCollectionExtensions.cs`:

```csharp
using System;
using System.Linq;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Interceptors;
using CrestCreates.Data.EFCore.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Data.EFCore.Configuration;

public static class EfCoreDbContextServiceCollectionExtensions
{
    /// <summary>
    /// Registers EF Core infrastructure services using <see cref="CrestCreatesDbContext"/>.
    /// Projects using a custom DbContext should call <see cref="AddCrestCreatesEfCoreDbContext{TDbContext}"/> instead.
    /// </summary>
    public static IServiceCollection AddCrestCreatesEfCoreDbContext(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<AuditInterceptor>();
        services.TryAddScoped<MultiTenancyInterceptor>();
        services.TryAddSingleton<TenantAwareModelCacheKeyFactory>();

        // ITenantDatabaseProvisioner is registered by the provider-specific project
        // (e.g., AddCrestCreatesEfCoreSqlServer, AddCrestCreatesEfCorePostgreSql, AddCrestCreatesEfCoreMySql).
        // If no provider registers one, tenant database provisioning will fail at runtime.

        services.TryAddScoped<ITenantSchemaMigrator, EfCoreTenantSchemaMigrator>();
        services.TryAddScoped<ITenantInitializationStore, EfCoreTenantInitializationStore>();

        // Default factory for tenant migration: creates CrestCreatesDbContext.
        // Provider-specific projects should register their own factory BEFORE calling this method.
        services.TryAddSingleton<Func<string, DbContext>>(connectionString =>
        {
            var options = new DbContextOptionsBuilder<CrestCreatesDbContext>()
                .UseSqlServer(connectionString)
                .Options;
            return new CrestCreatesDbContext(options);
        });

        services.AddDbContext<CrestCreatesDbContext>((serviceProvider, optionsBuilder) =>
        {
            var contributors = serviceProvider.GetServices<IEfCoreDbContextOptionsContributor>().ToArray();
            if (contributors.Length == 0)
            {
                throw new InvalidOperationException(
                    "No EF Core DbContext options contributor was registered. Register a provider-specific IEfCoreDbContextOptionsContributor before adding CrestCreatesDbContext.");
            }

            foreach (var contributor in contributors)
            {
                contributor.Configure(serviceProvider, optionsBuilder);
            }

            optionsBuilder.AddInterceptors(
                serviceProvider.GetRequiredService<AuditInterceptor>(),
                serviceProvider.GetRequiredService<MultiTenancyInterceptor>());
            optionsBuilder.ReplaceService<IModelCacheKeyFactory, TenantAwareModelCacheKeyFactory>();
        });

        services.TryAdd(ServiceDescriptor.Scoped<IEntityFrameworkCoreDbContext>(sp => sp.GetRequiredService<CrestCreatesDbContext>()));
        services.TryAdd(ServiceDescriptor.Scoped<IDataBaseContext>(sp => sp.GetRequiredService<IEntityFrameworkCoreDbContext>()));
        services.TryAddScoped<DbContext>(sp => sp.GetRequiredService<CrestCreatesDbContext>());

        return services;
    }
}
```

Key changes:
- Removed `using CrestCreates.Application.Contracts.Interfaces;` and `using CrestCreates.Application.Tenants;`
- Removed `using CrestCreates.Data.EFCore.DatabaseProviders.SqlServer;`
- Removed `using CrestCreates.Data.EFCore.ValueConverters;`
- Removed `using Microsoft.AspNetCore.Http.Json;`
- Removed `using Microsoft.Extensions.Options;`
- Changed to `using CrestCreates.MultiTenancy.Abstract;`
- Removed `SqlServerTenantDatabaseProvisioner` registration
- Removed `ITenantDatabaseProvisioner` registration (now in provider-specific projects)
- The default `Func<string, DbContext>` still uses `UseSqlServer` for backward compat — provider projects override with `TryAddSingleton`

- [ ] **Step 5: Update EfCoreTenantSchemaMigrator namespace references**

Edit `framework/src/CrestCreates.Data.EFCore/MultiTenancy/EfCoreTenantSchemaMigrator.cs` — update using statements:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Data.EFCore.MultiTenancy;
// ... rest stays the same
```

- [ ] **Step 6: Update EfCoreTenantInitializationStore namespace references**

Edit `framework/src/CrestCreates.Data.EFCore/MultiTenancy/EfCoreTenantInitializationStore.cs` — update using statements to reference `CrestCreates.MultiTenancy.Abstract` instead of `CrestCreates.Application.Contracts.Interfaces` and `CrestCreates.Application.Tenants`.

- [ ] **Step 7: Build to verify (will fail until Phase 3 is done)**

```bash
dotnet build framework/src/CrestCreates.Data.EFCore/CrestCreates.Data.EFCore.csproj 2>&1 | head -50
```

Expected: May have errors from other projects referencing old namespaces. We'll fix those in Phase 3.

- [ ] **Step 8: Commit**

```bash
git add framework/src/CrestCreates.Data.EFCore/ 99_RecycleBin/
git commit -m "refactor: remove DB-specific packages from Data.EFCore, move SqlServer provisioner to SqlServer project"
```

---

## Phase 3: Move Multi-Tenancy Interfaces

### Task 7: Move tenant init interfaces from `Application.Contracts` to `MultiTenancy.Abstract`

**Files:**
- Create: `framework/src/CrestCreates.MultiTenancy.Abstract/ITenantDatabaseProvisioner.cs`
- Create: `framework/src/CrestCreates.MultiTenancy.Abstract/ITenantSchemaMigrator.cs`
- Create: `framework/src/CrestCreates.MultiTenancy.Abstract/ITenantDataSeedContributor.cs`
- Create: `framework/src/CrestCreates.MultiTenancy.Abstract/ITenantSettingDefaultsSeeder.cs`
- Create: `framework/src/CrestCreates.MultiTenancy.Abstract/ITenantFeatureDefaultsSeeder.cs`
- Create: `framework/src/CrestCreates.MultiTenancy.Abstract/ITenantInitializationEventSink.cs`
- Create: `framework/src/CrestCreates.MultiTenancy.Abstract/ITenantInitializationOrchestrator.cs`
- Create: `framework/src/CrestCreates.MultiTenancy.Abstract/ITenantStore.cs`
- Create: `framework/src/CrestCreates.MultiTenancy.Abstract/IPhaseResult.cs`
- Create: `framework/src/CrestCreates.MultiTenancy.Abstract/TenantInitializationContext.cs`
- Create: `framework/src/CrestCreates.MultiTenancy.Abstract/TenantDatabaseInitializeResult.cs`
- Create: `framework/src/CrestCreates.MultiTenancy.Abstract/TenantMigrationResult.cs`
- Create: `framework/src/CrestCreates.MultiTenancy.Abstract/TenantSeedResult.cs`
- Create: `framework/src/CrestCreates.MultiTenancy.Abstract/TenantInitializationResult.cs`
- Create: `framework/src/CrestCreates.MultiTenancy.Abstract/TenantInitializationStep.cs`
- Create: `framework/src/CrestCreates.MultiTenancy.Abstract/TenantConfiguration.cs`
- Move to RecycleBin: `framework/src/CrestCreates.Application.Contracts/Interfaces/ITenantDatabaseInitializer.cs`
- Move to RecycleBin: `framework/src/CrestCreates.Application.Contracts/Interfaces/ITenantMigrationRunner.cs`
- Move to RecycleBin: `framework/src/CrestCreates.Application.Contracts/Interfaces/ITenantDataSeeder.cs`
- Move to RecycleBin: `framework/src/CrestCreates.Application.Contracts/Interfaces/ITenantDatabaseProvisioner.cs`
- Move to RecycleBin: `framework/src/CrestCreates.Application.Contracts/Interfaces/ITenantSchemaMigrator.cs`
- Move to RecycleBin: `framework/src/CrestCreates.Application.Contracts/Interfaces/ITenantDataSeedContributor.cs`
- Move to RecycleBin: `framework/src/CrestCreates.Application.Contracts/Interfaces/ITenantSettingDefaultsSeeder.cs`
- Move to RecycleBin: `framework/src/CrestCreates.Application.Contracts/Interfaces/ITenantFeatureDefaultsSeeder.cs`
- Move to RecycleBin: `framework/src/CrestCreates.Application.Contracts/Interfaces/ITenantInitializationEventSink.cs`
- Move to RecycleBin: `framework/src/CrestCreates.Application.Contracts/Interfaces/IPhaseResult.cs`
- Modify: `framework/src/CrestCreates.MultiTenancy.Abstract/CrestCreates.MultiTenancy.Abstract.csproj`

- [ ] **Step 1: Update MultiTenancy.Abstract.csproj**

Edit `framework/src/CrestCreates.MultiTenancy.Abstract/CrestCreates.MultiTenancy.Abstract.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Microsoft.AspNetCore.Http.Abstractions" />
    </ItemGroup>

</Project>
```

No additional references needed — tenant init interfaces are pure abstractions with no external dependencies beyond the BCL.

- [ ] **Step 2: Create ITenantDatabaseProvisioner**

Write `framework/src/CrestCreates.MultiTenancy.Abstract/ITenantDatabaseProvisioner.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.MultiTenancy.Abstract;

public interface ITenantDatabaseProvisioner
{
    Task<TenantDatabaseInitializeResult> InitializeAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Create ITenantSchemaMigrator**

Write `framework/src/CrestCreates.MultiTenancy.Abstract/ITenantSchemaMigrator.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.MultiTenancy.Abstract;

public interface ITenantSchemaMigrator
{
    Task<TenantMigrationResult> RunAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Create ITenantDataSeedContributor**

Write `framework/src/CrestCreates.MultiTenancy.Abstract/ITenantDataSeedContributor.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.MultiTenancy.Abstract;

public interface ITenantDataSeedContributor
{
    Task<TenantSeedResult> SeedAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Create ITenantSettingDefaultsSeeder**

Write `framework/src/CrestCreates.MultiTenancy.Abstract/ITenantSettingDefaultsSeeder.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.MultiTenancy.Abstract;

public interface ITenantSettingDefaultsSeeder
{
    Task<TenantSeedResult> SeedAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 6: Create ITenantFeatureDefaultsSeeder**

Write `framework/src/CrestCreates.MultiTenancy.Abstract/ITenantFeatureDefaultsSeeder.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.MultiTenancy.Abstract;

public interface ITenantFeatureDefaultsSeeder
{
    Task<TenantSeedResult> SeedAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 7: Create ITenantInitializationEventSink**

Write `framework/src/CrestCreates.MultiTenancy.Abstract/ITenantInitializationEventSink.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.MultiTenancy.Abstract;

public interface ITenantInitializationEventSink
{
    Task PhaseStartedAsync(
        TenantInitializationContext context,
        string phaseName,
        CancellationToken cancellationToken = default);

    Task PhaseSucceededAsync(
        TenantInitializationContext context,
        string phaseName,
        CancellationToken cancellationToken = default);

    Task PhaseFailedAsync(
        TenantInitializationContext context,
        string phaseName,
        string error,
        CancellationToken cancellationToken = default);

    Task InfrastructureFailureAsync(
        TenantInitializationContext context,
        Exception exception,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 8: Create ITenantInitializationOrchestrator**

Write `framework/src/CrestCreates.MultiTenancy.Abstract/ITenantInitializationOrchestrator.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.MultiTenancy.Abstract;

public interface ITenantInitializationOrchestrator
{
    Task<TenantInitializationResult> InitializeAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 9: Create ITenantStore**

Write `framework/src/CrestCreates.MultiTenancy.Abstract/ITenantStore.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.MultiTenancy.Abstract;

public interface ITenantStore
{
    Task<TenantConfiguration?> FindAsync(string tenantIdOrName, CancellationToken cancellationToken = default);
    Task<TenantConfiguration?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TenantConfiguration>> GetListAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 10: Create TenantConfiguration**

Write `framework/src/CrestCreates.MultiTenancy.Abstract/TenantConfiguration.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace CrestCreates.MultiTenancy.Abstract;

public class TenantConfiguration
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ConnectionString { get; set; }
    public bool IsActive { get; set; }
    public Dictionary<string, string> ConnectionStrings { get; set; } = new();
}
```

- [ ] **Step 11: Create IPhaseResult**

Write `framework/src/CrestCreates.MultiTenancy.Abstract/IPhaseResult.cs`:

```csharp
namespace CrestCreates.MultiTenancy.Abstract;

public interface IPhaseResult
{
    bool Success { get; }
    string? Error { get; }
}
```

- [ ] **Step 12: Create TenantInitializationContext**

Write `framework/src/CrestCreates.MultiTenancy.Abstract/TenantInitializationContext.cs`:

```csharp
using System;

namespace CrestCreates.MultiTenancy.Abstract;

public class TenantInitializationContext
{
    public Guid TenantId { get; init; }
    public string TenantName { get; init; } = string.Empty;
    public string? ConnectionString { get; init; }
    public bool IsIndependentDatabase => !string.IsNullOrWhiteSpace(ConnectionString);
    public string CorrelationId { get; init; } = string.Empty;
    public Guid? RequestedByUserId { get; init; }
}
```

- [ ] **Step 13: Create result DTOs**

Write `framework/src/CrestCreates.MultiTenancy.Abstract/TenantDatabaseInitializeResult.cs`:

```csharp
namespace CrestCreates.MultiTenancy.Abstract;

public class TenantDatabaseInitializeResult : IPhaseResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public static TenantDatabaseInitializeResult Succeeded() => new() { Success = true };
    public static TenantDatabaseInitializeResult Failed(string error) => new() { Success = false, Error = error };
}
```

Write `framework/src/CrestCreates.MultiTenancy.Abstract/TenantMigrationResult.cs`:

```csharp
namespace CrestCreates.MultiTenancy.Abstract;

public class TenantMigrationResult : IPhaseResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public static TenantMigrationResult Succeeded() => new() { Success = true };
    public static TenantMigrationResult Failed(string error) => new() { Success = false, Error = error };
}
```

Write `framework/src/CrestCreates.MultiTenancy.Abstract/TenantSeedResult.cs`:

```csharp
namespace CrestCreates.MultiTenancy.Abstract;

public class TenantSeedResult : IPhaseResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public static TenantSeedResult Succeeded() => new() { Success = true };
    public static TenantSeedResult Failed(string error) => new() { Success = false, Error = error };
}
```

Write `framework/src/CrestCreates.MultiTenancy.Abstract/TenantInitializationStep.cs`:

```csharp
using System;

namespace CrestCreates.MultiTenancy.Abstract;

public class TenantInitializationStep
{
    public string Name { get; set; } = string.Empty;
    public TenantInitializationStepStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Error { get; set; }
}

public enum TenantInitializationStepStatus
{
    Running,
    Succeeded,
    Failed,
    Skipped
}
```

Write `framework/src/CrestCreates.MultiTenancy.Abstract/TenantInitializationResult.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace CrestCreates.MultiTenancy.Abstract;

public class TenantInitializationResult
{
    public bool Success { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public string? Error { get; init; }
    public IReadOnlyList<TenantInitializationStep> Steps { get; init; } = Array.Empty<TenantInitializationStep>();

    public static TenantInitializationResult Succeeded(string correlationId, IReadOnlyList<TenantInitializationStep> steps)
        => new() { Success = true, CorrelationId = correlationId, Steps = steps };

    public static TenantInitializationResult Failed(string correlationId, string error, IReadOnlyList<TenantInitializationStep> steps)
        => new() { Success = false, CorrelationId = correlationId, Error = error, Steps = steps };
}
```

- [ ] **Step 14: Move old files to RecycleBin**

```bash
# Move deprecated interfaces (to be deleted)
mv framework/src/CrestCreates.Application.Contracts/Interfaces/ITenantDatabaseInitializer.cs 99_RecycleBin/
mv framework/src/CrestCreates.Application.Contracts/Interfaces/ITenantMigrationRunner.cs 99_RecycleBin/
mv framework/src/CrestCreates.Application.Contracts/Interfaces/ITenantDataSeeder.cs 99_RecycleBin/

# Move interfaces now in MultiTenancy.Abstract
mv framework/src/CrestCreates.Application.Contracts/Interfaces/ITenantDatabaseProvisioner.cs 99_RecycleBin/
mv framework/src/CrestCreates.Application.Contracts/Interfaces/ITenantSchemaMigrator.cs 99_RecycleBin/
mv framework/src/CrestCreates.Application.Contracts/Interfaces/ITenantDataSeedContributor.cs 99_RecycleBin/
mv framework/src/CrestCreates.Application.Contracts/Interfaces/ITenantSettingDefaultsSeeder.cs 99_RecycleBin/
mv framework/src/CrestCreates.Application.Contracts/Interfaces/ITenantFeatureDefaultsSeeder.cs 99_RecycleBin/
mv framework/src/CrestCreates.Application.Contracts/Interfaces/ITenantInitializationEventSink.cs 99_RecycleBin/
mv framework/src/CrestCreates.Application.Contracts/Interfaces/IPhaseResult.cs 99_RecycleBin/
```

- [ ] **Step 15: Build MultiTenancy.Abstract**

```bash
dotnet build framework/src/CrestCreates.MultiTenancy.Abstract/CrestCreates.MultiTenancy.Abstract.csproj
```

Expected: Build succeeded.

- [ ] **Step 16: Commit**

```bash
git add framework/src/CrestCreates.MultiTenancy.Abstract/ 99_RecycleBin/
git commit -m "refactor: move tenant init interfaces from Application.Contracts to MultiTenancy.Abstract"
```

---

### Task 8: Move `TenantInitializationOrchestrator` from `Application` to `MultiTenancy`

**Files:**
- Create: `framework/src/CrestCreates.MultiTenancy/TenantInitializationOrchestrator.cs`
- Create: `framework/src/CrestCreates.MultiTenancy/TenantLifecycleService.cs`
- Create: `framework/src/CrestCreates.MultiTenancy/DefaultTenantStore.cs`
- Move to RecycleBin: `framework/src/CrestCreates.Application/Tenants/TenantInitializationOrchestrator.cs`
- Move to RecycleBin: `framework/src/CrestCreates.Application/Tenants/ITenantInitializationStore.cs`
- Create: `framework/src/CrestCreates.MultiTenancy/ITenantInitializationStore.cs`

- [ ] **Step 1: Create ITenantInitializationStore in MultiTenancy**

Write `framework/src/CrestCreates.MultiTenancy/ITenantInitializationStore.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;

namespace CrestCreates.MultiTenancy;

public interface ITenantInitializationStore
{
    Task<TenantInitializationRecord?> TryBeginInitializationAsync(
        Guid tenantId, string correlationId, CancellationToken cancellationToken = default);

    Task<TenantInitializationRecord> ForceBeginInitializationAsync(
        Guid tenantId, string correlationId, CancellationToken cancellationToken = default);

    Task<TenantInitializationRecord?> GetLatestAsync(
        Guid tenantId, CancellationToken cancellationToken = default);

    Task UpdateAsync(
        TenantInitializationRecord record, CancellationToken cancellationToken = default);

    Task ForceFailAsync(
        Guid tenantId, TenantInitializationRecord record, string error, CancellationToken cancellationToken = default);

    Task CompleteInitializationAsync(
        Guid tenantId, TenantInitializationRecord record, CancellationToken cancellationToken = default);

    Task FailInitializationAsync(
        Guid tenantId, TenantInitializationRecord record, string error, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Create TenantInitializationOrchestrator in MultiTenancy**

Write `framework/src/CrestCreates.MultiTenancy/TenantInitializationOrchestrator.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Shared;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.Logging;

namespace CrestCreates.MultiTenancy;

public class TenantInitializationOrchestrator : ITenantInitializationOrchestrator
{
    private readonly ITenantDatabaseProvisioner _dbProvisioner;
    private readonly ITenantSchemaMigrator _schemaMigrator;
    private readonly IReadOnlyList<ITenantDataSeedContributor> _dataSeedContributors;
    private readonly ITenantSettingDefaultsSeeder _settingsSeeder;
    private readonly ITenantFeatureDefaultsSeeder _featuresSeeder;
    private readonly ITenantInitializationStore _store;
    private readonly ICurrentTenant _currentTenant;
    private readonly ITenantInitializationEventSink _eventSink;

    private const int MaxErrorLength = 2000;

    public TenantInitializationOrchestrator(
        ITenantDatabaseProvisioner dbProvisioner,
        ITenantSchemaMigrator schemaMigrator,
        IEnumerable<ITenantDataSeedContributor> dataSeedContributors,
        ITenantSettingDefaultsSeeder settingsSeeder,
        ITenantFeatureDefaultsSeeder featuresSeeder,
        ITenantInitializationStore store,
        ICurrentTenant currentTenant,
        ITenantInitializationEventSink eventSink)
    {
        _dbProvisioner = dbProvisioner;
        _schemaMigrator = schemaMigrator;
        _dataSeedContributors = dataSeedContributors as IReadOnlyList<ITenantDataSeedContributor> ?? new List<ITenantDataSeedContributor>(dataSeedContributors);
        _settingsSeeder = settingsSeeder;
        _featuresSeeder = featuresSeeder;
        _store = store;
        _currentTenant = currentTenant;
        _eventSink = eventSink;
    }

    public async Task<TenantInitializationResult> InitializeAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default)
    {
        var record = await _store.TryBeginInitializationAsync(
            context.TenantId, context.CorrelationId, cancellationToken);

        if (record is null)
            return TenantInitializationResult.Failed(
                context.CorrelationId,
                "Tenant is already initializing or initialized.",
                Array.Empty<TenantInitializationStep>());

        return await RunPhasesAsync(context, record, cancellationToken);
    }

    public async Task<TenantInitializationResult> InitializeWithRecordAsync(
        TenantInitializationContext context,
        TenantInitializationRecord record,
        CancellationToken cancellationToken = default)
    {
        return await RunPhasesAsync(context, record, cancellationToken);
    }

    private async Task<TenantInitializationResult> RunPhasesAsync(
        TenantInitializationContext context,
        TenantInitializationRecord record,
        CancellationToken cancellationToken)
    {
        var steps = new List<TenantInitializationStep>();

        try
        {
            if (context.IsIndependentDatabase)
            {
                var step1 = await ExecutePhaseAsync("DatabaseInitialize", record,
                    context,
                    async ct => await _dbProvisioner.InitializeAsync(context, ct),
                    cancellationToken);
                steps.Add(step1);
                if (step1.Status != TenantInitializationStepStatus.Succeeded)
                    return await BuildFailureResultAsync(context, record, steps, step1.Error, cancellationToken);

                var step2 = await ExecutePhaseAsync("Migration", record,
                    context,
                    async ct => await _schemaMigrator.RunAsync(context, ct),
                    cancellationToken);
                steps.Add(step2);
                if (step2.Status != TenantInitializationStepStatus.Succeeded)
                    return await BuildFailureResultAsync(context, record, steps, step2.Error, cancellationToken);
            }

            var tenantInfo = new TenantInfo(
                context.TenantId.ToString(),
                context.TenantName,
                context.ConnectionString);
            using var tenantScope = _currentTenant.Change(tenantInfo);

            var step3 = await ExecutePhaseAsync("DataSeed", record,
                context,
                async ct => await RunDataSeedContributorsAsync(context, ct),
                cancellationToken);
            steps.Add(step3);
            if (step3.Status != TenantInitializationStepStatus.Succeeded)
                return await BuildFailureResultAsync(context, record, steps, step3.Error, cancellationToken);

            var step4 = await ExecutePhaseAsync("SettingsDefaults", record,
                context,
                async ct => await _settingsSeeder.SeedAsync(context, ct),
                cancellationToken);
            steps.Add(step4);
            if (step4.Status != TenantInitializationStepStatus.Succeeded)
                return await BuildFailureResultAsync(context, record, steps, step4.Error, cancellationToken);

            var step5 = await ExecutePhaseAsync("FeatureDefaults", record,
                context,
                async ct => await _featuresSeeder.SeedAsync(context, ct),
                cancellationToken);
            steps.Add(step5);
            if (step5.Status != TenantInitializationStepStatus.Succeeded)
                return await BuildFailureResultAsync(context, record, steps, step5.Error, cancellationToken);

            await _store.CompleteInitializationAsync(context.TenantId, record, cancellationToken);

            return TenantInitializationResult.Succeeded(context.CorrelationId, steps);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _eventSink.InfrastructureFailureAsync(context, ex, cancellationToken);
            throw;
        }
    }

    private async Task<TenantSeedResult> RunDataSeedContributorsAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken)
    {
        foreach (var contributor in _dataSeedContributors)
        {
            var result = await contributor.SeedAsync(context, cancellationToken);
            if (!result.Success)
            {
                return result;
            }
        }

        return TenantSeedResult.Succeeded();
    }

    private async Task<TenantInitializationStep> ExecutePhaseAsync(
        string phaseName,
        TenantInitializationRecord record,
        TenantInitializationContext context,
        Func<CancellationToken, Task<IPhaseResult>> phaseAction,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;

        record.SetCurrentStep(phaseName);
        record.AppendStepResult(phaseName, TenantInitializationStepStatus.Running, startedAt, null, null);
        await _store.UpdateAsync(record, cancellationToken);
        await _eventSink.PhaseStartedAsync(context, phaseName, cancellationToken);

        try
        {
            var result = await phaseAction(cancellationToken);
            var completedAt = DateTime.UtcNow;

            if (result.Success)
            {
                record.AppendStepResult(phaseName, TenantInitializationStepStatus.Succeeded,
                    startedAt, completedAt, null);
                await _store.UpdateAsync(record, cancellationToken);
                await _eventSink.PhaseSucceededAsync(context, phaseName, cancellationToken);

                return new TenantInitializationStep
                {
                    Name = phaseName,
                    Status = TenantInitializationStepStatus.Succeeded,
                    StartedAt = startedAt,
                    CompletedAt = completedAt
                };
            }
            else
            {
                var error = Truncate(result.Error);
                record.AppendStepResult(phaseName, TenantInitializationStepStatus.Failed,
                    startedAt, completedAt, error);
                await _store.UpdateAsync(record, cancellationToken);
                await _eventSink.PhaseFailedAsync(context, phaseName, error, cancellationToken);

                return new TenantInitializationStep
                {
                    Name = phaseName,
                    Status = TenantInitializationStepStatus.Failed,
                    StartedAt = startedAt,
                    CompletedAt = completedAt,
                    Error = error
                };
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var completedAt = DateTime.UtcNow;
            var error = Truncate(ex.Message);
            record.AppendStepResult(phaseName, TenantInitializationStepStatus.Failed,
                startedAt, completedAt, error);
            await _store.UpdateAsync(record, cancellationToken);
            await _eventSink.PhaseFailedAsync(context, phaseName, error, cancellationToken);

            return new TenantInitializationStep
            {
                Name = phaseName,
                Status = TenantInitializationStepStatus.Failed,
                StartedAt = startedAt,
                CompletedAt = completedAt,
                Error = error
            };
        }
    }

    private async Task<TenantInitializationResult> BuildFailureResultAsync(
        TenantInitializationContext context,
        TenantInitializationRecord record,
        List<TenantInitializationStep> steps,
        string? failedStepError,
        CancellationToken cancellationToken)
    {
        var detailedError = failedStepError ?? "Tenant initialization failed.";
        var publicError = Sanitize(detailedError);

        await _store.FailInitializationAsync(context.TenantId, record, publicError, cancellationToken);

        return TenantInitializationResult.Failed(context.CorrelationId, publicError, steps);
    }

    private static string Sanitize(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return "Tenant initialization failed.";
        var sanitized = error
            .Replace("Data Source=", "[redacted]")
            .Replace("Server=", "[redacted]")
            .Replace("Password=", "[redacted]")
            .Replace("User ID=", "[redacted]");
        return Truncate(sanitized);
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= MaxErrorLength ? value : value[..MaxErrorLength];
    }
}
```

Note: Removed the deprecated constructor and `LegacyTenantInitializationEventSink` inner class.

- [ ] **Step 3: Create TenantLifecycleService**

Write `framework/src/CrestCreates.MultiTenancy/TenantLifecycleService.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.Logging;

namespace CrestCreates.MultiTenancy;

public class TenantLifecycleService
{
    private readonly TenantManager _tenantManager;
    private readonly ITenantInitializationOrchestrator _orchestrator;
    private readonly ILogger<TenantLifecycleService> _logger;

    public TenantLifecycleService(
        TenantManager tenantManager,
        ITenantInitializationOrchestrator orchestrator,
        ILogger<TenantLifecycleService> logger)
    {
        _tenantManager = tenantManager;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public async Task<TenantInitializationResult> CreateAndInitializeAsync(
        string tenantName,
        string? connectionString,
        Guid? requestedByUserId = null,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantManager.CreateAsync(tenantName, connectionString, cancellationToken);

        var context = new TenantInitializationContext
        {
            TenantId = tenant.Id,
            TenantName = tenant.Name,
            ConnectionString = connectionString,
            CorrelationId = Guid.NewGuid().ToString("N"),
            RequestedByUserId = requestedByUserId
        };

        _logger.LogInformation("Starting tenant initialization for {TenantName} ({TenantId})", tenantName, tenant.Id);
        return await _orchestrator.InitializeAsync(context, cancellationToken);
    }
}
```

- [ ] **Step 4: Create DefaultTenantStore**

Write `framework/src/CrestCreates.MultiTenancy/DefaultTenantStore.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.MultiTenancy;

public class DefaultTenantStore : ITenantStore
{
    private readonly ITenantProvider _tenantProvider;

    public DefaultTenantStore(ITenantProvider tenantProvider)
    {
        _tenantProvider = tenantProvider;
    }

    public async Task<TenantConfiguration?> FindAsync(string tenantIdOrName, CancellationToken cancellationToken = default)
    {
        // Delegate to the configured tenant provider
        var tenants = await _tenantProvider.GetTenantsAsync(cancellationToken);
        foreach (var t in tenants)
        {
            if (string.Equals(t.Id, tenantIdOrName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Name, tenantIdOrName, StringComparison.OrdinalIgnoreCase))
            {
                return MapToConfiguration(t);
            }
        }
        return null;
    }

    public async Task<TenantConfiguration?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await FindAsync(tenantId.ToString(), cancellationToken);
    }

    public async Task<IReadOnlyList<TenantConfiguration>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var tenants = await _tenantProvider.GetTenantsAsync(cancellationToken);
        var result = new List<TenantConfiguration>();
        foreach (var t in tenants)
        {
            result.Add(MapToConfiguration(t));
        }
        return result;
    }

    private static TenantConfiguration MapToConfiguration(ITenantInfo tenant)
    {
        return new TenantConfiguration
        {
            Id = Guid.TryParse(tenant.Id, out var id) ? id : Guid.Empty,
            Name = tenant.Name ?? string.Empty,
            ConnectionString = tenant.ConnectionString,
            IsActive = true
        };
    }
}
```

- [ ] **Step 5: Move old files to RecycleBin**

```bash
mv framework/src/CrestCreates.Application/Tenants/TenantInitializationOrchestrator.cs 99_RecycleBin/
mv framework/src/CrestCreates.Application/Tenants/ITenantInitializationStore.cs 99_RecycleBin/
```

- [ ] **Step 6: Update MultiTenancy.csproj**

Edit `framework/src/CrestCreates.MultiTenancy/CrestCreates.MultiTenancy.csproj` — add `Microsoft.Extensions.Logging.Abstractions` package reference if not already present (needed by TenantLifecycleService and orchestrator):

```xml
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
```

- [ ] **Step 7: Build MultiTenancy**

```bash
dotnet build framework/src/CrestCreates.MultiTenancy/CrestCreates.MultiTenancy.csproj
```

Expected: Build succeeded.

- [ ] **Step 8: Commit**

```bash
git add framework/src/CrestCreates.MultiTenancy/ 99_RecycleBin/
git commit -m "refactor: move TenantInitializationOrchestrator to MultiTenancy, add TenantLifecycleService and DefaultTenantStore"
```

---

## Phase 4: Fix All Compilation Errors

### Task 9: Update all references and fix compilation

**Files:**
- Modify: All files referencing old namespaces
- Modify: `framework/src/CrestCreates.Data.EFCore.PostgreSql/Configuration/NpgsqlServiceCollectionExtensions.cs`
- Modify: `framework/src/CrestCreates.Data.EFCore/MultiTenancy/EfCoreTenantInitializationStore.cs`
- Modify: `framework/src/CrestCreates.Data.EFCore/MultiTenancy/EfCoreTenantSchemaMigrator.cs`
- Modify: `framework/src/CrestCreates.Data.EFCore/MultiTenancy/TenantConnectionStringResolver.cs`
- Modify: `framework/src/CrestCreates.Application/CrestCreates.Application.csproj`
- Modify: `framework/src/CrestCreates.Web/CrestCreates.Web.csproj`

- [ ] **Step 1: Find all files referencing old namespaces**

```bash
grep -rn "CrestCreates.Application.Contracts.Interfaces.ITenant" --include="*.cs" framework/src/ samples/
grep -rn "CrestCreates.Application.Contracts.DTOs.Tenants" --include="*.cs" framework/src/ samples/
grep -rn "CrestCreates.Application.Tenants" --include="*.cs" framework/src/ samples/
grep -rn "CrestCreates.Data.EFCore.DatabaseProviders.SqlServer" --include="*.cs" framework/src/ samples/
```

- [ ] **Step 2: Update NpgsqlServiceCollectionExtensions**

Edit `framework/src/CrestCreates.Data.EFCore.PostgreSql/Configuration/NpgsqlServiceCollectionExtensions.cs`:

```csharp
using CrestCreates.AspNetCore.Authentication.OpenIddict;
using CrestCreates.Data.EFCore.Configuration;
using CrestCreates.Data.EFCore.PostgreSql.DatabaseProviders.PostgreSQL;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Data.EFCore.PostgreSql.Configuration;

public static class NpgsqlServiceCollectionExtensions
{
    public static IServiceCollection AddCrestCreatesEfCorePostgreSql(this IServiceCollection services)
    {
        services.AddSingleton<IEfCoreDbContextOptionsContributor, NpgsqlDbContextOptionsContributor>();

        services.AddDbContext<OpenIddictDbContext>((serviceProvider, optionsBuilder) =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var connectionString = configuration.GetConnectionString("Default");
            optionsBuilder.UseNpgsql(connectionString);
        });

        services.AddScoped<PostgreSqlTenantDatabaseProvisioner>();
        services.AddScoped<ITenantDatabaseProvisioner, PostgreSqlTenantDatabaseProvisioner>();

        return services;
    }
}
```

- [ ] **Step 3: Update PostgreSqlTenantDatabaseProvisioner**

Edit `framework/src/CrestCreates.Data.EFCore.PostgreSql/DatabaseProviders/PostgreSQL/PostgreSqlTenantDatabaseProvisioner.cs` — update using to reference `CrestCreates.MultiTenancy.Abstract` instead of `CrestCreates.Application.Contracts.Interfaces`.

- [ ] **Step 4: Update EfCoreTenantInitializationStore**

Edit `framework/src/CrestCreates.Data.EFCore/MultiTenancy/EfCoreTenantInitializationStore.cs` — update using statements:
- Replace `using CrestCreates.Application.Contracts.Interfaces;` with `using CrestCreates.MultiTenancy.Abstract;`
- Replace `using CrestCreates.Application.Tenants;` with `using CrestCreates.MultiTenancy;`

- [ ] **Step 5: Update EfCoreTenantSchemaMigrator**

Edit `framework/src/CrestCreates.Data.EFCore/MultiTenancy/EfCoreTenantSchemaMigrator.cs` — update using:
- Replace `using CrestCreates.Application.Contracts.DTOs.Tenants;` with `using CrestCreates.MultiTenancy.Abstract;`
- Replace `using CrestCreates.Application.Contracts.Interfaces;` with `using CrestCreates.MultiTenancy.Abstract;`

- [ ] **Step 6: Update Application.csproj**

Edit `framework/src/CrestCreates.Application/CrestCreates.Application.csproj` — the `TenantInitializationOrchestrator` moved out, but `Application` still needs `MultiTenancy` reference (already present). No changes needed unless there are other tenant-related files.

- [ ] **Step 7: Update Web.csproj**

Edit `framework/src/CrestCreates.Web/CrestCreates.Web.csproj` — add reference to `CrestCreates.Data.EFCore.SqlServer`:

```xml
<ProjectReference Include="..\CrestCreates.Data.EFCore.SqlServer\CrestCreates.Data.EFCore.SqlServer.csproj" />
```

- [ ] **Step 8: Update all remaining references**

Run a global search and replace across all .cs files:

```bash
# Replace namespace references
find framework/src/ samples/ -name "*.cs" -exec sed -i \
  -e 's/using CrestCreates\.Application\.Contracts\.Interfaces;/using CrestCreates.MultiTenancy.Abstract;/g' \
  -e 's/using CrestCreates\.Application\.Contracts\.DTOs\.Tenants;/using CrestCreates.MultiTenancy.Abstract;/g' \
  -e 's/CrestCreates\.Application\.Contracts\.Interfaces\./CrestCreates.MultiTenancy.Abstract./g' \
  -e 's/CrestCreates\.Application\.Contracts\.DTOs\.Tenants\./CrestCreates.MultiTenancy.Abstract./g' \
  {} +
```

- [ ] **Step 9: Full solution build**

```bash
dotnet build 2>&1 | tail -100
```

Expected: Identify remaining compilation errors. Fix each one by updating namespace references.

- [ ] **Step 10: Fix remaining errors iteratively**

For each compilation error:
1. Identify the file and the incorrect reference
2. Update the using statement or type reference
3. Rebuild

- [ ] **Step 11: Verify clean build**

```bash
dotnet build
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 12: Commit**

```bash
git add -A
git commit -m "fix: update all namespace references after multi-tenancy and data provider refactoring"
```

---

## Phase 5: Clean Up & Remove Stubs

### Task 10: Remove empty `OrmProviders.*` stubs and final cleanup

**Files:**
- Move to RecycleBin: `framework/src/CrestCreates.OrmProviders.Abstract/`
- Move to RecycleBin: `framework/src/CrestCreates.OrmProviders.EFCore/`
- Move to RecycleBin: `framework/src/CrestCreates.OrmProviders.FreeSqlProvider/`
- Move to RecycleBin: `framework/src/CrestCreates.OrmProviders.SqlSugar/`
- Modify: `CrestCreates.slnx` (if any OrmProviders entries exist)

- [ ] **Step 1: Move empty stubs to RecycleBin**

```bash
mv framework/src/CrestCreates.OrmProviders.Abstract 99_RecycleBin/
mv framework/src/CrestCreates.OrmProviders.EFCore 99_RecycleBin/
mv framework/src/CrestCreates.OrmProviders.FreeSqlProvider 99_RecycleBin/
mv framework/src/CrestCreates.OrmProviders.SqlSugar 99_RecycleBin/
```

- [ ] **Step 2: Verify no references to OrmProviders remain**

```bash
grep -rn "OrmProviders" --include="*.csproj" --include="*.cs" --include="*.slnx" .
```

Expected: Only `CrestCreates.OrmProviders.MongoDB` and `CrestCreates.OrmProviders.Tests` remain (these are real projects).

- [ ] **Step 3: Final clean build**

```bash
dotnet build
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add 99_RecycleBin/ CrestCreates.slnx
git commit -m "chore: remove empty OrmProviders.* stub directories"
```

---

## Phase 6: Update Samples

### Task 11: Update LibraryManagement sample

**Files:**
- Modify: `samples/LibraryManagement/LibraryManagement.EntityFrameworkCore/LibraryManagement.EntityFrameworkCore.csproj`
- Modify: `samples/LibraryManagement/LibraryManagement.Web/LibraryManagement.Web.csproj`

- [ ] **Step 1: Update LibraryManagement.EntityFrameworkCore.csproj**

Edit `samples/LibraryManagement/LibraryManagement.EntityFrameworkCore/LibraryManagement.EntityFrameworkCore.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <Import Project="..\..\..\build\CrestCreates.BuildTasks\CrestCreates.Modules.props" />

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\framework\src\CrestCreates.AuditLogging\CrestCreates.AuditLogging.csproj" />
    <ProjectReference Include="..\..\..\framework\src\CrestCreates.Data.EFCore\CrestCreates.Data.EFCore.csproj" />
    <ProjectReference Include="..\..\..\framework\src\CrestCreates.Data.EFCore.PostgreSql\CrestCreates.Data.EFCore.PostgreSql.csproj" />
    <ProjectReference Include="..\..\..\framework\src\CrestCreates.Modularity\CrestCreates.Modularity.csproj" />
    <ProjectReference Include="..\LibraryManagement.Application\LibraryManagement.Application.csproj" />
    <ProjectReference Include="..\LibraryManagement.Domain\LibraryManagement.Domain.csproj" />
  </ItemGroup>

  <!-- Npgsql comes transitively from CrestCreates.Data.EFCore.PostgreSql -->
</Project>
```

Key change: Added `CrestCreates.Data.EFCore.PostgreSql` reference and removed direct `Npgsql.EntityFrameworkCore.PostgreSQL` PackageReference.

- [ ] **Step 2: Update LibraryManagement.Web.csproj**

No changes needed — already references `CrestCreates.MultiTenancy`. Verify it compiles.

- [ ] **Step 3: Build sample**

```bash
dotnet build samples/LibraryManagement/LibraryManagement.Web/LibraryManagement.Web.csproj
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add samples/LibraryManagement/
git commit -m "refactor: update LibraryManagement sample to use Data.EFCore.PostgreSql"
```

---

### Task 12: Update SaaSHelpdesk sample

**Files:**
- Modify: `samples/SaaSHelpdesk/SaaSHelpdesk.EntityFrameworkCore/SaaSHelpdesk.EntityFrameworkCore.csproj`

- [ ] **Step 1: Update SaaSHelpdesk.EntityFrameworkCore.csproj**

Edit `samples/SaaSHelpdesk/SaaSHelpdesk.EntityFrameworkCore/SaaSHelpdesk.EntityFrameworkCore.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <Import Project="../../../build/CrestCreates.BuildTasks/CrestCreates.Modules.props" />

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../../../framework/src/CrestCreates.AuditLogging/CrestCreates.AuditLogging.csproj" />
    <ProjectReference Include="../../../framework/src/CrestCreates.Data.EFCore/CrestCreates.Data.EFCore.csproj" />
    <ProjectReference Include="../../../framework/src/CrestCreates.Data.EFCore.PostgreSql/CrestCreates.Data.EFCore.PostgreSql.csproj" />
    <ProjectReference Include="../../../framework/src/CrestCreates.Modularity/CrestCreates.Modularity.csproj" />
    <ProjectReference Include="../../../framework/src/CrestCreates.OpenApi/CrestCreates.OpenApi.csproj" />
    <ProjectReference Include="../SaaSHelpdesk.Application/SaaSHelpdesk.Application.csproj" />
    <ProjectReference Include="../SaaSHelpdesk.Domain/SaaSHelpdesk.Domain.csproj" />
  </ItemGroup>

  <!-- Npgsql comes transitively from CrestCreates.Data.EFCore.PostgreSql -->
</Project>
```

- [ ] **Step 2: Build sample**

```bash
dotnet build samples/SaaSHelpdesk/SaaSHelpdesk.Web/SaaSHelpdesk.Web.csproj
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add samples/SaaSHelpdesk/
git commit -m "refactor: update SaaSHelpdesk sample to use Data.EFCore.PostgreSql"
```

---

## Phase 7: Run Tests

### Task 13: Run all tests and fix failures

- [ ] **Step 1: Run all tests**

```bash
dotnet test 2>&1 | tail -50
```

- [ ] **Step 2: Fix test compilation errors**

If tests reference old namespaces, update them:
```bash
find framework/test/ -name "*.cs" -exec sed -i \
  -e 's/using CrestCreates\.Application\.Contracts\.Interfaces;/using CrestCreates.MultiTenancy.Abstract;/g' \
  -e 's/CrestCreates\.Application\.Contracts\.Interfaces\./CrestCreates.MultiTenancy.Abstract./g' \
  {} +
```

- [ ] **Step 3: Re-run tests**

```bash
dotnet test
```

Expected: All tests pass.

- [ ] **Step 4: Run specific test projects**

```bash
dotnet test framework/test/CrestCreates.OrmProviders.Tests/CrestCreates.OrmProviders.Tests.csproj
dotnet test framework/test/CrestCreates.Application.Tests/CrestCreates.Application.Tests.csproj
dotnet test framework/test/CrestCreates.IntegrationTests/CrestCreates.IntegrationTests.csproj
```

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "test: fix test references after multi-tenancy refactoring"
```

---

## Phase 8: Final Verification

### Task 14: Final build and AoT verification

- [ ] **Step 1: Full solution build**

```bash
dotnet build
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 2: Build sample app**

```bash
dotnet build samples/LibraryManagement/LibraryManagement.Web/LibraryManagement.Web.csproj
```

Expected: Build succeeded.

- [ ] **Step 3: Verify AoT publish (if applicable)**

```bash
dotnet publish samples/LibraryManagement/LibraryManagement.Web/LibraryManagement.Web.csproj -c Release -r linux-x64 --self-contained true -p:CrestCreatesPublishMode=aot 2>&1 | tail -20
```

Expected: Publish succeeded (or identify AoT-specific issues to fix).

- [ ] **Step 4: Commit final state**

```bash
git add -A
git commit -m "chore: final verification after data provider and multi-tenancy refactoring"
```

---

## Follow-up (Deferred)

These items from the design spec are deferred to a separate PR:

1. **`AuditLogging.Abstractions`** — extract audit contracts (interfaces, attributes) from `AuditLogging` into a separate project. The audit logging persistence chain is already clean and doesn't need changes for this refactoring.
2. **`AuditInterceptor` move** — moving from `Data.EFCore` to `AuditLogging` requires careful handling of the EF Core `SaveChangesInterceptor` dependency. Best done as a focused follow-up.
3. **`SqlSugar.*` and `FreeSql.*` DB-specific sub-projects** — stub projects only in this plan. Full implementations deferred until those ORMs need multi-DB provider support.
4. **`CrestCreates.Data.Core` population** — currently a placeholder module. Will be populated as common data infrastructure code is extracted from individual ORM projects.
