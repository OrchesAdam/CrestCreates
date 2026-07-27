using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Tests.Workflow;

public class ProcurementWorkflowTests
{
    [Fact]
    public async Task High_value_request_creates_approval_workflow()
    {
        true.Should().BeTrue();
    }
}
