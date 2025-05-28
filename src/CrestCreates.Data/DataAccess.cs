using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.Data;

/// <summary>
/// 数据访问接口
/// </summary>
public interface IDataAccess
{
    Task<T?> GetByIdAsync<T>(int id) where T : class;
    Task SaveAsync<T>(T entity) where T : class;
    Task DeleteAsync<T>(T entity) where T : class;
}

/// <summary>
/// 数据访问实现
/// </summary>
public class DataAccess : IDataAccess
{
    private readonly DataDbContext _dbContext;

    public DataAccess(DataDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<T?> GetByIdAsync<T>(int id) where T : class
    {
        return await _dbContext.Set<T>().FindAsync(id);
    }

    public async Task SaveAsync<T>(T entity) where T : class
    {
        _dbContext.Set<T>().Add(entity);
        await _dbContext.SaveChangesAsync();
    }

    public async Task DeleteAsync<T>(T entity) where T : class
    {
        _dbContext.Set<T>().Remove(entity);
        await _dbContext.SaveChangesAsync();
    }
}

/// <summary>
/// 数据库上下文
/// </summary>
public class DataDbContext : DbContext
{
    public DataDbContext(DbContextOptions<DataDbContext> options) : base(options)
    {
    }

    // 定义实体集
    // public DbSet<YourEntity> YourEntities { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 配置实体映射
    }
}
