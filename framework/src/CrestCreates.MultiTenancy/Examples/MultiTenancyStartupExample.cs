using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using CrestCreates.MultiTenancy;
using CrestCreates.Infrastructure.Providers.EFCore.MultiTenancy;
using CrestCreates.MultiTenancy.Abstractions;

namespace CrestCreates.Web.Examples
{
    /// <summary>
    /// 多租户配置示例
    /// 演示如何在 ASP.NET Core 应用中配置多租户功能
    /// </summary>
    public class MultiTenancyStartupExample
    {
        /// <summary>
        /// 示例 1: 使用内存提供者 + HTTP Header 识别（开发环境）
        /// </summary>
        public void ConfigureServicesExample1(IServiceCollection services)
        {
            // 添加多租户支持 - 内存模式
            services.AddMultiTenancyWithInMemory(
                options =>
                {
                    // 使用 HTTP Header 识别租户
                    options.ResolutionStrategy = TenantResolutionStrategy.Header;
                    options.TenantHeaderName = "X-Tenant-Id";

                    // 使用数据库隔离策略
                    options.IsolationStrategy = TenantIsolationStrategy.Database;
                },
                provider =>
                {
                    // 配置测试租户
                    provider.AddTenant(new TenantInfo(
                        id: "tenant1",
                        name: "租户1 - 开发环境",
                        connectionString: "Server=localhost;Database=Tenant1Db;User Id=sa;Password=YourPassword;TrustServerCertificate=True"));

                    provider.AddTenant(new TenantInfo(
                        id: "tenant2",
                        name: "租户2 - 开发环境",
                        connectionString: "Server=localhost;Database=Tenant2Db;User Id=sa;Password=YourPassword;TrustServerCertificate=True"));
                });

            // 配置 DbContext - 动态连接字符串
            services.AddDbContext<AppDbContext>((sp, options) =>
            {
                var currentTenant = sp.GetRequiredService<ICurrentTenant>();
                var connectionString = currentTenant.Tenant?.ConnectionString
                    ?? "Server=localhost;Database=DefaultDb;User Id=sa;Password=YourPassword;TrustServerCertificate=True";

                options.UseSqlServer(connectionString);
            });
        }

        /// <summary>
        /// 示例 2: 使用配置文件 + 子域名识别（生产环境）
        /// </summary>
        public void ConfigureServicesExample2(IServiceCollection services)
        {
            // appsettings.json 配置格式:
            // {
            //   "Tenants": [
            //     { "Id": "company1", "Name": "公司1", "ConnectionString": "..." },
            //     { "Id": "company2", "Name": "公司2", "ConnectionString": "..." }
            //   ]
            // }

            services.AddMultiTenancyWithConfiguration(options =>
            {
                // 使用子域名识别租户
                options.ResolutionStrategy = TenantResolutionStrategy.Subdomain;
                options.RootDomain = "myapp.com"; // company1.myapp.com -> company1

                options.IsolationStrategy = TenantIsolationStrategy.Database;
            });

            services.AddDbContext<AppDbContext>((sp, options) =>
            {
                var currentTenant = sp.GetRequiredService<ICurrentTenant>();
                var connectionString = currentTenant.Tenant?.ConnectionString
                    ?? throw new InvalidOperationException("未找到租户连接字符串");

                options.UseSqlServer(connectionString);
            });
        }

        /// <summary>
        /// 示例 3: 鉴别器模式 - 单数据库多租户
        /// </summary>
        public void ConfigureServicesExample3(IServiceCollection services)
        {
            services.AddMultiTenancyWithInMemory(
                options =>
                {
                    // 组合识别策略: Header 优先，回退到 QueryString
                    options.ResolutionStrategy =
                        TenantResolutionStrategy.Header |
                        TenantResolutionStrategy.QueryString;

                    // 使用鉴别器模式（共享数据库）
                    options.IsolationStrategy = TenantIsolationStrategy.Discriminator;
                    options.TenantIdColumnName = "TenantId";
                },
                provider =>
                {
                    // 所有租户使用同一个数据库
                    provider.AddTenant(new TenantInfo("tenant1", "租户1", "SharedConnection"));
                    provider.AddTenant(new TenantInfo("tenant2", "租户2", "SharedConnection"));
                });

            // 共享数据库配置
            services.AddDbContext<AppDbContext>((sp, options) =>
            {
                // 所有租户使用同一连接字符串
                options.UseSqlServer("Server=localhost;Database=SharedDb;...");
            });
        }

        /// <summary>
        /// 配置中间件管道
        /// </summary>
        public void Configure(IApplicationBuilder app, IHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            // ⚠️ 重要: 多租户中间件必须在路由之前
            app.UseMultiTenancy();

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }

    // ==================== DbContext 示例 ====================

    /// <summary>
    /// 数据库隔离模式的 DbContext
    /// </summary>
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<Product> Products { get; set; }
        public DbSet<Order> Orders { get; set; }
    }

    /// <summary>
    /// 鉴别器模式的 DbContext（推荐）
    /// </summary>
    public class MultiTenantDbContext : DbContext
    {
        private readonly ICurrentTenant _currentTenant;

        public MultiTenantDbContext(
            DbContextOptions<MultiTenantDbContext> options,
            ICurrentTenant currentTenant)
            : base(options)
        {
            _currentTenant = currentTenant;
        }

        public DbSet<Product> Products { get; set; }
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 配置多租户全局查询过滤器
            modelBuilder.ConfigureTenantDiscriminator(_currentTenant);

            // 其他配置...
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);

            // 添加多租户拦截器（自动设置 TenantId）
            if (_currentTenant != null)
            {
                optionsBuilder.AddInterceptors(
                    new CrestCreates.Infrastructure.EntityFrameworkCore.Interceptors.MultiTenancyInterceptor(_currentTenant));
            }
        }
    }

    // ==================== 实体示例 ====================

    /// <summary>
    /// 多租户实体 - 继承基类
    /// </summary>
    public class Product : MultiTenantEntity<Guid>
    {
        public string Name { get; set; }
        public decimal Price { get; set; }
        public string Description { get; set; }
        // TenantId 在基类中已定义
    }

    /// <summary>
    /// 多租户实体 - 实现接口
    /// </summary>
    public class Order : IMultiTenant
    {
        public Guid Id { get; set; }
        public string TenantId { get; set; } // 必须实现
        public string OrderNumber { get; set; }
        public DateTime OrderDate { get; set; }
        public decimal TotalAmount { get; set; }
    }

    // ==================== Controller 示例 ====================

    [ApiController]
    [Route("api/[controller]")]
    public class ProductsController : ControllerBase
    {
        private readonly MultiTenantDbContext _dbContext;
        private readonly ICurrentTenant _currentTenant;

        public ProductsController(
            MultiTenantDbContext dbContext,
            ICurrentTenant currentTenant)
        {
            _dbContext = dbContext;
            _currentTenant = currentTenant;
        }

        /// <summary>
        /// 获取当前租户的所有产品
        /// GET /api/products
        /// Header: X-Tenant-Id: tenant1
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetProducts()
        {
            // 自动过滤当前租户的数据
            var products = await _dbContext.Products.ToListAsync();

            return Ok(new
            {
                TenantId = _currentTenant.Id,
                TenantName = _currentTenant.Tenant?.Name,
                Count = products.Count,
                Products = products
            });
        }

        /// <summary>
        /// 创建产品
        /// POST /api/products
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateProduct([FromBody] CreateProductDto dto)
        {
            var product = new Product
            {
                Id = Guid.NewGuid(),
                Name = dto.Name,
                Price = dto.Price,
                Description = dto.Description
                // TenantId 会由拦截器自动设置
            };

            _dbContext.Products.Add(product);
            await _dbContext.SaveChangesAsync();

            return CreatedAtAction(nameof(GetProducts), new { id = product.Id }, product);
        }

        /// <summary>
        /// 获取租户信息
        /// GET /api/products/tenant-info
        /// </summary>
        [HttpGet("tenant-info")]
        public IActionResult GetTenantInfo()
        {
            if (_currentTenant.Tenant == null)
            {
                return BadRequest("未找到租户信息");
            }

            return Ok(new
            {
                Id = _currentTenant.Id,
                Name = _currentTenant.Tenant.Name,
                ConnectionString = _currentTenant.Tenant.ConnectionString?.Substring(0, 30) + "..." // 仅显示部分
            });
        }
    }

    // ==================== DTO ====================

    public class CreateProductDto
    {
        public string Name { get; set; }
        public decimal Price { get; set; }
        public string Description { get; set; }
    }
}
