using System.Text.Json;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Sanitization;
using CrestCreates.Accountability.Sanitization;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Accountability.Tests.Sanitization;

public sealed class AuditSanitizationRuleRegistryTests
{
    [Fact]
    public void DuplicateKindOwnersFailAtConstruction()
    {
        var action = () => new AuditPayloadSanitizationRuleRegistry([new Rule("same", 1), new Rule("same", 2)]);
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void UnknownKindIsRejectedWithoutFallback()
    {
        var registry = new AuditPayloadSanitizationRuleRegistry([new Rule("known", 1)]);
        var action = () => registry.Sanitize(new AuditPayload { Kind = "unknown", Version = 1, Data = JsonDocument.Parse("{}").RootElement.Clone() });
        action.Should().Throw<AuditSanitizationException>().Which.Code.Should().Be("AUDIT_UNKNOWN_SANITIZATION_RULE");
    }

    private sealed class Rule(string kind, int version) : IAuditPayloadSanitizationRule
    {
        public string Kind { get; } = kind;
        public int RuleVersion { get; } = version;
        public AuditPayload Sanitize(AuditPayload payload) => payload;
    }
}
