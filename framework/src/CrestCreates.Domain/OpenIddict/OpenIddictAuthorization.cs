using OpenIddict.EntityFrameworkCore.Models;

namespace CrestCreates.Domain.OpenIddict;

public class OpenIddictAuthorization : OpenIddictEntityFrameworkCoreAuthorization<long, OpenIddictApplication, OpenIddictToken>
{
}
