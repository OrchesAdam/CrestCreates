using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using CrestCreates.Domain.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaaSHelpdesk.Domain.Entities;

namespace SaaSHelpdesk.Web.Auth;

public class CustomerApiKeyAuthenticationHandler : AuthenticationHandler<CustomerApiKeyOptions>
{
    private readonly ICrestRepositoryBase<Customer, Guid> _customerRepository;

    public CustomerApiKeyAuthenticationHandler(
        IOptionsMonitor<CustomerApiKeyOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ICrestRepositoryBase<Customer, Guid> customerRepository)
        : base(options, logger, encoder)
    {
        _customerRepository = customerRepository;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Customer-Key", out var key))
            return AuthenticateResult.NoResult();

        var customers = await _customerRepository.GetListAsync(c => c.ApiKey == key.ToString());
        var customer = customers.FirstOrDefault();

        if (customer == null || !customer.IsActive)
            return AuthenticateResult.Fail("Invalid or inactive API key");

        var claims = new[]
        {
            new Claim("CustomerId", customer.Id.ToString()),
            new Claim("CustomerName", customer.Name),
            new Claim("TenantId", customer.TenantId.ToString()),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);

        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}

public class CustomerApiKeyOptions : AuthenticationSchemeOptions { }
