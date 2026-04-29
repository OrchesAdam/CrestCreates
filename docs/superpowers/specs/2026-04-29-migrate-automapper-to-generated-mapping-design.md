# Migrate AutoMapper to Generated Object Mapping

**Date:** 2026-04-29
**Status:** Approved
**Parent:** AOT-friendly object mapping platform capability

## 1. Overview

Remove all AutoMapper dependency from the framework and sample app, replacing it with the compile-time `[GenerateObjectMapping]` SourceGenerator. Base classes (`CrestAppServiceBase`, `CrudServiceBase`) become abstract, with mapping methods as abstract. Concrete services implement them by calling generated static mapper methods.

## 2. Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Base class mapping methods | `abstract` | Forces each service to explicitly declare how mapping works; no hidden runtime reflection |
| CreateDto → Entity mapping | Not generated; manual in service override | Entities with parameterized constructors need domain-logic construction; generated `new Entity()` + property assignment would bypass validation |
| UpdateDto → Entity mapping | Generated `Apply` direction | Natural fit: apply DTO properties onto existing entity |
| Entity → Dto mapping | Generated `Both` direction | Standard projection; also provides `ToTargetExpression` for LINQ queries |
| Mapper naming | `{SourceType}To{TargetType}Mapper` | Consistent, predictable, matches existing generator convention |

## 3. Base Class Changes

### 3.1 CrestAppServiceBase

- Remove `using AutoMapper;`
- Remove `protected readonly IMapper Mapper;` field
- Remove `IMapper mapper` constructor parameter
- Change `MapToEntity(TCreateDto)` from `virtual` to `abstract`
- Change `MapToEntity(TUpdateDto, TEntity)` from `virtual` to `abstract`
- Change `MapToDto(TEntity)` from `virtual` to `abstract`
- Mark class as `abstract`
- Replace `Mapper.Map<List<TDto>>(entities)` in `GetAllAsync` and `GetListAsync` with `entities.Select(MapToDto).ToList()`

### 3.2 CrudServiceBase

- Remove `using AutoMapper;`
- Remove `protected readonly IMapper Mapper;` field
- Remove `IMapper mapper` constructor parameter
- Change `MapToEntity(TCreateDto)` from `virtual` to `abstract`
- Change `MapToEntity(TUpdateDto, TEntity)` from `virtual` to `abstract`
- Change `MapToDto(TEntity)` from `virtual` to `abstract`
- Mark class as `abstract`
- Replace `Mapper.Map<List<TDto>>(pagedEntities)` in `GetListAsync` with `pagedEntities.Select(MapToDto).ToList()`

## 4. SourceGenerator Changes

### 4.1 EntitySourceGenerator

**Remove:** `GenerateDtoMappings` method (AutoMapper Profile generation)

**Add:** Generate `[GenerateObjectMapping]` declaration classes:

```csharp
// Entity ↔ EntityDto (Direction = Both)
[GenerateObjectMapping(typeof({EntityName}), typeof({EntityName}Dto))]
public static partial class {EntityName}To{EntityName}DtoMapper { }

// UpdateEntityDto → Entity (Direction = Apply)
[GenerateObjectMapping(typeof(Update{EntityName}Dto), typeof({EntityName}), Direction = MapDirection.Apply)]
public static partial class Update{EntityName}DtoTo{EntityName}Mapper { }
```

No `CreateEntityDto → Entity` mapper is generated.

### 4.2 CrudServiceSourceGenerator

**Remove:**
- `using AutoMapper;`
- `protected readonly IMapper _mapper;` field
- `IMapper mapper` constructor parameter and assignment
- `GenerateMappingProfile` method (AutoMapper Profile generation)

**Replace `_mapper.Map` calls:**
- `_mapper.Map<{EntityName}>(input)` → keep as abstract `MapToEntity(input)` call (Create direction, no generated mapper)
- `_mapper.Map<{EntityName}Dto>(entity)` → `{EntityName}To{EntityName}DtoMapper.ToTarget(entity)`
- `_mapper.Map(input, entity)` → `Update{EntityName}DtoTo{EntityName}Mapper.Apply(input, entity)`
- `_mapper.Map<List<{EntityName}Dto>>(items)` → `items.Select({EntityName}To{EntityName}DtoMapper.ToTarget).ToList()`

**Generated service class structure:**

```csharp
public abstract class {EntityName}CrudServiceBase : I{EntityName}CrudService
{
    protected readonly I{EntityName}Repository _repository;

    protected {EntityName}CrudServiceBase(I{EntityName}Repository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public virtual async Task<{EntityName}Dto> CreateAsync(Create{EntityName}Dto input, CancellationToken cancellationToken = default)
    {
        var entity = MapToEntity(input);  // abstract, subclass implements
        // ... rest of create logic
        return {EntityName}To{EntityName}DtoMapper.ToTarget(entity);
    }

    public virtual async Task<{EntityName}Dto> UpdateAsync({IdType} id, Update{EntityName}Dto input, CancellationToken cancellationToken = default)
    {
        // ... fetch entity
        Update{EntityName}DtoTo{EntityName}Mapper.Apply(input, entity);
        // ... save
        return {EntityName}To{EntityName}DtoMapper.ToTarget(entity);
    }

    protected abstract {EntityName} MapToEntity(Create{EntityName}Dto dto);
    protected abstract {EntityName}Dto MapToDto({EntityName} entity);
}
```

**Add:** Generate `[GenerateObjectMapping]` declaration classes (same as EntitySourceGenerator).

**Important distinction:** The generated CRUD service calls mapper static methods directly (not through abstract overrides). Only `CrestAppServiceBase`-derived hand-written services use the abstract `MapToEntity`/`MapToDto` pattern. The generated CRUD service does not inherit `CrestAppServiceBase` — it has its own inline mapping calls.

## 5. Sample App Migration

### 5.1 LibraryManagementAutoMapperProfile.cs → Move to 99_RecycleBin

All mapping logic replaced by generated mappers and service overrides.

### 5.2 ApplicationModule.cs

- Remove `using AutoMapper;`
- Remove `services.AddAutoMapper(configuration => { configuration.AddMaps(typeof(ApplicationModule).Assembly); });`

### 5.3 BookAppService.cs

- Remove `using AutoMapper;`, `IMapper _mapper` field, `IMapper mapper` constructor parameter
- Remove `base(repository, mapper, ...)` → `base(repository, ...)`
- Implement abstract methods:

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

protected override void MapToEntity(UpdateBookDto dto, Book entity)
{
    entity.SetTitle(dto.Title);
    entity.SetAuthor(dto.Author);
    entity.SetISBN(dto.ISBN);
    entity.SetParent(dto.CategoryId);
    entity.SetStatus((BookStatus)dto.Status);
    entity.SetDescription(dto.Description);
}

protected override BookDto MapToDto(Book entity)
    => BookToBookDtoMapper.ToTarget(entity);
```

- Replace `_mapper.Map<BookDto>(book)` in `GetByIsbnAsync` with `BookToBookDtoMapper.ToTarget(book)`

### 5.4 LoanAppService.cs

- Remove `using AutoMapper;`, `IMapper _mapper` field, `IMapper mapper` constructor parameter
- Implement abstract methods:

```csharp
protected override Loan MapToEntity(CreateLoanDto dto)
    => new Loan(Guid.NewGuid(), dto.BookId, dto.MemberId, dto.LoanDays ?? 0, dto.Notes);

// LoanAppService uses LoanDto as TUpdateDto — domain methods, not generated mapper
protected override void MapToEntity(LoanDto dto, Loan entity)
{
    // LoanDto as update input is unusual; no generated mapper for LoanDto → Loan
    // If specific domain methods exist, call them here
}

protected override LoanDto MapToDto(Loan entity)
    => LoanToLoanDtoMapper.ToTarget(entity);
```

- Replace `_mapper.Map<LoanDto>(loan)` in `MapToDtoAsync` with `LoanToLoanDtoMapper.ToTarget(loan)`
- Note: `LoanDto → Loan` mapping is not generated because `LoanDto` is not an `UpdateLoanDto`; this service uses `LoanDto` as `TUpdateDto` which is an atypical pattern

### 5.5 CategoryAppService.cs / MemberAppService.cs

Same pattern: remove `IMapper`, implement abstract methods with generated mapper calls.

### 5.6 Mapper Declarations

Add to the sample project (or let EntitySourceGenerator auto-generate):

```csharp
[GenerateObjectMapping(typeof(Book), typeof(BookDto))]
public static partial class BookToBookDtoMapper { }

[GenerateObjectMapping(typeof(Loan), typeof(LoanDto))]
public static partial class LoanToLoanDtoMapper { }

[GenerateObjectMapping(typeof(Category), typeof(CategoryDto))]
public static partial class CategoryToCategoryDtoMapper { }

[GenerateObjectMapping(typeof(Member), typeof(MemberDto))]
public static partial class MemberToMemberDtoMapper { }
```

## 6. Package Dependency Cleanup

**Remove from `Directory.Packages.props`:**
- `<PackageVersion Include="AutoMapper" Version="16.1.1" />`

**Remove from project files:**
- `CrestCreates.Application.csproj` — `<PackageReference Include="AutoMapper" />`
- `CrestCreates.Infrastructure.csproj` — `<PackageReference Include="AutoMapper" />`
- `CrestCreates.Database.Migrations.Tests.csproj` — `<PackageReference Include="AutoMapper" />`

## 7. Test Project Changes

- `TestOrderAppService.cs` — Remove `IMapper` dependency, implement abstract mapping methods with generated mapper calls

## 8. Migration Scope Summary

| Category | Files Changed | Nature |
|----------|--------------|--------|
| Framework base classes | 2 | Remove IMapper, make abstract |
| Source generators | 2 | Replace AutoMapper generation with [GenerateObjectMapping] |
| Sample app services | 4 | Remove IMapper, implement abstract methods |
| Sample app module/profile | 2 | Remove AutoMapper registration, delete profile |
| Project files (csproj) | 3 | Remove AutoMapper package ref |
| Central package mgmt | 1 | Remove AutoMapper version |
| Test files | 1 | Remove IMapper, implement abstract methods |
| **Total** | **15** | |

## 9. Acceptance Criteria

- [ ] `CrestAppServiceBase` and `CrudServiceBase` have no AutoMapper dependency
- [ ] Both base classes are abstract with abstract mapping methods
- [ ] EntitySourceGenerator generates `[GenerateObjectMapping]` declarations instead of AutoMapper Profiles
- [ ] CrudServiceSourceGenerator generates static mapper calls instead of `_mapper.Map`
- [ ] Sample app compiles and runs without AutoMapper
- [ ] No `using AutoMapper` remains in framework source or sample app
- [ ] AutoMapper package removed from all csproj files and Directory.Packages.props
- [ ] All existing tests pass
