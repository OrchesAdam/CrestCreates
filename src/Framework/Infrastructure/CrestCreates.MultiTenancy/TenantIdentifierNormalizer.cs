using System.Text.RegularExpressions;

namespace CrestCreates.MultiTenancy;

public class TenantIdentifierNormalizer
{
    private static readonly Regex SlugRegex = new(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?$", RegexOptions.Compiled);

    public string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        return name.Trim().ToUpperInvariant();
    }

    public string NormalizeSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return string.Empty;
        }

        slug = slug.Trim().ToLowerInvariant();

        if (!IsValidSlug(slug))
        {
            throw new ArgumentException($"Slug '{slug}' 格式无效，只能包含小写字母、数字和连字符");
        }

        return slug;
    }

    public bool IsValidSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return false;
        }

        return SlugRegex.IsMatch(slug) && slug.Length <= 63;
    }

    public string? NormalizeDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return null;
        }

        domain = domain.Trim().ToLowerInvariant();

        if (domain.StartsWith("http://"))
        {
            domain = domain[7..];
        }
        else if (domain.StartsWith("https://"))
        {
            domain = domain[8..];
        }

        if (domain.StartsWith("www."))
        {
            domain = domain[4..];
        }

        if (domain.EndsWith("/"))
        {
            domain = domain[..^1];
        }

        return domain;
    }

    public string? ExtractSubdomain(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        host = NormalizeDomain(host) ?? host;

        var parts = host.Split('.');
        if (parts.Length >= 3)
        {
            return parts[0];
        }

        return null;
    }
}
