using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;
using FluentAssertions;
using CrestCreates.OrmProviders.EFCore.DbContexts;

namespace CrestCreates.Database.Migrations.Tests
{
    public class MigrationTests
    {
        [Fact]
        public async Task Database_Should_Migrate_Successfully()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<CrestCreatesDbContext>()
                .UseSqlite("Data Source=:memory:")
                .Options;

            // Act & Assert
            using (var context = new CrestCreatesDbContext(options))
            {
                // 打开连接
                await context.Database.OpenConnectionAsync();
                
                try
                {
                    // 执行数据库迁移
                    await context.Database.MigrateAsync();
                    
                    // 验证数据库是否成功迁移
                    var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
                    appliedMigrations.Should().NotBeEmpty();
                    
                    // 验证数据库连接是否正常
                    var canConnect = await context.Database.CanConnectAsync();
                    canConnect.Should().BeTrue();
                }
                finally
                {
                    // 关闭连接
                    await context.Database.CloseConnectionAsync();
                }
            }
        }

        [Fact]
        public async Task Database_Should_Have_Products_Table()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<CrestCreatesDbContext>()
                .UseSqlite("Data Source=:memory:")
                .Options;

            // Act
            using (var context = new CrestCreatesDbContext(options))
            {
                // 打开连接
                await context.Database.OpenConnectionAsync();
                
                try
                {
                    // 执行数据库迁移
                    await context.Database.MigrateAsync();
                    
                    // 验证Products表是否存在
                    var result = await context.Database.ExecuteSqlRawAsync("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Products'");
                    var tableExists = result > 0;
                    
                    // Assert
                    tableExists.Should().BeTrue();
                }
                finally
                {
                    // 关闭连接
                    await context.Database.CloseConnectionAsync();
                }
            }
        }
    }
}