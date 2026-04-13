using OpenIddict.EntityFrameworkCore.Models;

namespace CrestCreates.Domain.OpenIddict;

public class OpenIddictToken : OpenIddictEntityFrameworkCoreToken<long, OpenIddictApplication, OpenIddictAuthorization>
{
}
