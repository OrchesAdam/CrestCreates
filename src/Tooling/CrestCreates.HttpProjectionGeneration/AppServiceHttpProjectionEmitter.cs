#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CrestCreates.Tooling.HttpProjection;

/// <summary>
/// Issue #129 S2.2: turns the frozen <see cref="AppServiceHttpProjectionModel"/> into ordinary Compile
/// artifacts. Nothing here registers through a module initializer or a process-global registry: the
/// host activates the generated contribution explicitly through AddGeneratedAppServiceHttp, exactly
/// like the #128 CRUD surface.
/// </summary>
public static class AppServiceHttpProjectionEmitter
{
    public const string GeneratedNamespace = "CrestCreates.Generated";

    private const string HttpNamespace = "CrestCreates.Generated.HttpProjection";

    private const string CapabilityAbstractions = "global::CrestCreates.Capability.Abstractions.";
    private const string DynamicApi = "global::CrestCreates.DynamicApi.";

    public static IReadOnlyList<AppServiceHttpArtifact> Emit(
        AppServiceHttpProjectionModel model,
        AppServiceHttpEmissionOptions options)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var token = AppServiceHttpWireConvention.CanonicalToken(model.ContractFullName);
        var artifacts = new List<AppServiceHttpArtifact>
        {
            EmitEnvelopes(model, token),
            EmitContribution(model, options, token),
            EmitActivation(model, options, token),
        };
        return artifacts;
    }

    /// <summary>
    /// Manifest entries for this projection. Declared types are only the types the emitted artifacts
    /// declare; referenced contract/DTO identities stay in their own field (addendum §3.3, CCCRUD008).
    /// </summary>
    public static AppServiceHttpManifestEntry BuildManifestEntry(
        AppServiceHttpProjectionModel model,
        AppServiceHttpEmissionOptions options)
    {
        var token = AppServiceHttpWireConvention.CanonicalToken(model.ContractFullName);
        var declared = new List<string>
        {
            HttpNamespace + "." + options.ContributionTypeName,
            HttpNamespace + "." + options.HandlerModuleTypeName,
            HttpNamespace + "." + options.ActivationTypeName,
        };
        foreach (var action in model.Actions)
        {
            declared.Add(HttpNamespace + "." + InvokerName(model, action));
        }

        declared.AddRange(model.Actions
            .Where(action => action.InputEnvelopeFullName is not null)
            .Select(action => action.InputEnvelopeFullName!));

        return new AppServiceHttpManifestEntry(
            OwnerAssemblyName: model.HttpOwnerAssemblyName,
            OwnerServiceFullName: model.ContractFullName,
            ContractAssemblyName: model.ContractAssemblyName,
            ArtifactHintName: "GeneratedAppServiceHttpContribution_" + token + ".g.cs",
            DeclaredTypeFullNames: declared.Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            ReferencedTypeFullNames: model.ReferencedTypes
                .Select(reference => reference.FullMetadataName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray(),
            SourceLocation: model.DeclarationLocation,
            ModelSchemaVersion: AppServiceHttpProjectionModel.ModelSchemaVersion);
    }

    private static AppServiceHttpArtifact EmitEnvelopes(AppServiceHttpProjectionModel model, string token)
    {
        var builder = StartDocument("envelopes", model);
        OpenNamespace(builder, GeneratedNamespace);
        foreach (var action in model.Actions.Where(action => action.InputEnvelopeFullName is not null))
        {
            var typeName = action.InputEnvelopeFullName!.Substring(action.InputEnvelopeFullName!.LastIndexOf('.') + 1);
            Line(builder, Indent + "internal sealed class " + typeName);
            Line(builder, Indent + "{");
            if (action.RouteIdentityParameterName is not null)
            {
                var route = action.ScalarInputs.Single(input => input.Binding == "route");
                Line(builder, NestedIndent + "public " + route.TypeFullName + " "
                    + CrudEnvelopeMemberRouteIdentity + " { get; init; } = default!;");
            }

            foreach (var input in action.ScalarInputs.Where(input => input.Binding == "query"))
            {
                Line(builder, NestedIndent + "public " + input.TypeFullName + " "
                    + Pascal(input.Name) + " { get; init; } = default!;");
            }

            if (action.BodyTypeFullName is not null)
            {
                Line(builder, NestedIndent + "public " + action.BodyTypeFullName + " "
                    + CrudEnvelopeMemberBody + " { get; init; } = default!;");
            }

            Line(builder, Indent + "}");
            Line(builder, string.Empty);
        }

        CloseNamespace(builder);
        return new AppServiceHttpArtifact("GeneratedAppServiceHttpEnvelopes_" + token + ".g.cs", builder.ToString());
    }

    private static AppServiceHttpArtifact EmitContribution(
        AppServiceHttpProjectionModel model,
        AppServiceHttpEmissionOptions options,
        string token)
    {
        var builder = StartDocument("contribution", model);
        OpenNamespace(builder, HttpNamespace);
        Line(builder, Indent + "internal sealed class " + options.ContributionTypeName
            + " : " + DynamicApi + "CapabilityEndpointContribution");
        Line(builder, Indent + "{");
        Line(builder, NestedIndent + "public " + options.ContributionTypeName + "()");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + Indent + "OwnerAssemblyName = \"" + Escape(model.HttpOwnerAssemblyName) + "\";");
        EmitDescriptors(builder, model);
        EmitEndpointDescriptors(builder, model);
        EmitBindings(builder, model);
        EmitHandlers(builder, model);
        EmitResultBindings(builder, model);
        EmitBodyTypes(builder, model);
        EmitJsonContract(builder, model, options);
        EmitRequiredIdentities(builder, model);
        Line(builder, NestedIndent + "}");
        Line(builder, string.Empty);
        foreach (var action in model.Actions)
        {
            EmitBinder(builder, model, action);
            EmitResultMapper(builder, action);
        }

        Line(builder, Indent + "}");
        Line(builder, string.Empty);
        foreach (var action in model.Actions)
        {
            EmitInvoker(builder, model, action);
        }

        CloseNamespace(builder);
        return new AppServiceHttpArtifact("GeneratedAppServiceHttpContribution_" + token + ".g.cs", builder.ToString());
    }

    private static AppServiceHttpArtifact EmitActivation(
        AppServiceHttpProjectionModel model,
        AppServiceHttpEmissionOptions options,
        string token)
    {
        var builder = StartDocument("activation", model);
        OpenNamespace(builder, HttpNamespace, new[]
        {
            "using Microsoft.Extensions.DependencyInjection;",
            "using Microsoft.Extensions.DependencyInjection.Extensions;",
        });
        Line(builder, Indent + "/// <summary>");
        Line(builder, Indent + "/// #129 ExplicitHost activation of " + model.ContractFullName + " (owner "
            + model.HttpOwnerAssemblyName + ").");
        Line(builder, Indent + "/// The host calls AddGeneratedAppServiceHttp for exactly the assemblies it activates; the call");
        Line(builder, Indent + "/// is idempotent by construction and never writes a process-global registry.");
        Line(builder, Indent + "/// </summary>");
        Line(builder, Indent + "public static class " + options.ActivationTypeName);
        Line(builder, Indent + "{");
        Line(builder, NestedIndent + "public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection "
            + AppServiceHttpProjectionModel.ActivationMethodName + "(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + Indent + "global::System.ArgumentNullException.ThrowIfNull(services);");
        Line(builder, NestedIndent + Indent
            + "global::CrestCreates.DynamicApi.CapabilityEndpointExtensions.AddCrestCapabilityEndpointContribution(");
        Line(builder, NestedIndent + Indent + Indent + "services,");
        Line(builder, NestedIndent + Indent + Indent + "new " + options.ContributionTypeName + "());");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + Indent
            + "global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddEnumerable(");
        Line(builder, NestedIndent + Indent + Indent + "services,");
        Line(builder, NestedIndent + Indent + Indent
            + "global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<"
            + CapabilityAbstractions + "ICapabilityHandlerModule,");
        Line(builder, NestedIndent + Indent + Indent + Indent + options.HandlerModuleTypeName + ">());");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + Indent
            + "global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddEnumerable(");
        Line(builder, NestedIndent + Indent + Indent + "services,");
        Line(builder, NestedIndent + Indent + Indent
            + "global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<"
            + "global::Microsoft.Extensions.Options.IConfigureOptions<global::Microsoft.AspNetCore.Http.Json.JsonOptions>,");
        Line(builder, NestedIndent + Indent + Indent + Indent + options.JsonOptionsConfiguratorTypeName + ">());");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + Indent + "return services;");
        Line(builder, NestedIndent + "}");
        Line(builder, Indent + "}");
        Line(builder, string.Empty);
        EmitHandlerModule(builder, model, options);
        Line(builder, string.Empty);
        EmitJsonConfigurator(builder, options);
        CloseNamespace(builder);
        return new AppServiceHttpArtifact("GeneratedAppServiceHttpActivation_" + token + ".g.cs", builder.ToString());
    }

    private static void EmitDescriptors(StringBuilder builder, AppServiceHttpProjectionModel model)
    {
        Line(builder, NestedIndent + Indent + "CapabilityDescriptors = new global::CrestCreates.Metadata.CapabilityDescriptor[]");
        Line(builder, NestedIndent + Indent + "{");
        foreach (var action in model.Actions)
        {
            Line(builder, QuadIndent + "new global::CrestCreates.Metadata.CapabilityDescriptor");
            Line(builder, QuadIndent + "{");
            Line(builder, QuintIndent + "Namespace = \"capability\",");
            Line(builder, QuintIndent + "Id = \"" + Escape(action.CapabilityId) + "\",");
            Line(builder, QuintIndent + "Name = \"" + Escape(action.Name) + "\",");
            Line(builder, QuintIndent + "State = global::CrestCreates.Metadata.Abstractions.DescriptorState.Active,");
            Line(builder, QuintIndent + "Version = 1,");
            Line(builder, QuintIndent + "CapabilityKind = global::CrestCreates.Metadata.Abstractions.DescriptorCapability.CapabilityKind."
                + (action.OperationKind == AppServiceHttpOperationKind.Query ? "Query," : "Command,"));
            Line(builder, QuintIndent + "Permissions = " + StringArray(action.Permissions) + ",");
            Line(builder, QuintIndent + "RiskLevel = global::CrestCreates.Metadata.Abstractions.DescriptorCapability.CapabilityRiskLevel.Medium,");
            Line(builder, QuintIndent + "InputSchema = null,");
            Line(builder, QuintIndent + "OutputSchema = null,");
            Line(builder, QuintIndent + "ProjectionKind = global::CrestCreates.Metadata.Abstractions.DescriptorCapability.CapabilityProjectionKind.AppServiceCompatibility,");
            Line(builder, QuadIndent + "},");
        }

        Line(builder, NestedIndent + Indent + "};");
    }

    private static void EmitEndpointDescriptors(StringBuilder builder, AppServiceHttpProjectionModel model)
    {
        Line(builder, NestedIndent + Indent + "EndpointDescriptors = new " + DynamicApi + "CapabilityEndpointDescriptor[]");
        Line(builder, NestedIndent + Indent + "{");
        foreach (var action in model.Actions)
        {
            Line(builder, QuadIndent + "new " + DynamicApi + "CapabilityEndpointDescriptor");
            Line(builder, QuadIndent + "{");
            Line(builder, QuintIndent + "Id = \"" + Escape(action.EndpointId) + "\",");
            Line(builder, QuintIndent + "Name = \"" + Escape(action.Name) + "\",");
            Line(builder, QuintIndent + "Version = 1,");
            Line(builder, QuintIndent + "State = global::CrestCreates.Metadata.Abstractions.DescriptorState.Active,");
            Line(builder, QuintIndent + "Capability = new global::CrestCreates.Metadata.Abstractions.VersionedDescriptorRef<global::CrestCreates.Metadata.CapabilityDescriptor>(\""
                + Escape(action.CapabilityId) + "\", 1),");
            Line(builder, QuintIndent + "HttpMethod = " + DynamicApi + "CapabilityEndpointHttpMethod."
                + EnumMethod(action.HttpMethod) + ",");
            Line(builder, QuintIndent + "RoutePattern = \"" + Escape(FullRoute(model, action)) + "\",");
            Line(builder, QuintIndent + "AuthorizationMode = " + DynamicApi + "CapabilityEndpointAuthorizationMode.InheritCapability,");
            Line(builder, QuintIndent + "InputBindings = new " + DynamicApi + "CapabilityEndpointInputBinding[]");
            Line(builder, QuintIndent + "{");
            foreach (var input in InputBindings(action))
            {
                Line(builder, SextIndent + "new " + DynamicApi + "CapabilityEndpointInputBinding");
                Line(builder, SextIndent + "{");
                Line(builder, SextIndent + Indent + "Name = \"" + Escape(input.Name) + "\",");
                Line(builder, SextIndent + Indent + "Source = " + DynamicApi + "CapabilityEndpointParameterSource." + input.Source + ",");
                Line(builder, SextIndent + Indent + "Required = " + (input.Required ? "true" : "false") + ",");
                Line(builder, SextIndent + "},");
            }

            Line(builder, QuintIndent + "},");
            Line(builder, QuintIndent + "OutputMapping = new " + DynamicApi + "CapabilityEndpointOutputMapping { SuccessStatusCode = 200 },");
            Line(builder, QuintIndent + "Projection = new " + DynamicApi + "CapabilityEndpointProjectionMetadata");
            Line(builder, QuintIndent + "{");
            Line(builder, QuintIndent + Indent + "OperationId = \"" + Escape(action.CapabilityId.Replace('.', '_')) + "\",");
            Line(builder, QuintIndent + "},");
            Line(builder, QuadIndent + "},");
        }

        Line(builder, NestedIndent + Indent + "};");
    }

    private static void EmitBindings(StringBuilder builder, AppServiceHttpProjectionModel model)
    {
        Line(builder, NestedIndent + Indent + "Bindings = new " + DynamicApi + "CapabilityEndpointBindingContract[]");
        Line(builder, NestedIndent + Indent + "{");
        foreach (var action in model.Actions)
        {
            Line(builder, QuadIndent + "new " + DynamicApi + "CapabilityEndpointBindingContract(");
            Line(builder, QuintIndent + "\"" + Escape(action.EndpointId) + "\",");
            Line(builder, QuintIndent + "1,");
            Line(builder, QuintIndent + BinderName(action) + "),");
        }

        Line(builder, NestedIndent + Indent + "};");
    }

    private static void EmitHandlers(StringBuilder builder, AppServiceHttpProjectionModel model)
    {
        Line(builder, NestedIndent + Indent + "Handlers = new " + DynamicApi + "CapabilityEndpointHandlerBinding[]");
        Line(builder, NestedIndent + Indent + "{");
        foreach (var action in model.Actions)
        {
            Line(builder, QuadIndent + "new " + DynamicApi + "CapabilityEndpointHandlerBinding(");
            Line(builder, QuintIndent + "\"" + Escape(action.CapabilityId) + "\",");
            Line(builder, QuintIndent + "new " + InvokerName(model, action) + "()),");
        }

        Line(builder, NestedIndent + Indent + "};");
    }

    private static void EmitResultBindings(StringBuilder builder, AppServiceHttpProjectionModel model)
    {
        Line(builder, NestedIndent + Indent + "ResultMappers = new " + DynamicApi + "CapabilityEndpointResultBinding[]");
        Line(builder, NestedIndent + Indent + "{");
        foreach (var action in model.Actions)
        {
            Line(builder, QuadIndent + "new " + DynamicApi + "CapabilityEndpointResultBinding(");
            Line(builder, QuintIndent + "\"" + Escape(action.EndpointId) + "\",");
            Line(builder, QuintIndent + "1,");
            Line(builder, QuintIndent + MapperName(action) + "),");
        }

        Line(builder, NestedIndent + Indent + "};");
    }

    private static void EmitBodyTypes(StringBuilder builder, AppServiceHttpProjectionModel model)
    {
        var bodyTypes = model.Actions
            .Where(action => action.BodyTypeFullName is not null)
            .Select(action => action.BodyTypeFullName!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Line(builder, NestedIndent + Indent + "BodyTypes = new global::System.Type[]");
        Line(builder, NestedIndent + Indent + "{");
        foreach (var bodyType in bodyTypes)
        {
            Line(builder, QuadIndent + "typeof(" + bodyType + "),");
        }

        Line(builder, NestedIndent + Indent + "};");
    }

    private static void EmitJsonContract(
        StringBuilder builder,
        AppServiceHttpProjectionModel model,
        AppServiceHttpEmissionOptions options)
    {
        Line(builder, NestedIndent + Indent + "JsonContext = " + options.JsonContextFullName + ".Default;");
        Line(builder, NestedIndent + Indent + "RequiredJsonRootTypes = new " + DynamicApi + "CapabilityEndpointJsonRootType[]");
        Line(builder, NestedIndent + Indent + "{");
        foreach (var root in JsonRoots(model))
        {
            Line(builder, QuadIndent + "new " + DynamicApi + "CapabilityEndpointJsonRootType(typeof(" + root.Type + "), \""
                + Escape(root.Action) + "\", \"" + root.Component + "\"),");
        }

        Line(builder, NestedIndent + Indent + "};");
    }

    private static void EmitRequiredIdentities(StringBuilder builder, AppServiceHttpProjectionModel model)
    {
        Line(builder, NestedIndent + Indent + "RequiredIdentityIds = new " + DynamicApi + "CapabilityEndpointRequiredIdentity[]");
        Line(builder, NestedIndent + Indent + "{");
        foreach (var action in model.Actions)
        {
            Line(builder, QuadIndent + "new " + DynamicApi + "CapabilityEndpointRequiredIdentity(");
            Line(builder, QuintIndent + "\"" + Escape(action.EndpointId) + "\",");
            Line(builder, QuintIndent + "\"" + Escape(model.HttpOwnerAssemblyName) + "\",");
            Line(builder, QuintIndent + "\"" + Escape(action.Name) + "\",");
            Line(builder, QuintIndent + "\"" + Escape(model.ContractName) + "\",");
            Line(builder, QuintIndent + "1),");
        }

        Line(builder, NestedIndent + Indent + "};");
    }

    private static void EmitBinder(StringBuilder builder, AppServiceHttpProjectionModel model, AppServiceHttpActionModel action)
    {
        var hasBody = action.BodyTypeFullName is not null;
        Line(builder, NestedIndent + "private static "
            + (hasBody ? "async " : string.Empty)
            + "global::System.Threading.Tasks.ValueTask<object?> " + BinderName(action) + "(");
        Line(builder, NestedIndent + Indent + "global::Microsoft.AspNetCore.Http.HttpContext context,");
        Line(builder, NestedIndent + Indent + "global::System.Threading.CancellationToken ct)");
        Line(builder, NestedIndent + "{");

        if (action.RouteIdentityParameterName is not null)
        {
            var route = action.ScalarInputs.Single(input => input.Binding == "route");
            EmitParse(builder, route, "context.Request.RouteValues[\"" + Escape(route.Name) + "\"]?.ToString()", "route");
        }

        foreach (var query in action.ScalarInputs.Where(input => input.Binding == "query"))
        {
            EmitParse(builder, query, "context.Request.Query[\"" + Escape(query.Name) + "\"].ToString()", "query");
        }

        string? bodyExpression = null;
        if (hasBody)
        {
            Line(builder, NestedIndent + Indent + "var bodyTypeInfo = " + DynamicApi + "CapabilityEndpointJsonTypeInfoResolver.Require<"
                + action.BodyTypeFullName + ">(context.RequestServices);");
            var optional = action.BodyPolicy == AppServiceHttpBodyPolicy.Optional ? "true" : "false";
            Line(builder, NestedIndent + Indent + "var body = await " + DynamicApi + "CapabilityEndpointBodyReader.ReadNativeBodyAsync(");
            Line(builder, NestedIndent + Indent + Indent + "context, bodyTypeInfo, optional: " + optional + ", ct);");
            bodyExpression = "body!";
        }

        if (action.InputEnvelopeFullName is not null)
        {
            var envelopeName = "global::" + action.InputEnvelopeFullName;
            Line(builder, NestedIndent + Indent + "var envelope = new " + envelopeName);
            Line(builder, NestedIndent + Indent + "{");
            if (action.RouteIdentityParameterName is not null)
            {
                Line(builder, NestedIndent + Indent + Indent + CrudEnvelopeMemberRouteIdentity + " = " + Local(action.RouteIdentityParameterName) + ",");
            }

            foreach (var query in action.ScalarInputs.Where(input => input.Binding == "query"))
            {
                Line(builder, NestedIndent + Indent + Indent + Pascal(query.Name) + " = " + Local(query.Name) + ",");
            }

            if (bodyExpression is not null)
            {
                Line(builder, NestedIndent + Indent + Indent + CrudEnvelopeMemberBody + " = " + bodyExpression + ",");
            }

            Line(builder, NestedIndent + Indent + "};");
            Line(builder, NestedIndent + Indent + (hasBody ? "return envelope;" : "return global::System.Threading.Tasks.ValueTask.FromResult<object?>(envelope);"));
        }
        else if (bodyExpression is not null)
        {
            Line(builder, NestedIndent + Indent + "return body;");
        }
        else if (action.RouteIdentityParameterName is not null)
        {
            Line(builder, NestedIndent + Indent + "return global::System.Threading.Tasks.ValueTask.FromResult<object?>("
                + Local(action.RouteIdentityParameterName) + ");");
        }
        else
        {
            Line(builder, NestedIndent + Indent + "return global::System.Threading.Tasks.ValueTask.FromResult<object?>(null);");
        }

        Line(builder, NestedIndent + "}");
        Line(builder, string.Empty);
    }

    private static void EmitParse(StringBuilder builder, AppServiceHttpInputParameter input, string expression, string source)
    {
        var local = Local(input.Name);
        var type = input.TypeFullName;
        var failure = "throw new global::Microsoft.AspNetCore.Http.BadHttpRequestException("
            + "\"The " + source + " value '" + Escape(input.Name) + "' is missing or not a valid "
            + Escape(MessageType(type)) + ".\");";
        switch (type)
        {
            case "global::System.Guid":
                Line(builder, NestedIndent + Indent + "var " + local + "Text = " + expression + ";");
                Line(builder, NestedIndent + Indent + "if (" + local + "Text is null || !global::System.Guid.TryParse(" + local + "Text, out var " + local + "))");
                Line(builder, NestedIndent + Indent + "{");
                Line(builder, NestedIndent + Indent + Indent + failure);
                Line(builder, NestedIndent + Indent + "}");
                break;
            case "global::System.String":
                Line(builder, NestedIndent + Indent + "var " + local + " = " + expression + ";");
                Line(builder, NestedIndent + Indent + "if (global::System.String.IsNullOrEmpty(" + local + "))");
                Line(builder, NestedIndent + Indent + "{");
                Line(builder, NestedIndent + Indent + Indent + failure);
                Line(builder, NestedIndent + Indent + "}");
                break;
            case "global::System.Int32":
                EmitTryParse(builder, "global::System.Int32", local, expression, failure);
                break;
            case "global::System.Int64":
                EmitTryParse(builder, "global::System.Int64", local, expression, failure);
                break;
            case "global::System.Boolean":
                EmitTryParse(builder, "global::System.Boolean", local, expression, failure);
                break;
            case "global::System.Decimal":
                EmitTryParse(builder, "global::System.Decimal", local, expression, failure);
                break;
            default:
                throw new InvalidOperationException(
                    AppServiceHttpDiagnosticCodes.UnsupportedDeclaration + ": scalar type '" + type
                    + "' has no bounded HTTP binder in the hand-written projection.");
        }
    }

    private static void EmitTryParse(StringBuilder builder, string typeName, string local, string expression, string failure)
    {
        Line(builder, NestedIndent + Indent + "var " + local + "Text = " + expression + ";");
        Line(builder, NestedIndent + Indent + "if (global::System.String.IsNullOrEmpty(" + local + "Text) || !" + typeName
            + ".TryParse(" + local + "Text, out var " + local + "))");
        Line(builder, NestedIndent + Indent + "{");
        Line(builder, NestedIndent + Indent + Indent + failure);
        Line(builder, NestedIndent + Indent + "}");
    }

    private static void EmitResultMapper(StringBuilder builder, AppServiceHttpActionModel action)
    {
        var payload = action.ResultTypeFullName;
        var voidEnvelope = DynamicApi + "DynamicApiResponse";
        var require = DynamicApi + "CapabilityEndpointJsonTypeInfoResolver.Require";
        var mapper = DynamicApi + "CompatibilityHttpResultMapper";
        string expression;
        if (payload is null)
        {
            expression = mapper + ".WrapVoidResult(" + require + "<" + voidEnvelope + ">(serviceProvider))";
        }
        else if (string.Equals(action.HttpMethod, "GET", StringComparison.Ordinal))
        {
            expression = mapper + ".WrapGetResult<" + payload + ">(ctx.Output as " + payload + ", "
                + require + "<" + DynamicApi + "DynamicApiResponse<" + payload + ">>(serviceProvider), "
                + require + "<" + voidEnvelope + ">(serviceProvider))";
        }
        else
        {
            expression = mapper + ".WrapResult<" + payload + ">((" + payload + ")ctx.Output!, "
                + require + "<" + DynamicApi + "DynamicApiResponse<" + payload + ">>(serviceProvider))";
        }

        Line(builder, NestedIndent + "private static object " + MapperName(action) + "(");
        Line(builder, NestedIndent + Indent + DynamicApi + "EndpointExecutionContext ctx,");
        Line(builder, NestedIndent + Indent + "global::System.IServiceProvider serviceProvider)");
        Line(builder, NestedIndent + Indent + "=> " + expression + ";");
        Line(builder, string.Empty);
    }

    private static void EmitInvoker(StringBuilder builder, AppServiceHttpProjectionModel model, AppServiceHttpActionModel action)
    {
        Line(builder, Indent + "internal sealed class " + InvokerName(model, action)
            + " : " + CapabilityAbstractions + "ICapabilityContextAwareHandlerInvoker");
        Line(builder, Indent + "{");
        Line(builder, NestedIndent + "public async global::System.Threading.Tasks.Task<object?> InvokeAsync(");
        Line(builder, NestedIndent + Indent + CapabilityAbstractions + "CapabilityExecutionContext context,");
        Line(builder, NestedIndent + Indent + "global::System.Threading.CancellationToken ct)");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + Indent + "var service = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<"
            + model.ContractFullName + ">(context.ServiceProvider);");
        var arguments = new List<string>();
        if (action.InputEnvelopeFullName is not null)
        {
            Line(builder, NestedIndent + Indent + "var envelope = (global::" + action.InputEnvelopeFullName + ")context.Input!;");
            if (action.RouteIdentityParameterName is not null)
            {
                arguments.Add("envelope." + CrudEnvelopeMemberRouteIdentity);
            }

            foreach (var query in action.ScalarInputs.Where(input => input.Binding == "query"))
            {
                arguments.Add("envelope." + Pascal(query.Name));
            }

            if (action.BodyTypeFullName is not null)
            {
                arguments.Add("envelope." + CrudEnvelopeMemberBody);
            }
        }
        else if (action.BodyTypeFullName is not null)
        {
            Line(builder, NestedIndent + Indent + "var typedInput = (" + action.BodyTypeFullName + ")context.Input!;");
            arguments.Add("typedInput");
        }
        else if (action.RouteIdentityParameterName is not null)
        {
            var route = action.ScalarInputs.Single(input => input.Binding == "route");
            Line(builder, NestedIndent + Indent + "var routeValue = (" + route.TypeFullName + ")context.Input!;");
            arguments.Add("routeValue");
        }

        if (action.CancellationTokenParameterName is not null)
        {
            arguments.Add("ct");
        }

        var invocation = "service." + action.Name + "(" + string.Join(", ", arguments) + ")";
        if (action.ResultTypeFullName is null)
        {
            Line(builder, NestedIndent + Indent + "await " + invocation + ".ConfigureAwait(false);");
            Line(builder, NestedIndent + Indent + "return null;");
        }
        else
        {
            Line(builder, NestedIndent + Indent + "return await " + invocation + ".ConfigureAwait(false);");
        }

        Line(builder, NestedIndent + "}");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "public global::System.Threading.Tasks.Task<object?> InvokeAsync(object? input, global::System.Threading.CancellationToken ct)");
        Line(builder, NestedIndent + Indent + "=> throw new global::System.InvalidOperationException(");
        Line(builder, NestedIndent + Indent + Indent + "\"The generated hand-written projection invoker requires a CapabilityExecutionContext.\");");
        Line(builder, Indent + "}");
        Line(builder, string.Empty);
    }

    private static void EmitHandlerModule(
        StringBuilder builder,
        AppServiceHttpProjectionModel model,
        AppServiceHttpEmissionOptions options)
    {
        Line(builder, Indent + "internal sealed class " + options.HandlerModuleTypeName
            + " : " + CapabilityAbstractions + "ICapabilityHandlerModule");
        Line(builder, Indent + "{");
        Line(builder, NestedIndent + "public string Id => \"" + Escape(model.HttpOwnerAssemblyName) + "\";");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "public void Apply(" + CapabilityAbstractions + "CapabilityHandlerResolver resolver)");
        Line(builder, NestedIndent + "{");
        foreach (var action in model.Actions)
        {
            Line(builder, NestedIndent + Indent + "resolver.Register(\"" + Escape(action.CapabilityId) + "\", new "
                + InvokerName(model, action) + "());");
        }

        Line(builder, NestedIndent + "}");
        Line(builder, Indent + "}");
    }

    private static void EmitJsonConfigurator(StringBuilder builder, AppServiceHttpEmissionOptions options)
    {
        Line(builder, Indent + "internal sealed class " + options.JsonOptionsConfiguratorTypeName
            + " : global::Microsoft.Extensions.Options.IConfigureOptions<global::Microsoft.AspNetCore.Http.Json.JsonOptions>");
        Line(builder, Indent + "{");
        Line(builder, NestedIndent + "public void Configure(global::Microsoft.AspNetCore.Http.Json.JsonOptions options)");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + Indent + "var generatedContext = " + options.JsonContextFullName + ".Default;");
        Line(builder, NestedIndent + Indent + "var chain = options.SerializerOptions.TypeInfoResolverChain;");
        Line(builder, NestedIndent + Indent + "for (var index = 0; index < chain.Count; index++)");
        Line(builder, NestedIndent + Indent + "{");
        Line(builder, NestedIndent + Indent + Indent + "if (global::System.Object.ReferenceEquals(chain[index], generatedContext))");
        Line(builder, NestedIndent + Indent + Indent + "{");
        Line(builder, NestedIndent + Indent + Indent + Indent + "return;");
        Line(builder, NestedIndent + Indent + Indent + "}");
        Line(builder, NestedIndent + Indent + "}");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + Indent + "var insertionIndex = chain.Count;");
        Line(builder, NestedIndent + Indent + "for (var index = 0; index < chain.Count; index++)");
        Line(builder, NestedIndent + Indent + "{");
        Line(builder, NestedIndent + Indent + Indent
            + "if (chain[index] is global::System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver defaultResolver");
        Line(builder, NestedIndent + Indent + Indent + Indent
            + "&& defaultResolver.GetType() == typeof(global::System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver)");
        Line(builder, NestedIndent + Indent + Indent + Indent + "&& defaultResolver.Modifiers.Count == 0)");
        Line(builder, NestedIndent + Indent + Indent + "{");
        Line(builder, NestedIndent + Indent + Indent + Indent + "insertionIndex = index;");
        Line(builder, NestedIndent + Indent + Indent + Indent + "break;");
        Line(builder, NestedIndent + Indent + Indent + "}");
        Line(builder, NestedIndent + Indent + "}");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + Indent + "chain.Insert(insertionIndex, generatedContext);");
        Line(builder, NestedIndent + "}");
        Line(builder, Indent + "}");
    }

    private static IEnumerable<(string Type, string Action, string Component)> JsonRoots(AppServiceHttpProjectionModel model)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in model.Actions)
        {
            if (action.BodyTypeFullName is not null && seen.Add("Body|" + action.BodyTypeFullName))
            {
                yield return (action.BodyTypeFullName, action.Name, "Body");
            }

            if (action.InputEnvelopeFullName is not null && seen.Add("Input|" + action.InputEnvelopeFullName))
            {
                yield return ("global::" + action.InputEnvelopeFullName, action.Name, "CapabilityInput");
            }

            if (action.ResultTypeFullName is not null)
            {
                if (seen.Add("Payload|" + action.ResultTypeFullName))
                {
                    yield return (action.ResultTypeFullName, action.Name, "Payload");
                }

                var envelope = DynamicApi + "DynamicApiResponse<" + action.ResultTypeFullName + ">";
                if (seen.Add("Result|" + envelope))
                {
                    yield return (envelope, action.Name, "ResultEnvelope");
                }
            }

            if (seen.Add("Void|" + action.Name) && (action.ResultTypeFullName is null || action.HttpMethod == "GET"))
            {
                yield return (DynamicApi + "DynamicApiResponse", action.Name, "VoidEnvelope");
            }
        }
    }

    private static IEnumerable<(string Name, string Source, bool Required)> InputBindings(AppServiceHttpActionModel action)
    {
        foreach (var input in action.ScalarInputs)
        {
            yield return (input.Name, input.Binding == "route" ? "Route" : "Query", true);
        }

        if (action.BodyTypeFullName is not null)
        {
            yield return ("body", "Body", action.BodyPolicy == AppServiceHttpBodyPolicy.Required);
        }

        if (action.RequiresConcurrencyToken)
        {
            yield return ("If-Match", "Header", false);
        }
    }

    private static string FullRoute(AppServiceHttpProjectionModel model, AppServiceHttpActionModel action)
        => "/" + model.RoutePrefix.Trim('/')
           + (action.RoutePattern.Length == 0 ? string.Empty : "/" + action.RoutePattern.Trim('/'));

    private static string EnumMethod(string httpMethod) => httpMethod switch
    {
        "GET" => "Get",
        "POST" => "Post",
        "PUT" => "Put",
        "PATCH" => "Patch",
        "DELETE" => "Delete",
        _ => throw new InvalidOperationException(AppServiceHttpDiagnosticCodes.UnsupportedDeclaration
            + ": unsupported HTTP method '" + httpMethod + "'."),
    };

    private static string BinderName(AppServiceHttpActionModel action) => "Bind_" + action.Name + "_Async";

    private static string MapperName(AppServiceHttpActionModel action) => "MapResult_" + action.Name;

    private static string InvokerName(AppServiceHttpProjectionModel model, AppServiceHttpActionModel action)
        => "_HttpProjection_" + AppServiceHttpWireConvention.CanonicalToken(model.ContractFullName) + "_" + action.Name + "_Invoker";

    private static string Local(string name) => char.ToLowerInvariant(name[0]) + name.Substring(1);

    private static string Pascal(string name) => char.ToUpperInvariant(name[0]) + name.Substring(1);

    private static string MessageType(string typeFullName)
        => typeFullName.StartsWith("global::", StringComparison.Ordinal) ? typeFullName.Substring("global::".Length) : typeFullName;

    private static string StringArray(IReadOnlyList<string> values)
        => values.Count == 0
            ? "global::System.Array.Empty<string>()"
            : "new[] { " + string.Join(", ", values.Select(value => "\"" + Escape(value) + "\"")) + " }";

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static StringBuilder StartDocument(string kind, AppServiceHttpProjectionModel model)
    {
        var builder = new StringBuilder();
        Line(builder, "// <auto-generated />");
        Line(builder, "// CrestCreates #129 hand-written HTTP projection " + kind + ". Owner: "
            + model.HttpOwnerAssemblyName + " | contract: " + model.ContractFullName
            + " | model: " + AppServiceHttpProjectionModel.ModelSchemaVersion + ". Do not edit.");
        Line(builder, "#nullable enable");
        Line(builder, string.Empty);
        return builder;
    }

    private static void OpenNamespace(StringBuilder builder, string ns, string[]? usings = null)
    {
        if (usings is not null)
        {
            foreach (var usingLine in usings)
            {
                Line(builder, usingLine);
            }

            Line(builder, string.Empty);
        }

        Line(builder, "namespace " + ns);
        Line(builder, "{");
    }

    private static void CloseNamespace(StringBuilder builder)
    {
        Line(builder, "}");
    }

    private const string Indent = "    ";
    private const string NestedIndent = "        ";
    private const string QuadIndent = "            ";
    private const string QuintIndent = "                ";
    private const string SextIndent = "                    ";

    private const string CrudEnvelopeMemberRouteIdentity = "Id";
    private const string CrudEnvelopeMemberBody = "Input";

    private static void Line(StringBuilder builder, string text) => builder.AppendLine(text);
}

public sealed record AppServiceHttpArtifact(string FileName, string Source);

public sealed record AppServiceHttpManifestEntry(
    string OwnerAssemblyName,
    string OwnerServiceFullName,
    string ContractAssemblyName,
    string ArtifactHintName,
    IReadOnlyList<string> DeclaredTypeFullNames,
    IReadOnlyList<string> ReferencedTypeFullNames,
    string SourceLocation,
    string ModelSchemaVersion);

public sealed record AppServiceHttpEmissionOptions
{
    public required string ContributionTypeName { get; init; }
    public required string HandlerModuleTypeName { get; init; }
    public required string ActivationTypeName { get; init; }
    public required string JsonOptionsConfiguratorTypeName { get; init; }
    public required string JsonContextFullName { get; init; }

    public static AppServiceHttpEmissionOptions ForContract(string contractFullMetadataName)
    {
        var token = AppServiceHttpWireConvention.CanonicalToken(contractFullMetadataName);
        return new AppServiceHttpEmissionOptions
        {
            ContributionTypeName = "GeneratedAppServiceHttpContribution_" + token,
            HandlerModuleTypeName = "GeneratedAppServiceHttpHandlerModule_" + token,
            ActivationTypeName = "GeneratedAppServiceHttpActivation_" + token,
            JsonOptionsConfiguratorTypeName = "GeneratedAppServiceHttpJsonConfigurator_" + token,
            JsonContextFullName = "global::" + GeneratedHttpJsonContext.Namespace + "." + GeneratedHttpJsonContext.TypePrefix + token,
        };
    }
}

/// <summary>Frozen naming for the JSON context the JsonContracts stage must materialize for this projection.</summary>
public static class GeneratedHttpJsonContext
{
    public const string Namespace = "CrestCreates.Generated.HttpProjection";
    public const string TypePrefix = "HttpProjectionJsonContext_";
}
