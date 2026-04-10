using CrestCreates.DynamicApi;
using FluentAssertions;
using Swashbuckle.AspNetCore.SwaggerGen;
using Xunit;

namespace CrestCreates.Web.Tests.DynamicApi;

public class DynamicApiSwaggerGenOptionsSetupTests
{
    [Fact]
    public void PostConfigure_ShouldRegisterDynamicApiDocumentFilterAndSchemaIdSelector()
    {
        var options = new SwaggerGenOptions();
        var setup = new DynamicApiSwaggerGenOptionsSetup();

        setup.PostConfigure(name: null, options);

        options.DocumentFilterDescriptors.Should().ContainSingle(descriptor =>
            descriptor.Type == typeof(DynamicApiSwaggerDocumentFilter));
        options.SchemaGeneratorOptions.SchemaIdSelector(typeof(CrestCreates.Application.Contracts.DTOs.Common.SortDescriptor))
            .Should().NotBe(options.SchemaGeneratorOptions.SchemaIdSelector(typeof(CrestCreates.Domain.Shared.DTOs.SortDescriptor)));
    }
}
