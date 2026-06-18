namespace CrestCreates.Infrastructure.Authorization;

public class IdentityAuthenticationOptions
{
    public const string SectionName = "Identity";

    public int MinPasswordLength { get; set; } = 8;
    public bool RequireDigit { get; set; } = true;
    public bool RequireLowercase { get; set; } = true;
    public bool RequireUppercase { get; set; } = true;
    public bool RequireNonAlphanumeric { get; set; }
    public int MaxAccessFailedCount { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}
