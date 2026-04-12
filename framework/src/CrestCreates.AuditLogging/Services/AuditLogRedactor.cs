using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CrestCreates.AuditLogging.Context;
using CrestCreates.AuditLogging.Options;
using Microsoft.Extensions.Options;

namespace CrestCreates.AuditLogging.Services;

/// <summary>
/// 统一审计日志脱敏服务实现
/// 在 AuditLog 落库前对 RequestBody、ResponseBody、Parameters、ReturnValue、ExtraProperties 中的敏感信息进行脱敏
/// </summary>
public class AuditLogRedactor : IAuditLogRedactor
{
    private readonly AuditLoggingOptions _options;
    private readonly HashSet<string> _sensitiveKeys;

    public AuditLogRedactor(IOptions<AuditLoggingOptions> options)
    {
        _options = options.Value;
        _sensitiveKeys = BuildSensitiveKeys(_options.SensitivePropertyNames);
    }

    public Task RedactAsync(AuditContext context)
    {
        context.RequestBody = RedactJson(context.RequestBody);
        context.ResponseBody = RedactJson(context.ResponseBody);
        context.Parameters = RedactJson(context.Parameters);
        context.ReturnValue = RedactJson(context.ReturnValue);
        context.ExtraProperties = RedactExtraProperties(context.ExtraProperties);

        // 统一脱敏也覆盖异常上下文中的敏感信息
        context.ExceptionMessage = RedactPlainText(context.ExceptionMessage);
        context.ExceptionStackTrace = RedactPlainText(context.ExceptionStackTrace);

        return Task.CompletedTask;
    }

    private string? RedactJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var sanitized = SanitizeElement(document.RootElement);
            return JsonSerializer.Serialize(sanitized);
        }
        catch (JsonException)
        {
            // Non-JSON string: do a simple key-based replacement
            return RedactPlainText(raw);
        }
    }

    private object? SanitizeElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => IsSensitive(property.Name)
                        ? "***"
                        : SanitizeElement(property.Value)),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(SanitizeElement)
                .ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var value) ? value : element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }

    private bool IsSensitive(string propertyName)
    {
        return _sensitiveKeys.Contains(propertyName.ToUpperInvariant());
    }

    private string? RedactPlainText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        // For plain text (e.g. exception messages, stack traces), replace sensitive key=value patterns
        // Handles: "key"="value", "key"="value", "key" "value" (space-separated), key=value
        var result = raw;
        foreach (var key in _options.SensitivePropertyNames)
        {
            // JSON-style: "password":"value"
            result = System.Text.RegularExpressions.Regex.Replace(
                result,
                $@"""{key}""\s*:\s*""[^""]*""",
                $@"""{key}"": ""***""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Key=value (no quotes around value): password=Secret123
            result = System.Text.RegularExpressions.Regex.Replace(
                result,
                $@"""{key}""\s*=\s*""[^""]*""",
                $@"""{key}"": ""***""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Key "value" or Key="value" with space/equals before opening value-quote
            result = System.Text.RegularExpressions.Regex.Replace(
                result,
                $@"""{key}""\s*[\s=""]+[^""]*""",
                $@"""{key}"" ""***""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // key=value without key quotes (e.g. password=secret in stack traces)
            result = System.Text.RegularExpressions.Regex.Replace(
                result,
                $@"\b{key}\s*=\s*""[^""]*""",
                $@"{key}= ""***""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        return result;
    }

    private Dictionary<string, object> RedactExtraProperties(Dictionary<string, object> extraProperties)
    {
        var result = new Dictionary<string, object>(extraProperties.Count);
        foreach (var kvp in extraProperties)
        {
            result[kvp.Key] = IsSensitive(kvp.Key) ? (object)"***" : kvp.Value;
        }
        return result;
    }

    private static HashSet<string> BuildSensitiveKeys(List<string> propertyNames)
    {
        return propertyNames
            .Select(k => k.ToUpperInvariant())
            .ToHashSet();
    }
}
