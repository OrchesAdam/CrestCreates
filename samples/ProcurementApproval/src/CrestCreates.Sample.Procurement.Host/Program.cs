using System.Text.Json;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.DynamicApi;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Registry;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Sample.Procurement.Application.Handlers;
using CrestCreates.Sample.Procurement.Host.Json;
using CrestCreates.Sample.Procurement.Host.Projections;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCapabilityRuntime();
builder.Services.AddSingleton<ICapabilityHandlerModule>(new ProcurementCapabilityModule());
builder.Services.AddCrestCapabilityEndpoints();
builder.Services.AddCrestCompatibilityProjection();
builder.Services.AddTransient<ProcurementAppService>();

builder.Services.AddSingleton<ISchemaRegistry>(sp =>
{
    var engine = new RegistryValidationEngine<SchemaDescriptor>([]);
    var registry = new SchemaRegistry(engine);
    registry.Build([]);
    return registry;
});

builder.Services.AddSingleton<ICapabilityRegistry>(sp =>
{
    var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
    var registry = new CapabilityRegistry(engine);
    registry.Build([new ProcurementCapabilityDescriptorProvider()]);
    return registry;
});

builder.Services.AddSingleton<ICapabilityHandlerRegistry, ProcurementHandlerRegistry>();
builder.Services.AddSingleton<IDescriptorLookup, EmptyDescriptorLookup>();
builder.Services.AddSingleton<ISchemaValidator, PassThroughSchemaValidator>();
builder.Services.AddScoped<ICurrentUser, StubCurrentUser>();
builder.Services.AddScoped<ITenantContext, StubTenantContext>();
builder.Services.AddScoped<ICurrentTenant, StubCurrentTenant>();
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.TypeInfoResolver = new ProcurementCombinedJsonResolver();
});

var app = builder.Build();

app.MapCrestCapabilityEndpoints();

app.Run();

public sealed class ProcurementHandlerRegistry : ICapabilityHandlerRegistry
{
    public IReadOnlyDictionary<string, Type> GetHandlerMappings() => new Dictionary<string, Type>
    {
        ["procurement.submit-request"] = typeof(SubmitProcurementRequestHandler),
        ["procurement.approve-request"] = typeof(ApproveProcurementRequestHandler),
        ["procurement.reject-request"] = typeof(RejectProcurementRequestHandler),
        ["procurement.get-request"] = typeof(GetProcurementRequestHandler),
    };
}

public sealed class EmptyDescriptorLookup : IDescriptorLookup
{
    public bool Exists(DescriptorRef descriptorRef) => false;
}

public sealed class ProcurementCapabilityDescriptorProvider : IDescriptorProvider<CapabilityDescriptor>
{
    public IReadOnlyList<CapabilityDescriptor> GetDescriptors()
    {
        return
        [
            new CapabilityDescriptor
            {
                Namespace = "capability",
                Id = "procurement.submit-request",
                Name = "submit-request",
                CapabilityKind = CapabilityKind.Command,
                RiskLevel = CapabilityRiskLevel.Low,
                State = DescriptorState.Active,
                Version = 1
            },
            new CapabilityDescriptor
            {
                Namespace = "capability",
                Id = "procurement.approve-request",
                Name = "approve-request",
                CapabilityKind = CapabilityKind.Command,
                RiskLevel = CapabilityRiskLevel.Medium,
                State = DescriptorState.Active,
                Version = 1
            },
            new CapabilityDescriptor
            {
                Namespace = "capability",
                Id = "procurement.reject-request",
                Name = "reject-request",
                CapabilityKind = CapabilityKind.Command,
                RiskLevel = CapabilityRiskLevel.Medium,
                State = DescriptorState.Active,
                Version = 1
            },
            new CapabilityDescriptor
            {
                Namespace = "capability",
                Id = "procurement.get-request",
                Name = "get-request",
                CapabilityKind = CapabilityKind.Query,
                RiskLevel = CapabilityRiskLevel.Low,
                State = DescriptorState.Active,
                Version = 1
            },
            new CapabilityDescriptor
            {
                Namespace = "capability",
                Id = "compat.appservice.procurement.submit",
                Name = "procurement.submit",
                CapabilityKind = CapabilityKind.Command,
                RiskLevel = CapabilityRiskLevel.Low,
                State = DescriptorState.Active,
                Version = 1
            },
            new CapabilityDescriptor
            {
                Namespace = "capability",
                Id = "compat.appservice.procurement.approve",
                Name = "procurement.approve",
                CapabilityKind = CapabilityKind.Command,
                RiskLevel = CapabilityRiskLevel.Medium,
                State = DescriptorState.Active,
                Version = 1
            },
            new CapabilityDescriptor
            {
                Namespace = "capability",
                Id = "compat.appservice.procurement.reject",
                Name = "procurement.reject",
                CapabilityKind = CapabilityKind.Command,
                RiskLevel = CapabilityRiskLevel.Medium,
                State = DescriptorState.Active,
                Version = 1
            }
        ];
    }
}

public sealed class StubCurrentUser : ICurrentUser
{
    public string Id => "system";
    public string? UserName => "System";
    public string? Email => null;
    public string TenantId => string.Empty;
    public Guid? OrganizationId => null;
    public IReadOnlyList<Guid> OrganizationIds => [];
    public int DataScopeValue => 0;
    public bool IsAuthenticated => true;
    public bool IsSuperAdmin => true;
    public string[] Roles => ["admin"];
    public string FindClaimValue(string claimType) => string.Empty;
    public string[] FindClaimValues(string claimType) => [];
    public bool IsInRole(string roleName) => Roles.Contains(roleName);
    public bool IsInOrganization(Guid orgId) => OrganizationIds.Contains(orgId);
}

public sealed class StubTenantContext : ITenantContext
{
    public string? CurrentTenantId => null;
}

public sealed class StubCurrentTenant : ICurrentTenant
{
    public ITenantInfo? Tenant => null;
    public string? Id => null;
    public Task<IDisposable> ChangeAsync(string tenantId) => Task.FromResult<IDisposable>(NullDisposable.Instance);
    public IDisposable Change(ITenantInfo tenant) => NullDisposable.Instance;
    public void SetTenantId(string tenantId) { }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose() { }
    }
}

public sealed class PassThroughSchemaValidator : ISchemaValidator
{
    public SchemaValidationResult Validate(SchemaDescriptor schema, object? payload, bool rejectUnknownProperties = false)
        => SchemaValidationResult.Success();

    public SchemaValidationResult Validate(SchemaDescriptor schema, JsonElement payload, bool rejectUnknownProperties = false)
        => SchemaValidationResult.Success();

    public SchemaValidationResult Validate(SchemaDescriptor schema, JsonElement payload, IReadOnlyList<SchemaDescriptor> referencedSchemas, bool rejectUnknownProperties = false)
        => SchemaValidationResult.Success();
}

public partial class Program;
