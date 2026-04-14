using System;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.MultiTenancy.Resolvers
{
    public class HeaderTenantResolver : ITenantResolver
    {
        private readonly MultiTenancyOptions _options;
        private readonly ITenantRepository _tenantRepository;
        private readonly TenantIdentifierNormalizer _normalizer;
        private readonly ILogger<HeaderTenantResolver> _logger;

        public HeaderTenantResolver(
            IOptions<MultiTenancyOptions> options,
            ITenantRepository tenantRepository,
            TenantIdentifierNormalizer normalizer,
            ILogger<HeaderTenantResolver> logger)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _tenantRepository = tenantRepository;
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
            var normalizedValue = rawValue.Trim();

            if (Guid.TryParse(normalizedValue, out var tenantId))
            {
                return await _tenantRepository.FindByIdAsync(tenantId, cancellationToken);
            }

            var normalizedName = _normalizer.NormalizeName(normalizedValue);
            return await _tenantRepository.FindByNameAsync(normalizedName, cancellationToken);
        }
    }
}
