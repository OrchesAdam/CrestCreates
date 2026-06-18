using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
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

    private class TestWorkflowProvider : IDescriptorProvider<WorkflowDescriptor>
    {
        private readonly List<WorkflowDescriptor> _descriptors;
        public TestWorkflowProvider(List<WorkflowDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<WorkflowDescriptor> GetDescriptors() => _descriptors;
    }

    private static WorkflowRegistry CreateRegistry(params WorkflowDescriptor[] descriptors)
    {
        var engine = new RegistryValidationEngine<WorkflowDescriptor>(Array.Empty<IRegistryValidator<WorkflowDescriptor>>());
        var registry = new WorkflowRegistry(engine);
        registry.Build([new TestWorkflowProvider(descriptors.ToList())]);
        return registry;
    }

    [Fact]
    public void Register_And_GetById_Works()
    {
        var registry = CreateRegistry(CreateWorkflow("wf_01", "employee.onboarding", 1));

        var result = registry.GetById("wf_01");

        result.Should().NotBeNull();
        result!.Name.Should().Be("employee.onboarding");
    }

    [Fact]
    public void GetActiveVersion_Returns_Highest_Active_Version()
    {
        var registry = CreateRegistry(
            CreateWorkflow("w1", "employee.onboarding", 1),
            CreateWorkflow("w2", "employee.onboarding", 2));

        var active = registry.GetActiveVersion("employee.onboarding");
        active.Should().NotBeNull();
        active!.Version.Should().Be(2);
    }

    [Fact]
    public void GetAll_Returns_All_Workflows()
    {
        var registry = CreateRegistry(
            CreateWorkflow("w1", "wf.a", 1),
            CreateWorkflow("w2", "wf.b", 1));

        var all = registry.GetAll();
        all.Should().HaveCount(2);
    }

    [Fact]
    public void Build_Sets_State_To_Built()
    {
        var engine = new RegistryValidationEngine<WorkflowDescriptor>(Array.Empty<IRegistryValidator<WorkflowDescriptor>>());
        var registry = new WorkflowRegistry(engine);
        var provider = new TestWorkflowProvider([CreateWorkflow("w1", "w", 1)]);

        registry.Build([provider]);

        registry.State.Should().Be(RegistryState.Built);
    }
}
