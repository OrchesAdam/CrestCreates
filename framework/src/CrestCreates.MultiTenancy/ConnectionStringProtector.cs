namespace CrestCreates.MultiTenancy;

public interface IConnectionStringProtector
{
    string Protect(string connectionString);
    string? Unprotect(string? protectedConnectionString);
    string Mask(string connectionString);
}

public class ConnectionStringProtector : IConnectionStringProtector
{
    private const string MaskedValue = "***";

    public string Protect(string connectionString)
    {
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(connectionString));
    }

    public string? Unprotect(string? protectedConnectionString)
    {
        if (string.IsNullOrEmpty(protectedConnectionString))
        {
            return null;
        }

        try
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(protectedConnectionString));
        }
        catch
        {
            return protectedConnectionString;
        }
    }

    public string Mask(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            return string.Empty;
        }

        if (connectionString.Length <= 8)
        {
            return MaskedValue;
        }

        return connectionString[..4] + "****" + connectionString[^4..];
    }
}
