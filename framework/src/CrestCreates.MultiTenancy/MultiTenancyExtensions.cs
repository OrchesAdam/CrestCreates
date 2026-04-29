using System;
using System.Collections.Generic;
using System.Linq;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.MultiTenancy.Middleware;
using CrestCreates.MultiTenancy.Providers;
using CrestCreates.MultiTenancy.Resolvers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
            services.TryAddSingleton<ITenantProvider>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<InMemoryTenantProvider>>();
                var provider = new InMemoryTenantProvider(logger);
                configure?.Invoke(provider);
                return provider;
            });
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
            var resolvers = BuildResolverList(strategy);

            foreach (var resolverType in resolvers)
            {
                services.TryAddScoped(resolverType);
            }

            services.TryAddScoped<ITenantResolver>(sp =>
            {
                var resolverInstances = resolvers
                    .Select(t => (ITenantResolver)sp.GetRequiredService(t))
                    .ToArray();

                return new CompositeTenantResolver(
                    resolverInstances,
                    sp.GetRequiredService<ILogger<CompositeTenantResolver>>());
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
            services.AddTenantResolversFromOptions();
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
            services.AddTenantResolversFromOptions();
            services.AddConfigurationTenantProvider();
            return services;
        }

        private static IServiceCollection AddTenantResolversFromOptions(this IServiceCollection services)
        {
            services.TryAddScoped<HeaderTenantResolver>();
            services.TryAddScoped<SubdomainTenantResolver>();
            services.TryAddScoped<QueryStringTenantResolver>();
            services.TryAddScoped<CookieTenantResolver>();
            services.TryAddScoped<RouteTenantResolver>();

            services.TryAddScoped<ITenantResolver>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<MultiTenancyOptions>>();
                var strategy = options.Value.ResolutionStrategy;
                var resolvers = BuildResolverList(strategy);
                var resolverInstances = resolvers
                    .Select(t => (ITenantResolver)sp.GetRequiredService(t))
                    .ToArray();
                return new CompositeTenantResolver(
                    resolverInstances,
                    sp.GetRequiredService<ILogger<CompositeTenantResolver>>());
            });

            return services;
        }

        private static List<Type> BuildResolverList(TenantResolutionStrategy strategy)
        {
            var resolvers = new List<Type>();
            if (strategy.HasFlag(TenantResolutionStrategy.Header))
                resolvers.Add(typeof(HeaderTenantResolver));
            if (strategy.HasFlag(TenantResolutionStrategy.Subdomain))
                resolvers.Add(typeof(SubdomainTenantResolver));
            if (strategy.HasFlag(TenantResolutionStrategy.QueryString))
                resolvers.Add(typeof(QueryStringTenantResolver));
            if (strategy.HasFlag(TenantResolutionStrategy.Cookie))
                resolvers.Add(typeof(CookieTenantResolver));
            if (strategy.HasFlag(TenantResolutionStrategy.Route))
                resolvers.Add(typeof(RouteTenantResolver));
            return resolvers;
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
