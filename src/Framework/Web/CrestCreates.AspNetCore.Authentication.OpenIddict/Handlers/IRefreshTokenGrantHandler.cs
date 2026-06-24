using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.AspNetCore.Authentication.OpenIddict.Handlers;

public interface IRefreshTokenGrantHandler
{
    Task<RefreshTokenGrantResult> HandleAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);
}
