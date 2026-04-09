using CrestCreates.DynamicApi;
using FluentAssertions;
using Swashbuckle.AspNetCore.SwaggerGen;
using Xunit;

namespace CrestCreates.Web.Tests.DynamicApi;

public class DynamicApiSwaggerGenOptionsSetupTests
{
    [Fact]
    public void Configure_ShouldRegisterDynamicApiDocumentFilter()
    {
        var options = new SwaggerGenOptions();
        var setup = new DynamicApiSwaggerGenOptionsSetup();

        setup.Configure(options);

        options.DocumentFilterDescriptors.Should().ContainSingle(descriptor =>
            descriptor.Type == typeof(DynamicApiSwaggerDocumentFilter));
    }
}
