using System.Text.RegularExpressions;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Internal;
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.Memory.Sanitization;

public sealed class DefaultAgentMemoryContentSanitizer : IAgentMemoryContentSanitizer
{
    private static readonly Regex[] RedactionPatterns =
    [
        // Connection string password segments: requires semicolon before password/pwd
        new Regex(@"(?i);\s*(password|pwd)\s*=\s*[^;]+", RegexOptions.Compiled),
        // Bearer tokens
        new Regex(@"bearer\s+\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // Password/api_key assignments
        new Regex(@"(?i)(password|api_key|apikey|secret|token)\s*=\s*\S+", RegexOptions.Compiled),
        // Long base64-like tokens (40+ consecutive base64 chars)
        new Regex(@"[A-Za-z0-9+/]{40,}={0,2}", RegexOptions.Compiled)
    ];

    private static readonly string[] RedactionKindLabels =
    [
        AgentMemoryDiagnosticCodes.AgentMemoryRedactionKinds.ConnectionCredential,
        AgentMemoryDiagnosticCodes.AgentMemoryRedactionKinds.BearerToken,
        AgentMemoryDiagnosticCodes.AgentMemoryRedactionKinds.Credential,
        AgentMemoryDiagnosticCodes.AgentMemoryRedactionKinds.LongToken
    ];

    public SanitizedAgentContent Sanitize(string tenantId, string content, IReadOnlyList<AgentContextSourceRef> sourceRefs)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new SanitizedAgentContent
            {
                SanitizedContent = string.Empty,
                CanonicalContentHash = AgentMemoryHash.ComputeCanonicalHash(string.Empty),
                Rejected = true,
                RedactionKinds = [AgentMemoryDiagnosticCodes.AgentMemoryRedactionKinds.EmptyContent],
                Diagnostics = [new AgentMemoryDiagnostic
                {
                    Code = AgentMemoryDiagnosticCodes.EmptyContent,
                    Message = "Content is empty or whitespace.",
                    Severity = SeverityLevel.Warning
                }]
            };
        }

        var sanitized = content.Trim();
        var redactionKinds = new List<string>();
        var diagnostics = new List<AgentMemoryDiagnostic>();

        for (var i = 0; i < RedactionPatterns.Length; i++)
        {
            var pattern = RedactionPatterns[i];
            var kind = RedactionKindLabels[i];
            var replacement = $"[REDACTED:{kind}]";
            var matches = pattern.Matches(sanitized);
            if (matches.Count > 0)
            {
                sanitized = pattern.Replace(sanitized, replacement);
                if (!redactionKinds.Contains(kind))
                {
                    redactionKinds.Add(kind);
                }
                diagnostics.Add(new AgentMemoryDiagnostic
                {
                    Code = AgentMemoryDiagnosticCodes.ContentRedacted,
                    Message = $"Redacted {matches.Count} occurrence(s) of '{kind}'.",
                    Severity = SeverityLevel.Info,
                    SourceRefs = sourceRefs
                });
            }
        }

        var hash = AgentMemoryHash.ComputeCanonicalHash(sanitized);

        // Check if content was entirely redacted: strip all redaction markers and check what remains
        var remaining = sanitized;
        foreach (var kind in redactionKinds)
        {
            remaining = remaining.Replace($"[REDACTED:{kind}]", "");
        }

        if (string.IsNullOrWhiteSpace(remaining))
        {
            diagnostics.Add(new AgentMemoryDiagnostic
            {
                Code = AgentMemoryDiagnosticCodes.ContentRejected,
                Message = "Content was entirely redacted after sanitization.",
                Severity = SeverityLevel.Warning
            });
            return new SanitizedAgentContent
            {
                SanitizedContent = sanitized,
                CanonicalContentHash = hash,
                Rejected = true,
                RedactionKinds = redactionKinds.ToArray(),
                Diagnostics = diagnostics.ToArray()
            };
        }

        return new SanitizedAgentContent
        {
            SanitizedContent = sanitized,
            CanonicalContentHash = hash,
            Rejected = false,
            RedactionKinds = redactionKinds.ToArray(),
            Diagnostics = diagnostics.ToArray()
        };
    }
}
