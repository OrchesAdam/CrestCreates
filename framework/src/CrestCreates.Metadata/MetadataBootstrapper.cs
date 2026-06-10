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
        IEventRegistry eventRegistry)
    {
        schemaRegistry.Build(DescriptorProviderRegistry.GetProviders<SchemaDescriptor>());
        formRegistry.Build(DescriptorProviderRegistry.GetProviders<FormDescriptor>());
        humanTaskRegistry.Build(DescriptorProviderRegistry.GetProviders<HumanTaskDescriptor>());
        workflowRegistry.Build(DescriptorProviderRegistry.GetProviders<WorkflowDescriptor>());
        eventRegistry.Build(DescriptorProviderRegistry.GetProviders<GeneratedEventDescriptor>());
    }
}
