using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using CrestCreates.OrmProviders.EFCore.DbContexts;

namespace CrestCreates.IntegrationTests
{
    public class WebApplicationFactory<TStartup> : WebApplicationFactory<TStartup>, IAsyncLifetime where TStartup : class
    {
        protected override IHostBuilder CreateHostBuilder()
        {
            var builder = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<TStartup>();
                });

            return builder;
        }
        
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // 替换数据库上下文为SQLite内存数据库
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == 
                        typeof(DbContextOptions<CrestCreatesDbContext>));

                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<CrestCreatesDbContext>(options =>
                {
                    options.UseSqlite("Data Source=:memory:");
                });
                
                // 确保数据库创建和迁移
                var sp = services.BuildServiceProvider();
                using (var scope = sp.CreateScope())
                {
                    var scopedServices = scope.ServiceProvider;
                    var db = scopedServices.GetRequiredService<CrestCreatesDbContext>();
                    
                    db.Database.OpenConnection();
                    db.Database.EnsureCreated();
                }
            });
        }
        
        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }
    }
}
