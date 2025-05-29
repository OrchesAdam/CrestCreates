using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using CrestCreates.Data.Repository;
using CrestCreates.Data.UnitOfWork;
using CrestCreates.Data.Context;
using CrestCreates.Repositories.EFCore;

namespace CrestCreates.Repositories.EFCore.Example
{    /// <summary>
    /// 使用示例
    /// </summary>
    public class Program
    {        public static async Task Main(string[] args)
        {
            // 创建服务集合
            var services = new ServiceCollection();
            
            // 配置 EF Core 服务
            services.AddDbContext<ExampleDbContext>(options =>
                options.UseInMemoryDatabase("ExampleDb"));
              // 注册 EF Core 适配器 - 使用 DbContextAdapter
            services.AddScoped<IDbContext>(provider =>
            {
                var dbContext = provider.GetRequiredService<ExampleDbContext>();
                return new DbContextAdapter(dbContext);
            });
            
            // 注册仓储和工作单元
            services.AddScoped<IRepository<User>, EFCoreRepository<User>>();
            services.AddScoped<IUnitOfWork, EFCoreUnitOfWork>();
            
            // 注册业务服务
            services.AddScoped<IUserService, UserService>();

            // 构建服务提供者
            var serviceProvider = services.BuildServiceProvider();

            // 获取服务并运行示例
            using var scope = serviceProvider.CreateScope();
            var userService = scope.ServiceProvider.GetRequiredService<IUserService>();

            try
            {
                await RunExampleAsync(userService);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"运行示例时发生错误: {ex.Message}");
            }}

        private static async Task RunExampleAsync(IUserService userService)
        {
            Console.WriteLine("开始 CrestCreates.Repositories.EFCore 示例");

            // 1. 创建用户
            Console.WriteLine("创建用户...");
            var user1 = await userService.CreateUserAsync("张三", "zhangsan@example.com");
            var user2 = await userService.CreateUserAsync("李四", "lisi@example.com");
            var user3 = await userService.CreateUserAsync("王五", "wangwu@example.com");

            Console.WriteLine($"创建了 3 个用户: {user1.Name}, {user2.Name}, {user3.Name}");

            // 2. 查询用户
            Console.WriteLine("查询用户...");
            var foundUser = await userService.GetUserByEmailAsync("zhangsan@example.com");
            if (foundUser != null)
            {
                Console.WriteLine($"找到用户: {foundUser.Name} ({foundUser.Email})");
            }

            // 3. 分页查询
            Console.WriteLine("分页查询用户...");
            var pagedResult = await userService.GetUsersPagedAsync(0, 2);
            Console.WriteLine($"分页结果: 第 {pagedResult.PageIndex + 1} 页, 共 {pagedResult.TotalPages} 页, 总计 {pagedResult.TotalCount} 个用户");
            
            foreach (var user in pagedResult.Items)
            {
                Console.WriteLine($"- {user.Name} ({user.Email})");
            }

            // 4. 更新用户
            Console.WriteLine("更新用户...");            if (foundUser != null)
            {
                await userService.UpdateUserAsync(foundUser.Id, "张三（已更新）", foundUser.Email);
                Console.WriteLine($"用户 {foundUser.Id} 已更新");
            }

            // 5. 统计用户数量
            Console.WriteLine("统计用户数量...");
            var userCount = await userService.GetUserCountAsync();
            Console.WriteLine($"当前用户总数: {userCount}");

            // 6. 删除用户
            Console.WriteLine("删除用户...");
            if (user3 != null)
            {
                await userService.DeleteUserAsync(user3.Id);
                Console.WriteLine($"用户 {user3.Name} 已删除");
            }

            // 7. 验证删除
            var finalCount = await userService.GetUserCountAsync();
            Console.WriteLine($"删除后用户总数: {finalCount}");

            Console.WriteLine("示例完成！");
        }
    }    // 示例实体
    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // 示例 DbContext
    public class ExampleDbContext : DbContext
    {
        public ExampleDbContext(DbContextOptions<ExampleDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; } = null!;
    }

    // 业务服务接口
    public interface IUserService
    {
        Task<User> CreateUserAsync(string name, string email);
        Task<User?> GetUserByEmailAsync(string email);
        Task<IPagedResult<User>> GetUsersPagedAsync(int pageIndex, int pageSize);
        Task<User> UpdateUserAsync(int id, string name, string email);
        Task<bool> DeleteUserAsync(int id);
        Task<int> GetUserCountAsync();
    }    // 业务服务实现
    public class UserService : IUserService
    {
        private readonly IUnitOfWork _unitOfWork;

        public UserService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<User> CreateUserAsync(string name, string email)
        {
            Console.WriteLine($"创建用户: {name} ({email})");
            
            var repository = _unitOfWork.GetRepository<User>();
            
            var user = new User
            {
                Name = name,
                Email = email,
                CreatedAt = DateTime.UtcNow
            };

            await repository.AddAsync(user);
            await _unitOfWork.CommitAsync();

            Console.WriteLine($"用户创建成功，ID: {user.Id}");
            return user;
        }

        public async Task<User?> GetUserByEmailAsync(string email)
        {
            Console.WriteLine($"根据邮箱查询用户: {email}");
            
            var repository = _unitOfWork.GetRepository<User>();
            var users = await repository.GetAsync(u => u.Email == email);
            
            return users.FirstOrDefault();
        }

        public async Task<IPagedResult<User>> GetUsersPagedAsync(int pageIndex, int pageSize)
        {
            Console.WriteLine($"分页查询用户: 页索引 {pageIndex}, 页大小 {pageSize}");
            
            var repository = _unitOfWork.GetRepository<User>();
            
            return await repository.GetPagedAsync(
                pageIndex: pageIndex,
                pageSize: pageSize,
                orderBy: query => query.OrderBy(u => u.CreatedAt)
            );
        }

        public async Task<User> UpdateUserAsync(int id, string name, string email)
        {
            Console.WriteLine($"更新用户: ID {id}");
            
            var repository = _unitOfWork.GetRepository<User>();
            var user = await repository.GetByIdAsync(id);
            
            if (user == null)
            {
                throw new InvalidOperationException($"用户 {id} 不存在");
            }

            user.Name = name;
            user.Email = email;
            user.UpdatedAt = DateTime.UtcNow;

            await repository.UpdateAsync(user);
            await _unitOfWork.CommitAsync();

            Console.WriteLine($"用户 {id} 更新成功");
            return user;
        }

        public async Task<bool> DeleteUserAsync(int id)
        {
            Console.WriteLine($"删除用户: ID {id}");
            
            var repository = _unitOfWork.GetRepository<User>();
            var result = await repository.DeleteAsync(id);
            
            if (result)
            {
                await _unitOfWork.CommitAsync();
                Console.WriteLine($"用户 {id} 删除成功");
            }
            
            return result;
        }

        public async Task<int> GetUserCountAsync()
        {
            Console.WriteLine("统计用户总数");
            
            var repository = _unitOfWork.GetRepository<User>();
            return await repository.CountAsync();
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
