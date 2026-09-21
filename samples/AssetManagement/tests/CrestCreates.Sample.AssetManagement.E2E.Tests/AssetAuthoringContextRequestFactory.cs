using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.ContextPack.Abstractions;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

internal static class AssetAuthoringContextRequestFactory
{
    public static MetadataContextPackRequest Create(
        string tenantId,
        string intent,
        DescriptorRef workflowFocus)
        => new()
        {
            Scope = MetadataContextPackScope.RuntimeScenario,
            TenantId = tenantId,
            Intent = intent,
            FocusDescriptors = [workflowFocus],
            ScenarioRecipe = new RuntimeScenarioRecipe
            {
                Name = "asset-maintenance-workflow-authoring",
                Steps =
                [
                    new ScenarioTraversalStep
                    {
                        FollowKind = RelationshipKind.Uses,
                        TargetKind = DescriptorKind.HumanTask,
                        MaxDepth = 1
                    },
                    new ScenarioTraversalStep
                    {
                        FollowKind = RelationshipKind.Uses,
                        Role = "Interaction",
                        TargetKind = DescriptorKind.Form,
                        MaxDepth = 1
                    },
                    new ScenarioTraversalStep
                    {
                        FollowKind = RelationshipKind.Uses,
                        Role = "Schema",
                        TargetKind = DescriptorKind.Schema,
                        MaxDepth = 1
                    }
                ]
            },
            MaxTraversalDepth = 3,
            MaxDescriptorCount = 8,
            IncludeStableHashes = true
        };
}
