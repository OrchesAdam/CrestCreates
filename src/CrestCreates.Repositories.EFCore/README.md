# CrestCreates.Repositories.EFCore

Entity Framework Core 实现的 CrestCreates 数据仓储库。这个包提供了基于 EF Core 的仓储模式和工作单元模式实现。

## 功能特性

- 🚀 基于 Entity Framework Core 的高性能实现
- 🔄 支持多种数据库提供程序（SQL Server、MySQL、PostgreSQL、SQLite、InMemory）
- 📦 完整的仓储模式和工作单元模式实现
- 🔗 与 CrestCreates.Data 抽象层完美集成
- ⚡ EF Core 特定的性能优化
- 🔍 支持包含查询（Include）、无跟踪查询、原始 SQL
- 📊 支持批量操作和分页查询

## 安装

```bash
dotnet add package CrestCreates.Repositories.EFCore
```

## 基本使用

### 1. 配置服务

```csharp
using CrestCreates.Repositories.EFCore.Extensions;

public void ConfigureServices(IServiceCollection services)
{
    // 配置 SQL Server
    services.AddEFCoreRepository(options =>
    {
        options.UseSqlServer("Server=.;Database=MyApp;Integrated Security=true;");
    });

    // 或者配置 SQLite
    services.AddEFCoreRepository(options =>
    {
        options.UseSqlite("Data Source=myapp.db");
    });

    // 或者配置 InMemory（用于测试）
    services.AddEFCoreRepository(options =>
    {
        options.UseInMemoryDatabase("TestDb");
    });
}
```

### 2. 定义实体

```csharp
public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    
    // 导航属性
    public List<Order> Orders { get; set; } = new();
}

public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public decimal Amount { get; set; }
    public DateTime OrderDate { get; set; }
    
    public User User { get; set; } = null!;
}
```

### 3. 使用仓储

```csharp
public class UserService
{
    private readonly IUnitOfWork _unitOfWork;

    public UserService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<User> CreateUserAsync(string name, string email)
    {
        var userRepository = _unitOfWork.GetRepository<User>();
        
        var user = new User
        {
            Name = name,
            Email = email,
            CreatedAt = DateTime.UtcNow
        };

        await userRepository.AddAsync(user);
        await _unitOfWork.CommitAsync();
        
        return user;
    }

    public async Task<IPagedResult<User>> GetUsersAsync(int pageIndex, int pageSize)
    {
        var userRepository = _unitOfWork.GetRepository<User>();
        
        return await userRepository.GetPagedAsync(
            pageIndex: pageIndex,
            pageSize: pageSize,
            predicate: u => u.Name.Contains("John"), // 可选条件
            orderBy: query => query.OrderBy(u => u.CreatedAt) // 可选排序
        );
    }
}
```

### 4. EF Core 特定功能

```csharp
public class AdvancedUserService
{
    private readonly IUnitOfWork _unitOfWork;

    public AdvancedUserService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<User?> GetUserWithOrdersAsync(int userId)
    {
        // 获取 EF Core 特定的仓储实现
        var userRepository = _unitOfWork.GetRepository<User>() as EFCoreRepository<User>;
        
        if (userRepository != null)
        {
            // 使用 Include 加载相关数据
            var query = userRepository.Include(u => u.Orders)
                                    .AsNoTracking(); // 无跟踪查询提高性能
            
            return await query.FirstOrDefaultAsync(u => u.Id == userId);
        }
        
        return await _unitOfWork.GetRepository<User>().GetByIdAsync(userId);
    }

    public async Task<int> BulkDeleteInactiveUsersAsync()
    {
        var userRepository = _unitOfWork.GetRepository<User>() as EFCoreRepository<User>;
        
        if (userRepository != null)
        {
            // 批量删除
            var deletedCount = await userRepository.BatchDeleteAsync(
                u => u.CreatedAt < DateTime.UtcNow.AddYears(-1)
            );
            
            await _unitOfWork.CommitAsync();
            return deletedCount;
        }
        
        return 0;
    }
}
```

## 配置选项

### 数据库提供程序配置

```csharp
// SQL Server
services.AddEFCoreRepository(options =>
{
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure();
        sqlOptions.CommandTimeout(30);
    });
});

// MySQL
services.AddEFCoreRepository(options =>
{
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
});

// PostgreSQL
services.AddEFCoreRepository(options =>
{
    options.UseNpgsql(connectionString);
});
```

### 全局配置

```csharp
services.AddEFCoreRepository(options =>
{
    options.UseSqlServer(connectionString);
}, contextOptions =>
{
    // 配置 DbContext 选项
    contextOptions.EnableSensitiveDataLogging(); // 仅在开发环境
    contextOptions.EnableDetailedErrors();
    contextOptions.LogTo(Console.WriteLine);
});
```

## 高级功能

### 自定义仓储

```csharp
public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email);
    Task<List<User>> GetActiveUsersAsync();
}

public class EFCoreUserRepository : EFCoreRepository<User>, IUserRepository
{
    public EFCoreUserRepository(EFCoreDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _efDbSet.FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<List<User>> GetActiveUsersAsync()
    {
        return await _efDbSet
            .Where(u => u.CreatedAt > DateTime.UtcNow.AddYears(-1))
            .AsNoTracking()
            .ToListAsync();
    }
}

// 注册自定义仓储
services.AddScoped<IUserRepository, EFCoreUserRepository>();
```

### 原始 SQL 支持

```csharp
public async Task<List<UserStatistics>> GetUserStatisticsAsync()
{
    var userRepository = _unitOfWork.GetRepository<User>() as EFCoreRepository<User>;
    
    if (userRepository != null)
    {
        // 执行原始 SQL 查询
        var users = userRepository.FromSqlRaw(@"
            SELECT * FROM Users 
            WHERE CreatedAt > {0} 
            ORDER BY Name", DateTime.UtcNow.AddMonths(-6));
        
        return await users.ToListAsync();
    }
    
    return new List<UserStatistics>();
}
```

## 最佳实践

1. **使用异步方法**：优先使用 `xxxAsync` 方法
2. **启用无跟踪查询**：对于只读操作使用 `AsNoTracking()`
3. **合理使用 Include**：避免加载不必要的相关数据
4. **批量操作**：对于大量数据操作使用批量方法
5. **事务管理**：使用工作单元管理事务边界

## 兼容性

- .NET 6.0+
- Entity Framework Core 6.0+
- CrestCreates.Data 1.0+

## 许可证

MIT License
