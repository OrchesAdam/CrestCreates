using System;
using System.Collections.Generic;
using System.Linq;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using CrestCreates.MultiTenancy.Middleware;
using CrestCreates.MultiTenancy.Providers;
using CrestCreates.MultiTenancy.Resolvers;

namespace CrestCreates.MultiTenancy
{
    /// <summary>
    /// 多租户服务注册扩展
    /// </summary>
    public static class MultiTenancyServiceCollectionExtensions
    {
        /// <summary>
        /// 添加多租户支持
        /// </summary>
        public static IServiceCollection AddMultiTenancy(
            this IServiceCollection services,
            Action<MultiTenancyOptions> configureOptions = null)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            // 注册配置选项
            if (configureOptions != null)
            {
                services.Configure(configureOptions);
            }
            else
            {
                services.Configure<MultiTenancyOptions>(options => { });
            }

            // 注册核心服务
            services.TryAddSingleton<ICurrentTenant, CurrentTenant>();
            services.TryAddSingleton<TenantIdentifierNormalizer>();

            return services;
        }

        /// <summary>
        /// 使用内存租户提供者
        /// </summary>
        public static IServiceCollection AddInMemoryTenantProvider(
            this IServiceCollection services,
            Action<InMemoryTenantProvider> configure = null)
        {
            var provider = new InMemoryTenantProvider(
                services.BuildServiceProvider().GetRequiredService<Microsoft.Extensions.Logging.ILogger<InMemoryTenantProvider>>());

            configure?.Invoke(provider);

            services.TryAddSingleton<ITenantProvider>(provider);
            return services;
        }

        /// <summary>
        /// 使用配置文件租户提供者
        /// </summary>
        public static IServiceCollection AddConfigurationTenantProvider(
            this IServiceCollection services)
        {
            services.TryAddSingleton<ITenantProvider, ConfigurationTenantProvider>();
            return services;
        }

        /// <summary>
        /// 使用基于仓储的租户提供者
        /// </summary>
        public static IServiceCollection AddRepositoryTenantProvider(
            this IServiceCollection services)
        {
            services.TryAddSingleton<ITenantProvider, RepositoryTenantProvider>();
            return services;
        }

        /// <summary>
        /// 使用自定义租户提供者
        /// </summary>
        public static IServiceCollection AddTenantProvider<TProvider>(
            this IServiceCollection services)
            where TProvider : class, ITenantProvider
        {
            services.TryAddSingleton<ITenantProvider, TProvider>();
            return services;
        }

        /// <summary>
        /// 添加租户解析器
        /// </summary>
        public static IServiceCollection AddTenantResolvers(
            this IServiceCollection services,
            TenantResolutionStrategy strategy)
        {
            var resolvers = new List<Type>();

            if (strategy.HasFlag(TenantResolutionStrategy.Header))
            {
                resolvers.Add(typeof(HeaderTenantResolver));
            }

            if (strategy.HasFlag(TenantResolutionStrategy.Subdomain))
            {
                resolvers.Add(typeof(SubdomainTenantResolver));
            }

            // 以下 Resolvers 需要额外的 ASP.NET Core 依赖,暂时注释掉
            // if (strategy.HasFlag(TenantResolutionStrategy.QueryString))
            // {
            //     resolvers.Add(typeof(QueryStringTenantResolver));
            // }

            // if (strategy.HasFlag(TenantResolutionStrategy.Cookie))
            // {
            //     resolvers.Add(typeof(CookieTenantResolver));
            // }

            // if (strategy.HasFlag(TenantResolutionStrategy.Route))
            // {
            //     resolvers.Add(typeof(RouteTenantResolver));
            // }

            // 注册各个解析器
            foreach (var resolverType in resolvers)
            {
                services.TryAddScoped(resolverType);
            }

            // 注册复合解析器
            services.TryAddScoped<ITenantResolver>(sp =>
            {
                var resolverInstances = resolvers
                    .Select(t => (ITenantResolver)sp.GetRequiredService(t))
                    .ToArray();

                return new CompositeTenantResolver(
                    resolverInstances,
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CompositeTenantResolver>>());
            });

            return services;
        }

        /// <summary>
        /// 添加多租户完整配置（快速配置）
        /// </summary>
        public static IServiceCollection AddMultiTenancyWithInMemory(
            this IServiceCollection services,
            Action<MultiTenancyOptions> configureOptions = null,
            Action<InMemoryTenantProvider> configureTenants = null)
        {
            services.AddMultiTenancy(configureOptions);

            // 从配置中获取策略，如果没有则使用默认的 Header
            var strategy = TenantResolutionStrategy.Header;
            using (var sp = services.BuildServiceProvider())
            {
                var options = sp.GetService<Microsoft.Extensions.Options.IOptions<MultiTenancyOptions>>();
                if (options?.Value != null)
                {
                    strategy = options.Value.ResolutionStrategy;
                }
            }

            services.AddTenantResolvers(strategy);
            services.AddInMemoryTenantProvider(configureTenants);

            return services;
        }

        /// <summary>
        /// 添加多租户配置文件支持（快速配置）
        /// </summary>
        public static IServiceCollection AddMultiTenancyWithConfiguration(
            this IServiceCollection services,
            Action<MultiTenancyOptions> configureOptions = null)
        {
            services.AddMultiTenancy(configureOptions);

            var strategy = TenantResolutionStrategy.Header;
            using (var sp = services.BuildServiceProvider())
            {
                var options = sp.GetService<Microsoft.Extensions.Options.IOptions<MultiTenancyOptions>>();
                if (options?.Value != null)
                {
                    strategy = options.Value.ResolutionStrategy;
                }
            }

            services.AddTenantResolvers(strategy);
            services.AddConfigurationTenantProvider();

            return services;
        }
    }

    /// <summary>
    /// 多租户应用程序构建器扩展
    /// </summary>
    public static class MultiTenancyApplicationBuilderExtensions
    {
        /// <summary>
        /// 使用多租户中间件
        /// </summary>
        public static IApplicationBuilder UseMultiTenancy(this IApplicationBuilder app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            return app.UseMiddleware<MultiTenancyMiddleware>();
        }

        /// <summary>
        /// 使用租户边界校验中间件
        /// </summary>
        public static IApplicationBuilder UseTenantBoundary(this IApplicationBuilder app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            return app.UseMiddleware<TenantBoundaryMiddleware>();
        }
    }
}
