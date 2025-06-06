using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using CrestCreates.Infrastructure.EntityFrameworkCore.DbContexts;
using CrestCreates.Infrastructure.MultiTenancy;
using CrestCreates.Domain.MultiTenancy;
using CrestCreates.Infrastructure.Localization;
using CrestCreates.Infrastructure.EntityFrameworkCore.MultiTenancy;
using CrestCreates.Infrastructure.EntityFrameworkCore.UnitOfWork;
using CrestCreates.Domain.UnitOfWork;

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

            // 添加工作单元
            services.AddScoped<IUnitOfWork, EfCoreUnitOfWork>();

            // 添加多租户支持
            services.AddSingleton<ICurrentTenant, CurrentTenant>();
            services.AddScoped<ITenantConnectionStringResolver, TenantConnectionStringResolver>();

            // 添加本地化
            services.AddScoped<ILocalizationProvider, JsonResourceLocalizationProvider>(sp => 
                new JsonResourceLocalizationProvider("Localization/Resources"));            // 添加Automapper
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
