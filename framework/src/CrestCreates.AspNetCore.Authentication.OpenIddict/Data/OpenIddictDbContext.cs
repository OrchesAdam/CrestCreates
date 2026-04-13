using CrestCreates.Domain.OpenIddict;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.AspNetCore.Authentication.OpenIddict;

public class OpenIddictDbContext : DbContext
{
    public OpenIddictDbContext(DbContextOptions options) : base(options)
    {
    }

    public DbSet<OpenIddictApplication> Applications { get; set; } = null!;
    public DbSet<OpenIddictAuthorization> Authorizations { get; set; } = null!;
    public DbSet<OpenIddictScope> Scopes { get; set; } = null!;
    public DbSet<OpenIddictToken> Tokens { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.UseOpenIddict<OpenIddictApplication, OpenIddictAuthorization, OpenIddictScope, OpenIddictToken, long>();
    }
}
