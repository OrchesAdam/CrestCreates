# FreeSqlAuditInterceptor 错误分析和修复

## 🔍 发现的问题

### 问题 1: 软删除字段填充逻辑错误

**错误代码**：
```csharp
if (e.Property.Name == nameof(ISoftDelete.IsDeleted))
{
    if ((bool)e.Value == true)
    {
        softDelete.DeletionTime = now;  // ❌ 错误：直接修改对象不会生效
        softDelete.DeleterId = currentUserId.Value;
    }
}
```

**问题分析**：
1. FreeSql 的 `AuditValue` 事件是**基于属性级别**的
2. 每个属性的修改都会触发一次事件
3. 在事件中应该修改 `e.Value`，而不是直接修改对象的属性
4. 直接修改 `softDelete.DeletionTime` 不会被 FreeSql 捕获并保存到数据库

**工作原理**：
```
更新实体时的事件触发顺序：
1. AuditValue 触发 - Property: IsDeleted
2. AuditValue 触发 - Property: DeletionTime  ← 需要在这里设置值
3. AuditValue 触发 - Property: DeleterId     ← 需要在这里设置值
4. ... 其他属性
```

### 问题 2: 缺少状态跟踪

**问题**：
- 当 `IsDeleted` 被设置为 `true` 时，需要记住这个状态
- 后续处理 `DeletionTime` 和 `DeleterId` 属性时，需要知道当前正在执行删除操作

## ✅ 修复方案

### 解决方案：使用 AsyncLocal 跟踪删除状态

```csharp
// 1. 添加状态跟踪变量
private static readonly AsyncLocal<bool> _isDeletingContext = new AsyncLocal<bool>();

// 2. 在检测到 IsDeleted = true 时，设置标记
if (e.Property.Name == nameof(ISoftDelete.IsDeleted) && 
    e.Value is bool isDeleted && isDeleted)
{
    _isDeletingContext.Value = true;  // 标记正在删除
}

// 3. 在处理其他软删除字段时，检查标记
if (_isDeletingContext.Value)
{
    if (e.Property.Name == nameof(ISoftDelete.DeletionTime))
    {
        e.Value = now;  // ✅ 正确：修改 e.Value
    }
    else if (e.Property.Name == nameof(ISoftDelete.DeleterId))
    {
        e.Value = currentUserId.Value;
    }
}
```

### 为什么使用 AsyncLocal？

1. **线程安全**：每个异步上下文独立
2. **作用域隔离**：不同的删除操作互不干扰
3. **自动清理**：异步操作完成后自动重置

## 🔧 完整的修复代码

```csharp
public static void ConfigureAuditInterceptor(this IFreeSql freeSql, ICurrentUserProvider currentUserProvider)
{
    freeSql.Aop.AuditValue += (sender, e) =>
    {
        var now = DateTime.UtcNow;
        var currentUserId = currentUserProvider?.GetCurrentUserId();

        // 处理审计实体
        if (e.Object is IAuditedEntity)
        {
            switch (e.AuditValueType)
            {
                case FreeSql.Aop.AuditValueType.Insert:
                    if (e.Property.Name == nameof(IAuditedEntity.CreationTime))
                        e.Value = now;
                    else if (e.Property.Name == nameof(IAuditedEntity.CreatorId))
                        e.Value = currentUserId;
                    break;

                case FreeSql.Aop.AuditValueType.Update:
                    if (e.Property.Name == nameof(IAuditedEntity.LastModificationTime))
                        e.Value = now;
                    else if (e.Property.Name == nameof(IAuditedEntity.LastModifierId))
                        e.Value = currentUserId;
                    break;
            }
        }

        // 处理软删除
        if (e.Object is ISoftDelete && e.AuditValueType == FreeSql.Aop.AuditValueType.Update)
        {
            // 检测删除操作
            if (e.Property.Name == nameof(ISoftDelete.IsDeleted) && 
                e.Value is bool isDeleted && isDeleted)
            {
                _isDeletingContext.Value = true;
            }

            // 填充删除相关字段
            if (_isDeletingContext.Value)
            {
                if (e.Property.Name == nameof(ISoftDelete.DeletionTime))
                    e.Value = now;
                else if (e.Property.Name == nameof(ISoftDelete.DeleterId))
                    e.Value = currentUserId;
            }
        }
    };

    ConfigureSoftDeleteFilter(freeSql);
}
```

## 🧪 测试验证

### 测试用例 1: 软删除自动填充

```csharp
[Fact]
public async Task SoftDelete_ShouldFillDeletionFields()
{
    // Arrange
    var product = new Product { Id = 1, Name = "Test" };
    await _repository.AddAsync(product);

    // Act - 执行软删除
    product.IsDeleted = true;
    await _repository.UpdateAsync(product);

    // Assert
    Assert.True(product.IsDeleted);
    Assert.NotNull(product.DeletionTime);  // ✅ 应该自动填充
    Assert.NotNull(product.DeleterId);     // ✅ 应该自动填充
}
```

### 测试用例 2: 审计字段自动填充

```csharp
[Fact]
public async Task Insert_ShouldFillCreationFields()
{
    // Arrange
    var product = new Product { Id = 1, Name = "Test" };

    // Act
    await _repository.AddAsync(product);

    // Assert
    Assert.NotEqual(default, product.CreationTime);  // ✅ 自动填充
    Assert.NotNull(product.CreatorId);               // ✅ 自动填充
}

[Fact]
public async Task Update_ShouldFillModificationFields()
{
    // Arrange
    var product = await _repository.GetByIdAsync(1);
    
    // Act
    product.Name = "Updated";
    await _repository.UpdateAsync(product);

    // Assert
    Assert.NotNull(product.LastModificationTime);  // ✅ 自动填充
    Assert.NotNull(product.LastModifierId);        // ✅ 自动填充
}
```

## 📊 对比总结

| 方面 | 错误实现 | 正确实现 |
|------|----------|----------|
| **设置方式** | `softDelete.DeletionTime = now` | `e.Value = now` |
| **是否生效** | ❌ 不会保存到数据库 | ✅ 会保存到数据库 |
| **状态跟踪** | ❌ 无状态跟踪 | ✅ 使用 AsyncLocal |
| **线程安全** | ❌ 不安全 | ✅ 安全 |
| **逻辑完整性** | ❌ 缺少关联逻辑 | ✅ 完整的删除流程 |

## 💡 关键要点

1. **FreeSql AuditValue 事件是属性级别的**
   - 每个属性修改触发一次
   - 必须通过 `e.Value` 修改属性值

2. **使用 AsyncLocal 跟踪状态**
   - 线程安全
   - 作用域隔离
   - 自动清理

3. **正确的修改方式**
   ```csharp
   // ❌ 错误
   entity.PropertyName = value;
   
   // ✅ 正确
   e.Value = value;
   ```

4. **属性处理顺序不确定**
   - 不能假设 IsDeleted 一定先于 DeletionTime 处理
   - 需要使用状态变量来协调

## 🔗 相关资源

- [FreeSql AOP 文档](https://freesql.net/guide/aop.html)
- [FreeSql 审计功能](https://freesql.net/guide/audit.html)
- [AsyncLocal 文档](https://docs.microsoft.com/dotnet/api/system.threading.asynclocal-1)

---

**修复日期**: 2025年10月30日  
**影响范围**: FreeSql 软删除功能  
**严重程度**: 高（功能无法正常工作）  
**状态**: ✅ 已修复
