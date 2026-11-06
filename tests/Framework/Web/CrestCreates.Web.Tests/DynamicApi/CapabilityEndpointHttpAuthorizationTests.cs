using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Capability.Abstractions;
using CrestCreates.DynamicApi;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Registry;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CrestCreates.Web.Tests.DynamicApi;

/// <summary>
/// R3-40: InheritCapability must expose the capability's actual authority at the HTTP layer.
/// Anonymous calls to a permission-bearing capability are challenged before dispatch; explicit
/// anonymous and permissionless inherited endpoints stay reachable.
/// </summary>
public class CapabilityEndpointHttpAuthorizationTests
{
    [Fact]
    public async Task Inherit_with_permissions_rejects_anonymous_with_401_before_dispatch()
    {
        var dispatcher = CreateDispatcher();
        await using var app = await StartAsync(dispatcher, authorizationMode: CapabilityEndpointAuthorizationMode.InheritCapability,
            permissions: ["Books.Create"]);

        var response = await app.GetTestClient().GetAsync("/api/authorization-probe");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        dispatcher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Inherit_with_permissions_dispatches_an_authenticated_caller()
    {
        var dispatcher = CreateDispatcher();
        await using var app = await StartAsync(dispatcher, authorizationMode: CapabilityEndpointAuthorizationMode.InheritCapability,
            permissions: ["Books.Create"]);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/authorization-probe");
        request.Headers.Add("X-Probe-Auth", "1");
        var response = await app.GetTestClient().SendAsync(request);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        dispatcher.Verify(dispatcher => dispatcher.DispatchAsync(
            It.IsAny<CapabilityDescriptor>(),
            InvocationSource.Http,
            It.IsAny<object?>(),
            It.IsAny<Action<CapabilityExecutionContext>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Explicit_allow_anonymous_dispatches_without_authentication()
    {
        var dispatcher = CreateDispatcher();
        await using var app = await StartAsync(dispatcher, authorizationMode: CapabilityEndpointAuthorizationMode.AllowAnonymous,
            permissions: []);

        var response = await app.GetTestClient().GetAsync("/api/authorization-probe");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        dispatcher.Verify(dispatcher => dispatcher.DispatchAsync(
            It.IsAny<CapabilityDescriptor>(),
            InvocationSource.Http,
            It.IsAny<object?>(),
            It.IsAny<Action<CapabilityExecutionContext>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Inherit_without_permissions_dispatches_without_authentication()
    {
        var dispatcher = CreateDispatcher();
        await using var app = await StartAsync(dispatcher, authorizationMode: CapabilityEndpointAuthorizationMode.InheritCapability,
            permissions: []);

        var response = await app.GetTestClient().GetAsync("/api/authorization-probe");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        dispatcher.Verify(dispatcher => dispatcher.DispatchAsync(
            It.IsAny<CapabilityDescriptor>(),
            InvocationSource.Http,
            It.IsAny<object?>(),
            It.IsAny<Action<CapabilityExecutionContext>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Mock<ICapabilityDispatcher> CreateDispatcher()
    {
        var dispatcher = new Mock<ICapabilityDispatcher>();
        dispatcher
            .Setup(d => d.DispatchAsync(
                It.IsAny<CapabilityDescriptor>(),
                It.IsAny<InvocationSource>(),
                It.IsAny<object?>(),
                It.IsAny<Action<CapabilityExecutionContext>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CapabilityExecutionResult.Success(new { ok = true }, TimeSpan.Zero));
        return dispatcher;
    }

    private static async Task<WebApplication> StartAsync(
        Mock<ICapabilityDispatcher> dispatcher,
        CapabilityEndpointAuthorizationMode authorizationMode,
        string[] permissions)
    {
        var capability = new CapabilityDescriptor
        {
            Id = "probe.capability",
            Name = "Probe capability",
            Version = 1,
            State = DescriptorState.Active,
            Permissions = permissions
        };
        var endpoint = new CapabilityEndpointDescriptor
        {
            Id = "probe.endpoint",
            Name = "Probe endpoint",
            Version = 1,
            State = DescriptorState.Active,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>(capability.Id, 1),
            HttpMethod = CapabilityEndpointHttpMethod.Get,
            RoutePattern = "/api/authorization-probe",
            AuthorizationMode = authorizationMode,
            OutputMapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 200 }
        };

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddRouting();
        builder.Services.AddAuthorization();
        builder.Services.AddAuthentication("probe")
            .AddScheme<AuthenticationSchemeOptions, ProbeAuthenticationHandler>("probe", _ => { });
        builder.Services.AddSingleton<ICapabilityRegistry>(new CapabilityRegistry(
            new RegistryValidationEngine<CapabilityDescriptor>(Array.Empty<IRegistryValidator<CapabilityDescriptor>>())));
        builder.Services.AddSingleton(dispatcher.Object);
        builder.Services.AddCrestCapabilityEndpoints();
        builder.Services.AddCrestCapabilityEndpointContribution(new ProbeContribution
        {
            OwnerAssemblyName = "probe",
            CapabilityDescriptors = [capability],
            EndpointDescriptors = [endpoint],
            Bindings = [new CapabilityEndpointBindingContract(endpoint.Id, 1, (_, _) => ValueTask.FromResult<object?>(null))],
            Handlers = [new CapabilityEndpointHandlerBinding(capability.Id, new EmptyInvoker())],
            ResultMappers = [new CapabilityEndpointResultBinding(endpoint.Id, 1, (_, _) => Results.Ok())]
        });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapCrestCapabilityEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class ProbeContribution : CapabilityEndpointContribution;

    private sealed class EmptyInvoker : ICapabilityHandlerInvoker
    {
        public Task<object?> InvokeAsync(object? input, CancellationToken cancellationToken)
            => Task.FromResult<object?>(null);
    }

    private sealed class ProbeAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public ProbeAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Probe-Auth"))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "probe-user")],
                Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
