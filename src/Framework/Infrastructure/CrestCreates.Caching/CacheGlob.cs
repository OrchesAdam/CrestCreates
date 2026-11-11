using System;
using System.Text;
using System.Text.RegularExpressions;

namespace CrestCreates.Caching;

/// <summary>
/// Cache-key glob shared by every <see cref="CrestCreates.Caching.Abstractions.ICrestCache"/> implementation so the
/// wildcard and escape rules cannot drift between providers.
/// </summary>
/// <remarks>
/// <c>*</c> matches any sequence, <c>?</c> matches exactly one character, <c>\</c> makes the next
/// character literal, and every other character (including regex metacharacters and Redis
/// character-class brackets) is literal. Patterns are matched against the key suffix after the
/// configured instance prefix, never against the prefix itself. Redis receives only escaped
/// literal prefixes for server-side scans and the same in-process matcher for the final filter.
/// </remarks>
internal static class CacheGlob
{
    public static string Escape(string literal)
    {
        var builder = new StringBuilder(literal.Length);
        foreach (var character in literal)
        {
            if (character is '*' or '?' or '[' or ']' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    public static bool IsMatch(ReadOnlySpan<char> value, string pattern)
    {
        return CreateRegex(pattern).IsMatch(value);
    }

    public static Regex CreateRegex(string pattern)
    {
        var builder = new StringBuilder("^");
        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];
            switch (character)
            {
                case '\\' when index + 1 < pattern.Length:
                    index++;
                    builder.Append(Regex.Escape(pattern[index].ToString()));
                    break;
                case '\\':
                    builder.Append(Regex.Escape("\\"));
                    break;
                case '*':
                    builder.Append(".*");
                    break;
                case '?':
                    builder.Append('.');
                    break;
                default:
                    builder.Append(Regex.Escape(character.ToString()));
                    break;
            }
        }

        builder.Append('$');
        return new Regex(builder.ToString(), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}
