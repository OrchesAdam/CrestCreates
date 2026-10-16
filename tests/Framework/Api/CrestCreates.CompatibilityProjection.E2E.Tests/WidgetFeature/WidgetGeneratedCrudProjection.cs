using System.Text.Json;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Capability.Abstractions;
using CrestCreates.CompatibilityProjection.E2E.WidgetFeature.Contracts;
using CrestCreates.CompatibilityProjection.E2E.WidgetFeature.Services;
using CrestCreates.DynamicApi;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.CompatibilityProjection.E2E.WidgetFeature.Generated;

// #128 S3-7 (J/H/Q, JIT leg): the hand-written contribution below mirrors, one member at a time,
// what CrudArtifactEmitter.EmitCapabilityContribution emits for a five-action CRUD surface — the
// capability and endpoint descriptors composed exactly as CrudGenerationModelBuilder.BuildActions
// composes them, typed body bindings built on CapabilityEndpointBodyReader +
// CapabilityEndpointJsonTypeInfoResolver.Require, the composed route-id/body/If-Match envelopes,
// typed result mappers and the S3-5 JSON contract members (JsonContext + RequiredJsonRootTypes).
// The generated-code fixture with real toolchain output is S4's scope (GeneratedCrud.AotFixture);
// nothing here replaces that evidence.

/// <summary>Mirrors the emitted <c>{ServiceClassName}_Update_CompatibilityInput</c> (route Id + body).</summary>
internal sealed class WidgetAppService_Update_CompatibilityInput
{
    public Guid Id { get; init; }

    public UpdateWidgetDto Input { get; init; } = default!;
}

/// <summary>Mirrors the emitted <c>{ServiceClassName}_Delete_CompatibilityInput</c> (route Id + optional If-Match).</summary>
internal sealed class WidgetAppService_Delete_CompatibilityInput
{
    public Guid Id { get; init; }

    public string? IfMatch { get; init; }
}

/// <summary>
/// The hand-written mirror of one generated per-assembly contribution: five actions with the emitted
/// wire matrix (POST prefix; GET prefix/{id}; POST prefix/search; PUT prefix/{id}; DELETE prefix/{id}),
/// one capability/endpoint descriptor per action, bindings keyed by <c>endpoint:{capabilityId}</c>,
/// handlers keyed by capability id, result mappers by endpoint id, plus the S3-5 members
/// (<see cref="CapabilityEndpointContribution.JsonContext"/> and
/// <see cref="CapabilityEndpointContribution.RequiredJsonRootTypes"/>).
/// </summary>
internal sealed class WidgetGeneratedCrudContribution : CapabilityEndpointContribution
{
    /// <summary>Owner identity of the assembly the emitted activation would belong to.</summary>
    public const string OwnerAssembly = "WidgetFeature";

    /// <summary>
    /// The capability identity prefix the model builder derives from (assembly, service class); the
    /// endpoint identity is always <c>"endpoint:" + capabilityId</c> (CrudWireConvention).
    /// </summary>
    private const string CapabilityIdPrefix = OwnerAssembly + ".WidgetAppService";

    public const string CreateCapabilityId = CapabilityIdPrefix + ".Create";
    public const string GetCapabilityId = CapabilityIdPrefix + ".Get";
    public const string SearchCapabilityId = CapabilityIdPrefix + ".Search";
    public const string UpdateCapabilityId = CapabilityIdPrefix + ".Update";
    public const string DeleteCapabilityId = CapabilityIdPrefix + ".Delete";

    public const string CreateEndpointId = "endpoint:" + CreateCapabilityId;
    public const string GetEndpointId = "endpoint:" + GetCapabilityId;
    public const string SearchEndpointId = "endpoint:" + SearchCapabilityId;
    public const string UpdateEndpointId = "endpoint:" + UpdateCapabilityId;
    public const string DeleteEndpointId = "endpoint:" + DeleteCapabilityId;

    public WidgetGeneratedCrudContribution()
    {
        OwnerAssemblyName = OwnerAssembly;
        CapabilityDescriptors =
        [
            Capability(CreateCapabilityId, "Create", CapabilityKind.Command, "Widget.Create"),
            Capability(GetCapabilityId, "Get", CapabilityKind.Query, "Widget.Get"),
            Capability(SearchCapabilityId, "Search", CapabilityKind.Query, "Widget.Search"),
            Capability(UpdateCapabilityId, "Update", CapabilityKind.Command, "Widget.Update"),
            Capability(DeleteCapabilityId, "Delete", CapabilityKind.Command, "Widget.Delete"),
        ];
        EndpointDescriptors =
        [
            Endpoint(CreateCapabilityId, "Create", CapabilityEndpointHttpMethod.Post, "/api/widget",
                [Body("input", required: true)]),
            Endpoint(GetCapabilityId, "Get", CapabilityEndpointHttpMethod.Get, "/api/widget/{id}",
                [Route("id")]),
            Endpoint(SearchCapabilityId, "Search", CapabilityEndpointHttpMethod.Post, "/api/widget/search",
                [Body("input", required: true)]),
            Endpoint(UpdateCapabilityId, "Update", CapabilityEndpointHttpMethod.Put, "/api/widget/{id}",
                [Route("id"), Body("input", required: true)]),
            Endpoint(DeleteCapabilityId, "Delete", CapabilityEndpointHttpMethod.Delete, "/api/widget/{id}",
                [Route("id"), Header("If-Match", required: false)]),
        ];
        Bindings =
        [
            new CapabilityEndpointBindingContract(CreateEndpointId, 1, Bind_Create_Async),
            new CapabilityEndpointBindingContract(GetEndpointId, 1, Bind_Get_Async),
            new CapabilityEndpointBindingContract(SearchEndpointId, 1, Bind_Search_Async),
            new CapabilityEndpointBindingContract(UpdateEndpointId, 1, Bind_Update_Async),
            new CapabilityEndpointBindingContract(DeleteEndpointId, 1, Bind_Delete_Async),
        ];
        Handlers =
        [
            new CapabilityEndpointHandlerBinding(CreateCapabilityId, new Create_Invoker()),
            new CapabilityEndpointHandlerBinding(GetCapabilityId, new Get_Invoker()),
            new CapabilityEndpointHandlerBinding(SearchCapabilityId, new Search_Invoker()),
            new CapabilityEndpointHandlerBinding(UpdateCapabilityId, new Update_Invoker()),
            new CapabilityEndpointHandlerBinding(DeleteCapabilityId, new Delete_Invoker()),
        ];
        ResultMappers =
        [
            new CapabilityEndpointResultBinding(CreateEndpointId, 1, MapResult_Create),
            new CapabilityEndpointResultBinding(GetEndpointId, 1, MapResult_Get),
            new CapabilityEndpointResultBinding(SearchEndpointId, 1, MapResult_Search),
            new CapabilityEndpointResultBinding(UpdateEndpointId, 1, MapResult_Update),
            new CapabilityEndpointResultBinding(DeleteEndpointId, 1, MapResult_Delete),
        ];
        BodyTypes =
        [
            typeof(CreateWidgetDto),
            typeof(WidgetListRequestDto),
            typeof(UpdateWidgetDto),
            typeof(WidgetAppService_Update_CompatibilityInput),
            typeof(WidgetAppService_Delete_CompatibilityInput),
        ];
        JsonContext = WidgetCrudJsonContext.Default;
        RequiredJsonRootTypes = BuildRequiredJsonRootTypes();
        RequiredIdentityIds =
        [
            new CapabilityEndpointRequiredIdentity(CreateEndpointId, OwnerAssembly, "Create", "Widget", 1),
            new CapabilityEndpointRequiredIdentity(GetEndpointId, OwnerAssembly, "Get", "Widget", 1),
            new CapabilityEndpointRequiredIdentity(SearchEndpointId, OwnerAssembly, "Search", "Widget", 1),
            new CapabilityEndpointRequiredIdentity(UpdateEndpointId, OwnerAssembly, "Update", "Widget", 1),
            new CapabilityEndpointRequiredIdentity(DeleteEndpointId, OwnerAssembly, "Delete", "Widget", 1),
        ];
    }

    /// <summary>
    /// The required JSON roots exactly as <c>CrudArtifactEmitter.JsonRootRequirements</c> derives them
    /// from the same action model: bodies, capability input envelopes, payloads, typed result
    /// envelopes and the void envelope (the GET branch also consumes the void envelope for its 404
    /// answer), deduplicated by type in canonical action order.
    /// </summary>
    private static CapabilityEndpointJsonRootType[] BuildRequiredJsonRootTypes() =>
    [
        new(typeof(CreateWidgetDto), "Create", "Body"),
        new(typeof(WidgetDto), "Create", "Payload"),
        new(typeof(DynamicApiResponse<WidgetDto>), "Create", "ResultEnvelope"),
        new(typeof(DynamicApiResponse), "Get", "VoidEnvelope"),
        new(typeof(WidgetListRequestDto), "Search", "Body"),
        new(typeof(PagedResultDto<WidgetDto>), "Search", "Payload"),
        new(typeof(DynamicApiResponse<PagedResultDto<WidgetDto>>), "Search", "ResultEnvelope"),
        new(typeof(UpdateWidgetDto), "Update", "Body"),
        new(typeof(WidgetAppService_Update_CompatibilityInput), "Update", "CapabilityInput"),
        new(typeof(WidgetAppService_Delete_CompatibilityInput), "Delete", "CapabilityInput"),
    ];

    private static CapabilityDescriptor Capability(
        string id, string name, CapabilityKind kind, string permission) => new()
        {
            Namespace = "capability",
            Id = id,
            Name = name,
            State = DescriptorState.Active,
            Version = 1,
            CapabilityKind = kind,
            Permissions = [permission],
            RiskLevel = CapabilityRiskLevel.Medium,
            InputSchema = null,
            OutputSchema = null,
            ProjectionKind = CapabilityProjectionKind.AppServiceCompatibility,
        };

    private static CapabilityEndpointDescriptor Endpoint(
        string capabilityId,
        string name,
        CapabilityEndpointHttpMethod method,
        string route,
        CapabilityEndpointInputBinding[] inputs) => new()
        {
            Id = "endpoint:" + capabilityId,
            Name = name,
            Version = 1,
            State = DescriptorState.Active,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>(capabilityId, 1),
            HttpMethod = method,
            RoutePattern = route,
            AuthorizationMode = CapabilityEndpointAuthorizationMode.InheritCapability,
            InputBindings = inputs,
            OutputMapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 200 },
            Projection = new CapabilityEndpointProjectionMetadata
            {
                OperationId = capabilityId.Replace('.', '_'),
                Tags = ["WidgetAppService"],
            },
        };

    private static CapabilityEndpointInputBinding Route(string name) => new()
    {
        Name = name,
        Source = CapabilityEndpointParameterSource.Route,
        Required = true,
    };

    private static CapabilityEndpointInputBinding Body(string name, bool required) => new()
    {
        Name = name,
        Source = CapabilityEndpointParameterSource.Body,
        Required = required,
    };

    private static CapabilityEndpointInputBinding Header(string name, bool required) => new()
    {
        Name = name,
        Source = CapabilityEndpointParameterSource.Header,
        Required = required,
    };

    // ---- binders: the emitted required-body + static route-parse + composed-envelope shape --------

    private static async ValueTask<object?> Bind_Create_Async(HttpContext context, CancellationToken ct)
    {
        var jsonTypeInfo = CapabilityEndpointJsonTypeInfoResolver.Require<CreateWidgetDto>(context.RequestServices);
        return await CapabilityEndpointBodyReader.ReadNativeBodyAsync(context, jsonTypeInfo, optional: false, ct);
    }

    private static ValueTask<object?> Bind_Get_Async(HttpContext context, CancellationToken ct)
    {
        var routeId = context.Request.RouteValues["id"]?.ToString();
        if (routeId is null || !Guid.TryParse(routeId, out var id))
        {
            throw new BadHttpRequestException("The route value 'id' must be a valid System.Guid.");
        }

        return ValueTask.FromResult<object?>(id);
    }

    private static async ValueTask<object?> Bind_Search_Async(HttpContext context, CancellationToken ct)
    {
        var jsonTypeInfo = CapabilityEndpointJsonTypeInfoResolver.Require<WidgetListRequestDto>(context.RequestServices);
        return await CapabilityEndpointBodyReader.ReadNativeBodyAsync(context, jsonTypeInfo, optional: false, ct);
    }

    private static async ValueTask<object?> Bind_Update_Async(HttpContext context, CancellationToken ct)
    {
        var routeId = context.Request.RouteValues["id"]?.ToString();
        if (routeId is null || !Guid.TryParse(routeId, out var id))
        {
            throw new BadHttpRequestException("The route value 'id' must be a valid System.Guid.");
        }

        var jsonTypeInfo = CapabilityEndpointJsonTypeInfoResolver.Require<UpdateWidgetDto>(context.RequestServices);
        var body = await CapabilityEndpointBodyReader.ReadNativeBodyAsync(context, jsonTypeInfo, optional: false, ct);

        return new WidgetAppService_Update_CompatibilityInput
        {
            Id = id,
            Input = body!,
        };
    }

    private static ValueTask<object?> Bind_Delete_Async(HttpContext context, CancellationToken ct)
    {
        var routeId = context.Request.RouteValues["id"]?.ToString();
        if (routeId is null || !Guid.TryParse(routeId, out var id))
        {
            throw new BadHttpRequestException("The route value 'id' must be a valid System.Guid.");
        }

        var envelope = new WidgetAppService_Delete_CompatibilityInput
        {
            Id = id,
            IfMatch = Enumerable.FirstOrDefault(context.Request.Headers["If-Match"])?.Trim('"'),
        };
        return ValueTask.FromResult<object?>(envelope);
    }

    // ---- typed result mappers: the declared static payload, never a reflection fallback -----------

    private static object MapResult_Create(EndpointExecutionContext ctx, IServiceProvider serviceProvider)
        => CompatibilityHttpResultMapper.WrapResult<WidgetDto>(
            (WidgetDto)ctx.Output!,
            CapabilityEndpointJsonTypeInfoResolver.Require<DynamicApiResponse<WidgetDto>>(serviceProvider));

    private static object MapResult_Get(EndpointExecutionContext ctx, IServiceProvider serviceProvider)
        => CompatibilityHttpResultMapper.WrapGetResult<WidgetDto>(
            ctx.Output as WidgetDto,
            CapabilityEndpointJsonTypeInfoResolver.Require<DynamicApiResponse<WidgetDto>>(serviceProvider),
            CapabilityEndpointJsonTypeInfoResolver.Require<DynamicApiResponse>(serviceProvider));

    private static object MapResult_Search(EndpointExecutionContext ctx, IServiceProvider serviceProvider)
        => CompatibilityHttpResultMapper.WrapResult<PagedResultDto<WidgetDto>>(
            (PagedResultDto<WidgetDto>)ctx.Output!,
            CapabilityEndpointJsonTypeInfoResolver.Require<DynamicApiResponse<PagedResultDto<WidgetDto>>>(serviceProvider));

    private static object MapResult_Update(EndpointExecutionContext ctx, IServiceProvider serviceProvider)
        => CompatibilityHttpResultMapper.WrapResult<WidgetDto>(
            (WidgetDto)ctx.Output!,
            CapabilityEndpointJsonTypeInfoResolver.Require<DynamicApiResponse<WidgetDto>>(serviceProvider));

    private static object MapResult_Delete(EndpointExecutionContext ctx, IServiceProvider serviceProvider)
        => CompatibilityHttpResultMapper.WrapVoidResult(
            CapabilityEndpointJsonTypeInfoResolver.Require<DynamicApiResponse>(serviceProvider));

    // ---- invokers: the scoped AppService from the endpoint context's services + the managed token --

    private abstract class WidgetInvoker : ICapabilityContextAwareHandlerInvoker
    {
        /// <summary>The emitted invoker contract: the plain overload is not an entry point.</summary>
        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
            => throw new InvalidOperationException(
                "The generated CRUD invoker requires a CapabilityExecutionContext; the plain "
                + "ICapabilityHandlerInvoker overload is not an entry point.");

        public Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
            => InvokeCoreAsync(context, ct);

        protected abstract Task<object?> InvokeCoreAsync(CapabilityExecutionContext context, CancellationToken ct);
    }

    private sealed class Create_Invoker : WidgetInvoker
    {
        protected override async Task<object?> InvokeCoreAsync(CapabilityExecutionContext context, CancellationToken ct)
        {
            var service = context.ServiceProvider.GetRequiredService<IWidgetAppService>();
            var typedInput = (CreateWidgetDto)context.Input!;
            return await service.CreateAsync(typedInput, ct).ConfigureAwait(false);
        }
    }

    private sealed class Get_Invoker : WidgetInvoker
    {
        protected override async Task<object?> InvokeCoreAsync(CapabilityExecutionContext context, CancellationToken ct)
        {
            var service = context.ServiceProvider.GetRequiredService<IWidgetAppService>();
            var id = (Guid)context.Input!;
            return await service.GetByIdAsync(id, ct).ConfigureAwait(false);
        }
    }

    private sealed class Search_Invoker : WidgetInvoker
    {
        protected override async Task<object?> InvokeCoreAsync(CapabilityExecutionContext context, CancellationToken ct)
        {
            var service = context.ServiceProvider.GetRequiredService<IWidgetAppService>();
            var typedInput = (WidgetListRequestDto)context.Input!;
            return await service.GetListAsync(typedInput, ct).ConfigureAwait(false);
        }
    }

    private sealed class Update_Invoker : WidgetInvoker
    {
        protected override async Task<object?> InvokeCoreAsync(CapabilityExecutionContext context, CancellationToken ct)
        {
            var service = context.ServiceProvider.GetRequiredService<IWidgetAppService>();
            var envelope = (WidgetAppService_Update_CompatibilityInput)context.Input!;
            return await service.UpdateAsync(envelope.Id, envelope.Input, ct).ConfigureAwait(false);
        }
    }

    private sealed class Delete_Invoker : WidgetInvoker
    {
        protected override async Task<object?> InvokeCoreAsync(CapabilityExecutionContext context, CancellationToken ct)
        {
            var service = context.ServiceProvider.GetRequiredService<IWidgetAppService>();
            var envelope = (WidgetAppService_Delete_CompatibilityInput)context.Input!;
            await service.DeleteAsync(envelope.Id, envelope.IfMatch, ct).ConfigureAwait(false);
            return null;
        }
    }
}
