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
        Action<IReadOnlyList<WorkflowDescriptor>>? onWorkflowBuilt = null,
        Action<IReadOnlyList<FormDescriptor>, ISchemaRegistry>? onFormBuilt = null)
    {
        schemaRegistry.Build(DescriptorProviderRegistry.GetProviders<SchemaDescriptor>());
        formRegistry.Build(DescriptorProviderRegistry.GetProviders<FormDescriptor>());

        // Post-build: Form→Schema binding validation (Phase 5g)
        onFormBuilt?.Invoke(formRegistry.GetAll(), schemaRegistry);

        humanTaskRegistry.Build(DescriptorProviderRegistry.GetProviders<HumanTaskDescriptor>());
        workflowRegistry.Build(DescriptorProviderRegistry.GetProviders<WorkflowDescriptor>());
        eventRegistry.Build(DescriptorProviderRegistry.GetProviders<GeneratedEventDescriptor>());

        // Post-build: workflow compatibility validation (Phase 4b)
        onWorkflowBuilt?.Invoke(workflowRegistry.GetAll());
    }
}
