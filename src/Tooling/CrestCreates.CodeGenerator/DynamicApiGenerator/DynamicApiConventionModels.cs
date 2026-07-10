using System.Collections.Immutable;

namespace CrestCreates.CodeGenerator.DynamicApiGenerator;

internal sealed record ServiceModel(
    string ServiceName,
    string RouteTemplate,
    bool HasCustomRoute,
    string ServiceTypeName,
    string ServiceAssemblyTypeName,
    ImmutableArray<ActionModel> Actions);

internal sealed record ActionModel(
    string ActionName,
    string DeclaringTypeName,
    string OperationId,
    string RelativeRoute,
    string HttpMethod,
    string PermissionName,
    ReturnModel ReturnModel,
    ImmutableArray<ParameterModel> Parameters,
    string ServiceMethodName,
    string ServiceTypeName,
    bool RequiresUnitOfWork,
    bool RequiresTransaction,
    ImmutableArray<string> MetadataCalls,
    bool AllowAnonymous,
    CrudAction? OverrideAction = null);

internal sealed record ParameterModel(
    string Name,
    string TypeName,
    ParameterSource Source,
    bool IsOptional,
    bool IsScalar,
    ImmutableArray<QueryPropertyModel> QueryProperties);

internal sealed record QueryPropertyModel(
    string Name,
    string TypeName,
    bool IsScalar,
    bool IsOptional);

internal sealed record ServiceRouteModel(string Template, bool IsCustom);

internal sealed record ReturnModel(bool IsVoid, string? PayloadTypeName);

internal enum ParameterSource
{
    Route,
    Query,
    Body,
    Header,
    CancellationToken
}

internal enum CrudAction
{
    Get = 0,
    GetList = 1,
    Create = 2,
    Update = 3,
    Delete = 4
}
