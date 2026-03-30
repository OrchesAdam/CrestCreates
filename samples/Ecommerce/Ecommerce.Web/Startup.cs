using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Ecommerce.Infrastructure.DbContexts;
using Ecommerce.Domain.Repositories;
using Ecommerce.Infrastructure.Repositories;
using Ecommerce.Application.Contracts.Interfaces;
using Ecommerce.Application.Services;
using AutoMapper;
using Ecommerce.Application.Profiles;
using CrestCreates.Infrastructure.Logging;
using CrestCreates.Infrastructure.Caching;
using CrestCreates.Infrastructure.EventBus;

namespace Ecommerce.Web
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            // 添加MVC
            services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.PropertyNamingPolicy = null;
                });

            // Swagger
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "Ecommerce API", Version = "v1" });
            });

            // 添加数据库上下文
            services.AddDbContext<EcommerceDbContext>(options =>
                options.UseSqlServer(Configuration.GetConnectionString("sqlserver")));

            // 添加MediatR
            services.AddMediatR(typeof(Startup).Assembly);

            // 添加事件总线
            services.AddEventBus();

            // 添加缓存
            services.AddCaching(config =>
            {
                config.Provider = "memory";
                config.DefaultExpiration = TimeSpan.FromMinutes(30);
            });

            // 添加日志
            services.AddSerilogLogging(config =>
            {
                config.MinimumLevel = Microsoft.Extensions.Logging.LogLevel.Information;
                config.EnableConsole = true;
                config.EnableFile = true;
                config.FilePath = "logs/log-.txt";
            });

            // 添加AutoMapper
            services.AddAutoMapper(typeof(ProductMappingProfile));

            // 注册服务
            services.AddScoped<IProductRepository, ProductRepository>();
            services.AddScoped<IProductService, ProductService>();
            services.AddScoped<CrestCreates.Domain.UnitOfWork.IUnitOfWork, CrestCreates.OrmProviders.EFCore.UnitOfWork.EfCoreUnitOfWork>();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Ecommerce API v1"));
            }

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}