using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using MediatR;
using CrestCreates.Domain.MultiTenancy;
using CrestCreates.Infrastructure.Localization;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Local;
using CrestCreates.Infrastructure.UnitOfWork;
using CrestCreates.Infrastructure.Logging;
using CrestCreates.OrmProviders.EFCore.DbContexts;
using CrestCreates.Web.Middlewares;

namespace CrestCreates.Web
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
            // 添加Serilog日志
            services.AddSerilogLogging(config =>
            {
                config.MinimumLevel = Microsoft.Extensions.Logging.LogLevel.Information;
                config.EnableConsole = true;
                config.EnableFile = true;
                config.FilePath = "logs/log-.txt";
            });

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
                c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "CrestCreates API", Version = "v1" });
            });

            // 添加DbContext
            services.AddDbContext<CrestCreatesDbContext>(options =>
                options.UseSqlServer(Configuration.GetConnectionString("Default")));

            // 添加MediatR
            services.AddMediatR(typeof(Startup).Assembly);

            // 添加事件总线
            services.AddScoped<CrestCreates.EventBus.Abstract.IEventBus, CrestCreates.EventBus.Local.LocalEventBus>();
            services.AddScoped<CrestCreates.Domain.DomainEvents.IDomainEventPublisher, CrestCreates.EventBus.Local.DomainEventPublisher>();

            // 添加工作单元
            services.AddUnitOfWork(OrmProvider.EfCore);

            // 添加多租户支持
            services.AddSingleton<ICurrentTenant, CurrentTenant>();

            // 添加本地化
            services.AddScoped<ILocalizationProvider, JsonResourceLocalizationProvider>(sp => 
                new JsonResourceLocalizationProvider("Localization/Resources"));            // 添加缓存系统
            services.AddCaching(config =>
            {
                config.Provider = "memory"; // 可以切换为 "redis"
                config.DefaultExpiration = TimeSpan.FromMinutes(30);
            });
            
            // 添加Automapper
            services.AddAutoMapper(typeof(Startup).Assembly);

            // TODO: 添加生成的服务（需要实现）
            // services.AddGeneratedServices();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "CrestCreates API v1"));
            }

            // 使用异常处理中间件
            app.UseExceptionHandling();

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
