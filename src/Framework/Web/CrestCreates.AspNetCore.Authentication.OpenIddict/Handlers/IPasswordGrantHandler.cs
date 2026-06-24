using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.AspNetCore.Authentication.OpenIddict.Handlers;

public interface IPasswordGrantHandler
{
    Task<PasswordGrantResult> HandleAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);
}
