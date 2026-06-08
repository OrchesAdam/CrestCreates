using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public class WorkflowRegistryTests
{
    private static WorkflowDescriptor CreateWorkflow(string id, string name, int version)
    {
        return new WorkflowDescriptor
        {
            Id = id,
            Name = name,
            Version = version
        };
    }

    [Fact]
    public void Register_And_GetById_Works()
    {
        var registry = new WorkflowRegistry();
        var wf = CreateWorkflow("wf_01", "employee.onboarding", 1);
        registry.Register(wf);

        var result = registry.GetById("wf_01");

        result.Should().NotBeNull();
        result!.Name.Should().Be("employee.onboarding");
    }

    [Fact]
    public void GetActiveVersion_Returns_Highest_Active_Version()
    {
        var registry = new WorkflowRegistry();
        registry.Register(CreateWorkflow("w1", "employee.onboarding", 1));
        registry.Register(CreateWorkflow("w2", "employee.onboarding", 2));

        var active = registry.GetActiveVersion("employee.onboarding");
        active.Should().NotBeNull();
        active!.Version.Should().Be(2);
    }

    [Fact]
    public void GetAll_Returns_All_Workflows()
    {
        var registry = new WorkflowRegistry();
        registry.Register(CreateWorkflow("w1", "wf.a", 1));
        registry.Register(CreateWorkflow("w2", "wf.b", 1));

        var all = registry.GetAll();
        all.Should().HaveCount(2);
    }
}
