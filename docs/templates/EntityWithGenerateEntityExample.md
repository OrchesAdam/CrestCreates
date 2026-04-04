# 使用 GenerateEntity 特性的示例

## 基本使用示例

### 实体定义

```csharp
using System;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Shared.Attributes;

namespace YourNamespace.Domain.Entities;

[GenerateEntity(
    GenerateRepository = true,
    GenerateCrudService = true,
    GenerateAsBaseClass = true,
    OrmProvider = OrmProvider.EfCore,
    GenerateQueryExtensions = true,
    GenerateController = true,
    ControllerRoute = "api/products",
    ExcludeProperties = new[] { "InternalField", "SecretData" })]
public class Product : AuditedEntity<Guid>
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? Description { get; set; }
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; }
    
    // 这个属性会被 ExcludeProperties 排除
    public string? InternalField { get; set; }
    
    // 这个属性也会被排除
    public string? SecretData { get; set; }
}
```

### Repository 实现

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using YourNamespace.Domain.Entities;
using YourNamespace.Domain.Repositories;
using YourNamespace.EntityFrameworkCore;

namespace YourNamespace.EntityFrameworkCore.Repositories;

public class ProductRepository : ProductRepositoryBase, IProductRepository
{
    public ProductRepository(YourDbContext dbContext) : base(dbContext)
    {
    }

    // 自定义查询方法
    public async Task<List<Product>> GetActiveProductsAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.CreationTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Product>> GetProductsByPriceRangeAsync(
        decimal minPrice, 
        decimal maxPrice, 
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(p => p.Price >= minPrice && p.Price <= maxPrice)
            .OrderBy(p => p.Price)
            .ToListAsync(cancellationToken);
    }
}
```

### Service 实现

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using YourNamespace.Application.Contracts.DTOs;
using YourNamespace.Application.Contracts.Interfaces;
using YourNamespace.Domain.Entities;
using YourNamespace.Domain.Repositories;

namespace YourNamespace.Application.Services;

public class ProductAppService : ProductCrudServiceBase, IProductAppService
{
    public ProductAppService(
        IProductRepository repository,
        IMapper mapper)
        : base(repository, mapper)
    {
    }

    // 重写创建方法，添加业务逻辑
    public override async Task<ProductDto> CreateAsync(CreateProductDto input, CancellationToken cancellationToken = default)
    {
        // 验证价格不能为负数
        if (input.Price < 0)
        {
            throw new ArgumentException("价格不能为负数", nameof(input.Price));
        }

        // 设置默认库存
        if (input.StockQuantity < 0)
        {
            input.StockQuantity = 0;
        }

        return await base.CreateAsync(input, cancellationToken);
    }

    // 重写钩子方法 - 创建前
    protected override async Task OnCreatingAsync(Product entity, CancellationToken cancellationToken = default)
    {
        // 在创建前设置默认值
        entity.IsActive = true;
        
        await base.OnCreatingAsync(entity, cancellationToken);
    }

    // 重写钩子方法 - 更新前
    protected override async Task OnUpdatingAsync(Product entity, UpdateProductDto input, CancellationToken cancellationToken = default)
    {
        // 在更新前验证
        if (input.Price < 0)
        {
            throw new ArgumentException("价格不能为负数", nameof(input.Price));
        }
        
        await base.OnUpdatingAsync(entity, input, cancellationToken);
    }

    // 自定义服务方法
    public async Task<List<ProductDto>> GetActiveProductsAsync(CancellationToken cancellationToken = default)
    {
        var products = await _repository.GetActiveProductsAsync(cancellationToken);
        return _mapper.Map<List<ProductDto>>(products);
    }
}
```

### 扩展 DTO（partial 类）

```csharp
using System.ComponentModel.DataAnnotations;

namespace YourNamespace.Dtos;

// 使用 partial 类扩展自动生成的 DTO
public partial class CreateProductDto
{
    // 添加额外的验证
    [Range(0.01, double.MaxValue, ErrorMessage = "价格必须大于 0")]
    public decimal Price { get; set; }
}

public partial class ProductDto
{
    // 添加计算属性
    public string PriceDisplay => $"¥{Price:F2}";
}
```

## 完整的项目结构示例

```
YourProject/
├── src/
│   ├── YourProject.Domain/
│   │   └── Entities/
│   │       └── Product.cs              // 使用 [GenerateEntity] 特性
│   ├── YourProject.Domain.Shared/
│   │   └── Attributes/
│   │       └── GenerateEntityAttribute.cs
│   ├── YourProject.Application.Contracts/
│   │   ├── Dtos/
│   │   │   ├── ProductDto.cs           // partial 类扩展
│   │   │   ├── CreateProductDto.cs     // partial 类扩展
│   │   │   └── UpdateProductDto.cs     // partial 类扩展
│   │   └── Interfaces/
│   │       └── IProductAppService.cs
│   ├── YourProject.Application/
│   │   └── Services/
│   │       └── ProductAppService.cs     // 继承 ProductCrudServiceBase
│   ├── YourProject.EntityFrameworkCore/
│   │   └── Repositories/
│   │       └── ProductRepository.cs     // 继承 ProductRepositoryBase
│   └── YourProject.HttpApi/
│       └── Controllers/
│           └── ProductController.cs
└── tools/
    └── YourProject.CodeGenerator/
        └── (Source Generators)
```

## GenerateEntity 特性配置选项

```csharp
[GenerateEntity(
    // 基础配置
    GenerateRepository = true,              // 是否生成 Repository
    GenerateCrudService = true,            // 是否生成 CRUD Service
    GenerateAsBaseClass = true,            // 是否生成基类（abstract）
    OrmProvider = OrmProvider.EfCore,      // ORM 提供者
    
    // 查询相关
    GenerateQueryExtensions = true,         // 是否生成查询扩展
    GenerateQueryBuilder = true,           // 是否生成查询构建器
    
    // Controller 相关
    GenerateController = true,              // 是否生成 Controller
    ControllerRoute = "api/products",       // Controller 路由
    
    // AOP 相关
    EnableTransaction = true,               // 是否启用事务
    EnableLogging = true,                   // 是否启用日志
    EnableCaching = false,                  // 是否启用缓存
    
    // DTO 相关
    ExcludeProperties = new[] { "InternalField" }  // 排除的属性
)]
```
