using System;
using CrestCreates.Capability;
using CrestCreates.DynamicApi;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Web;
using CrestCreates.Web.PlatformManagement;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.IntegrationTests;

/// <summary>
/// G-09 host wiring check without a database: AddCrestWeb's platform service registrations must
/// satisfy the CCHTTP007 descriptor-level verification performed by MapCrestCapabilityEndpoints
/// when the aggregate platform projection activation is present. Guards the duplicate-interface
/// trap where ServiceGenerator emits a same-name interface in the Application namespace
/// (TenantAppService now implements the Contracts contract explicitly).
/// </summary>
public class PlatformManagementHostActivationTests
{
    [Fact]
    public void Map_with_AddCrestWeb_registrations_passes_required_service_verification()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddCrestWeb();
        builder.Services.AddCapabilityRuntime();
        builder.Services.AddSingleton<ISchemaValidator, SchemaValidator>();
        builder.Services.AddCrestCapabilityEndpoints();
        builder.Services.AddCrestPlatformManagementHttp();

        using var app = builder.Build();

        var probe = app.Services.GetService<IServiceProviderIsService>();
        probe.Should().NotBeNull("the default provider supports IServiceProviderIsService");
        foreach (var contract in new[]
                 {
                     typeof(CrestCreates.Application.Contracts.Interfaces.ITenantAppService),
                     typeof(CrestCreates.Application.Contracts.Interfaces.IUserAppService),
                     typeof(CrestCreates.Application.Contracts.Interfaces.IRoleAppService),
                     typeof(CrestCreates.Application.Contracts.Interfaces.ISettingAppService),
                     typeof(CrestCreates.Application.Contracts.Interfaces.ISettingDefinitionAppService),
                     typeof(CrestCreates.Application.Contracts.Interfaces.IFeatureAppService),
                     typeof(CrestCreates.Application.Contracts.Interfaces.IFeatureDefinitionAppService),
                     typeof(CrestCreates.Application.Contracts.Interfaces.IPermissionGrantAppService),
                     typeof(CrestCreates.Application.Contracts.Interfaces.IAuditLogAppService),
                 })
        {
            probe!.IsService(contract).Should()
                .BeTrue($"AddCrestWeb must register {contract.Name} for the platform activation");
        }

        var act = () => app.MapCrestCapabilityEndpoints();
        act.Should().NotThrow("AddCrestWeb registers every platform contract the aggregate activation requires");
    }
}