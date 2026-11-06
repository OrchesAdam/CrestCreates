#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CrestCreates.Tooling.HttpProjection;
using static CrestCreates.Tooling.CrudGeneration.CrudArtifactWriter;

namespace CrestCreates.Tooling.CrudGeneration;

/// <summary>
/// Capability wire artifacts: composed input envelopes and the per-service ExplicitHost contribution.
/// The CRUD front-end normalizes its action model onto the shared HTTP wire model, so CRUD and
/// hand-written AppService projections cannot drift in envelope/binder/invoker/result shape.
/// </summary>
internal static class CrudCapabilityEmitter
{
    internal static CrudArtifact EmitCapabilityInputEnvelope(CrudGenerationModel model, CrudActionModel action)
    {
        // #129 S2.2: the composed input envelope is declared by the shared wire backend, so the CRUD and
        // hand-written fronts cannot drift in envelope shape (S22A-R2 review).
        var name = model.EnvelopeName(action.Name);
        var builder = StartDocument(model);
        OpenNamespace(builder, model.EnvelopeNamespace);
        var wireAction = ToWireAction(model, action);
        foreach (var line in HttpWireEmitter.EmitEnvelopeDeclaration(name, wireAction.Parameters))
        {
            Line(builder, line);
        }

        CloseNamespace(builder);
        return Artifact(name + ".g.cs", builder);
    }

    /// <summary>
    /// The per-service ExplicitHost contribution: descriptors for the five actions, their typed input
    /// bindings, handler invokers and typed result envelopes, the body types the host must serialize and
    /// the required registry identities. Everything the runtime needs is baked into this immutable type;
    /// no module initializer, no static registry write and no IServiceProvider capture.
    /// </summary>
    internal static CrudArtifact EmitCapabilityContribution(CrudGenerationModel model)
    {
        // #129 S2.2: the CRUD front-end normalizes its action model onto the neutral HTTP wire model and
        // uses the same emitter as hand-written AppService projections; it no longer owns a copy of the
        // descriptors/binder/invoker/result rules.
        var wire = ToWireModel(model);
        return Artifact(model.ContributionTypeName + ".g.cs",
            new StringBuilder(HttpWireEmitter.EmitContribution(wire)));
    }

    /// <summary>#129 S2.2: CRUD action model → shared HTTP wire model.</summary>
    private static HttpWireModel ToWireModel(CrudGenerationModel model) => new()
    {
        OwnerAssemblyName = model.OwnerAssemblyName,
        ContractFullName = Qualified(model.InterfaceNamespace + "." + model.ServiceInterfaceName),
        ContributionTypeName = model.ContributionTypeName,
        JsonContextFullName = Qualified(CrudJsonContractConvention.JsonContextFullName),
        DiagnosticsTypeName = model.EntityClassName,
        Actions = model.Actions.Select(action => ToWireAction(model, action)).ToArray(),
        ExtraJsonRoots = new[]
        {
            new HttpWireJsonRoot(
                "global::Microsoft.AspNetCore.Mvc.ProblemDetails", "*", "ErrorRoot"),
        },
    };

    internal static HttpWireAction ToWireAction(
        CrudGenerationModel model,
        CrudActionModel action)
    {
        var parameters = new List<HttpWireParameter>();
        var arguments = new List<HttpWireArgument>();
        if (action.RouteIdentityParameter is not null)
        {
            parameters.Add(new HttpWireParameter
            {
                Name = action.RouteIdentityParameter,
                Source = "Route",
                TypeFullName = model.IdTypeDisplay,
                ClrTypeFullName = model.IdTypeDisplay,
                Required = true,
                ScalarKind = IdScalarKind(model.IdWireKind),
                MemberName = CrudEnvelopeConvention.RouteIdentityMember,
                LocalName = "id",
            });
            arguments.Add(action.UsesCapabilityInputEnvelope
                ? new HttpWireArgument(
                    HttpWireArgumentKind.EnvelopeMember,
                    CrudEnvelopeConvention.RouteIdentityMember)
                : new HttpWireArgument(
                    HttpWireArgumentKind.Local,
                    "singleInput"));
        }

        if (action.BodyPayloadFullName is not null)
        {
            parameters.Add(new HttpWireParameter
            {
                Name = "input",
                Source = "Body",
                TypeFullName = Qualified(action.BodyPayloadFullName),
                ClrTypeFullName = Qualified(action.BodyPayloadFullName),
                Required = true,
                ScalarKind = AppServiceHttpScalarKind.NotScalar,
                MemberName = CrudEnvelopeConvention.BodyMember,
                LocalName = "body",
                NeedsDefaultInitializer = true,
            });
            arguments.Add(action.UsesCapabilityInputEnvelope
                ? new HttpWireArgument(
                    HttpWireArgumentKind.EnvelopeMember,
                    CrudEnvelopeConvention.BodyMember)
                : new HttpWireArgument(
                    HttpWireArgumentKind.Local,
                    "singleInput"));
        }

        foreach (var header in action.HeaderInputs)
        {
            parameters.Add(new HttpWireParameter
            {
                Name = header.HeaderName,
                Source = "Header",
                TypeFullName = "global::System.String?",
                ClrTypeFullName = "global::System.String",
                Required = header.Required,
                ScalarKind = AppServiceHttpScalarKind.String,
                MemberName = header.MemberName,
                HeaderName = header.HeaderName,
                TrimQuotes = header.TrimQuotes,
            });
            arguments.Add(new HttpWireArgument(
                HttpWireArgumentKind.EnvelopeMember,
                header.MemberName));
        }

        arguments.Add(new HttpWireArgument(
            HttpWireArgumentKind.CancellationToken,
            "ct"));

        var payload = ResultPayloadDisplay(action);
        return new HttpWireAction
        {
            Name = action.Name,
            ContractMethodName = action.ServiceMethodName,
            HttpMethod = action.HttpMethod,
            RoutePattern = action.RoutePattern,
            OperationKind = action.OperationKind == CrudOperationKind.Query
                ? AppServiceHttpOperationKind.Query
                : AppServiceHttpOperationKind.Command,
            Permissions = new[] { action.PermissionName },
            CapabilityId = action.CapabilityId,
            EndpointId = action.EndpointId,
            Parameters = parameters,
            UsesEnvelope = action.UsesCapabilityInputEnvelope,
            EnvelopeFullName = action.UsesCapabilityInputEnvelope ? model.EnvelopeFullName(action.Name) : null,
            InvokerArguments = arguments,
            BodyTypeFullName = action.BodyPayloadFullName is null ? null : Qualified(action.BodyPayloadFullName),
            BodyOptional = false,
            ResultPayloadFullName = payload,
            ResultKind = payload is null
                ? AppServiceHttpResultKind.TaskVoid
                : AppServiceHttpResultKind.TaskValue,
            ResultIsValueType = false,
            MapNotFoundOntoNull = payload is not null && string.Equals(action.HttpMethod, "GET", StringComparison.Ordinal),
            RequiredIdentityTypeName = model.EntityClassName,
        };
    }

    private static AppServiceHttpScalarKind IdScalarKind(CrudIdWireKind kind) => kind switch
    {
        CrudIdWireKind.Guid => AppServiceHttpScalarKind.Guid,
        CrudIdWireKind.Int16 => AppServiceHttpScalarKind.Int16,
        CrudIdWireKind.Int32 => AppServiceHttpScalarKind.Int32,
        CrudIdWireKind.Int64 => AppServiceHttpScalarKind.Int64,
        CrudIdWireKind.UInt16 => AppServiceHttpScalarKind.UInt16,
        CrudIdWireKind.UInt32 => AppServiceHttpScalarKind.UInt32,
        CrudIdWireKind.UInt64 => AppServiceHttpScalarKind.UInt64,
        CrudIdWireKind.Byte => AppServiceHttpScalarKind.Byte,
        CrudIdWireKind.SByte => AppServiceHttpScalarKind.SByte,
        _ => AppServiceHttpScalarKind.String,
    };

    /// <summary>
    /// The payload type of one action's result, with paged results mapped onto the shared paged
    /// envelope. <c>null</c> for void actions.
    /// </summary>
    private static string? ResultPayloadDisplay(CrudActionModel action) =>
        action.ResultPayloadFullName is null
            ? null
            : action.UsesPagedResult
                ? CrudResponseContract.PagedPayload(action.ResultPayloadFullName)
                : Qualified(action.ResultPayloadFullName);
}
