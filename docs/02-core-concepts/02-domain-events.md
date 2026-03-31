# 领域事件（Domain Events）

本文档介绍 CrestCreates 框架中的领域事件概念和实现。

## 概述

领域事件表示领域内发生的重要事情，用于实现领域对象之间的松耦合通信。领域事件是领域驱动设计（DDD）的核心概念之一。

## 核心概念

### 什么是领域事件？

领域事件是对领域内发生的重要事情的记录，它捕获了领域专家关心的业务事件。

**示例**：
- 订单已创建
- 订单已确认
- 产品价格已变更
- 库存已更新

### 领域事件的特点

1. **不可变性**：领域事件一旦创建就不能修改
2. **时态性**：领域事件表示过去发生的事情
3. **业务意义**：领域事件具有业务意义
4. **松耦合**：通过事件实现领域对象之间的松耦合

### 领域事件 vs 集成事件

| 特性 | 领域事件 | 集成事件 |
|------|----------|----------|
| 范围 | 单个领域 | 跨领域/跨服务 |
| 同步性 | 同步处理 | 异步处理 |
| 事务 | 在同一事务中 | 独立事务 |
| 用途 | 领域内通信 | 服务间通信 |

## 领域事件基类

### DomainEvent

`DomainEvent` 是所有领域事件的基类。

```csharp
public abstract class DomainEvent
{
    /// <summary>
    /// 事件 ID
    /// </summary>
    public Guid EventId { get; }
    
    /// <summary>
    /// 事件发生时间
    /// </summary>
    public DateTime OccurredOn { get; }
    
    protected DomainEvent()
    {
        EventId = Guid.NewGuid();
        OccurredOn = DateTime.UtcNow;
    }
}
```

## 创建领域事件

### 基本领域事件

```csharp
public class ProductCreatedEvent : DomainEvent
{
    public Guid ProductId { get; }
    public string ProductName { get; }
    public decimal Price { get; }
    
    public ProductCreatedEvent(Guid productId, string productName, decimal price)
    {
        ProductId = productId;
        ProductName = productName;
        Price = price;
    }
}
```

### 复杂领域事件

```csharp
public class OrderConfirmedEvent : DomainEvent
{
    public Guid OrderId { get; }
    public string OrderNo { get; }
    public decimal TotalAmount { get; }
    public IReadOnlyList<OrderItemInfo> Items { get; }
    
    public OrderConfirmedEvent(
        Guid orderId, 
        string orderNo, 
        decimal totalAmount, 
        IEnumerable<OrderItemInfo> items)
    {
        OrderId = orderId;
        OrderNo = orderNo;
        TotalAmount = totalAmount;
        Items = items.ToList().AsReadOnly();
    }
}

public class OrderItemInfo
{
    public Guid ProductId { get; }
    public string ProductName { get; }
    public int Quantity { get; }
    public decimal UnitPrice { get; }
    
    public OrderItemInfo(
        Guid productId, 
        string productName, 
        int quantity, 
        decimal unitPrice)
    {
        ProductId = productId;
        ProductName = productName;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }
}
```

## 发布领域事件

### 在实体中发布事件

```csharp
public class Product : AuditedEntity<Guid>
{
    public string Name { get; private set; }
    public decimal Price { get; private set; }
    
    private Product() { }
    
    public Product(string name, decimal price)
    {
        Id = Guid.NewGuid();
        Name = name;
        Price = price;
        
        // 发布领域事件
        AddDomainEvent(new ProductCreatedEvent(Id, name, price));
    }
    
    public void UpdatePrice(decimal newPrice)
    {
        if (newPrice < 0)
            throw new ArgumentException("Price cannot be negative");
            
        if (Price != newPrice)
        {
            var oldPrice = Price;
            Price = newPrice;
            
            // 发布领域事件
            AddDomainEvent(new ProductPriceChangedEvent(Id, oldPrice, newPrice));
        }
    }
}
```

### 在应用服务中发布事件

```csharp
public class OrderService : ApplicationService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IEventBus _eventBus;
    
    public async Task<OrderDto> CreateOrderAsync(CreateOrderInput input)
    {
        var order = new Order(input.CustomerId);
        
        foreach (var item in input.Items)
        {
            var product = await _productRepository.GetAsync(item.ProductId);
            order.AddItem(product, item.Quantity);
        }
        
        await _orderRepository.InsertAsync(order);
        
        // 发布领域事件
        await _eventBus.PublishAsync(new OrderCreatedEvent(
            order.Id, 
            order.OrderNo, 
            order.TotalAmount));
        
        return MapToDto(order);
    }
}
```

## 处理领域事件

### 事件处理器接口

```csharp
public interface IDomainEventHandler<in TEvent> where TEvent : DomainEvent
{
    Task HandleAsync(TEvent eventData);
}
```

### 实现事件处理器

```csharp
public class ProductCreatedEventHandler : IDomainEventHandler<ProductCreatedEvent>
{
    private readonly ILogger<ProductCreatedEventHandler> _logger;
    private readonly ICacheService _cacheService;
    
    public ProductCreatedEventHandler(
        ILogger<ProductCreatedEventHandler> logger,
        ICacheService cacheService)
    {
        _logger = logger;
        _cacheService = cacheService;
    }
    
    public async Task HandleAsync(ProductCreatedEvent eventData)
    {
        _logger.LogInformation(
            "Product created: {ProductId} - {ProductName}", 
            eventData.ProductId, 
            eventData.ProductName);
        
        // 更新缓存
        await _cacheService.RemoveAsync("products:all");
        
        // 发送通知
        await SendNotificationAsync(eventData);
    }
    
    private async Task SendNotificationAsync(ProductCreatedEvent eventData)
    {
        // 发送通知逻辑
    }
}
```

### 多个处理器处理同一事件

```csharp
// 处理器 1：更新缓存
public class ProductCreatedCacheHandler : IDomainEventHandler<ProductCreatedEvent>
{
    public async Task HandleAsync(ProductCreatedEvent eventData)
    {
        await _cacheService.RemoveAsync("products:all");
    }
}

// 处理器 2：发送通知
public class ProductCreatedNotificationHandler : IDomainEventHandler<ProductCreatedEvent>
{
    public async Task HandleAsync(ProductCreatedEvent eventData)
    {
        await _notificationService.SendAsync(
            $"New product created: {eventData.ProductName}");
    }
}

// 处理器 3：记录日志
public class ProductCreatedLogHandler : IDomainEventHandler<ProductCreatedEvent>
{
    public async Task HandleAsync(ProductCreatedEvent eventData)
    {
        _logger.LogInformation(
            "Product {ProductId} created at {OccurredOn}",
            eventData.ProductId,
            eventData.OccurredOn);
    }
}
```

## 事件总线

### 本地事件总线

本地事件总线在同一进程中同步处理事件。

```csharp
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent eventData) where TEvent : DomainEvent;
}

// 使用
await _eventBus.PublishAsync(new ProductCreatedEvent(id, name, price));
```

### 分布式事件总线

分布式事件总线用于跨服务通信。

```csharp
public interface IDistributedEventBus
{
    Task PublishAsync<TEvent>(TEvent eventData) where TEvent : DomainEvent;
}

// 使用
await _distributedEventBus.PublishAsync(new OrderConfirmedEvent(...));
```

## 事件存储

### 持久化领域事件

```csharp
public class EventStore : IEventStore
{
    private readonly DbContext _dbContext;
    
    public async Task StoreAsync(DomainEvent domainEvent)
    {
        var eventRecord = new EventRecord
        {
            EventId = domainEvent.EventId,
            EventType = domainEvent.GetType().FullName,
            EventData = JsonSerializer.Serialize(domainEvent),
            OccurredOn = domainEvent.OccurredOn
        };
        
        _dbContext.Set<EventRecord>().Add(eventRecord);
        await _dbContext.SaveChangesAsync();
    }
    
    public async Task<IReadOnlyList<DomainEvent>> GetEventsAsync(
        Guid aggregateId, 
        DateTime? from = null, 
        DateTime? to = null)
    {
        var query = _dbContext.Set<EventRecord>()
            .Where(e => e.AggregateId == aggregateId);
            
        if (from.HasValue)
            query = query.Where(e => e.OccurredOn >= from.Value);
            
        if (to.HasValue)
            query = query.Where(e => e.OccurredOn <= to.Value);
            
        var records = await query.OrderBy(e => e.OccurredOn).ToListAsync();
        
        return records.Select(r => DeserializeEvent(r)).ToList();
    }
}
```

## 最佳实践

### 1. 事件命名

使用过去时态命名事件：

```csharp
// 好的实践
public class OrderConfirmedEvent : DomainEvent { }
public class ProductPriceChangedEvent : DomainEvent { }

// 避免
public class ConfirmOrderEvent : DomainEvent { }
public class ChangeProductPriceEvent : DomainEvent { }
```

### 2. 事件内容

包含足够的信息，避免处理器需要查询数据库：

```csharp
// 好的实践
public class OrderConfirmedEvent : DomainEvent
{
    public Guid OrderId { get; }
    public string OrderNo { get; }
    public decimal TotalAmount { get; }
    public IReadOnlyList<OrderItemInfo> Items { get; }
}

// 避免：信息不足
public class OrderConfirmedEvent : DomainEvent
{
    public Guid OrderId { get; } // 处理器需要查询数据库获取其他信息
}
```

### 3. 事件处理器幂等性

确保事件处理器可以安全地重复执行：

```csharp
public class OrderConfirmedEventHandler : IDomainEventHandler<OrderConfirmedEvent>
{
    public async Task HandleAsync(OrderConfirmedEvent eventData)
    {
        // 检查是否已处理
        if (await _processedEventRepository.ExistsAsync(eventData.EventId))
            return;
            
        // 处理事件
        await ProcessOrderAsync(eventData);
        
        // 记录已处理
        await _processedEventRepository.AddAsync(eventData.EventId);
    }
}
```

### 4. 避免在事件处理器中抛出异常

事件处理器应该处理异常，避免影响主流程：

```csharp
public async Task HandleAsync(ProductCreatedEvent eventData)
{
    try
    {
        await _cacheService.RemoveAsync("products:all");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to clear cache for product {ProductId}", eventData.ProductId);
        // 不抛出异常，避免影响主流程
    }
}
```

### 5. 区分领域事件和集成事件

- **领域事件**：用于领域内通信，同步处理
- **集成事件**：用于跨服务通信，异步处理

```csharp
// 领域事件
public class ProductPriceChangedEvent : DomainEvent { }

// 集成事件
public class ProductPriceChangedIntegrationEvent : IntegrationEvent { }
```

### 6. 及时清理领域事件

实体中的领域事件在发布后应及时清理：

```csharp
public abstract class Entity<TId>
{
    private List<DomainEvent> _domainEvents;
    
    public void AddDomainEvent(DomainEvent eventItem)
    {
        _domainEvents ??= new List<DomainEvent>();
        _domainEvents.Add(eventItem);
    }
    
    public void ClearDomainEvents()
    {
        _domainEvents?.Clear();
    }
}

// 在保存后清理事件
public async Task SaveChangesAsync()
{
    var entities = GetEntitiesWithDomainEvents();
    var domainEvents = entities.SelectMany(e => e.DomainEvents).ToList();
    
    await PublishDomainEventsAsync(domainEvents);
    
    // 清理事件
    entities.ForEach(e => e.ClearDomainEvents());
    
    await base.SaveChangesAsync();
}
```

## 相关文档

- [实体](00-entities.md) - 实体详解
- [值对象](01-value-objects.md) - 值对象详解
- [事件总线](../03-infrastructure/02-event-bus.md) - 事件总线详解
- [领域驱动设计](../01-architecture/01-domain-driven-design.md) - DDD 设计原则
