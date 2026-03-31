using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CrestCreates.OrmProviders.EFCore.DbContexts;

namespace CrestCreates.Database.Migrations.Tests
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Testing database migration...");

            // 配置DbContext选项
            var options = new DbContextOptionsBuilder<CrestCreatesDbContext>()
                .UseSqlite("Data Source=:memory:", b => b.MigrationsAssembly("CrestCreates.Database.Migrations.Tests"))
                .Options;

            using (var context = new CrestCreatesDbContext(options))
            {
                try
                {
                    // 打开连接
                    await context.Database.OpenConnectionAsync();
                    Console.WriteLine("Connection opened successfully.");

                    // 执行数据库迁移
                    Console.WriteLine("Executing database migration...");
                    await context.Database.MigrateAsync();
                    Console.WriteLine("Migration executed successfully.");

                    // 验证数据库是否成功迁移
                    var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
                    Console.WriteLine($"Applied migrations: {string.Join(", ", appliedMigrations)}");

                    // 验证Products表是否存在
                    var result = await context.Database.ExecuteSqlRawAsync("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Products'");
                    var tableExists = result > 0;
                    Console.WriteLine($"Products table exists: {tableExists}");

                    Console.WriteLine("Database migration test completed successfully!");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
                finally
                {
                    // 关闭连接
                    await context.Database.CloseConnectionAsync();
                    Console.WriteLine("Connection closed.");
                }
            }

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}