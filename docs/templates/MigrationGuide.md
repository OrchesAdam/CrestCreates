# 从旧特性迁移到新基类模式的指南

## 概述

本指南帮助您从旧的代码生成方式迁移到新的基类模式。新的基类模式提供了更好的扩展性和更清晰的代码结构。

## 新旧对比

### 旧方式

```csharp
// 旧的特性方式
[GenerateRepository(OrmProvider = OrmProvider.EfCore)]
[GenerateCrudService(GenerateDto = true, GenerateController = true)]
public class Book : AuditedEntity<Guid>
{
    // ...
}

// 自动生成完整的 Repository 和 Service
// 无法方便地自定义和扩展
```

### 新方式

```csharp
// 新的统一特性
[GenerateEntity(
    GenerateRepository = true,
    GenerateCrudService = true,
    GenerateAsBaseClass = true,
    OrmProvider = OrmProvider.EfCore)]
public class Book : AuditedEntity<Guid>
{
    // ...
}

// 自动生成基类
// 您手动实现继承基类的具体类，按需重写方法
public class BookRepository : BookRepositoryBase, IBookRepository
{
    public BookRepository(LibraryDbContext dbContext) : base(dbContext)
    {
    }
    
    // 可以重写基类方法或添加自定义方法
}
```

## 迁移步骤

### 第一步：更新实体特性

1. 移除旧的特性：
   - `[GenerateRepository]`
   - `[GenerateCrudService]`
   - `[GenerateQueryBuilder]`

2. 添加新的 `[GenerateEntity]` 特性：

```csharp
// 旧代码
[GenerateRepository(OrmProvider = OrmProvider.EfCore)]
[GenerateCrudService(GenerateDto = true, GenerateController = true, ServiceRoute = "api/books")]
[GenerateQueryBuilder]
public class Book : AuditedEntity<Guid>
{
    // ...
}

// 新代码
[GenerateEntity(
    GenerateRepository = true,
    GenerateCrudService = true,
    GenerateAsBaseClass = true,
    OrmProvider = OrmProvider.EfCore,
    GenerateQueryExtensions = true,
    GenerateController = true,
    ControllerRoute = "api/books")]
public class Book : AuditedEntity<Guid>
{
    // ...
}
```

### 第二步：修改 Repository 实现

1. 删除旧的自动生成的 Repository（如果有）
2. 创建新的 Repository，继承基类：

```csharp
// 旧代码（通常是自动生成的）
public class BookRepository : EfCoreRepository<Book, Guid>, IBookRepository
{
    public BookRepository(LibraryDbContext context) : base(context)
    {
    }
    
    // 所有 CRUD 方法都在这里实现
}

// 新代码
public class BookRepository : BookRepositoryBase, IBookRepository
{
    public BookRepository(LibraryDbContext context) : base(context)
    {
    }
    
    // 只需要重写需要自定义的方法
    // 或者添加新的方法
    
    public async Task<List<Book>> GetActiveBooksAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(b => b.IsAvailable)
            .ToListAsync(cancellationToken);
    }
}
```

### 第三步：修改 Service 实现

1. 删除旧的自动生成的 Service（如果有）
2. 创建新的 Service，继承基类：

```csharp
// 旧代码（通常是自动生成的）
public class BookCrudService : IBookCrudService
{
    private readonly IBookRepository _repository;
    private readonly IMapper _mapper;
    
    public BookCrudService(IBookRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }
    
    // 所有 CRUD 方法都在这里实现
}

// 新代码
public class BookAppService : BookCrudServiceBase, IBookAppService
{
    public BookAppService(
        IBookRepository repository,
        IMapper mapper)
        : base(repository, mapper)
    {
    }
    
    // 重写需要自定义的方法
    public override async Task<BookDto> CreateAsync(CreateBookDto input, CancellationToken cancellationToken = default)
    {
        // 添加自定义逻辑
        if (input.Price < 0)
            throw new ArgumentException("价格不能为负数");
            
        return await base.CreateAsync(input, cancellationToken);
    }
    
    // 或使用钩子方法
    protected override async Task OnCreatingAsync(Book entity, CancellationToken cancellationToken = default)
    {
        entity.IsAvailable = true;
        await base.OnCreatingAsync(entity, cancellationToken);
    }
}
```

### 第四步：更新 DTO 的使用

新的 DTO 现在是 partial 类，您可以轻松扩展：

```csharp
// 在您的代码中
namespace LibraryManagement.Dtos;

// 扩展自动生成的 DTO
public partial class CreateBookDto
{
    // 添加自定义验证
    [Range(0.01, double.MaxValue)]
    public decimal Price { get; set; }
}

public partial class BookDto
{
    // 添加计算属性
    public string PriceDisplay => $"¥{Price:F2}";
}
```

### 第五步：使用新的查询扩展方法

```csharp
// 旧方式（使用 QueryBuilder）
var filterBuilder = new BookFilterBuilder();
filterBuilder.WhereTitleContains("C#");
filterBuilder.WhereIsAvailable(true);

var request = new BookQueryRequest(filterBuilder);
var books = await BookQueryExecutor.Execute(dbContext.Books, request);

// 新方式（使用查询扩展）
using LibraryManagement.QueryExtensions;

var books = await dbContext.Books
    .WhereTitleContains("C#")
    .WhereIsAvailable(true)
    .OrderByPrice()
    .ToListAsync();
```

## 配置选项对比

### 旧特性配置

```csharp
[GenerateRepository(OrmProvider = OrmProvider.EfCore)]
[GenerateCrudService(
    GenerateDto = true,
    GenerateController = true,
    ServiceRoute = "api/books")]
[GenerateQueryBuilder]
```

### 新特性配置

```csharp
[GenerateEntity(
    // Repository 配置
    GenerateRepository = true,
    OrmProvider = OrmProvider.EfCore,
    
    // CRUD Service 配置
    GenerateCrudService = true,
    GenerateAsBaseClass = true,
    
    // 查询配置
    GenerateQueryExtensions = true,
    
    // Controller 配置
    GenerateController = true,
    ControllerRoute = "api/books",
    
    // DTO 配置
    ExcludeProperties = new[] { "InternalField" },
    
    // AOP 配置
    EnableTransaction = true,
    EnableLogging = true,
    EnableCaching = false)]
```

## 常见问题

### Q1: 旧的代码还能继续用吗？

A: 可以！我们保持了完全的向后兼容性。旧的特性仍然可以使用，只是不会生成基类模式。

### Q2: 可以逐步迁移吗？

A: 当然可以！您可以一个实体一个实体地迁移，旧的和新的可以共存。

### Q3: 基类中的方法可以重写吗？

A: 是的！基类中的所有方法都是 `virtual` 的，您可以按需重写。

### Q4: 钩子方法什么时候被调用？

A: 
- `OnCreatingAsync` - 在创建实体之前
- `OnCreatedAsync` - 在创建实体之后
- `OnUpdatingAsync` - 在更新实体之前
- `OnUpdatedAsync` - 在更新实体之后
- `OnDeletingAsync` - 在删除实体之前
- `OnDeletedAsync` - 在删除实体之后

### Q5: 如何添加自定义查询？

A: 有两种方式：
1. 在 Repository 中添加自定义方法
2. 使用查询扩展方法链式调用

## 迁移检查清单

- [ ] 实体特性已更新为 `[GenerateEntity]`
- [ ] Repository 已更新为继承基类
- [ ] Service 已更新为继承基类
- [ ] DTO 的自定义部分已使用 partial 类
- [ ] 查询代码已使用新的查询扩展方法
- [ ] 项目编译成功
- [ ] 所有测试通过
- [ ] 功能测试完成

## 回滚方案

如果迁移后遇到问题，可以回滚：

1. 恢复旧的特性
2. 恢复旧的 Repository 和 Service 实现
3. 移除新的基类继承

```csharp
// 回滚到旧方式
[GenerateRepository(OrmProvider = OrmProvider.EfCore)]
[GenerateCrudService(GenerateDto = true, GenerateController = true)]
[GenerateQueryBuilder]
public class Book : AuditedEntity<Guid>
{
    // ...
}
```

## 总结

新的基类模式提供了：
- ✅ 更好的代码可扩展性
- ✅ 清晰的业务逻辑分离
- ✅ 灵活的钩子方法
- ✅ 类型安全的查询扩展
- ✅ 完整的向后兼容性

建议先在非关键实体上试用，确认无误后再全面迁移！
