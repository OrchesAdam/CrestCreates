using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Sample.Procurement.Tests.TestInfrastructure;

public sealed class AuditTestSink
{
    public List<CapabilityExecutionRecord> Records { get; } = [];
}
