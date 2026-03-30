# CrestCreates EntityGenerator

## 概述

CrestCreates EntityGenerator 是一个基于 Roslyn Source Generator 的代码生成器，用于为领域实体自动生成仓储、查询扩展、ORM映射等基础设施代码。

## 主要特性

### 1. 自动代码生成
- **仓储接口和实现**：为每个实体生成 IRepository 接口和对应的 ORM 实现
- **查询扩展方法**：生成分页、条件查询、属性查询等扩展方法
- **ORM映射配置**：支持 EF Core、SqlSugar、FreeSql 的映射配置
- **DTO映射配置**：生成 AutoMapper 配置文件
- **验证规则**：生成 FluentValidation 验证器

### 2. 多ORM支持
- **EF Core**：生成 IEntityTypeConfiguration 映射类
- **SqlSugar**：生成 SqlSugar 映射配置
- **FreeSql**：生成 FreeSql 映射配置

### 3. 审计功能
- **创建审计**：自动处理 CreationTime、CreatorId
- **修改审计**：自动处理 LastModificationTime、LastModifierId
- **软删除**：支持 IsDeleted、DeletionTime、DeleterId

## 使用方式

### 1. 实体定义

```csharp
[Entity(
    GenerateRepository = true,
    GenerateAuditing = true,
    OrmProvider = "EfCore",
    TableName = "Products"
)]
public class Product : FullyAuditedAggregateRoot<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public int StockQuantity { get; private set; }
    public bool IsActive { get; private set; }
    
    // 构造函数和业务方法...
}
```

### 2. 生成的代码结构

```
Generated/
├── Repositories/
│   ├── IProductRepository.g.cs           # 仓储接口
│   └── EfCore/
│       └── EfCoreProductRepository.g.cs  # EF Core 仓储实现
├── Extensions/
│   ├── ProductQueryExtensions.g.cs       # 查询扩展
│   └── ProductExtensions.g.cs            # 实体扩展
├── Mappings/
│   ├── EfCore/
│   │   └── ProductMapping.g.cs           # EF Core 映射
│   └── AutoMapper/
│       └── ProductMappingProfile.g.cs    # DTO 映射
└── Validators/
    └── ProductValidator.g.cs              # 验证规则
```

## 生成的代码示例

### 仓储接口

```csharp
public partial interface IProductRepository : IRepository<Product, Guid>
{
    // 基于属性的查询方法
    Task<List<Product>> FindByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<List<Product>> FindByCategoryAsync(string category, CancellationToken cancellationToken = default);
    
    // 分页查询方法
    Task<(List<Product> Items, int TotalCount)> GetPagedListAsync(
        int pageNumber, 
        int pageSize, 
        Expression<Func<Product, bool>>? predicate = null,
        Expression<Func<Product, object>>? orderBy = null,
        bool ascending = true,
        CancellationToken cancellationToken = default);

    // 软删除方法
    Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task RestoreAsync(Guid id, CancellationToken cancellationToken = default);
}
```

### 查询扩展

```csharp
public static class ProductQueryExtensions
{
    public static IQueryable<Product> PageBy(this IQueryable<Product> query, int pageNumber, int pageSize)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;
        return query.Skip((pageNumber - 1) * pageSize).Take(pageSize);
    }
    
    public static IQueryable<Product> WhereIf(this IQueryable<Product> query, bool condition, Expression<Func<Product, bool>> predicate)
    {
        return condition ? query.Where(predicate) : query;
    }

    // 审计查询
    public static IQueryable<Product> OrderByCreationTime(this IQueryable<Product> query, bool descending = false)
    {
        return descending ? query.OrderByDescending(x => x.CreationTime) : query.OrderBy(x => x.CreationTime);
    }

    // 软删除过滤
    public static IQueryable<Product> NotDeleted(this IQueryable<Product> query)
    {
        return query.Where(x => !x.IsDeleted);
    }

    // 属性查询
    public static IQueryable<Product> ByName(this IQueryable<Product> query, string name)
    {
        return query.Where(x => x.Name == name);
    }
    
    public static IQueryable<Product> ByNameContains(this IQueryable<Product> query, string name)
    {
        return query.Where(x => x.Name.Contains(name));
    }
}
```

### EF Core 映射

```csharp
public class ProductMapping : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        
        builder.HasKey(e => e.Id);
        
        // 属性映射
        builder.Property(e => e.Name)
            .HasMaxLength(100)
            .IsRequired();
            
        builder.Property(e => e.Description)
            .HasMaxLength(500)
            .IsRequired(false);
        
        // 审计字段映射
        builder.Property(e => e.CreationTime)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");
            
        // 索引配置
        builder.HasIndex(e => e.Name).HasDatabaseName("IX_Product_Name");
        builder.HasIndex(e => e.Category).HasDatabaseName("IX_Product_Category");
    }
}
```

## 配置选项

### EntityAttribute 参数

- **GenerateRepository**：是否生成仓储接口和实现（默认：true）
- **GenerateAuditing**：是否生成审计字段处理（默认：true）  
- **OrmProvider**：ORM提供者（EfCore/SqlSugar/FreeSql，默认：EfCore）
- **TableName**：自定义表名（默认：实体名+s）

## 最佳实践

### 1. 实体设计
- 继承适当的基类（Entity、AggregateRoot、AuditedEntity 等）
- 使用 private set 保护属性
- 提供无参构造函数供 ORM 使用
- 实现业务方法而非公开属性设置

### 2. 仓储使用
```csharp
public class ProductService
{
    private readonly IProductRepository _productRepository;
    
    public async Task<Product> GetActiveProductAsync(string name)
    {
        var products = await _productRepository.FindByNameAsync(name);
        return products.FirstOrDefault(p => p.IsActive);
    }
    
    public async Task<(List<Product>, int)> GetPagedProductsAsync(int page, int size)
    {
        return await _productRepository.GetPagedListAsync(
            page, 
            size, 
            p => p.IsActive,
            p => p.CreationTime,
            false);
    }
}
```

### 3. 查询优化
```csharp
var activeProducts = context.Products
    .NotDeleted()
    .WhereIf(!string.IsNullOrEmpty(searchName), p => p.Name.Contains(searchName))
    .WhereIf(categoryFilter != null, p => p.Category == categoryFilter)
    .OrderByCreationTime(descending: true)
    .PageBy(pageNumber, pageSize);
```

## 扩展点

### 1. Partial 类扩展
生成的接口和类都是 partial 的，可以通过 partial 类添加自定义方法：

```csharp
public partial interface IProductRepository
{
    Task<List<Product>> GetLowStockProductsAsync(int threshold);
}

public partial class EfCoreProductRepository
{
    public async Task<List<Product>> GetLowStockProductsAsync(int threshold)
    {
        return await DbContext.Set<Product>()
            .Where(p => p.StockQuantity < threshold)
            .ToListAsync();
    }
}
```

### 2. 模板自定义
可以通过修改 Templates 目录下的模板文件来自定义生成的代码结构。

## 性能考虑

- **编译时生成**：所有代码在编译时生成，无运行时性能开销
- **无反射**：生成的映射配置避免运行时反射
- **静态分析**：利用 Roslyn 的增量生成能力，只在实体改变时重新生成
- **AOT 友好**：生成的代码完全支持 .NET Native AOT 编译

## 故障排除

### 常见问题

1. **生成器未执行**
   - 确保实体类标记了 `[Entity]` 特性
   - 检查 Generator 项目是否正确引用

2. **编译错误**
   - 检查实体是否继承了正确的基类
   - 确保命名空间引用正确

3. **功能缺失**
   - 检查 EntityAttribute 的配置参数
   - 确认实体实现了相应的审计接口

### 调试技巧

- 查看生成的文件：在 `obj/Debug/net10.0/generated/` 目录下
- 开启详细构建日志：`dotnet build -v detailed`
- 使用条件编译符号进行调试：`#if DEBUG`