using CrestCreates.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Web.Tests;

public class CrestWebPresetTests
{
    [Fact]
    public void CrestWebOptions_ShouldConfigureGeneratedApiAssemblies()
    {
        var options = new CrestWebOptions();

        options.UseGeneratedApi(api => api.AddApplicationServiceAssembly<CrestWebPresetTests>());

        options.GeneratedApi.ServiceMarkerTypes.Should().Contain(typeof(CrestWebPresetTests));
    }

    [Fact]
    public void AddCrestWeb_ShouldAcceptOptionsDelegate()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddCrestWeb(options =>
        {
            options.UseGeneratedApi(api => api.AddApplicationServiceAssembly<CrestWebPresetTests>());
        });

        builder.Services.Should().NotBeEmpty();
    }
}
