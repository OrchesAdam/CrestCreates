using OpenIddict.EntityFrameworkCore.Models;

namespace CrestCreates.Domain.OpenIddict;

public class OpenIddictApplication : OpenIddictEntityFrameworkCoreApplication<long, OpenIddictAuthorization, OpenIddictToken>
{
}
