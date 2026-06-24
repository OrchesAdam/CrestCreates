namespace CrestCreates.MultiTenancy;

public interface IConnectionStringProtector
{
    string Protect(string connectionString);
    string? Unprotect(string? protectedConnectionString);
    string Mask(string connectionString);
}
