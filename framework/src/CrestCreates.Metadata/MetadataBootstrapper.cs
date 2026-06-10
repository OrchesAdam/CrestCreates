using CrestCreates.Event.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Metadata;

public static class MetadataBootstrapper
{
    public static void BuildAll(
        ISchemaRegistry schemaRegistry,
        IFormRegistry formRegistry,
        IHumanTaskRegistry humanTaskRegistry,
        IWorkflowRegistry workflowRegistry,
        IEventRegistry eventRegistry,
        Action<IReadOnlyList<WorkflowDescriptor>>? onWorkflowBuilt = null)
    {
        schemaRegistry.Build(DescriptorProviderRegistry.GetProviders<SchemaDescriptor>());
        formRegistry.Build(DescriptorProviderRegistry.GetProviders<FormDescriptor>());
        humanTaskRegistry.Build(DescriptorProviderRegistry.GetProviders<HumanTaskDescriptor>());
        workflowRegistry.Build(DescriptorProviderRegistry.GetProviders<WorkflowDescriptor>());
        eventRegistry.Build(DescriptorProviderRegistry.GetProviders<GeneratedEventDescriptor>());

        // Post-build: workflow compatibility validation (Phase 4b).
        // Validator registration alone does not activate validation.
        // Consumer must pass a callback that invokes WorkflowCompatibilityValidator.
        onWorkflowBuilt?.Invoke(workflowRegistry.GetAll());
    }
}
