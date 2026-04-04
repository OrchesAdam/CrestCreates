# Repository 模板

## Repository 实现模板

### 基本 Repository 实现（继承基类）

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

public class YourEntityRepository : YourEntityRepositoryBase, IYourEntityRepository
{
    public YourEntityRepository(YourDbContext dbContext) : base(dbContext)
    {
    }

    // 在这里添加自定义方法

    public async Task<YourEntity?> GetByCustomPropertyAsync(string customValue, CancellationToken cancellationToken = default)
    {
        return await DbSet.FirstOrDefaultAsync(e => e.CustomProperty == customValue, cancellationToken);
    }

    public async Task<List<YourEntity>> GetByStatusAsync(YourEntityStatus status, CancellationToken cancellationToken = default)
    {
        return await DbSet.Where(e => e.Status == status).ToListAsync(cancellationToken);
    }
}
```

### 自定义查询方法示例

```csharp
public class YourEntityRepository : YourEntityRepositoryBase, IYourEntityRepository
{
    public YourEntityRepository(YourDbContext dbContext) : base(dbContext)
    {
    }

    /// <summary>
    /// 根据名称模糊查询
    /// </summary>
    public async Task<List<YourEntity>> SearchByNameAsync(string keyword, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(e => e.Name.Contains(keyword))
            .OrderByDescending(e => e.CreationTime)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 获取最近创建的实体
    /// </summary>
    public async Task<List<YourEntity>> GetRecentCreatedAsync(int count, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .OrderByDescending(e => e.CreationTime)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 批量查询
    /// </summary>
    public async Task<bool> ExistsByUniquePropertyAsync(string uniqueValue, CancellationToken cancellationToken = default)
    {
        return await DbSet.AnyAsync(e => e.UniqueProperty == uniqueValue, cancellationToken);
    }
}
```

### 完整的 Repository 接口扩展

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using YourNamespace.Domain.Entities;

namespace YourNamespace.Domain.Repositories;

public partial interface IYourEntityRepository : IRepository<YourEntity, Guid>
{
    Task<YourEntity?> GetByCustomPropertyAsync(string customValue, CancellationToken cancellationToken = default);
    Task<List<YourEntity>> GetByStatusAsync(YourEntityStatus status, CancellationToken cancellationToken = default);
    Task<List<YourEntity>> SearchByNameAsync(string keyword, CancellationToken cancellationToken = default);
    Task<List<YourEntity>> GetRecentCreatedAsync(int count, CancellationToken cancellationToken = default);
    Task<bool> ExistsByUniquePropertyAsync(string uniqueValue, CancellationToken cancellationToken = default);
}
```
