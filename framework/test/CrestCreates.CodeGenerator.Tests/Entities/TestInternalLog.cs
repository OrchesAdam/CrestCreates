using System;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.CodeGenerator.Tests.Entities;

[Entity(GeneratePermissions = false)]
public class TestInternalLog : AuditedEntity<Guid>
{
    public string Message { get; private set; } = string.Empty;
    public string Level { get; private set; } = string.Empty;
    public string? StackTrace { get; private set; }
    public string? Source { get; private set; }
    public DateTime Timestamp { get; private set; }

    protected TestInternalLog() { }

    public TestInternalLog(Guid id, string message, string level, string? stackTrace = null, string? source = null)
    {
        Id = id;
        Message = message;
        Level = level;
        StackTrace = stackTrace;
        Source = source;
        Timestamp = DateTime.UtcNow;
    }
}
