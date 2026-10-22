using System;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.MultiTenancy.Resolvers
{
    public class QueryStringTenantResolver : ITenantResolver
    {
        private readonly MultiTenancyOptions _options;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TenantIdentifierNormalizer _normalizer;
        private readonly ILogger<QueryStringTenantResolver> _logger;

        public QueryStringTenantResolver(
            IOptions<MultiTenancyOptions> options,
            IServiceScopeFactory scopeFactory,
            TenantIdentifierNormalizer normalizer,
            ILogger<QueryStringTenantResolver> logger)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _normalizer = normalizer;
            _logger = logger;
        }

        public async Task<TenantResolutionResult> ResolveAsync(HttpContext httpContext)
        {
            if (httpContext?.Request?.Query == null)
            {
                return TenantResolutionResult.NotResolved("QueryString");
            }

            var queryParamName = _options.TenantQueryStringKey ?? "tenantId";

            if (httpContext.Request.Query.TryGetValue(queryParamName, out var tenantId))
            {
                var value = tenantId.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return await ResolveTenantAsync(value, "QueryString", httpContext.RequestAborted);
                }
            }

            return TenantResolutionResult.NotResolved("QueryString");
        }

        private async Task<TenantResolutionResult> ResolveTenantAsync(string rawValue, string resolvedBy, CancellationToken cancellationToken)
        {
            var normalizedName = _normalizer.NormalizeName(rawValue);
            using var scope = _scopeFactory.CreateScope();
            var tenantRepository = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
            var tenant = await tenantRepository.FindByNameAsync(normalizedName, cancellationToken);

            if (tenant == null) return TenantResolutionResult.NotFound(resolvedBy);
            if (!tenant.IsActive || tenant.LifecycleState != Domain.Permission.TenantLifecycleState.Active) return TenantResolutionResult.Inactive(resolvedBy);

            return TenantResolutionResult.Success(tenant.Id.ToString(), tenant.Name, tenant.GetDefaultConnectionString(), resolvedBy);
        }
    }

    public class CookieTenantResolver : ITenantResolver
    {
        private readonly MultiTenancyOptions _options;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TenantIdentifierNormalizer _normalizer;
        private readonly ILogger<CookieTenantResolver> _logger;

        public CookieTenantResolver(
            IOptions<MultiTenancyOptions> options,
            IServiceScopeFactory scopeFactory,
            TenantIdentifierNormalizer normalizer,
            ILogger<CookieTenantResolver> logger)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _normalizer = normalizer;
            _logger = logger;
        }

        public async Task<TenantResolutionResult> ResolveAsync(HttpContext httpContext)
        {
            if (httpContext?.Request?.Cookies == null)
            {
                return TenantResolutionResult.NotResolved("Cookie");
            }

            var cookieName = _options.TenantCookieName ?? "TenantId";

            if (httpContext.Request.Cookies.TryGetValue(cookieName, out var tenantId))
            {
                if (!string.IsNullOrWhiteSpace(tenantId))
                {
                    var normalizedName = _normalizer.NormalizeName(tenantId);
                    using var scope = _scopeFactory.CreateScope();
                    var tenantRepository = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
                    var tenant = await tenantRepository.FindByNameAsync(normalizedName, httpContext.RequestAborted);

                    if (tenant == null) return TenantResolutionResult.NotFound("Cookie");
                    if (!tenant.IsActive || tenant.LifecycleState != Domain.Permission.TenantLifecycleState.Active) return TenantResolutionResult.Inactive("Cookie");

                    return TenantResolutionResult.Success(tenant.Id.ToString(), tenant.Name, tenant.GetDefaultConnectionString(), "Cookie");
                }
            }

            return TenantResolutionResult.NotResolved("Cookie");
        }
    }

    public class RouteTenantResolver : ITenantResolver
    {
        private readonly MultiTenancyOptions _options;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TenantIdentifierNormalizer _normalizer;
        private readonly ILogger<RouteTenantResolver> _logger;

        public RouteTenantResolver(
            IOptions<MultiTenancyOptions> options,
            IServiceScopeFactory scopeFactory,
            TenantIdentifierNormalizer normalizer,
            ILogger<RouteTenantResolver> logger)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _normalizer = normalizer;
            _logger = logger;
        }

        public async Task<TenantResolutionResult> ResolveAsync(HttpContext httpContext)
        {
            if (httpContext?.GetRouteData() == null)
            {
                return TenantResolutionResult.NotResolved("Route");
            }

            var routeKey = _options.TenantRouteKey ?? "tenantId";
            var routeData = httpContext.GetRouteData();

            if (routeData.Values.TryGetValue(routeKey, out var routeValue))
            {
                var value = routeValue?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    var normalizedName = _normalizer.NormalizeName(value);
                    using var scope = _scopeFactory.CreateScope();
                    var tenantRepository = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
                    var tenant = await tenantRepository.FindByNameAsync(normalizedName, httpContext.RequestAborted);

                    if (tenant == null) return TenantResolutionResult.NotFound("Route");
                    if (!tenant.IsActive || tenant.LifecycleState != Domain.Permission.TenantLifecycleState.Active) return TenantResolutionResult.Inactive("Route");

                    return TenantResolutionResult.Success(tenant.Id.ToString(), tenant.Name, tenant.GetDefaultConnectionString(), "Route");
                }
            }

            return TenantResolutionResult.NotResolved("Route");
        }
    }
}
