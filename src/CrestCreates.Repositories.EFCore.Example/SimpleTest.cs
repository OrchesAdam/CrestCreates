using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using CrestCreates.Data.Repository;
using CrestCreates.Data.UnitOfWork;
using CrestCreates.Data.Context;
using CrestCreates.Repositories.EFCore;

namespace EFCoreTest
{
    /// <summary>
    /// 简单的EF Core测试程序
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== EF Core 仓储模式测试 ===");
            
            try
            {                // 创建服务集合
                var services = new ServiceCollection();
                
                // 注册 EF Core 服务 - 直接使用 EFCoreDbContext
                services.AddDbContext<EFCoreDbContext>(options =>
                    options.UseInMemoryDatabase("TestDb"));

                // 注册仓储和工作单元
                services.AddScoped<IRepository<TestUser>, EFCoreRepository<TestUser>>();
                services.AddScoped<IUnitOfWork, EFCoreUnitOfWork>();

                // 构建服务提供者
                var serviceProvider = services.BuildServiceProvider();

                // 执行测试
                await RunTests(serviceProvider);

                Console.WriteLine("\n✓ 所有测试已完成！");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ 测试失败: {ex.Message}");
                Console.WriteLine($"详细信息: {ex}");
            }
        }

        private static async Task RunTests(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<TestUser>>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            Console.WriteLine("1. 测试数据库连接...");
            var dbContext = scope.ServiceProvider.GetRequiredService<IDbContext>();
            var canConnect = await dbContext.CanConnectAsync();
            Console.WriteLine($"   数据库连接: {(canConnect ? "成功" : "失败")}");

            Console.WriteLine("\n2. 测试添加用户...");
            var user = new TestUser
            {
                Name = "张三",
                Email = "zhangsan@example.com", 
                CreatedAt = DateTime.Now
            };

            await repository.AddAsync(user);
            await unitOfWork.CommitAsync();
            Console.WriteLine($"   ✓ 用户已添加: {user.Name} (ID: {user.Id})");

            Console.WriteLine("\n3. 测试查询用户...");
            var foundUser = await repository.GetByIdAsync(user.Id);
            if (foundUser != null)
            {
                Console.WriteLine($"   ✓ 用户查询成功: {foundUser.Name} - {foundUser.Email}");
            }
            else
            {
                Console.WriteLine("   ❌ 用户查询失败");
            }

            Console.WriteLine("\n4. 测试更新用户...");
            if (foundUser != null)
            {
                foundUser.Email = "zhangsan.updated@example.com";
                await repository.UpdateAsync(foundUser);
                await unitOfWork.CommitAsync();
                Console.WriteLine($"   ✓ 用户已更新: {foundUser.Email}");
            }

            Console.WriteLine("\n5. 测试统计功能...");
            var count = await repository.CountAsync();
            Console.WriteLine($"   ✓ 用户总数: {count}");

            Console.WriteLine("\n6. 测试删除用户...");
            if (foundUser != null)
            {
                await repository.DeleteAsync(foundUser);
                await unitOfWork.CommitAsync();
                Console.WriteLine("   ✓ 用户已删除");

                var finalCount = await repository.CountAsync();
                Console.WriteLine($"   ✓ 删除后用户总数: {finalCount}");
            }
        }
    }

    // 测试实体
    public class TestUser
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }    // 测试数据库上下文 - 继承自 EFCoreDbContext
    public class TestEFCoreDbContext : EFCoreDbContext
    {
        public TestEFCoreDbContext(DbContextOptions<TestEFCoreDbContext> options) 
            : base(ConvertOptions(options))
        {
        }

        public DbSet<TestUser> Users { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            modelBuilder.Entity<TestUser>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Email).IsRequired().HasMaxLength(200);
                entity.Property(e => e.CreatedAt).IsRequired();
            });
        }

        private static DbContextOptions<EFCoreDbContext> ConvertOptions(DbContextOptions<TestEFCoreDbContext> options)
        {
            var builder = new DbContextOptionsBuilder<EFCoreDbContext>();
            
            // 复制所有扩展
            foreach (var extension in options.Extensions)
            {
                builder.Options.WithExtension(extension);
            }
            
            return builder.Options;
        }
    }

    // DbContext适配器
    public class DbContextAdapter : IDbContext
    {
        private readonly DbContext _dbContext;

        public DbContextAdapter(DbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public IDbSet<TEntity> Set<TEntity>() where TEntity : class
        {
            return new EFCoreDbSet<TEntity>(_dbContext.Set<TEntity>());
        }

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public int SaveChanges()
        {
            return _dbContext.SaveChanges();
        }

        public async Task<IDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            return new EFCoreTransaction(transaction);
        }

        public IDbTransaction? CurrentTransaction 
        { 
            get 
            { 
                var efTransaction = _dbContext.Database.CurrentTransaction;
                return efTransaction != null ? new EFCoreTransaction(efTransaction) : null;
            }
        }

        public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext.Database.CanConnectAsync(cancellationToken);
        }

        public async Task<T[]> ExecuteQueryAsync<T>(string sql, object[]? parameters = null, CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            throw new NotImplementedException("原始SQL查询在此测试适配器中未实现");
        }

        public async Task<int> ExecuteCommandAsync(string sql, object[]? parameters = null, CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            throw new NotImplementedException("原始SQL命令在此测试适配器中未实现");
        }

        public void Dispose()
        {
            _dbContext?.Dispose();
        }
    }
}
