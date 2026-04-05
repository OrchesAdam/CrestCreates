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
using CrestCreates.OrmProviders.Abstract;
using CrestCreates.Web.Middlewares;
using CrestCreates.Domain.Shared;
using CrestCreates.Modularity;
using CrestCreates.Aop.Extensions;

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
            services.AddSerilogLogging(config =>
            {
                config.MinimumLevel = Microsoft.Extensions.Logging.LogLevel.Information;
                config.EnableConsole = true;
                config.EnableFile = true;
                config.FilePath = "logs/log-.txt";
            });

            services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.PropertyNamingPolicy = null;
                });

            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo { Title = "CrestCreates API", Version = "v1" });
            });

            services.AddDbContext<CrestCreatesDbContext>(options =>
                options.UseSqlServer(Configuration.GetConnectionString("Default")));

            services.AddUnitOfWork(OrmProvider.EfCore);

            services.AddMediatR(typeof(Startup).Assembly);

            services.AddScoped<CrestCreates.EventBus.Abstract.IEventBus, CrestCreates.EventBus.Local.LocalEventBus>();
            services.AddScoped<CrestCreates.Domain.DomainEvents.IDomainEventPublisher, CrestCreates.EventBus.Local.DomainEventPublisher>();

            services.AddSingleton<ICurrentTenant, CurrentTenant>();

            services.AddScoped<ILocalizationProvider, JsonResourceLocalizationProvider>(sp =>
                new JsonResourceLocalizationProvider("Localization/Resources"));

            services.AddCaching(config =>
            {
                config.Provider = "memory";
                config.DefaultExpiration = TimeSpan.FromMinutes(30);
            });


            Console.WriteLine("=== Module Auto Registration Demo ===");
            Console.WriteLine("Modules discovered and registered:");
            foreach (var moduleName in ModuleAutoInitializer.RegisteredModules)
            {
                Console.WriteLine($"  - {moduleName}");
            }
            Console.WriteLine();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "CrestCreates API v1"));
            }

            app.UseExceptionHandling();

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
