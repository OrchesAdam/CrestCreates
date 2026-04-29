# Migrate AutoMapper to Generated Object Mapping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove all AutoMapper dependency and replace with compile-time `[GenerateObjectMapping]` static mapper calls.

**Architecture:** Base classes (`CrestAppServiceBase`, `CrudServiceBase`) become abstract with abstract mapping methods. Concrete services implement them by calling generated static mapper methods. SourceGenerators emit `[GenerateObjectMapping]` declarations instead of AutoMapper Profiles. CreateDto→Entity mapping is manual (parameterized constructors); UpdateDto→Entity uses generated `Apply`; Entity→Dto uses generated `ToTarget`.

**Tech Stack:** .NET 10, Roslyn SourceGenerators, `[GenerateObjectMapping]` attribute system

---

## Task 1: Migrate CrestAppServiceBase — Remove AutoMapper

**Files:**
- Modify: `framework/src/CrestCreates.Application/Services/CrestAppServiceBase.cs`

- [ ] **Step 1: Edit CrestAppServiceBase.cs — remove AutoMapper using and field**

Remove `using AutoMapper;` (line 6).

Remove `protected readonly IMapper Mapper;` (line 26).

Remove `IMapper mapper` from constructor parameter (line 38) and its null-check assignment (line 44).

The constructor becomes:

```csharp
protected CrestAppServiceBase(
    ICrestRepositoryBase<TEntity, TKey> repository,
    IServiceProvider serviceProvider,
    ICurrentUser currentUser,
    IDataPermissionFilter dataPermissionFilter,
    IPermissionChecker permissionChecker)
{
    _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    CurrentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    DataPermissionFilter = dataPermissionFilter ?? throw new ArgumentNullException(nameof(dataPermissionFilter));
    PermissionChecker = permissionChecker ?? throw new ArgumentNullException(nameof(permissionChecker));
}
```

- [ ] **Step 2: Make mapping methods abstract and class abstract**

Add `abstract` to the class declaration (line 20):

```csharp
public abstract class CrestAppServiceBase<TEntity, TKey, TDto, TCreateDto, TUpdateDto> : ICrestAppServiceBase<TEntity, TKey, TDto, TCreateDto, TUpdateDto>
```

Change the three mapping methods from `virtual` to `abstract` and remove their bodies:

```csharp
protected abstract TEntity MapToEntity(TCreateDto dto);

protected abstract void MapToEntity(TUpdateDto dto, TEntity entity);

protected abstract TDto MapToDto(TEntity entity);
```

- [ ] **Step 3: Replace Mapper.Map<List<TDto>> calls with Select**

In `GetAllAsync` (line 195), replace:

```csharp
return Mapper.Map<List<TDto>>(entities);
```

with:

```csharp
return entities.Select(MapToDto).ToList();
```

In `GetListAsync` (line 211), replace:

```csharp
var dtos = Mapper.Map<List<TDto>>(entities);
```

with:

```csharp
var dtos = entities.Select(MapToDto).ToList();
```

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Application`

Expected: Build fails with errors in all classes that inherit `CrestAppServiceBase` (they now need to implement the abstract methods). This is expected — we'll fix them in later tasks.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Application/Services/CrestAppServiceBase.cs
git commit -m "refactor(CrestAppServiceBase): remove AutoMapper, make mapping methods abstract"
```

---

## Task 2: Migrate CrudServiceBase — Remove AutoMapper

**Files:**
- Modify: `framework/src/CrestCreates.Application/Services/CrudServiceBase.cs`

- [ ] **Step 1: Edit CrudServiceBase.cs — remove AutoMapper entirely**

Remove `using AutoMapper;` (line 5).

Remove `protected readonly IMapper Mapper;` (line 32).

Remove `IMapper mapper` from constructor parameter (line 34) and its null-check assignment (line 37).

The constructor becomes:

```csharp
protected CrudServiceBase(IRepository<TEntity, TKey> repository)
{
    Repository = repository ?? throw new ArgumentNullException(nameof(repository));
}
```

- [ ] **Step 2: Make mapping methods abstract and class abstract**

Add `abstract` to the class declaration (line 19):

```csharp
public abstract class CrudServiceBase<TEntity, TKey, TDto, TCreateDto, TUpdateDto>
```

Change the three mapping methods from `virtual` with body to `abstract` without body:

```csharp
protected abstract TEntity MapToEntity(TCreateDto dto);

protected abstract void MapToEntity(TUpdateDto dto, TEntity entity);

protected abstract TDto MapToDto(TEntity entity);
```

- [ ] **Step 3: Replace Mapper.Map<List<TDto>> call**

In `GetListAsync` (line 72), replace:

```csharp
var dtos = Mapper.Map<List<TDto>>(pagedEntities);
```

with:

```csharp
var dtos = pagedEntities.Select(MapToDto).ToList();
```

Add `using System.Linq;` if not already present.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Application/Services/CrudServiceBase.cs
git commit -m "refactor(CrudServiceBase): remove AutoMapper, make mapping methods abstract"
```

---

## Task 3: Migrate CrudServiceSourceGenerator — Replace AutoMapper with Static Mappers

**Files:**
- Modify: `framework/tools/CrestCreates.CodeGenerator/CrudServiceGenerator/CrudServiceSourceGenerator.cs`

- [ ] **Step 1: Remove AutoMapper from generated using list**

In `GenerateCrudServiceImplementation` (line 498), remove:

```csharp
builder.AppendLine("using AutoMapper;");
```

- [ ] **Step 2: Remove IMapper field and constructor parameter**

Remove the field (line 526):

```csharp
builder.AppendLine("        protected readonly IMapper _mapper;");
```

Change constructor (lines 529-534) to remove `IMapper mapper`:

```csharp
var modifier = generateAsBaseClass ? "protected" : "public";
builder.AppendLine($"        {modifier} {className}(I{entityName}Repository repository)");
builder.AppendLine("        {");
builder.AppendLine("            _repository = repository ?? throw new ArgumentNullException(nameof(repository));");
builder.AppendLine("        }");
```

- [ ] **Step 3: Replace _mapper.Map calls in CreateAsync**

In `CreateAsync` (lines 547, 551), replace:

```csharp
var entity = _mapper.Map<{entityName}>(input);
```

with:

```csharp
var entity = MapToEntity(input);
```

Replace:

```csharp
return _mapper.Map<{entityName}Dto>(entity);
```

with:

```csharp
return {entityName}To{entityName}DtoMapper.ToTarget(entity);
```

- [ ] **Step 4: Replace _mapper.Map call in GetByIdAsync**

In `GetByIdAsync` (line 564), replace:

```csharp
return _mapper.Map<{entityName}Dto>(entity);
```

with:

```csharp
return {entityName}To{entityName}DtoMapper.ToTarget(entity);
```

- [ ] **Step 5: Replace _mapper.Map call in GetListAsync**

In `GetListAsync` (line 634), replace:

```csharp
var dtos = _mapper.Map<List<{entityName}Dto>>(items);
```

with:

```csharp
var dtos = items.Select({entityName}To{entityName}DtoMapper.ToTarget).ToList();
```

- [ ] **Step 6: Replace _mapper.Map calls in UpdateAsync**

In `UpdateAsync` (line 652), replace:

```csharp
_mapper.Map(input, entity);
```

with:

```csharp
Update{entityName}DtoTo{entityName}Mapper.Apply(input, entity);
```

Replace (line 655):

```csharp
return _mapper.Map<{entityName}Dto>(entity);
```

with:

```csharp
return {entityName}To{entityName}DtoMapper.ToTarget(entity);
```

- [ ] **Step 7: Add abstract MapToEntity method for Create direction**

The generated class always needs `MapToEntity(CreateDto)` as abstract because CreateDto→Entity mapping is manual (no generated mapper for this direction per 方案C). When `generateAsBaseClass` is true, the class is already abstract. When `generateAsBaseClass` is false, the class must also be made abstract since it contains abstract methods.

After the constructor, add (unconditionally):

```csharp
builder.AppendLine($"        protected abstract {entityName} MapToEntity(Create{entityName}Dto dto);");
```

Also ensure the class is always abstract — change the class modifier logic:

```csharp
var classModifier = "abstract";  // Always abstract since it contains abstract MapToEntity
```

Note: `MapToDto` and `MapToEntity(UpdateDto, Entity)` are NOT abstract — they directly call the generated static mappers.

- [ ] **Step 8: Replace GenerateMappingProfile with GenerateObjectMappingDeclarations**

Replace the `GenerateMappingProfile` method (lines 757-783) with a new method `GenerateObjectMappingDeclarations`:

```csharp
private void GenerateObjectMappingDeclarations(SourceProductionContext context, string entityName, string namespaceName, string dtosNamespace)
{
    var builder = new StringBuilder();
    builder.AppendLine("#nullable enable");
    builder.AppendLine("// <auto-generated />");
    builder.AppendLine("using CrestCreates.Domain.Shared.ObjectMapping;");
    builder.AppendLine($"using {namespaceName};");
    builder.AppendLine($"using {dtosNamespace};");
    builder.AppendLine();
    builder.AppendLine($"namespace {namespaceName}.Mappings");
    builder.AppendLine("{");
    builder.AppendLine($"    [GenerateObjectMapping(typeof({entityName}), typeof({entityName}Dto))]");
    builder.AppendLine($"    public static partial class {entityName}To{entityName}DtoMapper {{ }}");
    builder.AppendLine();
    builder.AppendLine($"    [GenerateObjectMapping(typeof(Update{entityName}Dto), typeof({entityName}), Direction = MapDirection.Apply)]");
    builder.AppendLine($"    public static partial class Update{entityName}DtoTo{entityName}Mapper {{ }}");
    builder.AppendLine("}");

    context.AddSource($"{entityName}ObjectMappings.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
}
```

- [ ] **Step 9: Update the call site in the pipeline**

At line 188, replace:

```csharp
GenerateMappingProfile(context, entityClass, entityName, namespaceName);
```

with:

```csharp
var dtosNamespace = $"{namespaceName}.Dtos";
GenerateObjectMappingDeclarations(context, entityName, namespaceName, dtosNamespace);
```

- [ ] **Step 10: Build the CodeGenerator project**

Run: `dotnet build framework/tools/CrestCreates.CodeGenerator`

Expected: Build succeeds.

- [ ] **Step 11: Commit**

```bash
git add framework/tools/CrestCreates.CodeGenerator/CrudServiceGenerator/CrudServiceSourceGenerator.cs
git commit -m "refactor(CrudServiceSourceGenerator): replace AutoMapper with generated static mappers"
```

---

## Task 4: Migrate EntitySourceGenerator — Replace AutoMapper Profile with ObjectMapping Declarations

**Files:**
- Modify: `framework/tools/CrestCreates.CodeGenerator/EntityGenerator/EntitySourceGenerator.cs`

- [ ] **Step 1: Replace GenerateDtoMappings method**

Replace the `GenerateDtoMappings` method (lines 1256-1287) with `GenerateObjectMappingDeclarations`:

```csharp
private void GenerateObjectMappingDeclarations(SourceProductionContext context, INamedTypeSymbol entityClass)
{
    var entityName = entityClass.Name;
    var namespaceName = entityClass.ContainingNamespace.ToDisplayString();
    var dtosNamespace = GetTargetNamespace(namespaceName, GeneratedCodeType.Dto);
    var mappingsNamespace = GetTargetNamespace(namespaceName, GeneratedCodeType.MappingProfile);

    var builder = new StringBuilder();
    builder.AppendLine("#nullable enable");
    builder.AppendLine("// <auto-generated />");
    builder.AppendLine("using CrestCreates.Domain.Shared.ObjectMapping;");
    builder.AppendLine($"using {namespaceName};");
    builder.AppendLine($"using {dtosNamespace};");
    builder.AppendLine();
    builder.AppendLine($"namespace {mappingsNamespace}");
    builder.AppendLine("{");
    builder.AppendLine($"    [GenerateObjectMapping(typeof({entityName}), typeof({entityName}Dto))]");
    builder.AppendLine($"    public static partial class {entityName}To{entityName}DtoMapper {{ }}");
    builder.AppendLine();
    builder.AppendLine($"    [GenerateObjectMapping(typeof(Update{entityName}Dto), typeof({entityName}), Direction = MapDirection.Apply)]");
    builder.AppendLine($"    public static partial class Update{entityName}DtoTo{entityName}Mapper {{ }}");
    builder.AppendLine("}");

    context.AddSource($"{entityName}ObjectMappings.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
}
```

- [ ] **Step 2: Add call to GenerateObjectMappingDeclarations in the pipeline**

In the entity processing pipeline (around line 82, after `GenerateUpdateEntityDto`), add:

```csharp
GenerateObjectMappingDeclarations(context, entityClass);
```

- [ ] **Step 3: Build the CodeGenerator project**

Run: `dotnet build framework/tools/CrestCreates.CodeGenerator`

Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add framework/tools/CrestCreates.CodeGenerator/EntityGenerator/EntitySourceGenerator.cs
git commit -m "refactor(EntitySourceGenerator): replace AutoMapper Profile with GenerateObjectMapping declarations"
```

---

## Task 5: Migrate Sample App — BookAppService

**Files:**
- Modify: `samples/LibraryManagement/LibraryManagement.Application/Services/BookAppService.cs`

- [ ] **Step 1: Remove AutoMapper dependency from BookAppService**

Remove `using AutoMapper;` (line 1).

Remove `private readonly IMapper _mapper;` (line 20).

Remove `IMapper mapper` from constructor parameter and `base(repository, mapper, ...)` call.

The constructor becomes:

```csharp
public BookAppService(ICrestRepositoryBase<Book, Guid> repository, IServiceProvider serviceProvider, ICurrentUser currentUser, IDataPermissionFilter dataPermissionFilter, IPermissionChecker permissionChecker, IBookRepository repository2) : base(repository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
{
    _repository = repository2;
}
```

- [ ] **Step 2: Implement abstract mapping methods**

Add the three abstract method implementations:

```csharp
protected override Book MapToEntity(CreateBookDto dto)
{
    return new Book(
        Guid.NewGuid(),
        dto.Title,
        dto.Author,
        dto.ISBN,
        dto.CategoryId,
        dto.TotalCopies,
        dto.Description,
        dto.PublishDate,
        dto.Publisher,
        dto.Location
    );
}

protected override BookDto MapToDto(Book entity)
    => BookToBookDtoMapper.ToTarget(entity);
```

The existing `MapToEntity(UpdateBookDto dto, Book entity)` override (already present) stays as-is — it calls domain methods directly.

- [ ] **Step 3: Replace _mapper.Map in GetByIsbnAsync**

In `GetByIsbnAsync` (line 32), replace:

```csharp
return book == null ? null : _mapper.Map<BookDto>(book);
```

with:

```csharp
return book == null ? null : BookToBookDtoMapper.ToTarget(book);
```

- [ ] **Step 4: Commit**

```bash
git add samples/LibraryManagement/LibraryManagement.Application/Services/BookAppService.cs
git commit -m "refactor(BookAppService): replace AutoMapper with generated static mappers"
```

---

## Task 6: Migrate Sample App — LoanAppService

**Files:**
- Modify: `samples/LibraryManagement/LibraryManagement.Application/Services/LoanAppService.cs`

- [ ] **Step 1: Remove AutoMapper dependency from LoanAppService**

Remove `using AutoMapper;` (line 1).

Remove `private readonly IMapper _mapper;` (line 29).

Remove `IMapper mapper` from constructor parameter and `base(repository, mapper, ...)` call.

The constructor becomes:

```csharp
public LoanAppService(ICrestRepositoryBase<Loan, Guid> repository, IServiceProvider serviceProvider, ICurrentUser currentUser, IDataPermissionFilter dataPermissionFilter, IPermissionChecker permissionChecker, ILoanRepository loanRepository, IBookRepository bookRepository, IMemberRepository memberRepository) : base(repository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
{
    _loanRepository = loanRepository;
    _bookRepository = bookRepository;
    _memberRepository = memberRepository;
}
```

- [ ] **Step 2: Implement abstract mapping methods**

```csharp
protected override Loan MapToEntity(CreateLoanDto dto)
    => new Loan(Guid.NewGuid(), dto.BookId, dto.MemberId, dto.LoanDays ?? 0, dto.Notes);

protected override void MapToEntity(LoanDto dto, Loan entity)
{
    // LoanDto is used as TUpdateDto — no generated mapper for this direction
    // Update Loan entity properties from LoanDto if needed
}

protected override LoanDto MapToDto(Loan entity)
    => LoanToLoanDtoMapper.ToTarget(entity);
```

- [ ] **Step 3: Replace _mapper.Map in MapToDtoAsync**

In the private `MapToDtoAsync` method (line 194), replace:

```csharp
var dto = _mapper.Map<LoanDto>(loan);
```

with:

```csharp
var dto = LoanToLoanDtoMapper.ToTarget(loan);
```

- [ ] **Step 4: Commit**

```bash
git add samples/LibraryManagement/LibraryManagement.Application/Services/LoanAppService.cs
git commit -m "refactor(LoanAppService): replace AutoMapper with generated static mappers"
```

---

## Task 7: Migrate Sample App — CategoryAppService

**Files:**
- Modify: `samples/LibraryManagement/LibraryManagement.Application/Services/CategoryAppService.cs`

- [ ] **Step 1: Remove AutoMapper dependency from CategoryAppService**

Remove `using AutoMapper;` (line 1).

Remove `private readonly IMapper _mapper;` (line 19).

Remove `IMapper mapper` from constructor parameter and `base(repository, mapper, ...)` call.

The constructor becomes:

```csharp
public CategoryAppService(ICrestRepositoryBase<Category, Guid> repository, IServiceProvider serviceProvider, ICurrentUser currentUser, IDataPermissionFilter dataPermissionFilter, IPermissionChecker permissionChecker, ICategoryRepository categoryRepository) : base(repository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
{
    _categoryRepository = categoryRepository;
}
```

- [ ] **Step 2: Implement abstract mapping methods**

```csharp
protected override Category MapToEntity(CreateCategoryDto dto)
    => new Category(Guid.NewGuid(), dto.Name, dto.Description, dto.ParentId);

protected override void MapToEntity(UpdateCategoryDto dto, Category entity)
    => UpdateCategoryDtoToCategoryMapper.Apply(dto, entity);

protected override CategoryDto MapToDto(Category entity)
    => CategoryToCategoryDtoMapper.ToTarget(entity);
```

- [ ] **Step 3: Replace _mapper.Map in private MapToDtoAsync**

In the private `MapToDtoAsync` method (line 62), replace:

```csharp
var dto = _mapper.Map<CategoryDto>(category);
```

with:

```csharp
var dto = CategoryToCategoryDtoMapper.ToTarget(category);
```

- [ ] **Step 4: Commit**

```bash
git add samples/LibraryManagement/LibraryManagement.Application/Services/CategoryAppService.cs
git commit -m "refactor(CategoryAppService): replace AutoMapper with generated static mappers"
```

---

## Task 8: Migrate Sample App — MemberAppService

**Files:**
- Modify: `samples/LibraryManagement/LibraryManagement.Application/Services/MemberAppService.cs`

- [ ] **Step 1: Remove AutoMapper dependency from MemberAppService**

Remove `using AutoMapper;` (line 1).

Remove `private readonly IMapper _mapper;` (line 28).

Remove `IMapper mapper` from constructor parameter and `base(repository, mapper, ...)` call.

The constructor becomes:

```csharp
public MemberAppService(ICrestRepositoryBase<Member, Guid> repository, IServiceProvider serviceProvider, ICurrentUser currentUser, IDataPermissionFilter dataPermissionFilter, IPermissionChecker permissionChecker, IMemberRepository memberRepository, ILoanRepository loanRepository) : base(repository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
{
    _memberRepository = memberRepository;
    _loanRepository = loanRepository;
}
```

- [ ] **Step 2: Implement abstract mapping methods**

```csharp
protected override Member MapToEntity(CreateMemberDto dto)
    => new Member(Guid.NewGuid(), dto.Name, dto.Email, dto.Type, dto.Phone, dto.Address, dto.ExpiryDate);

protected override void MapToEntity(MemberDto dto, Member entity)
{
    // MemberDto is used as TUpdateDto — no generated mapper for this direction
}

protected override MemberDto MapToDto(Member entity)
    => MemberToMemberDtoMapper.ToTarget(entity);
```

- [ ] **Step 3: Replace _mapper.Map in private MapToDtoAsync**

In the private `MapToDtoAsync` method (line 79), replace:

```csharp
var dto = _mapper.Map<MemberDto>(member);
```

with:

```csharp
var dto = MemberToMemberDtoMapper.ToTarget(member);
```

- [ ] **Step 4: Commit**

```bash
git add samples/LibraryManagement/LibraryManagement.Application/Services/MemberAppService.cs
git commit -m "refactor(MemberAppService): replace AutoMapper with generated static mappers"
```

---

## Task 9: Migrate Sample App — Module and Profile Cleanup

**Files:**
- Modify: `samples/LibraryManagement/LibraryManagement.Application/Modules/ApplicationModule.cs`
- Move: `samples/LibraryManagement/LibraryManagement.Application/LibraryManagementAutoMapperProfile.cs` → `99_RecycleBin/`

- [ ] **Step 1: Remove AutoMapper registration from ApplicationModule**

In `ApplicationModule.cs`, remove `using AutoMapper;` and remove the `services.AddAutoMapper(...)` block (lines 18-21).

The `OnConfigureServices` method becomes:

```csharp
public override void OnConfigureServices(IServiceCollection services)
{
    services.AddScoped<IBookAppService, BookAppService>();
    services.AddScoped<ICategoryAppService, CategoryAppService>();
    services.AddScoped<IMemberAppService, MemberAppService>();
    services.AddScoped<ILoanAppService, LoanAppService>();
}
```

- [ ] **Step 2: Move AutoMapper profile to recycle bin**

```bash
mkdir -p 99_RecycleBin
mv samples/LibraryManagement/LibraryManagement.Application/LibraryManagementAutoMapperProfile.cs 99_RecycleBin/
```

- [ ] **Step 3: Commit**

```bash
git add samples/LibraryManagement/LibraryManagement.Application/Modules/ApplicationModule.cs
git add 99_RecycleBin/LibraryManagementAutoMapperProfile.cs
git add samples/LibraryManagement/LibraryManagement.Application/LibraryManagementAutoMapperProfile.cs
git commit -m "refactor(sample): remove AutoMapper registration and profile"
```

---

## Task 10: Migrate TestOrderAppService

**Files:**
- Modify: `framework/test/CrestCreates.CodeGenerator.Tests/Services/TestOrderAppService.cs`

- [ ] **Step 1: Remove AutoMapper dependency from TestOrderAppService**

Remove `using AutoMapper;` (line 4).

Remove `IMapper mapper` from constructor parameter (line 22) and `base(repository, mapper, ...)` call.

The constructor becomes:

```csharp
public TestOrderAppService(
    ICrestRepositoryBase<TestOrder, long> repository,
    IServiceProvider serviceProvider,
    ICurrentUser currentUser,
    IDataPermissionFilter dataPermissionFilter,
    IPermissionChecker permissionChecker)
    : base(repository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
{
}
```

- [ ] **Step 2: Implement abstract mapping methods**

Add the three abstract method implementations:

```csharp
protected override TestOrder MapToEntity(CreateTestOrderDto dto)
{
    return new TestOrder
    {
        OrderNumber = dto.OrderNumber,
        CustomerId = dto.CustomerId,
        TotalAmount = dto.TotalAmount,
        Notes = dto.Notes ?? string.Empty
    };
}

protected override void MapToEntity(UpdateTestOrderDto dto, TestOrder entity)
{
    if (dto.Notes != null)
        entity.Notes = dto.Notes;
}

protected override TestOrderDto MapToDto(TestOrder entity)
{
    return new TestOrderDto
    {
        Id = entity.Id,
        OrderNumber = entity.OrderNumber,
        CustomerId = entity.CustomerId,
        TotalAmount = entity.TotalAmount,
        OrderDate = entity.OrderDate,
        Status = entity.Status,
        Notes = entity.Notes
    };
}
```

- [ ] **Step 3: Replace Mapper.Map calls in business methods**

Replace all four `Mapper.Map<TestOrderDto>(order)` calls (lines 45, 62, 79, 96) with `MapToDto(order)`:

```csharp
return MapToDto(order);
```

- [ ] **Step 4: Commit**

```bash
git add framework/test/CrestCreates.CodeGenerator.Tests/Services/TestOrderAppService.cs
git commit -m "refactor(TestOrderAppService): replace AutoMapper with manual mapping methods"
```

---

## Task 11: Remove AutoMapper Package References

**Files:**
- Modify: `Directory.Packages.props` (line 49)
- Modify: `framework/src/CrestCreates.Application/CrestCreates.Application.csproj` (line 23)
- Modify: `framework/src/CrestCreates.Infrastructure/CrestCreates.Infrastructure.csproj` (line 23)
- Modify: `framework/test/CrestCreates.Database.Migrations.Tests/CrestCreates.Database.Migrations.Tests.csproj` (line 26)

- [ ] **Step 1: Remove AutoMapper from Directory.Packages.props**

Remove line 49:

```xml
<PackageVersion Include="AutoMapper" Version="16.1.1" />
```

- [ ] **Step 2: Remove AutoMapper from CrestCreates.Application.csproj**

Remove line 23:

```xml
<PackageReference Include="AutoMapper" />
```

- [ ] **Step 3: Remove AutoMapper from CrestCreates.Infrastructure.csproj**

Remove line 23:

```xml
<PackageReference Include="AutoMapper" />
```

- [ ] **Step 4: Remove AutoMapper from CrestCreates.Database.Migrations.Tests.csproj**

Remove line 26:

```xml
<PackageReference Include="AutoMapper" />
```

- [ ] **Step 5: Commit**

```bash
git add Directory.Packages.props framework/src/CrestCreates.Application/CrestCreates.Application.csproj framework/src/CrestCreates.Infrastructure/CrestCreates.Infrastructure.csproj framework/test/CrestCreates.Database.Migrations.Tests/CrestCreates.Database.Migrations.Tests.csproj
git commit -m "chore: remove AutoMapper package references"
```

---

## Task 12: Full Build Verification

**Files:** None (verification only)

- [ ] **Step 1: Build the entire solution**

Run: `dotnet build CrestCreates.slnx`

Expected: Build succeeds with no errors. There should be no remaining references to AutoMapper.

- [ ] **Step 2: Search for any remaining AutoMapper references**

Run: `grep -r "AutoMapper" --include="*.cs" --include="*.csproj" --include="*.props" framework/ samples/`

Expected: No results (or only comments/docs).

- [ ] **Step 3: Run existing tests**

Run: `dotnet test CrestCreates.slnx`

Expected: All tests pass.

- [ ] **Step 4: Final commit if any fixes were needed**

```bash
git add -A
git commit -m "fix: resolve build issues after AutoMapper migration"
```
