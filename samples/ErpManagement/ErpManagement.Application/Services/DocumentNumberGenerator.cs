namespace ErpManagement.Application.Services;

/// <summary>
/// Server-side document numbering: PREFIX-yyyyMMdd-short random. The unique index remains the
/// authority; the bounded pre-check loop only avoids predictable collisions at insert time.
/// </summary>
internal static class DocumentNumberGenerator
{
    public static async Task<string> GenerateAsync(
        string prefix,
        Func<string, CancellationToken, Task<bool>> existsAsync,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = $"{prefix}-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(0, 0x1000000):X6}";
            if (!await existsAsync(candidate, cancellationToken))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"无法为前缀 '{prefix}' 生成唯一单据编号（重试已用尽）。");
    }
}