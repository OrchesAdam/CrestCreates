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
using CrestCreates.Logging.Extensions;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Local;
using CrestCreates.Infrastructure.UnitOfWork;
using CrestCreates.OrmProviders.EFCore.DbContexts;
using CrestCreates.OrmProviders.Abstract;
using CrestCreates.Web.Middlewares;
using CrestCreates.Domain.Shared;
using CrestCreates.Modularity;
using CrestCreates.Aop.Extensions;
using CrestCreates.AuditLogging.Middlewares;
using CrestCreates.AuditLogging.Options;
using Microsoft.Extensions.Options;

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
            services.AddCrestLogging(Configuration);
            services.Configure<AuditLoggingOptions>(Configuration.GetSection(AuditLoggingOptions.SectionName));

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

            app.UseCrestRequestLogging();
            app.UseExceptionHandling();
            app.UseAuditLogging();

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
