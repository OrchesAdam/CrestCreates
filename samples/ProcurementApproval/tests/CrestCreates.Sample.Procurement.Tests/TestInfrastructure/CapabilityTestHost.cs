using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Registry;
using CrestCreates.Sample.Procurement.Application.Handlers;
using CrestCreates.Schema.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Sample.Procurement.Tests.TestInfrastructure;

internal sealed class CapabilityTestHost
{
    private static readonly Lazy<CapabilityTestHost> _instance = new(() => new CapabilityTestHost());

    public static ICapabilityPipeline BuildPipeline() => _instance.Value.CreatePipeline();

    public static CapabilityExecutionContext CreateContext(string capabilityId, object? input)
    {
        return new CapabilityExecutionContext
        {
            CapabilityId = capabilityId,
            CapabilityName = capabilityId,
            CapabilityVersion = 1,
            InvocationSource = InvocationSource.Http,
            Input = input,
            ServiceProvider = _instance.Value._serviceProvider
        };
    }

    private readonly ServiceProvider _serviceProvider;

    public CapabilityTestHost()
    {
        var services = new ServiceCollection();

        var handlerResolver = new CapabilityHandlerResolver();
        var module = new ProcurementCapabilityModule();
        module.Apply(handlerResolver);
        services.AddSingleton<ICapabilityHandlerResolver>(handlerResolver);

        services.AddSingleton<ISchemaValidator>(new NullSchemaValidator());

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddCapabilityRuntime();

        var descriptors = BuildDescriptors();
        var providers = new IDescriptorProvider<CapabilityDescriptor>[]
        {
            new InlineDescriptorProvider<CapabilityDescriptor>(descriptors)
        };
        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        registry.Build(providers);
        services.AddSingleton<ICapabilityRegistry>(registry);

        _serviceProvider = services.BuildServiceProvider();
    }

    public ICapabilityPipeline CreatePipeline()
    {
        var scope = _serviceProvider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ICapabilityPipeline>();
    }

    private static List<CapabilityDescriptor> BuildDescriptors()
    {
        return
        [
            new CapabilityDescriptor
            {
                Namespace = "procurement",
                Id = "procurement.submit-request",
                Name = "submit-request",
                CapabilityKind = CapabilityKind.Command,
                RiskLevel = CapabilityRiskLevel.Low,
                State = DescriptorState.Active,
                Version = 1
            },
            new CapabilityDescriptor
            {
                Namespace = "procurement",
                Id = "procurement.approve-request",
                Name = "approve-request",
                CapabilityKind = CapabilityKind.Command,
                RiskLevel = CapabilityRiskLevel.Medium,
                State = DescriptorState.Active,
                Version = 1
            },
            new CapabilityDescriptor
            {
                Namespace = "procurement",
                Id = "procurement.reject-request",
                Name = "reject-request",
                CapabilityKind = CapabilityKind.Command,
                RiskLevel = CapabilityRiskLevel.Medium,
                State = DescriptorState.Active,
                Version = 1
            }
        ];
    }

    private sealed class InlineDescriptorProvider<T>(List<T> descriptors) : IDescriptorProvider<T>
        where T : IDescriptor
    {
        public IReadOnlyList<T> GetDescriptors() => descriptors;
    }

    private sealed class NullSchemaValidator : ISchemaValidator
    {
        public SchemaValidationResult Validate(SchemaDescriptor schema, object? payload, bool rejectUnknownProperties = false)
            => SchemaValidationResult.Success();

        public SchemaValidationResult Validate(SchemaDescriptor schema, System.Text.Json.JsonElement payload, bool rejectUnknownProperties = false)
            => SchemaValidationResult.Success();

        public SchemaValidationResult Validate(SchemaDescriptor schema, System.Text.Json.JsonElement payload, IReadOnlyList<SchemaDescriptor> referencedSchemas, bool rejectUnknownProperties = false)
            => SchemaValidationResult.Success();
    }
}
