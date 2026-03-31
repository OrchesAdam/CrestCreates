# 领域驱动设计（DDD）

本文档介绍 CrestCreates 框架中领域驱动设计的实现。

## 什么是领域驱动设计？

领域驱动设计（Domain-Driven Design，DDD）是一种软件开发方法，强调以领域为核心，通过建立统一的领域模型来解决复杂业务问题。

## 核心概念

### 实体（Entity）

实体是具有唯一标识的对象，其标识在整个生命周期中保持不变。

**特点**：
- 有唯一标识（ID）
- 标识不变，属性可以变化
- 通过 ID 判断相等性

**示例**：

```csharp
public class Product : AuditedEntity<Guid>
{
    public string Name { get; set; }
    public decimal Price { get; set; }
    
    public void UpdatePrice(decimal newPrice)
    {
        if (newPrice < 0)
            throw new ArgumentException("Price cannot be negative");
            
        Price = newPrice;
        AddDomainEvent(new ProductPriceChangedEvent(Id, newPrice));
    }
}
```

### 聚合根（Aggregate Root）

聚合根是一组相关对象的入口，负责维护聚合内的一致性。

**特点**：
- 聚合的入口点
- 负责聚合内对象的生命周期
- 通过聚合根引用聚合内的对象

**示例**：

```csharp
public class Order : AggregateRoot<Guid>
{
    private readonly List<OrderItem> _items = new();
    
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();
    public decimal TotalAmount => _items.Sum(i => i.TotalPrice);
    
    public void AddItem(Product product, int quantity)
    {
        var existingItem = _items.FirstOrDefault(i => i.ProductId == product.Id);
        if (existingItem != null)
        {
            existingItem.IncreaseQuantity(quantity);
        }
        else
        {
            _items.Add(new OrderItem(product.Id, product.Price, quantity));
        }
        
        AddDomainEvent(new OrderItemAddedEvent(Id, product.Id, quantity));
    }
}
```

### 值对象（Value Object）

值对象是没有唯一标识的对象，通过属性值判断相等性。

**特点**：
- 没有唯一标识
- 不可变
- 通过属性值判断相等性

**示例**：

```csharp
public class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }
    
    public Money(decimal amount, string currency)
    {
        if (amount < 0)
            throw new ArgumentException("Amount cannot be negative");
            
        Amount = amount;
        Currency = currency ?? throw new ArgumentNullException(nameof(currency));
    }
    
    public Money Add(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException("Cannot add money with different currencies");
            
        return new Money(Amount + other.Amount, Currency);
    }
    
    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }
}
```

### 领域事件（Domain Event）

领域事件表示领域内发生的重要事情，用于实现松耦合。

**特点**：
- 表示领域内发生的重要事情
- 用于实现松耦合
- 可以持久化和重放

**示例**：

```csharp
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

public class ProductPriceChangedEventHandler : IDomainEventHandler<ProductPriceChangedEvent>
{
    public async Task HandleAsync(ProductPriceChangedEvent eventData)
    {
        // 处理价格变更事件
        // 例如：发送通知、更新缓存等
    }
}
```

### 仓储（Repository）

仓储封装数据访问逻辑，提供领域对象的持久化。

**特点**：
- 封装数据访问逻辑
- 提供领域对象的持久化
- 实现领域层定义的接口

**示例**：

```csharp
// 领域层定义接口
public interface IProductRepository : IRepository<Product, Guid>
{
    Task<Product> FindByNameAsync(string name);
    Task<List<Product>> GetProductsByPriceRangeAsync(decimal minPrice, decimal maxPrice);
}

// 基础设施层实现
public class ProductRepository : EfCoreRepository<Product, Guid>, IProductRepository
{
    public ProductRepository(IUnitOfWork unitOfWork) : base(unitOfWork)
    {
    }
    
    public async Task<Product> FindByNameAsync(string name)
    {
        return await DbSet.FirstOrDefaultAsync(p => p.Name == name);
    }
    
    public async Task<List<Product>> GetProductsByPriceRangeAsync(decimal minPrice, decimal maxPrice)
    {
        return await DbSet
            .Where(p => p.Price >= minPrice && p.Price <= maxPrice)
            .ToListAsync();
    }
}
```

### 工作单元（Unit of Work）

工作单元管理事务和数据一致性。

**特点**：
- 管理事务
- 确保数据一致性
- 批量提交变更

**示例**：

```csharp
public class OrderService : ApplicationService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;
    
    public async Task<OrderDto> CreateOrderAsync(CreateOrderInput input)
    {
        var order = new Order();
        
        foreach (var item in input.Items)
        {
            var product = await _productRepository.GetAsync(item.ProductId);
            order.AddItem(product, item.Quantity);
        }
        
        await _orderRepository.InsertAsync(order);
        await _unitOfWork.SaveChangesAsync();
        
        return ObjectMapper.Map<Order, OrderDto>(order);
    }
}
```

## 分层架构

### 领域层（Domain Layer）

**职责**：
- 定义领域模型（实体、值对象、领域事件）
- 定义业务规则
- 定义仓储接口
- 不依赖任何外部库

### 应用层（Application Layer）

**职责**：
- 协调领域对象完成用例
- 实现应用服务
- 处理 DTO 映射
- 不包含业务逻辑

### 基础设施层（Infrastructure Layer）

**职责**：
- 实现领域层定义的接口
- 提供技术实现（数据访问、消息队列等）
- 处理外部依赖

## 最佳实践

### 1. 保持领域层纯净

领域层不应依赖任何外部库，只包含纯业务逻辑。

### 2. 使用值对象

对于不可变的领域概念，使用值对象而不是实体。

### 3. 发布领域事件

对于重要的领域变化，发布领域事件实现松耦合。

### 4. 使用聚合根

通过聚合根管理领域对象的一致性，避免直接操作聚合内的对象。

### 5. 避免贫血领域模型

领域对象应包含业务逻辑，而不仅仅是数据容器。

### 6. 合理使用仓储

通过仓储接口访问数据，避免直接访问数据库。

### 7. 使用工作单元管理事务

通过工作单元管理事务，确保数据一致性。

## 相关文档

- [实体](../02-core-concepts/00-entities.md) - 实体详解
- [值对象](../02-core-concepts/01-value-objects.md) - 值对象详解
- [领域事件](../02-core-concepts/02-domain-events.md) - 领域事件详解
- [仓储](../02-core-concepts/03-repositories.md) - 仓储详解
- [工作单元](../02-core-concepts/04-unit-of-work.md) - 工作单元详解
