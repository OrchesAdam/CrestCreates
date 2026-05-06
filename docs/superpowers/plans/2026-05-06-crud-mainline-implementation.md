# CRUD Mainline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make generated CRUD the official mainline: one marked entity produces DTOs, contract, AppService, permissions, mappings, and generated Dynamic API endpoints with descriptor query, UoW, audit, concurrency, and global exception behavior.

**Architecture:** The implementation updates `CrudServiceSourceGenerator` so it emits a directly registered partial `{Entity}AppService` using `ICrestRepositoryBase<TEntity,TKey>`, not inheritance-oriented CRUD base classes. Generated CRUD services are discovered by `DynamicApiAotSourceGenerator`; MVC `CrudControllerBase` remains compatibility-only. Querying uses `PagedRequestDto.Filters` / `Sorts` through `QueryExecutor`, and write operations use permissions, `[UnitOfWorkMo]`, generated mapping, audit helpers, and concurrency exceptions.

**Tech Stack:** .NET, C#, Roslyn incremental source generators, xUnit, FluentAssertions, ASP.NET Core generated Dynamic API, EF Core repository abstractions.

---

## Scope Check

This plan implements one feature: CRUD generated mainline. It touches code generation, generated Dynamic API binding, application/query behavior, and tests because those are one connected mainline. It does not implement MongoDB, MVC CRUD controller expansion, AutoMapper, batch import/export, or legacy `CrudServiceBase` enhancements.

---

## File Map

| File | Action | Responsibility |
|---|---|---|
| `framework/tools/CrestCreates.CodeGenerator/CrudServiceGenerator/CrudServiceSourceGenerator.cs` | Modify | Generate DTOs, `I{Entity}AppService`, concrete partial `{Entity}AppService`, permissions, mapping declarations, descriptor query allowlists, and CRUD methods. |
| `framework/tools/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiAotSourceGenerator.cs` | Modify | Bind generated CRUD `expectedStamp` delete parameter from `If-Match` header. |
| `framework/tools/CrestCreates.CodeGenerator/Authorization/AuthorizationHelper.cs` | Modify | Align CRUD permission action names with `Create/Get/Search/Update/Delete` for generated CRUD. |
| `framework/tools/CrestCreates.CodeGenerator/Authorization/AuthorizationAttributeGenerator.cs` | Modify | Keep generated CRUD permission output consistent with new action names. |
| `framework/test/CrestCreates.CodeGenerator.Tests/CrudServiceGenerator/CrudServiceMainlineSourceGeneratorTests.cs` | Create | New generator tests for mainline output shape, permissions, query DTOs, repository dependency, exceptions, and concurrency. |
| `framework/test/CrestCreates.CodeGenerator.Tests/DynamicApiGenerator/DynamicApiCrudMainlineTests.cs` | Create | Generator tests proving generated Dynamic API binds CRUD delete `expectedStamp` from `If-Match`. |
| `framework/test/CrestCreates.Application.Tests/Crud/GeneratedCrudQueryTests.cs` | Create | Application-level tests for descriptor filters/sorts and invalid fields, using generated-equivalent service behavior. |
| `framework/test/CrestCreates.IntegrationTests/GeneratedCrudMainlineIntegrationTests.cs` | Create | End-to-end generated Dynamic API tests for CRUD, UoW, permissions, and concurrency. |
| `samples/LibraryManagement/LibraryManagement.Domain/Entities/Book.cs` | Modify | Mark `Book` for generated CRUD mainline. |
| `samples/LibraryManagement/LibraryManagement.Application/Services/BookAppService.cs` | Modify or delete portions | Remove duplicate hand-written CRUD behavior only after generated path replaces it. Keep custom business methods separate. |
| `samples/LibraryManagement/LibraryManagement.Application.Contracts/Interfaces/IBookAppService.cs` | Modify | Move non-CRUD custom methods to a separate business interface before generated CRUD owns `IBookAppService`. |
| `docs/superpowers/specs/2026-05-06-crud-mainline-design.md` | Verify only | Source design spec. Do not change unless implementation reveals a spec contradiction. |

---

## Task 1: Add Generator Tests For CRUD Mainline Shape

**Files:**
- Create: `framework/test/CrestCreates.CodeGenerator.Tests/CrudServiceGenerator/CrudServiceMainlineSourceGeneratorTests.cs`
- Reference: `framework/test/CrestCreates.CodeGenerator.Tests/TestHelpers/SourceGeneratorTestHelper.cs`
- Reference: `framework/tools/CrestCreates.CodeGenerator/CrudServiceGenerator/CrudServiceSourceGenerator.cs`

- [ ] **Step 1: Create the failing test file**

Create `framework/test/CrestCreates.CodeGenerator.Tests/CrudServiceGenerator/CrudServiceMainlineSourceGeneratorTests.cs` with this content:

```csharp
using System.Linq;
using CrestCreates.CodeGenerator.CrudServiceGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.CrudServiceGenerator;

public sealed class CrudServiceMainlineSourceGeneratorTests
{
    [Fact]
    public void GeneratedCrud_ShouldCompileForSampleEntity()
    {
        var result = RunProductGenerator();

        Assert.True(result.CompilationSuccess, string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
        Assert.True(result.ContainsFile("ProductDto.g.cs"));
        Assert.True(result.ContainsFile("CreateProductDto.g.cs"));
        Assert.True(result.ContainsFile("UpdateProductDto.g.cs"));
        Assert.True(result.ContainsFile("ProductListRequestDto.g.cs"));
        Assert.True(result.ContainsFile("IProductAppService.g.cs"));
        Assert.True(result.ContainsFile("ProductAppService.g.cs"));
        Assert.True(result.ContainsFile("ProductCrudPermissions.g.cs"));
        Assert.True(result.ContainsFile("ProductObjectMappings.g.cs"));
    }

    [Fact]
    public void GeneratedCrud_ShouldUseICrestRepositoryBase()
    {
        var result = RunProductGenerator();

        var source = result.GetSourceByFileName("ProductAppService.g.cs").SourceText;

        Assert.Contains("ICrestRepositoryBase<Product, System.Guid>", source);
        Assert.DoesNotContain("IProductRepository", source);
        Assert.DoesNotContain("ProductCrudServiceBase", source);
        Assert.DoesNotContain("abstract class ProductAppService", source);
    }

    [Fact]
    public void GeneratedCrud_Contract_ShouldUseAppServiceNaming()
    {
        var result = RunProductGenerator();

        var source = result.GetSourceByFileName("IProductAppService.g.cs").SourceText;

        Assert.Contains("public partial interface IProductAppService", source);
        Assert.Contains("ICrudAppService<System.Guid, ProductDto, CreateProductDto, UpdateProductDto, ProductListRequestDto>", source);
        Assert.DoesNotContain("IProductCrudService", source);
    }

    [Fact]
    public void GeneratedCrud_ListRequest_ShouldOnlyUsePagedRequestDtoDescriptors()
    {
        var result = RunProductGenerator();

        var source = result.GetSourceByFileName("ProductListRequestDto.g.cs").SourceText;

        Assert.Contains("public partial class ProductListRequestDto : PagedRequestDto", source);
        Assert.DoesNotContain("Keyword", source);
        Assert.DoesNotContain("StartTime", source);
        Assert.DoesNotContain("EndTime", source);
        Assert.DoesNotContain("public string? Name", source);
        Assert.DoesNotContain("public string? Category", source);
    }

    [Fact]
    public void GeneratedCrud_ShouldGeneratePermissions()
    {
        var result = RunProductGenerator();

        var source = result.GetSourceByFileName("ProductCrudPermissions.g.cs").SourceText;

        Assert.Contains("public const string Create = \"Product.Create\";", source);
        Assert.Contains("public const string Get = \"Product.Get\";", source);
        Assert.Contains("public const string Search = \"Product.Search\";", source);
        Assert.Contains("public const string Update = \"Product.Update\";", source);
        Assert.Contains("public const string Delete = \"Product.Delete\";", source);
        Assert.DoesNotContain("\"Product.View\"", source);
        Assert.DoesNotContain("\"Product.Export\"", source);
    }

    [Fact]
    public void GeneratedCrud_UpdateDto_ShouldIncludeConcurrencyStamp_AndCreateDtoShouldExcludeIt()
    {
        var result = RunProductGenerator();

        var updateSource = result.GetSourceByFileName("UpdateProductDto.g.cs").SourceText;
        var createSource = result.GetSourceByFileName("CreateProductDto.g.cs").SourceText;
        var outputSource = result.GetSourceByFileName("ProductDto.g.cs").SourceText;

        Assert.Contains("public string ConcurrencyStamp { get; set; }", updateSource);
        Assert.Contains("public string ConcurrencyStamp { get; set; }", outputSource);
        Assert.DoesNotContain("ConcurrencyStamp", createSource);
    }

    [Fact]
    public void GeneratedCrud_Delete_ShouldRequireExpectedStampForConcurrentEntity()
    {
        var result = RunProductGenerator();

        var source = result.GetSourceByFileName("ProductAppService.g.cs").SourceText;

        Assert.Contains("Task DeleteAsync(System.Guid id, string? expectedStamp = null", source);
        Assert.Contains("CrestPreconditionRequiredException", source);
        Assert.Contains("Repository.DeleteAsync(id, expectedStamp", source);
    }

    [Fact]
    public void GeneratedCrud_ShouldNotWrapPlatformExceptions()
    {
        var result = RunProductGenerator();

        var source = result.GetSourceByFileName("ProductAppService.g.cs").SourceText;

        Assert.DoesNotContain("catch (System.Data.Common.DbException", source);
        Assert.DoesNotContain("catch (Exception ex)", source);
        Assert.DoesNotContain("throw new Exception(", source);
        Assert.Contains("CrestEntityNotFoundException", source);
    }

    private static SourceGeneratorResult RunProductGenerator()
    {
        const string source = """
using System;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.Entities;

namespace TestNamespace;

[GenerateCrudService]
public class Product : AuditedAggregateRoot<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
}
""";

        const string support = """
using System;

namespace CrestCreates.Domain.Shared.Entities;

public interface IEntity<TKey>
{
    TKey Id { get; set; }
}

public interface IHasConcurrencyStamp
{
    string ConcurrencyStamp { get; set; }
}

public abstract class AuditedAggregateRoot<TKey> : IEntity<TKey>, IHasConcurrencyStamp
    where TKey : IEquatable<TKey>
{
    public TKey Id { get; set; } = default!;
    public DateTime CreationTime { get; set; }
    public Guid? CreatorId { get; set; }
    public DateTime? LastModificationTime { get; set; }
    public Guid? LastModifierId { get; set; }
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();
}

namespace CrestCreates.Domain.Repositories;

public interface ICrestRepositoryBase<TEntity, TKey>
{
}

namespace CrestCreates.Application.Contracts.Interfaces;

public interface ICrudAppService<TKey, TDto, in TCreateDto, in TUpdateDto, in TListRequestDto>
    where TKey : IEquatable<TKey>
{
}
""";

        return SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
            source,
            new[] { support });
    }
}
```

- [ ] **Step 2: Run the focused generator tests and verify they fail**

Run:

```powershell
dotnet test framework\test\CrestCreates.CodeGenerator.Tests\CrestCreates.CodeGenerator.Tests.csproj --filter "FullyQualifiedName~CrudServiceMainlineSourceGeneratorTests"
```

Expected result:

```text
Failed GeneratedCrud_ShouldCompileForSampleEntity
Failed GeneratedCrud_ShouldUseICrestRepositoryBase
Failed GeneratedCrud_Contract_ShouldUseAppServiceNaming
Failed GeneratedCrud_ListRequest_ShouldOnlyUsePagedRequestDtoDescriptors
Failed GeneratedCrud_ShouldGeneratePermissions
Failed GeneratedCrud_ShouldNotWrapPlatformExceptions
```

The current generator emits `IProductCrudService`, `ProductCrudService`, guessed list fields, and entity-specific repository references, so these tests should fail before implementation.

- [ ] **Step 3: Commit the failing tests**

Run:

```powershell
git add framework\test\CrestCreates.CodeGenerator.Tests\CrudServiceGenerator\CrudServiceMainlineSourceGeneratorTests.cs
git commit -m "test: add crud mainline generator tests"
```

---

## Task 2: Generate Mainline DTOs, Contract, Permissions, And Mapping Declarations

**Files:**
- Modify: `framework/tools/CrestCreates.CodeGenerator/CrudServiceGenerator/CrudServiceSourceGenerator.cs`
- Test: `framework/test/CrestCreates.CodeGenerator.Tests/CrudServiceGenerator/CrudServiceMainlineSourceGeneratorTests.cs`

- [ ] **Step 1: Replace generated service naming outputs**

In `CrudServiceSourceGenerator.ExecuteGeneration`, keep the existing entity discovery but change the generated output methods so they target the mainline names:

```csharp
GenerateEntityDto(context, entityClass, entityName, namespaceName, properties);
GenerateCreateEntityDto(context, entityClass, entityName, namespaceName, properties);
GenerateUpdateEntityDto(context, entityClass, entityName, namespaceName, properties);
GenerateEntityListRequestDto(context, entityClass, entityName, namespaceName);
GenerateCrudPermissions(context, entityName, namespaceName);
GenerateObjectMappingDeclarations(context, entityClass, entityName, namespaceName);
GenerateCrudServiceInterface(context, entityName, namespaceName, idType);
GenerateCrudServiceImplementation(context, entityClass, entityName, namespaceName, idType, properties);
```

Remove `generateController`, `controllerRoute`, and `generateAsBaseClass` from the mainline generation call chain. Do not delete the compatibility controller generator in this task.

- [ ] **Step 2: Replace `GenerateEntityListRequestDto` with descriptor-only output**

Change the method signature and body to:

```csharp
private void GenerateEntityListRequestDto(
    SourceProductionContext context,
    INamedTypeSymbol entityClass,
    string entityName,
    string namespaceName)
{
    var builder = new StringBuilder();
    builder.AppendLine("#nullable enable");
    builder.AppendLine("// <auto-generated />");
    builder.AppendLine("using CrestCreates.Application.Contracts.DTOs.Common;");
    builder.AppendLine();
    builder.AppendLine($"namespace {namespaceName}.Dtos");
    builder.AppendLine("{");
    builder.AppendLine($"    public partial class {entityName}ListRequestDto : PagedRequestDto");
    builder.AppendLine("    {");
    builder.AppendLine("    }");
    builder.AppendLine("}");

    context.AddSource($"{entityName}ListRequestDto.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
}
```

- [ ] **Step 3: Replace `GenerateCrudServiceInterface` with AppService naming**

Replace the mainline interface generator with:

```csharp
private void GenerateCrudServiceInterface(
    SourceProductionContext context,
    string entityName,
    string namespaceName,
    string idType)
{
    var builder = new StringBuilder();
    builder.AppendLine("#nullable enable");
    builder.AppendLine("// <auto-generated />");
    builder.AppendLine("using System;");
    builder.AppendLine("using CrestCreates.Application.Contracts.Interfaces;");
    builder.AppendLine($"using {namespaceName}.Dtos;");
    builder.AppendLine();
    builder.AppendLine($"namespace {namespaceName}.Services");
    builder.AppendLine("{");
    builder.AppendLine($"    public partial interface I{entityName}AppService : ICrudAppService<{idType}, {entityName}Dto, Create{entityName}Dto, Update{entityName}Dto, {entityName}ListRequestDto>");
    builder.AppendLine("    {");
    builder.AppendLine("    }");
    builder.AppendLine("}");

    context.AddSource($"I{entityName}AppService.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
}
```

- [ ] **Step 4: Add permission generation**

Add this method to `CrudServiceSourceGenerator`:

```csharp
private void GenerateCrudPermissions(SourceProductionContext context, string entityName, string namespaceName)
{
    var builder = new StringBuilder();
    builder.AppendLine("#nullable enable");
    builder.AppendLine("// <auto-generated />");
    builder.AppendLine();
    builder.AppendLine($"namespace {namespaceName}.Permissions");
    builder.AppendLine("{");
    builder.AppendLine($"    public static partial class {entityName}CrudPermissions");
    builder.AppendLine("    {");
    builder.AppendLine($"        public const string Create = \"{entityName}.Create\";");
    builder.AppendLine($"        public const string Get = \"{entityName}.Get\";");
    builder.AppendLine($"        public const string Search = \"{entityName}.Search\";");
    builder.AppendLine($"        public const string Update = \"{entityName}.Update\";");
    builder.AppendLine($"        public const string Delete = \"{entityName}.Delete\";");
    builder.AppendLine("    }");
    builder.AppendLine("}");

    context.AddSource($"{entityName}CrudPermissions.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
}
```

- [ ] **Step 5: Add object mapping declaration output**

Add this method:

```csharp
private void GenerateObjectMappingDeclarations(
    SourceProductionContext context,
    INamedTypeSymbol entityClass,
    string entityName,
    string namespaceName)
{
    var entityFullName = entityClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    var builder = new StringBuilder();
    builder.AppendLine("#nullable enable");
    builder.AppendLine("// <auto-generated />");
    builder.AppendLine("using CrestCreates.Domain.Shared.ObjectMapping;");
    builder.AppendLine($"using {namespaceName}.Dtos;");
    builder.AppendLine();
    builder.AppendLine($"namespace {namespaceName}.Mappings");
    builder.AppendLine("{");
    builder.AppendLine($"    [GenerateObjectMapping(typeof({entityFullName}), typeof({entityName}Dto))]");
    builder.AppendLine($"    [GenerateObjectMapping(typeof(Create{entityName}Dto), typeof({entityFullName}), Direction = MapDirection.Create)]");
    builder.AppendLine($"    [GenerateObjectMapping(typeof(Update{entityName}Dto), typeof({entityFullName}), Direction = MapDirection.Apply)]");
    builder.AppendLine($"    public static partial class {entityName}ObjectMappings");
    builder.AppendLine("    {");
    builder.AppendLine("    }");
    builder.AppendLine("}");

    context.AddSource($"{entityName}ObjectMappings.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
}
```

If `SymbolDisplayFormat` is not currently imported in this file, use the existing `Microsoft.CodeAnalysis` namespace and add:

```csharp
private static readonly SymbolDisplayFormat FullyQualifiedFormat = SymbolDisplayFormat.FullyQualifiedFormat;
```

Then use `entityClass.ToDisplayString(FullyQualifiedFormat)`.

- [ ] **Step 6: Run the focused tests**

Run:

```powershell
dotnet test framework\test\CrestCreates.CodeGenerator.Tests\CrestCreates.CodeGenerator.Tests.csproj --filter "FullyQualifiedName~CrudServiceMainlineSourceGeneratorTests"
```

Expected result:

```text
GeneratedCrud_ShouldCompileForSampleEntity passes or reaches ProductAppService.g.cs compilation failures
GeneratedCrud_ListRequest_ShouldOnlyUsePagedRequestDtoDescriptors passes
GeneratedCrud_Contract_ShouldUseAppServiceNaming passes
GeneratedCrud_ShouldGeneratePermissions passes
```

Remaining failures should point to app service implementation shape, repository dependency, and exception behavior, which are handled in the next task.

- [ ] **Step 7: Commit DTO/contract/permission/mapping generator changes**

Run:

```powershell
git add framework\tools\CrestCreates.CodeGenerator\CrudServiceGenerator\CrudServiceSourceGenerator.cs framework\test\CrestCreates.CodeGenerator.Tests\CrudServiceGenerator\CrudServiceMainlineSourceGeneratorTests.cs
git commit -m "feat: generate crud mainline contracts and dto shape"
```

---

## Task 3: Generate Concrete Partial AppService Using ICrestRepositoryBase

**Files:**
- Modify: `framework/tools/CrestCreates.CodeGenerator/CrudServiceGenerator/CrudServiceSourceGenerator.cs`
- Test: `framework/test/CrestCreates.CodeGenerator.Tests/CrudServiceGenerator/CrudServiceMainlineSourceGeneratorTests.cs`

- [ ] **Step 1: Replace AppService class header generation**

In `GenerateCrudServiceImplementation`, replace the class declaration and constructor generation with this output shape:

```csharp
builder.AppendLine($"    public partial class {entityName}AppService : I{entityName}AppService");
builder.AppendLine("    {");
builder.AppendLine($"        protected readonly ICrestRepositoryBase<{entityName}, {idType}> Repository;");
builder.AppendLine("        protected readonly IPermissionChecker PermissionChecker;");
builder.AppendLine("        protected readonly ICurrentUser CurrentUser;");
builder.AppendLine("        protected readonly IDataPermissionFilter DataPermissionFilter;");
builder.AppendLine();
builder.AppendLine($"        public {entityName}AppService(");
builder.AppendLine($"            ICrestRepositoryBase<{entityName}, {idType}> repository,");
builder.AppendLine("            IPermissionChecker permissionChecker,");
builder.AppendLine("            ICurrentUser currentUser,");
builder.AppendLine("            IDataPermissionFilter dataPermissionFilter)");
builder.AppendLine("        {");
builder.AppendLine("            Repository = repository ?? throw new ArgumentNullException(nameof(repository));");
builder.AppendLine("            PermissionChecker = permissionChecker ?? throw new ArgumentNullException(nameof(permissionChecker));");
builder.AppendLine("            CurrentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));");
builder.AppendLine("            DataPermissionFilter = dataPermissionFilter ?? throw new ArgumentNullException(nameof(dataPermissionFilter));");
builder.AppendLine("        }");
```

Make sure the generated file includes:

```csharp
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Application.Contracts.Query;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.DataFilter;
using CrestCreates.Domain.Shared.Exceptions;
using CrestCreates.Domain.Exceptions;
using CrestCreates.Aop.Interceptors;
```

- [ ] **Step 2: Generate permission helper**

Add this emitted helper inside the generated app service:

```csharp
builder.AppendLine("        protected virtual Task CheckPermissionAsync(string permissionName, CancellationToken cancellationToken = default)");
builder.AppendLine("        {");
builder.AppendLine("            return PermissionChecker.CheckAsync(permissionName);");
builder.AppendLine("        }");
```

If `IPermissionChecker.CheckAsync` has a `CancellationToken` overload in the project, emit that overload call. If it does not, use the exact call above.

- [ ] **Step 3: Generate audit and ownership helpers**

Emit helpers equivalent to the existing `CrestAppServiceBase` behavior:

```csharp
builder.AppendLine($"        protected virtual async Task<IQueryable<{entityName}>> ApplyDataPermissionFilterAsync(IQueryable<{entityName}> query)");
builder.AppendLine("        {");
builder.AppendLine("            return await DataPermissionFilter.ApplyFilterAsync(query);");
builder.AppendLine("        }");
builder.AppendLine();
builder.AppendLine($"        protected virtual Task SetCreationAuditPropertiesAsync({entityName} entity)");
builder.AppendLine("        {");
builder.AppendLine("            if (entity is IMustHaveTenant mustHaveTenant)");
builder.AppendLine("                mustHaveTenant.TenantId = CurrentUser.TenantId ?? throw new InvalidOperationException(\"当前用户没有关联租户\");");
builder.AppendLine("            var creatorId = Guid.TryParse(CurrentUser.Id, out var userId) ? userId : (Guid?)null;");
builder.AppendLine("            if (entity is IHasCreator hasCreator)");
builder.AppendLine("                hasCreator.CreatorId = creatorId;");
builder.AppendLine("            if (entity is IAuditedEntity audited)");
builder.AppendLine("            {");
builder.AppendLine("                audited.CreationTime = DateTime.UtcNow;");
builder.AppendLine("                audited.CreatorId = creatorId;");
builder.AppendLine("            }");
builder.AppendLine("            return Task.CompletedTask;");
builder.AppendLine("        }");
builder.AppendLine();
builder.AppendLine($"        protected virtual Task SetModificationAuditPropertiesAsync({entityName} entity)");
builder.AppendLine("        {");
builder.AppendLine("            if (entity is IAuditedEntity audited)");
builder.AppendLine("            {");
builder.AppendLine("                audited.LastModificationTime = DateTime.UtcNow;");
builder.AppendLine("                audited.LastModifierId = Guid.TryParse(CurrentUser.Id, out var userId) ? userId : (Guid?)null;");
builder.AppendLine("            }");
builder.AppendLine("            return Task.CompletedTask;");
builder.AppendLine("        }");
```

Also emit a tenant ownership helper:

```csharp
builder.AppendLine($"        protected virtual Task ValidateDataOwnershipAsync({entityName} entity)");
builder.AppendLine("        {");
builder.AppendLine("            if (entity is IMustHaveTenant mustHaveTenant && mustHaveTenant.TenantId != CurrentUser.TenantId)");
builder.AppendLine("                throw new UnauthorizedAccessException(\"您没有权限访问此数据：租户不匹配\");");
builder.AppendLine("            return Task.CompletedTask;");
builder.AppendLine("        }");
```

- [ ] **Step 4: Generate create/get/list/update/delete method skeletons**

Emit these method signatures:

```csharp
[UnitOfWorkMo]
public virtual async Task<ProductDto> CreateAsync(CreateProductDto input, CancellationToken cancellationToken = default)

public virtual async Task<ProductDto?> GetByIdAsync(System.Guid id, CancellationToken cancellationToken = default)

public virtual async Task<PagedResultDto<ProductDto>> GetListAsync(ProductListRequestDto input, CancellationToken cancellationToken = default)

[UnitOfWorkMo]
public virtual async Task<ProductDto> UpdateAsync(System.Guid id, UpdateProductDto input, CancellationToken cancellationToken = default)

[UnitOfWorkMo]
public virtual async Task DeleteAsync(System.Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default)
```

For the current task, keep method bodies compilable and direct:

```csharp
await CheckPermissionAsync(ProductCrudPermissions.Create, cancellationToken);
```

Use the generated permission constants for all methods.

- [ ] **Step 5: Generate extension hooks**

Emit these hooks in the generated app service:

```csharp
protected virtual Task ValidateCreateAsync(CreateProductDto input, CancellationToken cancellationToken = default) => Task.CompletedTask;
protected virtual Task ValidateUpdateAsync(System.Guid id, UpdateProductDto input, CancellationToken cancellationToken = default) => Task.CompletedTask;
protected virtual Task OnCreatingAsync(Product entity, CreateProductDto input, CancellationToken cancellationToken = default) => Task.CompletedTask;
protected virtual Task OnCreatedAsync(Product entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
protected virtual Task OnUpdatingAsync(Product entity, UpdateProductDto input, CancellationToken cancellationToken = default) => Task.CompletedTask;
protected virtual Task OnUpdatedAsync(Product entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
protected virtual Task OnDeletingAsync(Product entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
protected virtual Task OnDeletedAsync(System.Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
protected virtual Task<IQueryable<Product>> ConfigureListQueryAsync(IQueryable<Product> query, ProductListRequestDto input, CancellationToken cancellationToken = default) => Task.FromResult(query);
```

Generate with actual `entityName` and `idType`.

- [ ] **Step 6: Run focused tests**

Run:

```powershell
dotnet test framework\test\CrestCreates.CodeGenerator.Tests\CrestCreates.CodeGenerator.Tests.csproj --filter "FullyQualifiedName~CrudServiceMainlineSourceGeneratorTests"
```

Expected result:

```text
GeneratedCrud_ShouldUseICrestRepositoryBase passes
GeneratedCrud_ShouldNotWrapPlatformExceptions passes
```

Compilation may still fail if mapping method names are not aligned with the object mapping generator. Resolve by inspecting existing generated mapper method names in `framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator` and emit those exact method calls in Task 4.

- [ ] **Step 7: Commit service shape changes**

Run:

```powershell
git add framework\tools\CrestCreates.CodeGenerator\CrudServiceGenerator\CrudServiceSourceGenerator.cs
git commit -m "feat: generate crud mainline app service"
```

---

## Task 4: Implement Generated CRUD Method Bodies And Descriptor Query

**Files:**
- Modify: `framework/tools/CrestCreates.CodeGenerator/CrudServiceGenerator/CrudServiceSourceGenerator.cs`
- Create: `framework/test/CrestCreates.Application.Tests/Crud/GeneratedCrudQueryTests.cs`
- Test: `framework/test/CrestCreates.CodeGenerator.Tests/CrudServiceGenerator/CrudServiceMainlineSourceGeneratorTests.cs`

- [ ] **Step 1: Add application tests for descriptor query behavior**

Create `framework/test/CrestCreates.Application.Tests/Crud/GeneratedCrudQueryTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Application.Contracts.Query;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Application.Tests.Crud;

public sealed class GeneratedCrudQueryTests
{
    [Fact]
    public void GeneratedCrud_GetList_ShouldApplyFiltersAndSorts()
    {
        var query = new[]
        {
            new TestProduct(Guid.NewGuid(), "Keyboard", "Hardware", 99m),
            new TestProduct(Guid.NewGuid(), "Mouse", "Hardware", 39m),
            new TestProduct(Guid.NewGuid(), "Notebook", "Stationery", 9m)
        }.AsQueryable();

        var request = new PagedRequestDto
        {
            PageIndex = 0,
            PageSize = 10,
            Filters = new List<FilterDescriptor>
            {
                new("Category", FilterOperator.Equals, "Hardware")
            },
            Sorts = new List<SortDescriptor>
            {
                new("Price", SortDirection.Descending)
            }
        };

        var filtered = QueryExecutor<TestProduct>.ApplyFilters(query, request.Filters);
        var sorted = QueryExecutor<TestProduct>.ApplySorts(filtered, request.Sorts);

        sorted.Select(x => x.Name).Should().Equal("Keyboard", "Mouse");
    }

    [Fact]
    public void GeneratedCrud_InvalidFilterField_ShouldFailClearly()
    {
        var allowedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Id",
            "Name",
            "Category",
            "Price"
        };

        var filter = new FilterDescriptor("PasswordHash", FilterOperator.Equals, "x");

        var action = () => GeneratedCrudQueryGuard.EnsureAllowedFilterFields(new[] { filter }, allowedFields, "TestProduct");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*TestProduct*PasswordHash*");
    }

    private sealed record TestProduct(Guid Id, string Name, string Category, decimal Price);

    private static class GeneratedCrudQueryGuard
    {
        public static void EnsureAllowedFilterFields(
            IEnumerable<FilterDescriptor> filters,
            IReadOnlySet<string> allowedFields,
            string entityName)
        {
            foreach (var filter in filters)
            {
                if (!allowedFields.Contains(filter.Field))
                    throw new InvalidOperationException($"{entityName} 不支持过滤字段: {filter.Field}");
            }
        }
    }
}
```

This test uses a local guard to document expected generated behavior. The generator will emit equivalent allowlist checks inside each generated app service.

- [ ] **Step 2: Run application query tests**

Run:

```powershell
dotnet test framework\test\CrestCreates.Application.Tests\CrestCreates.Application.Tests.csproj --filter "FullyQualifiedName~GeneratedCrudQueryTests"
```

Expected result:

```text
Passed GeneratedCrud_GetList_ShouldApplyFiltersAndSorts
Passed GeneratedCrud_InvalidFilterField_ShouldFailClearly
```

These tests verify existing `QueryExecutor` behavior and the intended guard semantics before generator changes.

- [ ] **Step 3: Generate allowed field arrays**

In `CrudServiceSourceGenerator`, add helper methods:

```csharp
private static List<IPropertySymbol> GetQueryableProperties(List<IPropertySymbol> properties)
{
    return properties
        .Where(p => p.Name != "DomainEvents")
        .Where(p => p.Name != "IsDeleted" && p.Name != "DeletionTime" && p.Name != "DeleterId")
        .Where(p => p.Type.TypeKind != TypeKind.Array)
        .Where(p => p.Type.SpecialType != SpecialType.None ||
                    p.Type.TypeKind == TypeKind.Enum ||
                    p.Type.ToDisplayString() == "System.Guid" ||
                    p.Type.ToDisplayString() == "System.DateTime" ||
                    p.Type.ToDisplayString() == "System.DateTimeOffset")
        .ToList();
}

private static string ToStringArrayInitializer(IEnumerable<IPropertySymbol> properties)
{
    return string.Join(", ", properties.Select(p => $"\"{p.Name}\""));
}
```

Emit inside the generated app service:

```csharp
private static readonly HashSet<string> AllowedQueryFields = new(StringComparer.OrdinalIgnoreCase)
{
    "Id",
    "Name",
    "Category",
    "Price",
    "StockQuantity",
    "CreationTime"
};
```

Generate actual property names from `GetQueryableProperties(properties)`.

- [ ] **Step 4: Generate field validation helpers**

Emit these helpers inside each generated app service:

```csharp
private static void EnsureAllowedFilterFields(IEnumerable<FilterDescriptor>? filters)
{
    if (filters == null)
        return;

    foreach (var filter in filters)
    {
        if (!AllowedQueryFields.Contains(filter.Field))
            throw new CrestBusinessException("Crest.Crud.InvalidFilterField", typeof(Product).Name, filter.Field);
    }
}

private static void EnsureAllowedSortFields(IEnumerable<SortDescriptor>? sorts)
{
    if (sorts == null)
        return;

    foreach (var sort in sorts)
    {
        if (!AllowedQueryFields.Contains(sort.Field))
            throw new CrestBusinessException("Crest.Crud.InvalidSortField", typeof(Product).Name, sort.Field);
    }
}
```

Replace `Product` with `entityName` in generated code. If `CrestBusinessException` constructor signatures differ, use the existing constructor pattern from Feature Management exception code and keep the error code strings exactly:

```text
Crest.Crud.InvalidFilterField
Crest.Crud.InvalidSortField
```

- [ ] **Step 5: Generate real `GetListAsync` body**

Emit:

```csharp
public virtual async Task<PagedResultDto<ProductDto>> GetListAsync(ProductListRequestDto input, CancellationToken cancellationToken = default)
{
    if (input == null)
        throw new ArgumentNullException(nameof(input));

    await CheckPermissionAsync(ProductCrudPermissions.Search, cancellationToken);

    EnsureAllowedFilterFields(input.Filters);
    EnsureAllowedSortFields(input.Sorts);

    var query = Repository.GetQueryable();
    query = await ApplyDataPermissionFilterAsync(query);
    query = await ConfigureListQueryAsync(query, input, cancellationToken);

    query = QueryExecutor<Product>.ApplyFilters(query, input.Filters ?? new List<FilterDescriptor>());
    query = QueryExecutor<Product>.ApplySorts(query, input.Sorts ?? new List<SortDescriptor>());

    var totalCount = query.Count();
    query = QueryExecutor<Product>.ApplyPaging(query, input.GetSkipCount(), input.PageSize);

    var entities = query.ToList();
    var dtos = entities.Select(ProductObjectMappings.ToProductDto).ToList();

    return new PagedResultDto<ProductDto>(dtos, totalCount, input.PageIndex, input.PageSize);
}
```

Use the actual generated mapping method name from `ObjectMappingSourceGenerator`. If the mapper emits `entity.ToDto()` extension methods instead of static methods, use the extension method consistently:

```csharp
var dtos = entities.Select(entity => entity.ToDto()).ToList();
```

- [ ] **Step 6: Generate create/get/update/delete method bodies**

Emit create:

```csharp
[UnitOfWorkMo]
public virtual async Task<ProductDto> CreateAsync(CreateProductDto input, CancellationToken cancellationToken = default)
{
    if (input == null)
        throw new ArgumentNullException(nameof(input));

    await CheckPermissionAsync(ProductCrudPermissions.Create, cancellationToken);
    await ValidateCreateAsync(input, cancellationToken);

    var entity = ProductObjectMappings.ToProduct(input);
    await SetCreationAuditPropertiesAsync(entity);
    await OnCreatingAsync(entity, input, cancellationToken);

    var created = await Repository.InsertAsync(entity, cancellationToken);
    await OnCreatedAsync(created, cancellationToken);

    return ProductObjectMappings.ToProductDto(created);
}
```

Emit get:

```csharp
public virtual async Task<ProductDto?> GetByIdAsync(System.Guid id, CancellationToken cancellationToken = default)
{
    await CheckPermissionAsync(ProductCrudPermissions.Get, cancellationToken);

    var query = Repository.GetQueryable();
    query = await ApplyDataPermissionFilterAsync(query);
    var entity = query.FirstOrDefault(x => x.Id.Equals(id));
    if (entity == null)
        throw new CrestEntityNotFoundException(typeof(Product), id);

    await ValidateDataOwnershipAsync(entity);
    return ProductObjectMappings.ToProductDto(entity);
}
```

Emit update:

```csharp
[UnitOfWorkMo]
public virtual async Task<ProductDto> UpdateAsync(System.Guid id, UpdateProductDto input, CancellationToken cancellationToken = default)
{
    if (input == null)
        throw new ArgumentNullException(nameof(input));

    await CheckPermissionAsync(ProductCrudPermissions.Update, cancellationToken);
    await ValidateUpdateAsync(id, input, cancellationToken);

    var entity = await Repository.GetAsync(id, cancellationToken);
    if (entity == null)
        throw new CrestEntityNotFoundException(typeof(Product), id);

    await ValidateDataOwnershipAsync(entity);
    await OnUpdatingAsync(entity, input, cancellationToken);

    ProductObjectMappings.ApplyTo(input, entity);
    await SetModificationAuditPropertiesAsync(entity);

    var updated = await Repository.UpdateAsync(entity, cancellationToken);
    await OnUpdatedAsync(updated, cancellationToken);

    return ProductObjectMappings.ToProductDto(updated);
}
```

Emit delete:

```csharp
[UnitOfWorkMo]
public virtual async Task DeleteAsync(System.Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default)
{
    await CheckPermissionAsync(ProductCrudPermissions.Delete, cancellationToken);

    if (typeof(IHasConcurrencyStamp).IsAssignableFrom(typeof(Product)))
    {
        if (string.IsNullOrWhiteSpace(expectedStamp))
            throw new CrestPreconditionRequiredException(typeof(Product).Name, id);

        await Repository.DeleteAsync(id, expectedStamp, cancellationToken);
        await OnDeletedAsync(id, cancellationToken);
        return;
    }

    var entity = await Repository.GetAsync(id, cancellationToken);
    if (entity == null)
        throw new CrestEntityNotFoundException(typeof(Product), id);

    await ValidateDataOwnershipAsync(entity);
    await OnDeletingAsync(entity, cancellationToken);
    await Repository.DeleteAsync(entity, cancellationToken);
    await OnDeletedAsync(id, cancellationToken);
}
```

Replace `Product`, `ProductDto`, `ProductCrudPermissions`, `ProductObjectMappings`, and id type with generated values.

- [ ] **Step 7: Run generator and application tests**

Run:

```powershell
dotnet test framework\test\CrestCreates.CodeGenerator.Tests\CrestCreates.CodeGenerator.Tests.csproj --filter "FullyQualifiedName~CrudServiceMainlineSourceGeneratorTests"
dotnet test framework\test\CrestCreates.Application.Tests\CrestCreates.Application.Tests.csproj --filter "FullyQualifiedName~GeneratedCrudQueryTests"
```

Expected result:

```text
CrudServiceMainlineSourceGeneratorTests: Passed
GeneratedCrudQueryTests: Passed
```

- [ ] **Step 8: Commit CRUD method implementation**

Run:

```powershell
git add framework\tools\CrestCreates.CodeGenerator\CrudServiceGenerator\CrudServiceSourceGenerator.cs framework\test\CrestCreates.Application.Tests\Crud\GeneratedCrudQueryTests.cs
git commit -m "feat: generate crud mainline method bodies"
```

---

## Task 5: Bind CRUD Delete If-Match In Generated Dynamic API

**Files:**
- Create: `framework/test/CrestCreates.CodeGenerator.Tests/DynamicApiGenerator/DynamicApiCrudMainlineTests.cs`
- Modify: `framework/tools/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiAotSourceGenerator.cs`

- [ ] **Step 1: Add failing Dynamic API generator test**

Create `framework/test/CrestCreates.CodeGenerator.Tests/DynamicApiGenerator/DynamicApiCrudMainlineTests.cs`:

```csharp
using System;
using System.Linq;
using CrestCreates.CodeGenerator.DynamicApiGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.DynamicApiGenerator;

public sealed class DynamicApiCrudMainlineTests
{
    [Fact]
    public void GeneratedDynamicApi_DeleteExpectedStamp_ShouldBindFromIfMatchHeader()
    {
        const string source = """
using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.Interfaces;

namespace TestNamespace;

public interface IProductAppService : ICrudAppService<Guid, ProductDto, CreateProductDto, UpdateProductDto, ProductListRequestDto>
{
}

public sealed class ProductAppService : IProductAppService
{
    public Task<ProductDto> CreateAsync(CreateProductDto input, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ProductDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<PagedResultDto<ProductDto>> GetListAsync(ProductListRequestDto input, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ProductDto> UpdateAsync(Guid id, UpdateProductDto input, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}

public sealed class ProductDto { }
public sealed class CreateProductDto { }
public sealed class UpdateProductDto { }
public sealed class ProductListRequestDto { }
public sealed class PagedResultDto<T> { }
""";

        const string support = """
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface ICrudAppService<TKey, TDto, in TCreateDto, in TUpdateDto, in TListRequestDto>
    where TKey : IEquatable<TKey>
{
    Task<TDto> CreateAsync(TCreateDto input, CancellationToken cancellationToken = default);
    Task<TDto?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);
    Task<PagedResultDto<TDto>> GetListAsync(TListRequestDto input, CancellationToken cancellationToken = default);
    Task<TDto> UpdateAsync(TKey id, TUpdateDto input, CancellationToken cancellationToken = default);
    Task DeleteAsync(TKey id, string? expectedStamp = null, CancellationToken cancellationToken = default);
}

public sealed class PagedResultDto<T> { }
""";

        var result = SourceGeneratorTestHelper.RunGenerator<DynamicApiAotSourceGenerator>(
            source,
            new[] { support });

        var endpoints = result.GeneratedSources.Single(x => x.FileName.Contains("GeneratedDynamicApiEndpoints")).SourceText;

        Assert.Contains("If-Match", endpoints);
        Assert.Contains("expectedStamp", endpoints);
        Assert.Contains("Request.Headers", endpoints);
    }
}
```

- [ ] **Step 2: Run the focused Dynamic API generator test and verify it fails**

Run:

```powershell
dotnet test framework\test\CrestCreates.CodeGenerator.Tests\CrestCreates.CodeGenerator.Tests.csproj --filter "FullyQualifiedName~DynamicApiCrudMainlineTests"
```

Expected result:

```text
Failed GeneratedDynamicApi_DeleteExpectedStamp_ShouldBindFromIfMatchHeader
```

- [ ] **Step 3: Update parameter source detection**

In `DynamicApiAotSourceGenerator`, find the logic that resolves method parameter source. Add this rule before the generic query/body fallback:

```csharp
private static bool IsCrudDeleteExpectedStampParameter(IMethodSymbol method, IParameterSymbol parameter)
{
    return method.Name == "DeleteAsync"
        && parameter.Name == "expectedStamp"
        && parameter.Type.SpecialType == SpecialType.System_String
        && method.Parameters.Any(p => p.Name == "id");
}
```

When this returns true, emit parameter binding from header:

```csharp
var expectedStamp = httpContext.Request.Headers["If-Match"].FirstOrDefault();
```

Use the actual generated endpoint method's `HttpContext` variable name. If the generated code currently uses `context`, emit:

```csharp
var expectedStamp = context.Request.Headers["If-Match"].FirstOrDefault();
```

- [ ] **Step 4: Add descriptor metadata for header source**

If `DynamicApiParameterSource` already has `Header`, use it. If it does not, add:

```csharp
Header
```

to `framework/src/CrestCreates.DynamicApi/DynamicApiDescriptors.cs`.

When generating the parameter descriptor for `expectedStamp`, set:

```csharp
Source = DynamicApiParameterSource.Header,
Name = "expectedStamp"
```

and include a header name metadata value if the descriptor model supports it. If no header-name property exists, keep the generated endpoint binding as the source of truth and add a comment in the generated code:

```csharp
// CRUD delete concurrency token is bound from If-Match.
```

- [ ] **Step 5: Run Dynamic API tests**

Run:

```powershell
dotnet test framework\test\CrestCreates.CodeGenerator.Tests\CrestCreates.CodeGenerator.Tests.csproj --filter "FullyQualifiedName~DynamicApiCrudMainlineTests"
dotnet test framework\test\CrestCreates.CodeGenerator.Tests\CrestCreates.CodeGenerator.Tests.csproj --filter "FullyQualifiedName~DynamicApiAotSourceGeneratorTests"
```

Expected result:

```text
DynamicApiCrudMainlineTests: Passed
DynamicApiAotSourceGeneratorTests: Passed
```

- [ ] **Step 6: Commit Dynamic API header binding**

Run:

```powershell
git add framework\tools\CrestCreates.CodeGenerator\DynamicApiGenerator\DynamicApiAotSourceGenerator.cs framework\src\CrestCreates.DynamicApi\DynamicApiDescriptors.cs framework\test\CrestCreates.CodeGenerator.Tests\DynamicApiGenerator\DynamicApiCrudMainlineTests.cs
git commit -m "feat: bind crud delete if-match header"
```

---

## Task 6: Align Generated CRUD Permission Names

**Files:**
- Modify: `framework/tools/CrestCreates.CodeGenerator/Authorization/AuthorizationHelper.cs`
- Modify: `framework/tools/CrestCreates.CodeGenerator/Authorization/AuthorizationAttributeGenerator.cs`
- Test: `framework/test/CrestCreates.CodeGenerator.Tests/CrudServiceGenerator/CrudServiceMainlineSourceGeneratorTests.cs`

- [ ] **Step 1: Add explicit assertions for no `View` permissions**

Extend `GeneratedCrud_ShouldGeneratePermissions` with:

```csharp
Assert.DoesNotContain("View", source);
Assert.DoesNotContain("Export", source);
```

Add a new test:

```csharp
[Fact]
public void GeneratedCrud_AppService_ShouldCheckGeneratedPermissions()
{
    var result = RunProductGenerator();

    var source = result.GetSourceByFileName("ProductAppService.g.cs").SourceText;

    Assert.Contains("CheckPermissionAsync(ProductCrudPermissions.Create", source);
    Assert.Contains("CheckPermissionAsync(ProductCrudPermissions.Get", source);
    Assert.Contains("CheckPermissionAsync(ProductCrudPermissions.Search", source);
    Assert.Contains("CheckPermissionAsync(ProductCrudPermissions.Update", source);
    Assert.Contains("CheckPermissionAsync(ProductCrudPermissions.Delete", source);
    Assert.DoesNotContain("Product.View", source);
}
```

- [ ] **Step 2: Run permission-focused test and verify status**

Run:

```powershell
dotnet test framework\test\CrestCreates.CodeGenerator.Tests\CrestCreates.CodeGenerator.Tests.csproj --filter "GeneratedCrud_ShouldGeneratePermissions|GeneratedCrud_AppService_ShouldCheckGeneratedPermissions"
```

Expected result before code changes:

```text
At least one test fails if generated code still emits View/Export or checks raw string permissions.
```

- [ ] **Step 3: Update action mapping helper**

In `AuthorizationHelper.cs`, update CRUD method/action mapping so:

```csharp
GetByIdAsync -> Get
GetAsync -> Get
GetListAsync -> Search
SearchAsync -> Search
CreateAsync -> Create
UpdateAsync -> Update
DeleteAsync -> Delete
```

Use a dictionary equivalent to:

```csharp
private static readonly Dictionary<string, string> CrudMethodActions = new(StringComparer.Ordinal)
{
    ["CreateAsync"] = "Create",
    ["GetByIdAsync"] = "Get",
    ["GetAsync"] = "Get",
    ["GetListAsync"] = "Search",
    ["SearchAsync"] = "Search",
    ["UpdateAsync"] = "Update",
    ["DeleteAsync"] = "Delete"
};
```

If the existing helper uses HTTP verb mapping, keep that for non-CRUD controllers and apply this method-name mapping to generated CRUD app services first.

- [ ] **Step 4: Update permission generation defaults**

In `AuthorizationAttributeGenerator.cs`, ensure generated CRUD permission lists use exactly:

```csharp
new[] { "Create", "Get", "Search", "Update", "Delete" }
```

Do not generate `View` or `Export` for CRUD mainline.

- [ ] **Step 5: Run CodeGenerator tests**

Run:

```powershell
dotnet test framework\test\CrestCreates.CodeGenerator.Tests\CrestCreates.CodeGenerator.Tests.csproj --filter "FullyQualifiedName~CrudServiceMainlineSourceGeneratorTests"
dotnet test framework\test\CrestCreates.CodeGenerator.Tests\CrestCreates.CodeGenerator.Tests.csproj --filter "FullyQualifiedName~Authorization"
```

Expected result:

```text
CrudServiceMainlineSourceGeneratorTests: Passed
Authorization-related generator tests: Passed
```

- [ ] **Step 6: Commit permission alignment**

Run:

```powershell
git add framework\tools\CrestCreates.CodeGenerator\Authorization\AuthorizationHelper.cs framework\tools\CrestCreates.CodeGenerator\Authorization\AuthorizationAttributeGenerator.cs framework\test\CrestCreates.CodeGenerator.Tests\CrudServiceGenerator\CrudServiceMainlineSourceGeneratorTests.cs
git commit -m "feat: align generated crud permissions"
```

---

## Task 7: Add Generated CRUD Integration Tests

**Files:**
- Create: `framework/test/CrestCreates.IntegrationTests/GeneratedCrudMainlineIntegrationTests.cs`
- Modify: `samples/LibraryManagement/LibraryManagement.Domain/Entities/Book.cs`
- Modify: `samples/LibraryManagement/LibraryManagement.Application/Services/BookAppService.cs`
- Modify: `samples/LibraryManagement/LibraryManagement.Application.Contracts/Interfaces/IBookAppService.cs`

- [ ] **Step 1: Create integration test skeleton**

Create `framework/test/CrestCreates.IntegrationTests/GeneratedCrudMainlineIntegrationTests.cs`:

```csharp
using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace CrestCreates.IntegrationTests;

public sealed class GeneratedCrudMainlineIntegrationTests : IClassFixture<WebApplicationFactory>
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Admin123!";
    private const string HostTenantId = "host";
    private readonly WebApplicationFactory _factory;

    public GeneratedCrudMainlineIntegrationTests(WebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GeneratedCrud_ShouldUseDynamicApiGeneratedEndpoint()
    {
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        var response = await client.GetAsync("/api/app/book");

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GeneratedCrud_DeleteConcurrentEntity_RequiresIfMatch()
    {
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);
        var id = await CreateBookAsync(client);

        var response = await client.DeleteAsync($"/api/app/book/{id}");

        response.StatusCode.Should().Be((HttpStatusCode)428);
    }

    [Fact]
    public async Task GeneratedCrud_ShouldCheckPermissions()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/app/book");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<(HttpClient Client, TokenResponse LoginResult)> CreateAuthenticatedClientAsync(
        string userName,
        string password,
        string tenantId)
    {
        var client = _factory.CreateClient();
        var loginResult = await LoginAsync(client, userName, password, tenantId);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginResult.AccessToken);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);
        return (client, loginResult);
    }

    private static async Task<TokenResponse> LoginAsync(
        HttpClient client,
        string userName,
        string password,
        string tenantId)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = userName,
            Password = password,
            TenantId = tenantId
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginResult = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return loginResult!;
    }

    private static async Task<Guid> CreateBookAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/app/book", new
        {
            title = "Generated CRUD book",
            author = "Test Author",
            isbn = Guid.NewGuid().ToString("N")[..13],
            categoryId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            totalCopies = 3,
            availableCopies = 3
        });

        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<BookDto>();
        return dto!.Id;
    }

    private sealed class BookDto
    {
        public Guid Id { get; set; }
        public string ConcurrencyStamp { get; set; } = string.Empty;
    }

    private sealed class TokenResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }
}
```

Use the existing `WebApplicationFactory` type from `framework/test/CrestCreates.IntegrationTests/WebApplicationFactory.cs`. The helper methods above mirror the authentication pattern already used in `IntegrationTests.cs`.

- [ ] **Step 2: Run integration test and verify it fails on missing endpoint or auth helper**

Run:

```powershell
dotnet test framework\test\CrestCreates.IntegrationTests\CrestCreates.IntegrationTests.csproj --filter "FullyQualifiedName~GeneratedCrudMainlineIntegrationTests"
```

Expected result before sample wiring:

```text
Failed GeneratedCrud_ShouldUseDynamicApiGeneratedEndpoint
```

The expected failure is 404 or route missing until the sample entity is marked and generated Dynamic API includes the generated CRUD service.

- [ ] **Step 3: Mark one sample entity for generated CRUD**

Modify `samples/LibraryManagement/LibraryManagement.Domain/Entities/Book.cs`. Replace:

```csharp
[Entity]
public class Book : AuditedEntity<Guid>
{
    // existing properties
}
```

with:

```csharp
[Entity]
[GenerateCrudService]
public class Book : AuditedEntity<Guid>
{
    // existing properties
}
```

Do not mark multiple sample entities in this task.

- [ ] **Step 4: Remove duplicate hand-written CRUD endpoint conflicts**

Remove route conflicts with the existing hand-written controller. In `samples/LibraryManagement/LibraryManagement.Web/Controllers/BooksController.cs`, keep custom endpoints only when they do not overlap with `/api/app/book`. Generated CRUD acceptance must use `/api/app/book`.

Keep custom business methods in a separate service contract:

```csharp
public interface IBookBusinessAppService
{
    Task BorrowAsync(Guid bookId, Guid memberId, CancellationToken cancellationToken = default);
}
```

Do not mix custom business methods into generated CRUD acceptance.

- [ ] **Step 5: Run integration test**

Run:

```powershell
dotnet test framework\test\CrestCreates.IntegrationTests\CrestCreates.IntegrationTests.csproj --filter "FullyQualifiedName~GeneratedCrudMainlineIntegrationTests"
```

Expected result:

```text
GeneratedCrud_ShouldUseDynamicApiGeneratedEndpoint: Passed
GeneratedCrud_DeleteConcurrentEntity_RequiresIfMatch: Passed
GeneratedCrud_ShouldCheckPermissions: Passed
```

- [ ] **Step 6: Commit integration wiring**

Run:

```powershell
git add framework\test\CrestCreates.IntegrationTests\GeneratedCrudMainlineIntegrationTests.cs samples\LibraryManagement
git commit -m "test: cover generated crud dynamic api mainline"
```

---

## Task 8: Preserve Legacy Boundaries And Run Final Verification

**Files:**
- Modify: `framework/test/CrestCreates.CodeGenerator.Tests/CrudServiceGenerator/CrudServiceMainlineSourceGeneratorTests.cs`
- Verify: `framework/src/CrestCreates.Application/Services/CrudServiceBase.cs`
- Verify: `framework/src/CrestCreates.Application/Services/ICrudService.cs`
- Verify: `framework/src/CrestCreates.AspNetCore/Controllers/CrudControllerBase.cs`
- Verify: `framework/tools/CrestCreates.CodeGenerator/ControllerGenerator/CrudControllerSourceGenerator.cs`

- [ ] **Step 1: Add legacy boundary test**

Add this test to `CrudServiceMainlineSourceGeneratorTests`:

```csharp
[Fact]
public void GeneratedCrud_ShouldNotGenerateMainlineMvcController()
{
    var result = RunProductGenerator();

    Assert.DoesNotContain(result.GeneratedSources, x => x.FileName.Contains("Controller"));
    Assert.DoesNotContain(result.GeneratedSources, x => x.SourceText.Contains("CrudControllerBase<"));
    Assert.DoesNotContain(result.GeneratedSources, x => x.SourceText.Contains("[ApiController]"));
}
```

- [ ] **Step 2: Run boundary test**

Run:

```powershell
dotnet test framework\test\CrestCreates.CodeGenerator.Tests\CrestCreates.CodeGenerator.Tests.csproj --filter "GeneratedCrud_ShouldNotGenerateMainlineMvcController"
```

Expected result:

```text
Passed GeneratedCrud_ShouldNotGenerateMainlineMvcController
```

- [ ] **Step 3: Verify legacy files were not enhanced**

Run:

```powershell
git diff -- framework\src\CrestCreates.Application\Services\CrudServiceBase.cs framework\src\CrestCreates.Application\Services\ICrudService.cs framework\src\CrestCreates.AspNetCore\Controllers\CrudControllerBase.cs framework\tools\CrestCreates.CodeGenerator\ControllerGenerator\CrudControllerSourceGenerator.cs
```

Expected result:

```text
no diff
```

If there is a diff in these files, revert only changes made during this CRUD mainline work unless the diff is an explicit compile fix required by generated Dynamic API tests.

- [ ] **Step 4: Run focused verification suite**

Run:

```powershell
dotnet test framework\test\CrestCreates.CodeGenerator.Tests\CrestCreates.CodeGenerator.Tests.csproj --filter "FullyQualifiedName~CrudServiceMainlineSourceGeneratorTests|FullyQualifiedName~DynamicApiCrudMainlineTests"
dotnet test framework\test\CrestCreates.Application.Tests\CrestCreates.Application.Tests.csproj --filter "FullyQualifiedName~GeneratedCrudQueryTests"
dotnet test framework\test\CrestCreates.IntegrationTests\CrestCreates.IntegrationTests.csproj --filter "FullyQualifiedName~GeneratedCrudMainlineIntegrationTests"
```

Expected result:

```text
All focused CRUD mainline tests pass.
```

- [ ] **Step 5: Run broader regression tests**

Run:

```powershell
dotnet test framework\test\CrestCreates.CodeGenerator.Tests\CrestCreates.CodeGenerator.Tests.csproj
dotnet test framework\test\CrestCreates.Web.Tests\CrestCreates.Web.Tests.csproj
dotnet test framework\test\CrestCreates.Application.Tests\CrestCreates.Application.Tests.csproj
```

Expected result:

```text
All tests pass, except failures already documented before this branch.
```

If any failure is unrelated and pre-existing, record the test name and error in the final implementation summary. Do not change unrelated tests.

- [ ] **Step 6: Commit final test/boundary updates**

Run:

```powershell
git add framework\test\CrestCreates.CodeGenerator.Tests\CrudServiceGenerator\CrudServiceMainlineSourceGeneratorTests.cs
git commit -m "test: lock crud mainline legacy boundary"
```

---

## Final Implementation Checklist

| Spec requirement | Covered by task |
|---|---|
| One entity marker generates full CRUD surface | Tasks 1, 2, 3 |
| Generated path is AoT-friendly | Tasks 1, 5, 8 |
| Dynamic API generated endpoints expose CRUD | Tasks 5, 7 |
| Query descriptors work | Task 4 |
| Permissions are generated and checked | Tasks 2, 3, 6, 7 |
| Write operations use UoW | Tasks 3, 7 |
| Concurrency delete uses `If-Match` | Tasks 1, 5, 7 |
| Update DTO includes `ConcurrencyStamp` | Tasks 1, 2 |
| Create DTO excludes `ConcurrencyStamp` | Tasks 1, 2 |
| Platform exceptions are preserved | Tasks 1, 3, 4 |
| Legacy paths remain legacy | Task 8 |
| Sample proves mainline | Task 7 |

---

## Handoff Notes For Implementers

| Rule | Required behavior |
|---|---|
| Do not edit generated files | Edit source generators and tests only. |
| Do not add runtime reflection fallback | CRUD APIs must come from generated Dynamic API. |
| Do not expand `CrudServiceBase` | It stays obsolete legacy. |
| Do not make MVC CRUD controllers the acceptance path | Integration tests must hit generated Dynamic API. |
| Do not introduce AutoMapper | Use generated object mapping declarations and generated mapping calls. |
| Keep commits small | Commit after each task as listed. |
| Preserve unrelated worktree changes | Check `git status --short` before every commit and stage only task files. |
