# 分层架构

本文档详细介绍 CrestCreates 框架的分层架构设计。

## 概述

分层架构是一种将系统划分为多个层次的架构模式，每个层次都有明确的职责和边界。CrestCreates 采用经典的分层架构，清晰分离关注点，提高代码可维护性和可测试性。

## 架构图

```
┌─────────────────────────────────────────┐
│              表示层（Web）               │
│  - 控制器（Controllers）                 │
│  - 中间件（Middlewares）                 │
│  - 视图模型（ViewModels）                │
└─────────────────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────┐
│              应用层（Application）        │
│  - 应用服务（Application Services）       │
│  - DTOs（Data Transfer Objects）         │
│  - 事件处理器（Event Handlers）           │
└─────────────────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────┐
│              领域层（Domain）             │
│  - 实体（Entities）                      │
│  - 值对象（Value Objects）               │
│  - 领域事件（Domain Events）              │
│  - 仓储接口（Repository Interfaces）      │
└─────────────────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────┐
│           基础设施层（Infrastructure）     │
│  - 仓储实现（Repository Implementations） │
│  - 外部服务（External Services）          │
│  - 数据访问（Data Access）                │
└─────────────────────────────────────────┘
```

## 依赖关系

```
Web Layer ────────┐
                  │
Application Layer─┼───► Domain Layer
                  │
Infrastructure────┘
```

**关键原则**：
- 上层依赖下层
- 下层不依赖上层
- 领域层不依赖任何其他层
- 依赖通过接口抽象

## 各层职责

### 1. 表示层（Web Layer）

**职责**：
- 处理 HTTP 请求和响应
- 接收用户输入
- 返回视图或数据
- 处理 Web 特定逻辑

**组件**：
- **控制器（Controllers）**：处理 HTTP 请求，调用应用服务
- **中间件（Middlewares）**：处理请求管道，如认证、日志等
- **视图模型（ViewModels）**：定义视图所需的数据结构

**示例**：

```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;
    
    public ProductsController(IProductService productService)
    {
        _productService = productService;
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<ProductDto>> Get(Guid id)
    {
        var product = await _productService.GetAsync(id);
        return Ok(product);
    }
    
    [HttpPost]
    public async Task<ActionResult<ProductDto>> Create(CreateProductInput input)
    {
        var product = await _productService.CreateAsync(input);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, product);
    }
}
```

### 2. 应用层（Application Layer）

**职责**：
- 协调领域对象完成用例
- 编排业务流程
- 处理事务边界
- DTO 映射

**组件**：
- **应用服务（Application Services）**：实现用例，协调领域对象
- **DTOs（Data Transfer Objects）**：定义数据传输对象
- **事件处理器（Event Handlers）**：处理领域事件

**示例**：

```csharp
public class ProductService : ApplicationService, IProductService
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;
    
    public ProductService(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }
    
    public async Task<ProductDto> GetAsync(Guid id)
    {
        var product = await _productRepository.GetAsync(id);
        return ObjectMapper.Map<Product, ProductDto>(product);
    }
    
    public async Task<ProductDto> CreateAsync(CreateProductInput input)
    {
        var product = new Product(input.Name, input.Price, input.Description);
        await _productRepository.InsertAsync(product);
        await _unitOfWork.SaveChangesAsync();
        
        return ObjectMapper.Map<Product, ProductDto>(product);
    }
}
```

### 3. 领域层（Domain Layer）

**职责**：
- 实现业务逻辑
- 定义领域模型
- 定义业务规则
- 定义领域事件

**组件**：
- **实体（Entities）**：具有唯一标识的领域对象
- **值对象（Value Objects）**：通过属性值判断相等性的对象
- **领域事件（Domain Events）**：表示领域内发生的重要事情
- **仓储接口（Repository Interfaces）**：数据访问抽象

**示例**：

```csharp
public class Product : AuditedEntity<Guid>
{
    public string Name { get; private set; }
    public decimal Price { get; private set; }
    public string Description { get; private set; }
    
    private Product() { } // EF Core 需要
    
    public Product(string name, decimal price, string description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty");
        if (price < 0)
            throw new ArgumentException("Price cannot be negative");
            
        Name = name;
        Price = price;
        Description = description;
    }
    
    public void UpdatePrice(decimal newPrice)
    {
        if (newPrice < 0)
            throw new ArgumentException("Price cannot be negative");
            
        Price = newPrice;
        AddDomainEvent(new ProductPriceChangedEvent(Id, newPrice));
    }
}

public class ProductPriceChangedEvent : DomainEvent
{
    public Guid ProductId { get; }
    public decimal NewPrice { get; }
    
    public ProductPriceChangedEvent(Guid productId, decimal newPrice)
    {
        ProductId = productId;
        NewPrice = newPrice;
    }
}
```

### 4. 基础设施层（Infrastructure Layer）

**职责**：
- 实现领域层定义的接口
- 提供技术实现
- 处理外部依赖
- 数据访问

**组件**：
- **仓储实现（Repository Implementations）**：实现仓储接口
- **外部服务（External Services）**：调用外部服务
- **数据访问（Data Access）**：数据库访问实现

**示例**：

```csharp
public class ProductRepository : EfCoreRepository<Product, Guid>, IProductRepository
{
    public ProductRepository(IUnitOfWork unitOfWork) : base(unitOfWork)
    {
    }
    
    public async Task<Product> FindByNameAsync(string name)
    {
        return await DbSet.FirstOrDefaultAsync(p => p.Name == name);
    }
}
```

## 跨层关注点

### 依赖注入

所有层都通过依赖注入获取依赖：

```csharp
// 在 Startup.cs 或 Program.cs 中
services.AddScoped<IProductService, ProductService>();
services.AddScoped<IProductRepository, ProductRepository>();
```

### 事务管理

事务在应用层管理：

```csharp
public async Task<OrderDto> CreateOrderAsync(CreateOrderInput input)
{
    using var transaction = await _unitOfWork.BeginTransactionAsync();
    
    try
    {
        // 执行业务逻辑
        await _unitOfWork.SaveChangesAsync();
        await transaction.CommitAsync();
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}
```

### 日志记录

日志记录跨所有层：

```csharp
public class ProductService : ApplicationService
{
    private readonly ILogger<ProductService> _logger;
    
    public async Task<ProductDto> CreateAsync(CreateProductInput input)
    {
        _logger.LogInformation("Creating product: {ProductName}", input.Name);
        
        // 业务逻辑
        
        _logger.LogInformation("Product created: {ProductId}", product.Id);
    }
}
```

## 最佳实践

### 1. 保持领域层纯净

领域层不应依赖任何外部库，只包含纯业务逻辑。

### 2. 应用层不包含业务逻辑

应用层只负责协调领域对象，不包含业务规则。

### 3. 表示层保持简单

表示层只处理 HTTP 特定逻辑，不包含业务逻辑。

### 4. 通过接口抽象依赖

层与层之间通过接口通信，不直接依赖具体实现。

### 5. 合理使用 DTO

使用 DTO 在不同层之间传输数据，避免直接暴露领域对象。

### 6. 事务在应用层管理

事务边界应在应用层定义，领域层不管理事务。

## 相关文档

- [领域驱动设计](01-domain-driven-design.md) - DDD 设计原则
- [模块化设计](03-modularity.md) - 模块化开发指南
