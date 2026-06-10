using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Schema;
using CrestCreates.Form.Abstractions;
using CrestCreates.Form;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Workflow;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.HumanTask;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorReferenceValidationTests
{
    private sealed class ListProvider<T> : IDescriptorProvider<T> where T : class, IDescriptor
    {
        private readonly List<T> _descriptors;
        public ListProvider(List<T> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<T> GetDescriptors() => _descriptors;
    }

    [Fact]
    public void Form_ReferencesSchema_Existing_Ok()
    {
        var schemaEngine = new RegistryValidationEngine<SchemaDescriptor>([]);
        var schemaRegistry = new SchemaRegistry(schemaEngine);
        var formEngine = new RegistryValidationEngine<FormDescriptor>([]);
        var formRegistry = new FormRegistry(formEngine);

        var schema = new SchemaDescriptor { Id = "schema_01", Name = "Customer", Version = 1 };
        var form = new FormDescriptor { Id = "form_01", Name = "CustomerForm", Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1) };

        schemaRegistry.Build([new ListProvider<SchemaDescriptor>([schema])]);
        formRegistry.Build([new ListProvider<FormDescriptor>([form])]);

        formRegistry.State.Should().Be(RegistryState.Built);
    }

    [Fact]
    public void Workflow_ReferencesCapability_Existing_Ok()
    {
        var capEngine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var capRegistry = new CapabilityRegistry(capEngine);
        var wfEngine = new RegistryValidationEngine<WorkflowDescriptor>([]);
        var wfRegistry = new WorkflowRegistry(wfEngine);

        var cap = new CapabilityDescriptor { Id = "cap_01", Name = "Create Customer", Version = 1 };
        var wf = new WorkflowDescriptor { Id = "wf_01", Name = "Onboarding", Version = 1,
            Steps = new List<WorkflowStep> { new() { Id = "step_01", Name = "Create",
                Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_01", 1) } } } };

        capRegistry.Build([new ListProvider<CapabilityDescriptor>([cap])]);
        wfRegistry.Build([new ListProvider<WorkflowDescriptor>([wf])]);

        wfRegistry.State.Should().Be(RegistryState.Built);
    }

    [Fact]
    public void HumanTask_ReferencesForm_Existing_Ok()
    {
        var formEngine = new RegistryValidationEngine<FormDescriptor>([]);
        var formRegistry = new FormRegistry(formEngine);
        var htEngine = new RegistryValidationEngine<HumanTaskDescriptor>([]);
        var htRegistry = new HumanTaskRegistry(htEngine);

        var form = new FormDescriptor { Id = "form_01", Name = "ApprovalForm", Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1) };
        var ht = new HumanTaskDescriptor { Id = "ht_01", Name = "Approval", Version = 1,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor>("form_01", 1) };

        formRegistry.Build([new ListProvider<FormDescriptor>([form])]);
        htRegistry.Build([new ListProvider<HumanTaskDescriptor>([ht])]);

        htRegistry.State.Should().Be(RegistryState.Built);
    }
}
