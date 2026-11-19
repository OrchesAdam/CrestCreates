using System.Text.Json;
using System.Text.Json.Serialization;
using CrestCreates.Domain.Features;
using CrestCreates.MultiTenancy.Abstract;
using ErpManagement.Application.Contracts.DTOs;
using ErpManagement.Application.Contracts.Integration;
using ErpManagement.Application.Contracts.Interfaces;
using ErpManagement.Application.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace ErpManagement.Web.Integration;

internal static class ErpIntegrationEndpoints
{
    private const string MesPolicy = "IntegrationMes";
    private const string CrmPolicy = "IntegrationCrm";

    public static void AddErpIntegration(this IServiceCollection services)
    {
        services.AddAuthentication().AddScheme<IntegrationApiKeyOptions, IntegrationApiKeyAuthenticationHandler>(
            ErpIntegrationClients.Scheme, _ => { });
        services.AddAuthorizationBuilder()
            .AddPolicy(MesPolicy, policy => policy.AddAuthenticationSchemes(ErpIntegrationClients.Scheme)
                .RequireAuthenticatedUser().RequireClaim(ErpIntegrationClients.ClaimType, ErpIntegrationClients.Mes))
            .AddPolicy(CrmPolicy, policy => policy.AddAuthenticationSchemes(ErpIntegrationClients.Scheme)
                .RequireAuthenticatedUser().RequireClaim(ErpIntegrationClients.ClaimType, ErpIntegrationClients.Crm));
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, ErpIntegrationJsonContext.Default));
    }

    public static void MapErpIntegration(this IEndpointRouteBuilder app)
    {
        // Bind tenant identity to the authenticated client configuration, independent of X-Tenant-Id.
        var integration = app.MapGroup("/api/integration").WithMetadata(new SkipTenantResolutionMetadata());
        integration.MapPost("/mes/work-orders/{workOrderId:guid}/report", ReportAsync)
            .RequireAuthorization(MesPolicy)
            .Produces<WorkOrderDto>()
            .Produces(StatusCodes.Status409Conflict);
        integration.MapPost("/crm/customers", UpsertCustomerAsync).RequireAuthorization(CrmPolicy);
    }

    private static async Task<IResult> ReportAsync(Guid workOrderId, [FromBody] WorkOrderReportInput input,
        HttpContext context, [FromServices] ICurrentTenant tenant, [FromServices] IFeatureChecker features,
        [FromServices] IWorkOrderAppService workOrders, CancellationToken cancellationToken)
    {
        var tenantId = context.User.FindFirst("tenantid")?.Value ?? string.Empty;
        using var tenantScope = await tenant.ChangeAsync(tenantId);
        if (tenantId.Length == 0 || tenant.Id != tenantId || !await features.IsEnabledAsync("Erp.MesIntegration", cancellationToken))
            return Results.Problem(statusCode: 403, title: "MES integration is unavailable for this tenant.");

        return Results.Ok(await workOrders.ReportAsync(workOrderId, input, cancellationToken));
    }

    private static async Task<IResult> UpsertCustomerAsync([FromBody] CrmCustomerRequest request,
        HttpContext context, [FromServices] ICurrentTenant tenant, [FromServices] IFeatureChecker features,
        [FromServices] CustomerAppService customers, [FromServices] IValidator<CreateCustomerDto> validator,
        CancellationToken cancellationToken)
    {
        var tenantId = context.User.FindFirst("tenantid")?.Value ?? string.Empty;
        using var tenantScope = await tenant.ChangeAsync(tenantId);
        if (tenantId.Length == 0 || tenant.Id != tenantId || !await features.IsEnabledAsync("Erp.CrmIntegration", cancellationToken))
            return Results.Problem(statusCode: 403, title: "CRM integration is unavailable for this tenant.");

        var input = new CreateCustomerDto
        {
            Code = request.Code,
            Name = request.Name,
            ContactName = request.ContactName,
            Phone = request.Phone,
            Email = request.Email,
            CreditLimit = request.CreditLimit ?? 0
        };
        var validation = await validator.ValidateAsync(input, cancellationToken);
        if (!validation.IsValid)
            return Results.ValidationProblem(validation.ToDictionary());

        var (customer, created) = await customers.UpsertFromCrmAsync(input, request.CreditLimit, cancellationToken);
        return created ? Results.Created($"/api/customer/{customer.Id}", customer) : Results.Ok(customer);
    }
}

internal sealed record CrmCustomerRequest(string Code, string Name, string? ContactName, string? Phone,
    string? Email, decimal? CreditLimit);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(CrmCustomerRequest))]
[JsonSerializable(typeof(WorkOrderReportInput))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
internal partial class ErpIntegrationJsonContext : JsonSerializerContext;
