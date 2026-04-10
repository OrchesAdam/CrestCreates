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
    public class SubdomainTenantResolver : ITenantResolver
    {
        private readonly MultiTenancyOptions _options;
        private readonly ITenantRepository _tenantRepository;
        private readonly ITenantDomainMappingRepository _domainMappingRepository;
        private readonly TenantIdentifierNormalizer _normalizer;
        private readonly ILogger<SubdomainTenantResolver> _logger;

        public SubdomainTenantResolver(
            IOptions<MultiTenancyOptions> options,
            ITenantRepository tenantRepository,
            ITenantDomainMappingRepository domainMappingRepository,
            TenantIdentifierNormalizer normalizer,
            ILogger<SubdomainTenantResolver> logger)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _tenantRepository = tenantRepository;
            _domainMappingRepository = domainMappingRepository;
            _normalizer = normalizer;
            _logger = logger;
        }

        public async Task<TenantResolutionResult> ResolveAsync(HttpContext httpContext)
        {
            if (httpContext?.Request?.Host == null)
            {
                return TenantResolutionResult.NotResolved("Subdomain");
            }

            var host = httpContext.Request.Host.Host;
            if (string.IsNullOrEmpty(host))
            {
                return TenantResolutionResult.NotResolved("Subdomain");
            }

            var normalizedDomain = _normalizer.NormalizeDomain(host);
            if (normalizedDomain == null)
            {
                return TenantResolutionResult.NotResolved("Subdomain");
            }

            var domainMapping = await _domainMappingRepository.FindByDomainAsync(normalizedDomain, httpContext.RequestAborted);
            if (domainMapping != null && domainMapping.IsActive)
            {
                var tenant = await _tenantRepository.GetAsync(domainMapping.TenantId, httpContext.RequestAborted);
                if (tenant != null && tenant.IsActive && tenant.LifecycleState == Domain.Permission.TenantLifecycleState.Active)
                {
                    return TenantResolutionResult.Success(tenant.Id.ToString(), tenant.Name, tenant.GetDefaultConnectionString(), "Subdomain");
                }

                return tenant == null ? TenantResolutionResult.NotFound("Subdomain") : TenantResolutionResult.Inactive("Subdomain");
            }

            var subdomain = _normalizer.ExtractSubdomain(host);
            if (!string.IsNullOrEmpty(subdomain) && !IsReservedSubdomain(subdomain))
            {
                var normalizedName = _normalizer.NormalizeName(subdomain);
                var tenant = await _tenantRepository.FindByNameAsync(normalizedName, httpContext.RequestAborted);

                if (tenant != null)
                {
                    if (!tenant.IsActive || tenant.LifecycleState != Domain.Permission.TenantLifecycleState.Active)
                    {
                        return TenantResolutionResult.Inactive("Subdomain");
                    }

                    return TenantResolutionResult.Success(tenant.Id.ToString(), tenant.Name, tenant.GetDefaultConnectionString(), "Subdomain");
                }
            }

            return TenantResolutionResult.NotResolved("Subdomain");
        }

        private bool IsReservedSubdomain(string subdomain)
        {
            var reserved = new[]
            {
                "www", "api", "admin", "app", "cdn", "static",
                "mail", "smtp", "ftp", "dev", "test", "staging"
            };

            return reserved.Contains(subdomain, StringComparer.OrdinalIgnoreCase);
        }
    }
}
