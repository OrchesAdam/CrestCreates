using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using CrestCreates.OrmProviders.EFCore.DbContexts;

namespace CrestCreates.Database.Migrations.Tests
{
    public class CrestCreatesDbContextFactory : IDesignTimeDbContextFactory<CrestCreatesDbContext>
    {
        public CrestCreatesDbContext CreateDbContext(string[] args)
        {
            // 创建配置构建器
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .Build();

            // 配置DbContext选项
            var optionsBuilder = new DbContextOptionsBuilder<CrestCreatesDbContext>();
            
            // 使用SQLite内存数据库进行设计时操作
            optionsBuilder.UseSqlite("Data Source=:memory:", b => b.MigrationsAssembly("CrestCreates.Database.Migrations.Tests"));

            return new CrestCreatesDbContext(optionsBuilder.Options);
        }
    }
}