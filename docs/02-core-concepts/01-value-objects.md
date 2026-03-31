# 值对象（Value Objects）

本文档介绍 CrestCreates 框架中的值对象概念和实现。

## 概述

值对象是没有唯一标识的领域对象，通过属性值判断相等性。值对象是不可变的，一旦创建就不能修改。

## 核心概念

### 值对象的特点

1. **无标识**：值对象没有唯一标识符
2. **不可变性**：值对象创建后不可修改
3. **基于值的相等性**：通过属性值判断相等性
4. **可替换性**：值对象可以被另一个相同值的值对象替换

### 值对象 vs 实体

| 特性 | 值对象 | 实体 |
|------|--------|------|
| 标识 | 无标识 | 有唯一标识 |
| 相等性 | 基于属性值 | 基于标识 |
| 可变性 | 不可变 | 可变 |
| 生命周期 | 无生命周期 | 有生命周期 |

## 值对象基类

### ValueObject

`ValueObject` 是所有值对象的基类，提供基本的值对象功能。

```csharp
public abstract class ValueObject
{
    // 获取用于相等性比较的组件
    protected abstract IEnumerable<object> GetEqualityComponents();
    
    // 相等性判断
    public override bool Equals(object obj)
    {
        if (obj is not ValueObject other)
            return false;
            
        if (GetType() != other.GetType())
            return false;
            
        return GetEqualityComponents()
            .SequenceEqual(other.GetEqualityComponents());
    }
    
    // 获取哈希码
    public override int GetHashCode()
    {
        return GetEqualityComponents()
            .Select(x => x != null ? x.GetHashCode() : 0)
            .Aggregate((x, y) => x ^ y);
    }
    
    // 相等运算符
    public static bool operator ==(ValueObject left, ValueObject right)
    {
        if (ReferenceEquals(left, null) && ReferenceEquals(right, null))
            return true;
            
        if (ReferenceEquals(left, null) || ReferenceEquals(right, null))
            return false;
            
        return left.Equals(right);
    }
    
    // 不等运算符
    public static bool operator !=(ValueObject left, ValueObject right)
    {
        return !(left == right);
    }
}
```

## 创建值对象

### 基本值对象

```csharp
public class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }
    
    public Money(decimal amount, string currency)
    {
        if (amount < 0)
            throw new ArgumentException("Amount cannot be negative");
            
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency cannot be empty");
            
        Amount = amount;
        Currency = currency;
    }
    
    // 业务方法
    public Money Add(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException("Cannot add money with different currencies");
            
        return new Money(Amount + other.Amount, Currency);
    }
    
    public Money Subtract(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException("Cannot subtract money with different currencies");
            
        var newAmount = Amount - other.Amount;
        if (newAmount < 0)
            throw new InvalidOperationException("Insufficient funds");
            
        return new Money(newAmount, Currency);
    }
    
    public Money Multiply(decimal factor)
    {
        if (factor < 0)
            throw new ArgumentException("Factor cannot be negative");
            
        return new Money(Amount * factor, Currency);
    }
    
    // 相等性组件
    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }
    
    public override string ToString()
    {
        return $"{Amount} {Currency}";
    }
}
```

### 复杂值对象

```csharp
public class Address : ValueObject
{
    public string Street { get; }
    public string City { get; }
    public string State { get; }
    public string Country { get; }
    public string ZipCode { get; }
    
    public Address(string street, string city, string state, string country, string zipCode)
    {
        Street = street ?? throw new ArgumentNullException(nameof(street));
        City = city ?? throw new ArgumentNullException(nameof(city));
        State = state ?? throw new ArgumentNullException(nameof(state));
        Country = country ?? throw new ArgumentNullException(nameof(country));
        ZipCode = zipCode ?? throw new ArgumentNullException(nameof(zipCode));
    }
    
    public string GetFullAddress()
    {
        return $"{Street}, {City}, {State} {ZipCode}, {Country}";
    }
    
    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Street;
        yield return City;
        yield return State;
        yield return Country;
        yield return ZipCode;
    }
}
```

### 在实体中使用值对象

```csharp
public class Product : AuditedEntity<Guid>
{
    public string Name { get; private set; }
    public Money Price { get; private set; }
    public Address WarehouseLocation { get; private set; }
    
    private Product() { }
    
    public Product(string name, Money price, Address warehouseLocation)
    {
        Id = Guid.NewGuid();
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Price = price ?? throw new ArgumentNullException(nameof(price));
        WarehouseLocation = warehouseLocation ?? throw new ArgumentNullException(nameof(warehouseLocation));
    }
    
    public void UpdatePrice(Money newPrice)
    {
        if (newPrice.Currency != Price.Currency)
            throw new InvalidOperationException("Cannot change currency");
            
        Price = newPrice;
        AddDomainEvent(new ProductPriceChangedEvent(Id, newPrice.Amount));
    }
    
    public void RelocateWarehouse(Address newLocation)
    {
        WarehouseLocation = newLocation;
        AddDomainEvent(new ProductRelocatedEvent(Id, newLocation.GetFullAddress()));
    }
}
```

## 常见值对象示例

### 1. 货币

```csharp
public class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }
    
    public Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }
    
    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }
}
```

### 2. 地址

```csharp
public class Address : ValueObject
{
    public string Street { get; }
    public string City { get; }
    public string ZipCode { get; }
    
    public Address(string street, string city, string zipCode)
    {
        Street = street;
        City = city;
        ZipCode = zipCode;
    }
    
    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Street;
        yield return City;
        yield return ZipCode;
    }
}
```

### 3. 日期范围

```csharp
public class DateRange : ValueObject
{
    public DateTime Start { get; }
    public DateTime End { get; }
    
    public DateRange(DateTime start, DateTime end)
    {
        if (start > end)
            throw new ArgumentException("Start date must be before end date");
            
        Start = start;
        End = end;
    }
    
    public int Days => (End - Start).Days;
    
    public bool Contains(DateTime date)
    {
        return date >= Start && date <= End;
    }
    
    public bool Overlaps(DateRange other)
    {
        return Start < other.End && other.Start < End;
    }
    
    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Start;
        yield return End;
    }
}
```

### 4. 电子邮件

```csharp
public class Email : ValueObject
{
    public string Value { get; }
    
    public Email(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Email cannot be empty");
            
        if (!IsValidEmail(value))
            throw new ArgumentException("Invalid email format");
            
        Value = value.ToLowerInvariant();
    }
    
    private static bool IsValidEmail(string email)
    {
        // 简单的邮箱验证
        return email.Contains("@") && email.Contains(".");
    }
    
    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
    
    public override string ToString() => Value;
}
```

## 最佳实践

### 1. 保持不可变性

值对象一旦创建就不能修改：

```csharp
// 好的实践
public Money Add(Money other)
{
    return new Money(Amount + other.Amount, Currency);
}

// 避免：修改现有对象
public void AddAmount(decimal amount)
{
    Amount += amount; // 错误！
}
```

### 2. 实现完整的相等性逻辑

确保正确实现 `Equals`、`GetHashCode` 和运算符：

```csharp
protected abstract IEnumerable<object> GetEqualityComponents();

public override bool Equals(object obj)
{
    // 实现相等性判断
}

public override int GetHashCode()
{
    // 实现哈希码计算
}

public static bool operator ==(ValueObject left, ValueObject right)
{
    // 实现相等运算符
}

public static bool operator !=(ValueObject left, ValueObject right)
{
    // 实现不等运算符
}
```

### 3. 验证输入

在构造函数中验证输入：

```csharp
public Money(decimal amount, string currency)
{
    if (amount < 0)
        throw new ArgumentException("Amount cannot be negative");
        
    if (string.IsNullOrWhiteSpace(currency))
        throw new ArgumentException("Currency cannot be empty");
        
    Amount = amount;
    Currency = currency;
}
```

### 4. 提供业务方法

值对象应包含业务逻辑：

```csharp
public Money Add(Money other)
{
    if (Currency != other.Currency)
        throw new InvalidOperationException("Cannot add money with different currencies");
        
    return new Money(Amount + other.Amount, Currency);
}
```

### 5. 使用值对象表示概念

将相关属性封装为值对象：

```csharp
// 好的实践
public class Customer : Entity<Guid>
{
    public Email Email { get; private set; }
    public Address Address { get; private set; }
}

// 避免：分散的属性
public class Customer : Entity<Guid>
{
    public string Email { get; set; }
    public string Street { get; set; }
    public string City { get; set; }
    // ...
}
```

## 相关文档

- [实体](00-entities.md) - 实体详解
- [领域事件](02-domain-events.md) - 领域事件详解
- [领域驱动设计](../01-architecture/01-domain-driven-design.md) - DDD 设计原则
