using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace CrestCreates.OrmProviders.EFCore.DbContexts
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
            optionsBuilder.UseSqlite("Data Source=:memory:");

            return new CrestCreatesDbContext(optionsBuilder.Options);
        }
    }
}