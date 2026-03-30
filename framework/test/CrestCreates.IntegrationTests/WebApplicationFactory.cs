using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace CrestCreates.IntegrationTests
{
    public class WebApplicationFactory<TStartup> : WebApplicationFactory<TStartup>, IAsyncLifetime where TStartup : class
    {
        private readonly TestcontainersContainer _dbContainer;
        private readonly string _connectionString;
        
        public WebApplicationFactory()
        {
            _dbContainer = new TestcontainersBuilder<MsSqlTestcontainer>()
                .WithDatabase(new MsSqlTestcontainerConfiguration
                {
                    Password = "St1ongPassw0rd!",
                    Database = "CrestCreatesTestDb"
                })
                .Build();
                
            _connectionString = _dbContainer.ConnectionString;
        }
        
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
                // 替换数据库上下文为测试数据库
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == 
                        typeof(DbContextOptions<CrestCreatesDbContext>));

                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<CrestCreatesDbContext>(options =>
                {
                    options.UseSqlServer(_connectionString);
                });
                
                // 确保数据库创建和迁移
                var sp = services.BuildServiceProvider();
                using (var scope = sp.CreateScope())
                {
                    var scopedServices = scope.ServiceProvider;
                    var db = scopedServices.GetRequiredService<CrestCreatesDbContext>();
                    
                    db.Database.EnsureCreated();
                }
            });
        }
        
        public async Task InitializeAsync()
        {
            await _dbContainer.StartAsync();
        }

        public new async Task DisposeAsync()
        {
            await _dbContainer.StopAsync();
        }
    }
}
