# 实体（Entities）

本文档介绍 CrestCreates 框架中的实体概念和实现。

## 概述

实体是具有唯一标识的领域对象，其标识在整个生命周期中保持不变。实体是领域驱动设计（DDD）的核心概念之一，代表业务领域中的重要概念。

## 核心概念

### 实体的特点

1. **唯一标识**：每个实体都有唯一的标识符（ID）
2. **生命周期**：实体有创建、修改、删除的生命周期
3. **可变性**：实体的属性可以变化，但标识不变
4. **业务逻辑**：实体包含业务逻辑和行为

### 实体 vs 值对象

| 特性 | 实体 | 值对象 |
|------|------|--------|
| 标识 | 有唯一标识 | 无标识 |
| 相等性 | 基于标识 | 基于属性值 |
| 可变性 | 可变 | 不可变 |
| 生命周期 | 有生命周期 | 无生命周期 |

## 实体基类

### Entity<TId>

`Entity<TId>` 是所有实体的基类，提供基本的实体功能。

```csharp
public abstract class Entity<TId> : IEntity<TId>
{
    public TId Id { get; protected set; }
    
    // 领域事件集合
    private List<DomainEvent> _domainEvents;
    public IReadOnlyCollection<DomainEvent> DomainEvents => _domainEvents?.AsReadOnly();
    
    // 添加领域事件
    public void AddDomainEvent(DomainEvent eventItem)
    {
        _domainEvents ??= new List<DomainEvent>();
        _domainEvents.Add(eventItem);
    }
    
    // 清除领域事件
    public void ClearDomainEvents()
    {
        _domainEvents?.Clear();
    }
    
    // 相等性判断
    public override bool Equals(object obj)
    {
        if (obj is not Entity<TId> other)
            return false;
            
        if (ReferenceEquals(this, other))
            return true;
            
        if (GetType() != other.GetType())
            return false;
            
        if (Id.Equals(default(TId)) || other.Id.Equals(default(TId)))
            return false;
            
        return Id.Equals(other.Id);
    }
    
    public override int GetHashCode()
    {
        return (GetType().ToString() + Id).GetHashCode();
    }
}
```

### AuditedEntity<TId>

`AuditedEntity<TId>` 是支持审计的实体基类，自动记录创建和修改信息。

```csharp
public abstract class AuditedEntity<TId> : Entity<TId>, IAuditedEntity
{
    public DateTime CreationTime { get; set; }
    public Guid? CreatorId { get; set; }
    public DateTime? LastModificationTime { get; set; }
    public Guid? LastModifierId { get; set; }
}
```

### AggregateRoot<TId>

`AggregateRoot<TId>` 是聚合根基类，作为聚合的入口点。

```csharp
public abstract class AggregateRoot<TId> : AuditedEntity<TId>, IAggregateRoot<TId>
{
    // 聚合根特定功能
}
```

## 创建实体

### 基本实体

```csharp
public class Product : Entity<Guid>
{
    public string Name { get; private set; }
    public decimal Price { get; private set; }
    public string Description { get; private set; }
    
    // 私有构造函数，用于 ORM
    private Product() { }
    
    public Product(string name, decimal price, string description)
    {
        Id = Guid.NewGuid();
        SetName(name);
        SetPrice(price);
        Description = description;
        
        AddDomainEvent(new ProductCreatedEvent(Id));
    }
    
    public void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Product name cannot be empty");
            
        if (name.Length > 200)
            throw new ArgumentException("Product name cannot exceed 200 characters");
            
        Name = name;
    }
    
    public void SetPrice(decimal price)
    {
        if (price < 0)
            throw new ArgumentException("Product price cannot be negative");
            
        if (Price != price)
        {
            Price = price;
            AddDomainEvent(new ProductPriceChangedEvent(Id, price));
        }
    }
}
```

### 审计实体

```csharp
public class Order : AuditedEntity<Guid>
{
    public string OrderNo { get; private set; }
    public OrderStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    
    private readonly List<OrderItem> _items = new();
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();
    
    private Order() { }
    
    public Order(string orderNo)
    {
        Id = Guid.NewGuid();
        OrderNo = orderNo ?? throw new ArgumentNullException(nameof(orderNo));
        Status = OrderStatus.Pending;
        
        AddDomainEvent(new OrderCreatedEvent(Id, OrderNo));
    }
    
    public void AddItem(Product product, int quantity)
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException("Cannot modify confirmed order");
            
        var existingItem = _items.FirstOrDefault(i => i.ProductId == product.Id);
        if (existingItem != null)
        {
            existingItem.IncreaseQuantity(quantity);
        }
        else
        {
            _items.Add(new OrderItem(product.Id, product.Price, quantity));
        }
        
        CalculateTotalAmount();
        AddDomainEvent(new OrderItemAddedEvent(Id, product.Id, quantity));
    }
    
    public void Confirm()
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException("Order is already confirmed");
            
        if (!_items.Any())
            throw new InvalidOperationException("Cannot confirm empty order");
            
        Status = OrderStatus.Confirmed;
        AddDomainEvent(new OrderConfirmedEvent(Id));
    }
    
    private void CalculateTotalAmount()
    {
        TotalAmount = _items.Sum(i => i.TotalPrice);
    }
}
```

### 聚合根

```csharp
public class Category : AggregateRoot<Guid>
{
    public string Name { get; private set; }
    public string Description { get; private set; }
    
    private readonly List<Product> _products = new();
    public IReadOnlyCollection<Product> Products => _products.AsReadOnly();
    
    private Category() { }
    
    public Category(string name, string description)
    {
        Id = Guid.NewGuid();
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description;
    }
    
    public void AddProduct(Product product)
    {
        if (_products.Any(p => p.Id == product.Id))
            throw new InvalidOperationException("Product already exists in this category");
            
        _products.Add(product);
        AddDomainEvent(new ProductAddedToCategoryEvent(Id, product.Id));
    }
    
    public void RemoveProduct(Guid productId)
    {
        var product = _products.FirstOrDefault(p => p.Id == productId);
        if (product == null)
            throw new InvalidOperationException("Product not found in this category");
            
        _products.Remove(product);
        AddDomainEvent(new ProductRemovedFromCategoryEvent(Id, productId));
    }
}
```

## 实体特性

### EntityAttribute

使用 `[Entity]` 特性标记实体类：

```csharp
[Entity("products")]
public class Product : AuditedEntity<Guid>
{
    // 实体实现
}
```

特性参数：
- `tableName`：数据库表名（可选）

## 最佳实践

### 1. 保护属性 setter

使用 `private set` 或 `protected set` 保护属性：

```csharp
public class Product : Entity<Guid>
{
    public string Name { get; private set; }
    public decimal Price { get; private set; }
}
```

### 2. 提供领域方法

通过领域方法修改实体状态：

```csharp
public void UpdatePrice(decimal newPrice)
{
    if (newPrice < 0)
        throw new ArgumentException("Price cannot be negative");
        
    if (Price != newPrice)
    {
        Price = newPrice;
        AddDomainEvent(new ProductPriceChangedEvent(Id, newPrice));
    }
}
```

### 3. 验证业务规则

在领域方法中验证业务规则：

```csharp
public void Confirm()
{
    if (Status != OrderStatus.Pending)
        throw new InvalidOperationException("Order is already confirmed");
        
    if (!_items.Any())
        throw new InvalidOperationException("Cannot confirm empty order");
        
    Status = OrderStatus.Confirmed;
}
```

### 4. 发布领域事件

在重要状态变更时发布领域事件：

```csharp
public Product(string name, decimal price)
{
    Id = Guid.NewGuid();
    Name = name;
    Price = price;
    
    AddDomainEvent(new ProductCreatedEvent(Id));
}
```

### 5. 使用私有构造函数

为 ORM 提供私有构造函数：

```csharp
private Product() { } // EF Core 需要

public Product(string name, decimal price)
{
    // 公共构造函数
}
```

### 6. 避免贫血领域模型

实体应包含业务逻辑，而不仅仅是数据容器：

```csharp
// 好的实践
public void AddItem(Product product, int quantity)
{
    ValidateProduct(product);
    ValidateQuantity(quantity);
    
    var item = new OrderItem(product.Id, product.Price, quantity);
    _items.Add(item);
    CalculateTotal();
}

// 避免：只有 getter/setter
public List<OrderItem> Items { get; set; }
```

## 相关文档

- [值对象](01-value-objects.md) - 值对象详解
- [领域事件](02-domain-events.md) - 领域事件详解
- [仓储](03-repositories.md) - 仓储详解
- [领域驱动设计](../01-architecture/01-domain-driven-design.md) - DDD 设计原则
