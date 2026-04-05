# AGENTS.md

## Project Overview

CrestCreates 是一个类Abp Framework，功能强大、模块化的 .NET 企业级应用开发框架，基于领域驱动设计（DDD）和分层架构原则构建。

### 核心目标
- 提供开箱即用的企业级应用开发基础设施
- 支持模块化开发和自动模块发现
- 实现多 ORM 抽象层（EF Core、FreeSql、SqlSugar）
- 内置代码生成器减少重复代码
- 支持多租户、事件总线、任务调度等企业级特性

### 架构特点
- **分层架构**：Domain → Application → Infrastructure → Web
- **模块化设计**：基于 ModuleBase 的模块系统，支持依赖管理和生命周期控制
- **ORM 抽象**：统一的仓储接口，支持多种 ORM 提供者切换
- **代码生成**：使用 Roslyn SourceGenerator 自动生成 CRUD、权限、事件处理等代码
- **AOP 支持**：基于 Rougamo.Fody 的切面编程（缓存、审计、权限、工作单元等）

### 技术栈
- **.NET 10.0** - 最新 LTS 版本
- **C# 12.0** - 最新语言特性
- **ORM**: EF Core 10.0.5 / FreeSql 3.5.308 / SqlSugar 5.1.4.214
- **事件总线**: MediatR 14.1.0（本地）、RabbitMQ/Kafka（分布式）
- **缓存**: Memory Cache / Redis (StackExchange.Redis)
- **日志**: Serilog 4.3.1（结构化日志）
- **对象映射**: AutoMapper 16.1.1
- **验证**: FluentValidation 12.1.1
- **测试**: xUnit 2.9.3 + Moq 4.20.72 + FluentAssertions 8.9.0
- **认证授权**: JWT Bearer / OpenIddict / ASP.NET Identity
- **任务调度**: Quartz 3.17.1
- **健康检查**: Microsoft.Extensions.Diagnostics.HealthChecks

---

## Build & Commands

### 环境要求
- **.NET SDK**: 10.0.100 或更高版本（见 `global.json`）
- **IDE**: Visual Studio 2022 / JetBrains Rider / VS Code
- **数据库**: SQL Server / SQLite / PostgreSQL（根据选择的 ORM）

### 常用命令

#### 恢复依赖
```powershell
dotnet restore
```

#### 编译项目
```powershell
# 编译整个解决方案
dotnet build

# 编译特定项目
dotnet build framework/src/CrestCreates.Domain/CrestCreates.Domain.csproj

# Release 模式编译
dotnet build --configuration Release
```

#### 运行项目
```powershell
# 运行示例项目
dotnet run --project samples/LibraryManagement/LibraryManagement.Web

# 运行 AppHost（Aspire Dashboard）
dotnet run --project CrestCreates.AppHost/CrestCreates.AppHost.csproj
```

#### 运行测试
```powershell
# 运行所有测试
dotnet test

# 运行特定测试项目
dotnet test framework/test/CrestCreates.Domain.Tests/CrestCreates.Domain.Tests.csproj

# 带代码覆盖率
dotnet test --collect:"XPlat Code Coverage"

# 过滤测试
dotnet test --filter "FullyQualifiedName~CrestCreates.Domain.Tests"
```

#### 清理构建产物
```powershell
dotnet clean
dotnet clean --configuration Release
```

#### 发布
```powershell
# 发布单个项目
dotnet publish samples/LibraryManagement/LibraryManagement.Web -c Release -o ./publish

# 自包含发布
dotnet publish -c Release -r win-x64 --self-contained true
```

#### NuGet 包管理
```powershell
# 打包（中央包管理）
# 版本在 Directory.Packages.props 中统一管理

# 还原时强制使用中央包版本
dotnet restore --force-evaluate
```

#### 代码生成器
```powershell
# 代码生成器作为 SourceGenerator 集成
# 编译时自动生成代码，无需手动执行
```

### 开发工作流

1. **拉取最新代码**
   ```powershell
   git pull origin main
   ```

2. **恢复依赖并编译**
   ```powershell
   dotnet restore
   dotnet build
   ```

3. **运行测试确保无破坏**
   ```powershell
   dotnet test
   ```

4. **开发新功能/修复 Bug**

5. **再次运行测试**
   ```powershell
   dotnet test
   ```

6. **提交代码**
   ```powershell
   git add .
   git commit -m "feat: description"
   git push
   ```

---

## Code Style

### 命名规范

#### 命名空间
- 使用 PascalCase：`CrestCreates.Domain.Entities`
- 遵循项目结构：`{ProjectName}.{Layer}.{Feature}`

#### 类和接口
- **类名**：PascalCase，名词或名词短语
  ```csharp
  public class BookRepository : IBookRepository { }
  public class CreateBookDto { }
  ```

- **接口名**：以 `I` 开头，PascalCase
  ```csharp
  public interface IBookRepository { }
  public interface IAppService { }
  ```

- **抽象类**：以 `Abstract` 或 `Base` 结尾
  ```csharp
  public abstract class RepositoryBase<TEntity> { }
  public abstract class ModuleBase { }
  ```

#### 方法
- PascalCase，动词或动词短语
  ```csharp
  public Task<BookDto> GetByIdAsync(Guid id) { }
  public async Task CreateAsync(CreateBookDto input) { }
  ```

- 异步方法以 `Async` 后缀结尾
  ```csharp
  public Task<List<Book>> GetAllAsync() { }
  ```

#### 属性
- PascalCase
  ```csharp
  public string Name { get; set; }
  public Guid Id { get; private set; }
  ```

#### 字段
- 私有字段：`_camelCase`（下划线前缀）
  ```csharp
  private readonly IBookRepository _bookRepository;
  private string _name;
  ```

- 常量：UPPER_SNAKE_CASE
  ```csharp
  public const int MAX_RETRY_COUNT = 3;
  ```

#### 参数
- camelCase
  ```csharp
  public void ProcessData(string inputData, int timeout) { }
  ```

#### 泛型类型参数
- 以 `T` 开头，PascalCase
  ```csharp
  public class RepositoryBase<TEntity, TKey> where TEntity : Entity<TKey> { }
  ```

### 代码组织

#### 文件结构
```
CrestCreates.Domain/
├── Entities/           # 实体类
├── ValueObjects/       # 值对象
├── Repositories/       # 仓储接口
├── DomainEvents/       # 领域事件
├── Exceptions/         # 领域异常
└── Services/           # 领域服务
```

#### 类成员顺序
1. 常量
2. 静态字段
3. 私有字段
4. 构造函数
5. 公共属性
6. 公共方法
7. 受保护方法
8. 私有方法

```csharp
public class BookService : IBookService
{
    // 1. 常量
    private const int MaxTitleLength = 200;

    // 2. 私有字段
    private readonly IBookRepository _repository;
    private readonly IMapper _mapper;

    // 3. 构造函数
    public BookService(IBookRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    // 4. 公共方法
    public async Task<BookDto> GetByIdAsync(Guid id)
    {
        // ...
    }

    // 5. 私有方法
    private void ValidateTitle(string title)
    {
        // ...
    }
}
```

### 编码最佳实践

#### Nullable 引用类型
- 项目已启用 `<Nullable>enable</Nullable>`
- 明确标注可空类型
  ```csharp
  public string? Description { get; set; }
  public Task<Book?> FindByIdAsync(Guid id) { }
  ```

#### 异步编程
- 优先使用异步方法
- 避免 `.Result` 或 `.Wait()`或`GetAwait().GetResult()`等，使用 `await`
- 传递 `CancellationToken`
  ```csharp
  public async Task<BookDto> CreateAsync(
      CreateBookDto input, 
      CancellationToken cancellationToken = default)
  {
      var book = _mapper.Map<Book>(input);
      await _repository.InsertAsync(book, cancellationToken);
      return _mapper.Map<BookDto>(book);
  }
  ```

#### 依赖注入
- 通过构造函数注入依赖
- 使用接口而非具体实现
- 标记为 `readonly`
  ```csharp
  private readonly IBookRepository _repository;
  
  public BookService(IBookRepository repository)
  {
      _repository = repository;
  }
  ```

#### 错误处理
- 使用自定义异常类
- 提供有意义的错误消息
  ```csharp
  if (book == null)
  {
      throw new BusinessException($"Book with id {id} not found");
  }
  ```

#### 注释规范
- XML 文档注释用于公共 API
- 使用中文注释业务逻辑
- 保持注释简洁明了
  ```csharp
  /// <summary>
  /// 根据 ID 获取图书信息
  /// </summary>
  /// <param name="id">图书 ID</param>
  /// <returns>图书 DTO</returns>
  public async Task<BookDto> GetByIdAsync(Guid id)
  {
      // 查询数据库
      var book = await _repository.GetByIdAsync(id);
      
      // 转换为 DTO
      return _mapper.Map<BookDto>(book);
  }
  ```

#### LINQ 使用
- 优先使用方法语法
- 复杂查询使用查询表达式
- 注意性能，避免 N+1 问题
  ```csharp
  // ✅ 推荐
  var books = await _repository.GetAllAsync()
      .Where(b => b.IsActive)
      .OrderBy(b => b.Name)
      .ToListAsync();
  
  // ❌ 避免
  var books = _repository.GetAll().ToList()
      .Where(b => b.IsActive)
      .ToList();
  ```

### 格式化规则

项目建议使用以下格式化工具：
- **EditorConfig**：`.editorconfig` 文件定义代码风格
- **DotNetFormat**：`dotnet format` 自动格式化
- **ReSharper/Rider**：使用预设的代码风格配置

```powershell
# 格式化代码
dotnet format whitespace
dotnet format style
```

---

## Testing

### 测试框架

- **xUnit**：主测试框架
- **Moq**：模拟框架
- **FluentAssertions**：断言库
- **AutoFixture**：测试数据生成
- **Microsoft.AspNetCore.Mvc.Testing**：集成测试支持

### 测试项目结构

```
framework/test/
├── CrestCreates.TestBase/              # 测试基类
├── CrestCreates.Domain.Tests/          # 领域层测试
├── CrestCreates.Application.Tests/     # 应用层测试
├── CrestCreates.Infrastructure.Tests/  # 基础设施测试
├── CrestCreates.EventBus.Tests/        # 事件总线测试
├── CrestCreates.OrmProviders.Tests/    # ORM 提供者测试
└── CrestCreates.IntegrationTests/      # 集成测试
```

### 测试约定

#### 测试类命名
- 测试类：`{ClassName}Tests`
  ```csharp
  public class BookRepositoryTests { }
  public class BookAppServiceTests { }
  ```

#### 测试方法命名
- 格式：`{MethodUnderTest}_{Scenario}_{ExpectedResult}`
  ```csharp
  [Fact]
  public async Task GetByIdAsync_WithValidId_ReturnsBook() { }
  
  [Fact]
  public async Task GetByIdAsync_WithInvalidId_ThrowsException() { }
  
  [Fact]
  public async Task CreateAsync_WithDuplicateName_ThrowsBusinessException() { }
  ```

### 测试类型

#### 单元测试
- 测试单个类或方法
- 隔离外部依赖（使用 Mock）
- 快速执行

```csharp
public class BookAppServiceTests : AppTestBase
{
    private readonly IBookAppService _service;
    private readonly Mock<IBookRepository> _repositoryMock;

    public BookAppServiceTests()
    {
        _repositoryMock = new Mock<IBookRepository>();
        _service = new BookAppService(
            _repositoryMock.Object,
            Mapper,
            UnitOfWork
        );
    }

    [Fact]
    public async Task GetByIdAsync_WithValidId_ReturnsBook()
    {
        // Arrange
        var bookId = Guid.NewGuid();
        var book = new Book { Id = bookId, Name = "Test Book" };
        _repositoryMock.Setup(r => r.GetByIdAsync(bookId))
            .ReturnsAsync(book);

        // Act
        var result = await _service.GetByIdAsync(bookId);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Test Book");
    }
}
```

#### 集成测试
- 测试多个组件协作
- 使用真实数据库（内存数据库或测试数据库）
- 测试完整的工作流

```csharp
public class BookIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task CreateBook_ShouldPersistToDatabase()
    {
        // Arrange
        var service = GetRequiredService<IBookAppService>();
        var createDto = new CreateBookDto 
        { 
            Name = "Test Book",
            Author = "Test Author"
        };

        // Act
        var result = await service.CreateAsync(createDto);

        // Assert
        result.Id.Should().NotBeEmpty();
        
        // 验证数据库中确实存在
        var dbContext = GetRequiredService<LibraryDbContext>();
        var book = await dbContext.Books.FindAsync(result.Id);
        book.Should().NotBeNull();
        book.Name.Should().Be("Test Book");
    }
}
```

### 测试最佳实践

#### AAA 模式
每个测试应遵循 Arrange-Act-Assert 模式：

```csharp
[Fact]
public async Task Example_Test()
{
    // Arrange - 准备测试数据和 Mock
    var input = new CreateBookDto { Name = "Test" };
    _repositoryMock.Setup(r => r.InsertAsync(It.IsAny<Book>()))
        .ReturnsAsync(new Book { Id = Guid.NewGuid() });

    // Act - 执行被测方法
    var result = await _service.CreateAsync(input);

    // Assert - 验证结果
    result.Should().NotBeNull();
    _repositoryMock.Verify(r => r.InsertAsync(It.IsAny<Book>()), Times.Once);
}
```

#### 测试隔离
- 每个测试独立运行
- 不依赖测试执行顺序
- 使用 `[Fact]` 而非 `[Theory]`（除非需要参数化）

#### 测试数据生成
使用 AutoFixture 生成测试数据：

```csharp
private readonly IFixture _fixture;

public BookTests()
{
    _fixture = new Fixture();
    _fixture.Customize<Book>(c => c.Without(b => b.Id));
}

[Fact]
public void Test_With_AutoFixture()
{
    var book = _fixture.Create<Book>();
    // 使用生成的测试数据
}
```

#### 断言使用 FluentAssertions
```csharp
// ✅ 推荐
result.Should().NotBeNull();
result.Name.Should().Be("Expected Name");
result.Tags.Should().Contain("tag1");

// ❌ 避免
Assert.NotNull(result);
Assert.Equal("Expected Name", result.Name);
```

### 运行测试

```powershell
# 运行所有测试
dotnet test

# 运行特定测试项目
dotnet test framework/test/CrestCreates.Domain.Tests

# 运行特定测试类
dotnet test --filter "FullyQualifiedName~BookRepositoryTests"

# 运行特定测试方法
dotnet test --filter "FullyQualifiedName~GetByIdAsync_WithValidId_ReturnsBook"

# 查看详细输出
dotnet test --logger "console;verbosity=detailed"

# 生成代码覆盖率报告
dotnet test --collect:"XPlat Code Coverage"
# 查看报告：test/*/coverage.cobertura.xml
```

### 测试覆盖率目标

- **领域层**：≥ 90%
- **应用层**：≥ 80%
- **基础设施层**：≥ 70%
- **Web 层**：≥ 60%

---

## Security

### 认证与授权

#### 支持的认证方式
- **JWT Bearer**：基于 Token 的认证
- **OpenIddict**：OAuth 2.0 / OpenID Connect
- **ASP.NET Identity**：用户管理系统

#### 权限控制
- 基于角色的访问控制（RBAC）
- 基于资源的权限检查
- 数据权限过滤（行级安全）

```csharp
// 控制器级别权限
[AuthorizePermission("Books.View")]
public class BooksController : ControllerBase
{
    // 方法级别权限
    [AuthorizePermission("Books.Create")]
    public async Task<IActionResult> Create([FromBody] CreateBookDto input)
    {
        // ...
    }
}
```

### 数据安全

#### 敏感数据处理
- 密码必须哈希存储（使用 ASP.NET Identity PasswordHasher）
- 个人敏感信息加密存储
- 不要在日志中记录敏感信息

```csharp
// ✅ 正确
_logger.LogInformation("User {UserId} logged in", userId);

// ❌ 错误
_logger.LogInformation("User {Email} with password {Password} logged in", email, password);
```

#### SQL 注入防护
- 始终使用参数化查询
- ORM 自动处理参数化
- 避免拼接 SQL 字符串

```csharp
// ✅ ORM 自动参数化
var books = await _repository.GetAllAsync()
    .Where(b => b.Name.Contains(searchTerm))
    .ToListAsync();

// ❌ 避免
var sql = $"SELECT * FROM Books WHERE Name LIKE '%{searchTerm}%'";
```

### XSS 防护

- Razor 视图自动编码输出
- API 返回 JSON，前端负责转义
- 使用 Content-Security-Policy 头

### CSRF 防护

- POST/PUT/DELETE 请求需要 Anti-Forgery Token
- 使用 `[ValidateAntiForgeryToken]` 特性

```csharp
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Create(CreateBookDto input)
{
    // ...
}
```

### HTTPS 强制

- 生产环境强制使用 HTTPS
- 配置 HSTS（HTTP Strict Transport Security）

```csharp
app.UseHttpsRedirection();
app.UseHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.Preload = true;
});
```

### 安全头配置

```csharp
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});
```

### 输入验证

- 使用 FluentValidation 验证输入
- 白名单验证文件上传类型
- 限制文件大小

```csharp
public class CreateBookValidator : AbstractValidator<CreateBookDto>
{
    public CreateBookValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(200);
        
        RuleFor(x => x.Price)
            .GreaterThan(0)
            .LessThan(10000);
    }
}
```

### 审计日志

- 记录关键操作（创建、更新、删除）
- 记录用户 ID、时间戳、IP 地址
- 审计日志不可篡改

```csharp
[Audited]
public class BookAppService : IBookAppService
{
    // 所有操作自动记录审计日志
}
```

### 依赖项安全

- 定期更新 NuGet 包
- 使用 `dotnet list package --vulnerable` 检查漏洞
- 订阅安全公告

```powershell
# 检查漏洞
dotnet list package --vulnerable

# 更新包
dotnet add package <PackageName> --version <NewVersion>
```

---

## Configuration

### 配置来源优先级

1. **环境变量**（最高优先级）
2. **appsettings.{Environment}.json**（如 appsettings.Production.json）
3. **appsettings.json**（基础配置）
4. **用户机密**（开发环境）
5. **命令行参数**

### 配置文件结构

#### appsettings.json
```json
{
  "ConnectionStrings": {
    "Default": "Server=localhost;Database=CrestCreates;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  
  "Security": {
    "Jwt": {
      "Key": "your-secret-key-min-32-characters",
      "Issuer": "CrestCreates",
      "Audience": "CrestCreates.Api",
      "ExpirationMinutes": 60
    }
  },
  
  "FileManagement": {
    "ProviderType": "LocalFileSystem",
    "LocalFileSystem": {
      "RootPath": "wwwroot/files",
      "UseAbsolutePath": false
    },
    "Validation": {
      "AllowedExtensions": [".jpg", ".png", ".pdf"],
      "MaxFileSize": 10485760,
      "AllowOverwrite": false
    }
  },
  
  "Caching": {
    "EnableCache": true,
    "DefaultExpirationMinutes": 10,
    "Redis": {
      "ConnectionString": "localhost:6379",
      "InstanceName": "CrestCreates"
    }
  },
  
  "EventBus": {
    "Provider": "Local",
    "RabbitMQ": {
      "HostName": "localhost",
      "Port": 5672,
      "UserName": "guest",
      "Password": "guest",
      "VirtualHost": "/"
    }
  },
  
  "MultiTenancy": {
    "IsEnabled": true,
    "TenantResolveStrategy": "Domain",
    "DefaultTenantId": "default"
  }
}
```

#### appsettings.Development.json
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  },
  
  "ConnectionStrings": {
    "Default": "Server=localhost;Database=CrestCreates_Dev;..."
  }
}
```

#### appsettings.Production.json
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning"
    }
  },
  
  "Security": {
    "Jwt": {
      "Key": "${JWT_KEY}",
      "ExpirationMinutes": 30
    }
  }
}
```

### 环境变量配置

使用环境变量覆盖配置文件：

```powershell
# Windows PowerShell
$env:ConnectionStrings__Default = "Server=prod-server;Database=CrestCreates_Prod;..."
$env:Security__Jwt__Key = "production-secret-key"
$env:Logging__LogLevel__Default = "Warning"

# Linux/macOS
export ConnectionStrings__Default="Server=prod-server;..."
export Security__Jwt__Key="production-secret-key"
```

**注意**：使用双下划线 `__` 表示嵌套配置。

### 用户机密（开发环境）

```powershell
# 初始化用户机密
dotnet user-secrets init

# 设置机密
dotnet user-secrets set "Security:Jwt:Key" "dev-secret-key"
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost;..."

# 列出机密
dotnet user-secrets list

# 清除机密
dotnet user-secrets clear
```

### 配置类示例

```csharp
namespace CrestCreates.Configuration.Options
{
    public class JwtOptions
    {
        public const string SectionName = "Security:Jwt";
        
        public string Key { get; set; } = string.Empty;
        public string Issuer { get; set; } = "CrestCreates";
        public string Audience { get; set; } = "CrestCreates.Api";
        public int ExpirationMinutes { get; set; } = 60;
    }
    
    public class CacheOptions
    {
        public const string SectionName = "Caching";
        
        public bool EnableCache { get; set; } = true;
        public int DefaultExpirationMinutes { get; set; } = 10;
        public RedisOptions Redis { get; set; } = new();
    }
    
    public class RedisOptions
    {
        public string ConnectionString { get; set; } = "localhost:6379";
        public string InstanceName { get; set; } = "CrestCreates";
    }
}
```

### 在模块中使用配置

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Modularity;

public class CachingModule : ModuleBase
{
    private readonly IConfiguration _configuration;
    
    public CachingModule(IConfiguration configuration)
    {
        _configuration = configuration;
    }
    
    public override void OnConfigureServices(IServiceCollection services)
    {
        // 绑定配置
        var cacheOptions = new CacheOptions();
        _configuration.GetSection(CacheOptions.SectionName).Bind(cacheOptions);
        
        services.AddSingleton(cacheOptions);
        
        // 注册缓存服务
        if (cacheOptions.EnableCache)
        {
            if (!string.IsNullOrEmpty(cacheOptions.Redis.ConnectionString))
            {
                services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = cacheOptions.Redis.ConnectionString;
                    options.InstanceName = cacheOptions.Redis.InstanceName;
                });
            }
            else
            {
                services.AddDistributedMemoryCache();
            }
        }
    }
}
```

### 配置验证

使用 FluentValidation 验证配置：

```csharp
public class JwtOptionsValidator : AbstractValidator<JwtOptions>
{
    public JwtOptionsValidator()
    {
        RuleFor(x => x.Key)
            .NotEmpty()
            .MinimumLength(32)
            .WithMessage("JWT Key must be at least 32 characters");
        
        RuleFor(x => x.Issuer)
            .NotEmpty();
        
        RuleFor(x => x.Audience)
            .NotEmpty();
        
        RuleFor(x => x.ExpirationMinutes)
            .GreaterThan(0)
            .LessThanOrEqualTo(1440); // 最大 24 小时
    }
}
```

### 环境特定配置

```csharp
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        
        if (environment == "Development")
        {
            // 开发环境配置
            services.AddDeveloperTools();
        }
        else if (environment == "Production")
        {
            // 生产环境配置
            services.AddProductionOptimizations();
        }
    }
}
```

### 配置最佳实践

1. **不要硬编码配置值**：始终从配置文件或环境变量读取
2. **敏感信息使用用户机密或密钥管理服务**：不在代码或配置文件中明文存储
3. **使用强类型配置类**：避免魔法字符串
4. **验证配置**：启动时验证必需的配置项
5. **提供默认值**：配置缺失时使用合理的默认值
6. **文档化配置项**：在 README 或 Wiki 中说明所有配置项
7. **使用环境变量部署**：生产环境通过环境变量注入配置

---

## Additional Resources

- **官方文档**: [docs/INDEX.md](docs/INDEX.md)
- **架构文档**: [docs/01-architecture/](docs/01-architecture/)
- **示例项目**: [samples/LibraryManagement/](samples/LibraryManagement/)
- **GitHub Issues**: 报告问题和功能请求
- **贡献指南**: [docs/08-development/03-contribution-guide.md](docs/08-development/03-contribution-guide.md)

---

**最后更新**: 2026-04-05  
**文档版本**: v1.0.0  
**框架版本**: v1.0.0
