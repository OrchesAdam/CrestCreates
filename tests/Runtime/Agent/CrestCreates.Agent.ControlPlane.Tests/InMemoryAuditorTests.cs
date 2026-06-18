using Xunit;
using CrestCreates.Agent.ControlPlane.Abstractions;
using FluentAssertions;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Tests for the in-memory auditor.
/// Verifies: all invocations recorded, tenant isolation, tool-name filtering.
/// </summary>
public class InMemoryAuditorTests
{
    private readonly InMemoryAgentToolInvocationAuditor _auditor = new();

    [Fact]
    public async Task RecordAsync_Stores_Record()
    {
        var record = CreateAudit("GetDescriptorByRef", "tenant-001");

        await _auditor.RecordAsync(record);

        _auditor.GetAllRecords().Should().ContainSingle();
    }

    [Fact]
    public async Task GetAllRecords_Returns_All_Records()
    {
        await _auditor.RecordAsync(CreateAudit("Tool1", "t1"));
        await _auditor.RecordAsync(CreateAudit("Tool2", "t2"));

        _auditor.GetAllRecords().Should().HaveCount(2);
    }

    [Fact]
    public async Task GetRecordsByToolName_Filters_Correctly()
    {
        await _auditor.RecordAsync(CreateAudit("GetDescriptorByRef", "t1"));
        await _auditor.RecordAsync(CreateAudit("SearchDescriptors", "t1"));
        await _auditor.RecordAsync(CreateAudit("GetDescriptorByRef", "t2"));

        var results = _auditor.GetRecordsByToolName("GetDescriptorByRef");
        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetRecordsByTenant_Filters_Correctly()
    {
        await _auditor.RecordAsync(CreateAudit("Tool1", "tenant-A"));
        await _auditor.RecordAsync(CreateAudit("Tool2", "tenant-B"));
        await _auditor.RecordAsync(CreateAudit("Tool3", "tenant-A"));

        var results = _auditor.GetRecordsByTenant("tenant-A");
        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetRecordsByCorrelationId_Filters_Correctly()
    {
        await _auditor.RecordAsync(CreateAudit("Tool1", "t1", corrId: "corr-A"));
        await _auditor.RecordAsync(CreateAudit("Tool2", "t1", corrId: "corr-B"));
        await _auditor.RecordAsync(CreateAudit("Tool3", "t1", corrId: "corr-A"));

        var results = _auditor.GetRecordsByCorrelationId("corr-A");
        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task Clear_Removes_All_Records()
    {
        await _auditor.RecordAsync(CreateAudit("Tool1", "t1"));
        _auditor.Clear();
        _auditor.GetAllRecords().Should().BeEmpty();
    }

    private static AgentToolInvocationAuditRecord CreateAudit(
        string toolName, string tenantId, string corrId = "corr-001")
    {
        return new AgentToolInvocationAuditRecord
        {
            AuditId = Guid.NewGuid().ToString("N"),
            Timestamp = DateTimeOffset.UtcNow,
            Context = new AgentToolInvocationContext
            {
                TenantId = tenantId,
                ActorId = "actor-001",
                ActorKind = AgentToolActorKind.Agent,
                CorrelationId = corrId,
                ToolName = toolName,
                InvocationSource = AgentToolInvocationSource.Direct
            },
            ResultStatus = AgentToolResultStatus.Success,
            Diagnostics = Array.Empty<AgentToolDiagnostic>()
        };
    }
}
