using System;
using System.Linq;
using CrestCreates.CodeGenerator.DynamicApiGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.DynamicApiGenerator;

public sealed class DynamicApiCrudMainlineTests
{
    [Fact]
    public void GeneratedDynamicApi_DeleteExpectedStamp_ShouldBindFromIfMatchHeader()
    {
        const string source = """
using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Shared.Attributes;

namespace TestContracts;

public interface IProductAppService
{
    Task<ProductDto> CreateAsync(CreateProductDto input, CancellationToken cancellationToken = default);
    Task<ProductDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProductDto> UpdateAsync(Guid id, UpdateProductDto input, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default);
}

[CrestService]
public sealed class ProductAppService : IProductAppService
{
    public Task<ProductDto> CreateAsync(CreateProductDto input, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
    public Task<ProductDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
    public Task<ProductDto> UpdateAsync(Guid id, UpdateProductDto input, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
    public Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}

public sealed class ProductDto { }
public sealed class CreateProductDto { }
public sealed class UpdateProductDto { }
public sealed class ProductListRequestDto { }
""";

        var result = SourceGeneratorTestHelper.RunGenerator<DynamicApiAotSourceGenerator>(
            source,
            additionalSources: new[] { DynamicApiAotSourceGeneratorTests.BuildDynamicApiStubs() });

        Assert.True(result.ContainsFile("GeneratedDynamicApiEndpoints.g.cs"),
            "Endpoints file not generated. Diagnostics: " +
            string.Join("\n", result.Diagnostics.Select(d => d.ToString())));

        var endpoints = result.GetSourceByFileName("GeneratedDynamicApiEndpoints.g.cs")!.SourceText;

        Assert.Contains("If-Match", endpoints);
        Assert.Contains("expectedStamp", endpoints);
        Assert.Contains("context.Request.Headers", endpoints);
    }
}
