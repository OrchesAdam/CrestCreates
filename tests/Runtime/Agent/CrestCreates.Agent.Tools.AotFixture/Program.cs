using System.Text.Json;
using System.Text.Json.Serialization;
using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Registry;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

return await CrestCreates.Agent.Tools.AotFixture.AgentToolFixtureRunner.RunAsync();

namespace CrestCreates.Agent.Tools.AotFixture
{
    public static class AgentToolFixtureRunner
    {
        public static async Task<int> RunAsync()
        {
            try
            {
                var inputSchema = Schema("fixture.agent.input");
                var outputSchema = Schema("fixture.agent.output");
                var schemas = new SchemaRegistry(new RegistryValidationEngine<SchemaDescriptor>([]));
                DescriptorProviderRegistry.Register<SchemaDescriptor>(
                    new FixtureProvider<SchemaDescriptor>([inputSchema, outputSchema]));

                var capability = new CapabilityDescriptor
                {
                    Id = "fixture.agent.echo",
                    Name = "Agent Echo",
                    Version = 1,
                    State = DescriptorState.Active,
                    CapabilityKind = CapabilityKind.Command,
                    RiskLevel = CapabilityRiskLevel.Low,
                    InputSchema = new VersionedDescriptorRef<SchemaDescriptor>(inputSchema.Id, 1),
                    OutputSchema = new VersionedDescriptorRef<SchemaDescriptor>(outputSchema.Id, 1)
                };
                var capabilities = new CapabilityRegistry(new RegistryValidationEngine<CapabilityDescriptor>([]));
                DescriptorProviderRegistry.Register<CapabilityDescriptor>(
                    new FixtureProvider<CapabilityDescriptor>([capability]));

                var builder = Host.CreateApplicationBuilder();
                builder.Services.AddSingleton<ISchemaRegistry>(schemas);
                builder.Services.AddSingleton<ICapabilityRegistry>(capabilities);
                builder.Services.AddSingleton<ICurrentUser, FixtureCurrentUser>();
                builder.Services.AddSingleton<ITenantContext, FixtureTenantContext>();
                builder.Services.AddSingleton<IAgentExecutionContextAccessor, FixtureAgentExecutionContextAccessor>();
                builder.Services.AddSingleton<IAgentToolInvocationGate, DevelopmentInMemoryAgentToolInvocationGate>();
                builder.Services.AddSingleton<IAgentToolInvocationLeaseAbandoner>(provider =>
                    (IAgentToolInvocationLeaseAbandoner)provider.GetRequiredService<IAgentToolInvocationGate>());
                builder.Services.AddSingleton<IAgentToolBudgetGate, DevelopmentInMemoryAgentToolBudgetGate>();
                builder.Services.AddSingleton<IAgentToolGovernanceAuditor, DevelopmentInMemoryAgentToolGovernanceAuditor>();
                // Generated capability invokers resolve handlers from the scoped
                // service provider; registration is explicit and AOT-safe.
                builder.Services.AddScoped<FixtureEchoHandler>();
                builder.Services.AddCapabilityRuntime();
                builder.Services.AddCrestAgentTools(options =>
                    options.SerializerOptions.TypeInfoResolver = AgentToolFixtureJsonContext.Default);

                using var host = builder.Build();
                await host.StartAsync();
                using var scope = host.Services.CreateScope();
                var catalog = scope.ServiceProvider.GetRequiredService<IAgentToolCatalog>();
                var tools = await catalog.ListAsync();
                if (tools.Count != 1 || tools[0].ToolName != "fixture.agent.echo")
                    return 2;

                var invoker = scope.ServiceProvider.GetRequiredService<IAgentToolInvoker>();
                using var arguments = JsonDocument.Parse("{\"Value\":\"native-aot\"}");
                var request = new AgentToolInvocationRequest(
                    "fixture.agent.echo",
                    arguments.RootElement.Clone());
                var outcome = await invoker.InvokeAsync(request);
                var replay = await invoker.InvokeAsync(request);
                if (!outcome.IsSuccess
                    || !replay.IsSuccess
                    || outcome.StructuredOutput?.GetProperty("Value").GetString() != "native-aot"
                    || FixtureEchoHandler.CallCount != 1
                    || FixtureEchoHandler.LastInput?.Value != "native-aot")
                {
                    return 3;
                }

                Console.WriteLine("AGENT_TOOL_NATIVEAOT_PIPELINE_OK");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        }

        private static SchemaDescriptor Schema(string id)
            => new()
            {
                Id = id,
                Name = id,
                Version = 1,
                State = DescriptorState.Active,
                Fields =
                [
                    new SchemaFieldDescriptor
                    {
                        Name = nameof(FixtureInput.Value),
                        FieldType = "string",
                        IsRequired = true,
                        IsNullable = false
                    }
                ]
            };
    }

    [AgentToolSpecs]
    public static partial class FixtureAgentTools
    {
        [AgentToolSpec(
            "fixture.agent.echo",
            DescriptorId = "agent-tool:fixture.agent.echo",
            CapabilityVersion = 1,
            InputType = typeof(FixtureInput),
            OutputType = typeof(FixtureOutput),
            ToolName = "fixture.agent.echo",
            Title = "Echo value",
            Description = "Echoes a value through the governed Agent Tool pipeline.",
            SelectionPolicy = CrestCreates.Metadata.AgentTool.AgentToolSelectionPolicy.AutomaticAllowed,
            SideEffectKind = CrestCreates.Metadata.AgentTool.AgentToolSideEffectKind.InternalWrite,
            ApprovalMode = CrestCreates.Metadata.AgentTool.AgentToolApprovalMode.None,
            BudgetCategory = "fixture",
            CostUnits = 1,
            MaxCallsPerExecution = 1,
            AuditMode = CrestCreates.Metadata.AgentTool.AgentToolAuditMode.Required,
            AllowedAgentRoles = new[] { "fixture-agent" })]
        public sealed class Echo;
    }

    public sealed class FixtureInput
    {
        public string Value { get; set; } = string.Empty;
    }

    public sealed class FixtureOutput
    {
        public required string Value { get; set; }
    }

    [JsonSerializable(typeof(FixtureInput))]
    [JsonSerializable(typeof(FixtureOutput))]
    internal partial class AgentToolFixtureJsonContext : JsonSerializerContext;

    [CapabilityName("fixture.agent.echo")]
    internal sealed class FixtureEchoHandler : ICapabilityHandler<FixtureInput, FixtureOutput>
    {
        public static int CallCount { get; private set; }
        public static FixtureInput? LastInput { get; private set; }

        public Task<FixtureOutput> ExecuteAsync(FixtureInput input, CancellationToken ct)
        {
            CallCount++;
            LastInput = input;
            return Task.FromResult(new FixtureOutput { Value = input.Value });
        }
    }

    internal sealed class FixtureProvider<T>(IReadOnlyList<T> descriptors) : IDescriptorProvider<T>
        where T : IDescriptor
    {
        public IReadOnlyList<T> GetDescriptors() => descriptors;
    }

    internal sealed class FixtureAgentExecutionContextAccessor : IAgentExecutionContextAccessor
    {
        public AgentExecutionContext Current { get; } = new()
        {
            ExecutionId = "fixture-execution",
            InvocationId = "fixture-invocation",
            AgentId = "fixture-agent",
            AgentRoles = new HashSet<string>(StringComparer.Ordinal) { "fixture-agent" },
            CallOrigin = AgentToolCallOrigin.AutomaticSelection,
            CausationId = "fixture-causation"
        };
    }

    internal sealed class FixtureTenantContext : ITenantContext
    {
        public string? CurrentTenantId => "fixture-tenant";
    }

    internal sealed class FixtureCurrentUser : ICurrentUser
    {
        public string Id => "fixture-user";
        public string UserName => "fixture-user";
        public bool IsAuthenticated => true;
        public string TenantId => "fixture-tenant";
        public string[] Roles => [];
        public Guid? OrganizationId => null;
        public IReadOnlyList<Guid> OrganizationIds => Array.Empty<Guid>();
        public int DataScopeValue => 0;
        public bool IsSuperAdmin => false;
        public string FindClaimValue(string claimType) => string.Empty;
        public string[] FindClaimValues(string claimType) => [];
        public bool IsInRole(string roleName) => false;
        public bool IsInOrganization(Guid orgId) => false;
    }
}
