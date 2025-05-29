using System;
using CrestCreates.Data.Context;
using CrestCreates.Data.Repository;
using CrestCreates.Data.UnitOfWork;

namespace CrestCreates.Data.Adapters
{    /// <summary>
    /// ORM适配器接口
    /// </summary>
    public interface IOrmAdapter : IDisposable
    {
        /// <summary>
        /// 适配器名称
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 支持的ORM类型
        /// </summary>
        OrmType OrmType { get; }

        /// <summary>
        /// 创建数据库上下文
        /// </summary>
        /// <param name="connectionString">连接字符串</param>
        /// <param name="options">配置选项</param>
        /// <returns>数据库上下文</returns>
        IDbContext CreateDbContext(string connectionString, DatabaseOptions? options = null);

        /// <summary>
        /// 创建仓储实例
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <param name="dbContext">数据库上下文</param>
        /// <returns>仓储实例</returns>
        IRepository<TEntity> CreateRepository<TEntity>(IDbContext dbContext) where TEntity : class;

        /// <summary>
        /// 创建工作单元实例
        /// </summary>
        /// <param name="dbContext">数据库上下文</param>
        /// <returns>工作单元实例</returns>
        IUnitOfWork CreateUnitOfWork(IDbContext dbContext);

        /// <summary>
        /// 检查是否支持指定的数据库类型
        /// </summary>
        /// <param name="databaseType">数据库类型</param>
        /// <returns>是否支持</returns>
        bool SupportsDatabase(DatabaseType databaseType);

        /// <summary>
        /// 初始化适配器
        /// </summary>
        /// <param name="options">配置选项</param>
        void Initialize(DatabaseOptions options);
    }

    /// <summary>
    /// ORM类型枚举
    /// </summary>
    public enum OrmType
    {
        /// <summary>
        /// Entity Framework Core
        /// </summary>
        EntityFrameworkCore,

        /// <summary>
        /// Dapper
        /// </summary>
        Dapper,

        /// <summary>
        /// FreeSql
        /// </summary>
        FreeSql,

        /// <summary>
        /// SqlSugar
        /// </summary>
        SqlSugar,

        /// <summary>
        /// MongoDB
        /// </summary>
        MongoDB,

        /// <summary>
        /// 自定义
        /// </summary>
        Custom
    }

    /// <summary>
    /// 数据库类型枚举
    /// </summary>
    public enum DatabaseType
    {
        /// <summary>
        /// SQL Server
        /// </summary>
        SqlServer,

        /// <summary>
        /// MySQL
        /// </summary>
        MySQL,

        /// <summary>
        /// PostgreSQL
        /// </summary>
        PostgreSQL,

        /// <summary>
        /// SQLite
        /// </summary>
        SQLite,

        /// <summary>
        /// Oracle
        /// </summary>
        Oracle,

        /// <summary>
        /// MongoDB
        /// </summary>
        MongoDB,

        /// <summary>
        /// 内存数据库
        /// </summary>
        InMemory
    }

    /// <summary>
    /// 数据库配置选项
    /// </summary>
    public class DatabaseOptions
    {
        /// <summary>
        /// 数据库类型
        /// </summary>
        public DatabaseType DatabaseType { get; set; } = DatabaseType.SqlServer;

        /// <summary>
        /// 连接字符串
        /// </summary>
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// 命令超时时间（秒）
        /// </summary>
        public int CommandTimeout { get; set; } = 30;

        /// <summary>
        /// 是否启用连接池
        /// </summary>
        public bool EnableConnectionPooling { get; set; } = true;

        /// <summary>
        /// 最大连接池大小
        /// </summary>
        public int MaxPoolSize { get; set; } = 100;

        /// <summary>
        /// 最小连接池大小
        /// </summary>
        public int MinPoolSize { get; set; } = 1;

        /// <summary>
        /// 是否启用敏感数据日志
        /// </summary>
        public bool EnableSensitiveDataLogging { get; set; } = false;

        /// <summary>
        /// 是否启用详细错误信息
        /// </summary>
        public bool EnableDetailedErrors { get; set; } = false;

        /// <summary>
        /// 是否自动迁移
        /// </summary>
        public bool AutoMigrate { get; set; } = false;

        /// <summary>
        /// 自定义配置
        /// </summary>
        public System.Collections.Generic.Dictionary<string, object> CustomOptions { get; set; } = 
            new System.Collections.Generic.Dictionary<string, object>();
    }
}
