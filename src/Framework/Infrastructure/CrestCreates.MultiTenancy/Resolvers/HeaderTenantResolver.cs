using System;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.MultiTenancy.Resolvers
{
    public class HeaderTenantResolver : ITenantResolver
    {
        private readonly MultiTenancyOptions _options;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TenantIdentifierNormalizer _normalizer;
        private readonly ILogger<HeaderTenantResolver> _logger;

        public HeaderTenantResolver(
            IOptions<MultiTenancyOptions> options,
            IServiceScopeFactory scopeFactory,
            TenantIdentifierNormalizer normalizer,
            ILogger<HeaderTenantResolver> logger)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _normalizer = normalizer;
            _logger = logger;
        }

        public async Task<TenantResolutionResult> ResolveAsync(HttpContext httpContext)
        {
            if (httpContext?.Request?.Headers == null)
            {
                return TenantResolutionResult.NotResolved("Header");
            }

            var headerName = _options.TenantHeaderName ?? "X-Tenant-Id";
            string? rawValue = null;

            if (httpContext.Request.Headers.TryGetValue(headerName, out var tenantId))
            {
                rawValue = tenantId.FirstOrDefault();
            }

            if (string.IsNullOrWhiteSpace(rawValue))
            {
                foreach (var fallbackHeader in new[] { "X-Tenant-Id", "TenantId", "Tenant" })
                {
                    if (httpContext.Request.Headers.TryGetValue(fallbackHeader, out var fallbackTenantId))
                    {
                        rawValue = fallbackTenantId.FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(rawValue)) break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return TenantResolutionResult.NotResolved("Header");
            }

            var tenant = await ResolveTenantAsync(rawValue, httpContext.RequestAborted);

            if (tenant == null)
            {
                return TenantResolutionResult.NotFound("Header");
            }

            if (!tenant.IsActive || tenant.LifecycleState == Domain.Permission.TenantLifecycleState.Archived || tenant.LifecycleState == Domain.Permission.TenantLifecycleState.Deleted)
            {
                return TenantResolutionResult.Inactive("Header");
            }

            return TenantResolutionResult.Success(tenant.Id.ToString(), tenant.Name, tenant.GetDefaultConnectionString(), "Header");
        }

        private async Task<Domain.Permission.Tenant?> ResolveTenantAsync(string rawValue, CancellationToken cancellationToken)
        {
            // The tenant directory is host-owned lookup data. Resolving its repository from the
            // request scope would construct the business DbContext before the tenant frame exists,
            // pinning the host connection for the rest of the request. A dedicated scope keeps the
            // directory read and the tenant-selected business context independent.
            using var scope = _scopeFactory.CreateScope();
            var tenantRepository = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
            var normalizedValue = rawValue.Trim();

            if (Guid.TryParse(normalizedValue, out var tenantId))
            {
                return await tenantRepository.FindByIdAsync(tenantId, cancellationToken);
            }

            var normalizedName = _normalizer.NormalizeName(normalizedValue);
            return await tenantRepository.FindByNameAsync(normalizedName, cancellationToken);
        }
    }
}
