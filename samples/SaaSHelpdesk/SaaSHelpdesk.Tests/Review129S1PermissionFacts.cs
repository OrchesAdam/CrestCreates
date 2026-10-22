using System.Security.Claims;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Shared.Permissions;

namespace SaaSHelpdesk.Tests;

public class Review129S1PermissionFacts : BaseTest
{
    public Review129S1PermissionFacts(Fixtures.HelpdeskWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Undefined_permission_can_be_granted_to_a_non_super_admin_by_existing_manager()
    {
        using var scope = Factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var definitions = services.GetRequiredService<IPermissionDefinitionManager>();
        definitions.GetOrNull("Ticket.Get").Should().NotBeNull("S3 registers the five formal Ticket definitions");
        definitions.GetOrNull("Ticket.Unknown").Should().BeNull();
        var userId = Guid.NewGuid().ToString();
        var grants = services.GetRequiredService<IPermissionGrantManager>();
        var undefined = "Ticket.Unknown";
        await grants.GrantAsync(new PermissionGrantInfo
        {
            PermissionName = undefined,
            ProviderType = PermissionGrantProviderType.User,
            ProviderKey = userId,
            Scope = PermissionGrantScope.Global
        });
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("is_super_admin", "false")
        }, "review-fact-control"));
        var checker = services.GetRequiredService<IPermissionChecker>();
        (await checker.IsGrantedAsync(principal, undefined)).Should().BeTrue(
            "the existing grant manager accepts undefined names; definitions are a separate authority");
        (await checker.IsGrantedAsync(principal, "Ticket.Update")).Should().BeFalse();
    }
    [Fact]
    public async Task GetAll_endpoint_permission_does_not_satisfy_the_real_service_permission()
    {
        using var scope = Factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var userId = Guid.NewGuid().ToString();
        await services.GetRequiredService<IPermissionGrantManager>().GrantAsync(new PermissionGrantInfo
        {
            PermissionName = "Ticket.GetAll", ProviderType = PermissionGrantProviderType.User,
            ProviderKey = userId, Scope = PermissionGrantScope.Global
        });
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId), new Claim("is_super_admin", "false")
        }, "review-fact-control"));
        using var frame = services.GetRequiredService<ICurrentPrincipalAccessor>().Change(principal);
        (await services.GetRequiredService<IPermissionChecker>().IsGrantedAsync("Ticket.GetAll")).Should().BeTrue();
        Func<Task> invoke = async () => await services.GetRequiredService<SaaSHelpdesk.Application.Contracts.Interfaces.ITicketAppService>().GetAllAsync();
        var denied = await invoke.Should().ThrowAsync<CrestPermissionException>();
        denied.Which.PermissionName.Should().Be("Ticket.Search");
    }

}
