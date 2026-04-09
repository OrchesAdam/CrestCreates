using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.DynamicApi;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Web.Tests.DynamicApi;

public class DynamicApiScannerTests
{
    [Fact]
    public void Scan_WithCrestServiceAppService_ProducesExpectedRoutesAndVerbs()
    {
        var options = new DynamicApiOptions();
        options.AddApplicationServiceAssembly<TestBookAppService>();

        var scanner = new DynamicApiScanner(new DynamicApiRouteConvention());

        var registry = scanner.Scan(options);
        var service = registry.Services.Single(service => service.RoutePrefix == "api/test-book");

        service.RoutePrefix.Should().Be("api/test-book");
        service.Actions.Should().Contain(action => action.ActionName == "Create" && action.HttpMethod == "POST" && action.FullRoute == "api/test-book");
        service.Actions.Should().Contain(action => action.ActionName == "GetById" && action.HttpMethod == "GET" && action.FullRoute == "api/test-book/{id}");
        service.Actions.Should().Contain(action => action.ActionName == "GetByIsbn" && action.HttpMethod == "GET" && action.FullRoute == "api/test-book/by-isbn/{isbn}");
        service.Actions.Should().Contain(action => action.ActionName == "Update" && action.HttpMethod == "PUT" && action.FullRoute == "api/test-book/{id}");
        service.Actions.Should().Contain(action => action.ActionName == "Delete" && action.HttpMethod == "DELETE" && action.FullRoute == "api/test-book/{id}");
    }

    [Fact]
    public void Scan_WithInheritedCrudContract_ProducesCrudRoutesOnConcreteServiceOnly()
    {
        var options = new DynamicApiOptions();
        options.AddApplicationServiceAssembly<InheritedBookAppService>();

        var scanner = new DynamicApiScanner(new DynamicApiRouteConvention());

        var registry = scanner.Scan(options);

        registry.Services.Should().ContainSingle(service => service.RoutePrefix == "api/inherited-book");
        registry.Services.Should().NotContain(service => service.RoutePrefix == "api/test-crud");

        var service = registry.Services.Single(service => service.RoutePrefix == "api/inherited-book");
        service.Actions.Should().Contain(action => action.ActionName == "Create" && action.HttpMethod == "POST" && action.FullRoute == "api/inherited-book");
        service.Actions.Should().Contain(action => action.ActionName == "GetById" && action.HttpMethod == "GET" && action.FullRoute == "api/inherited-book/{id}");
        service.Actions.Should().Contain(action => action.ActionName == "GetList" && action.HttpMethod == "GET" && action.FullRoute == "api/inherited-book");
        service.Actions.Should().Contain(action => action.ActionName == "Update" && action.HttpMethod == "PUT" && action.FullRoute == "api/inherited-book/{id}");
        service.Actions.Should().Contain(action => action.ActionName == "Delete" && action.HttpMethod == "DELETE" && action.FullRoute == "api/inherited-book/{id}");
        service.Actions.Should().Contain(action => action.ActionName == "GetByIsbn" && action.HttpMethod == "GET" && action.FullRoute == "api/inherited-book/by-isbn/{isbn}");
    }
}

public interface ITestBookAppService
{
    Task<TestBookDto> CreateAsync(CreateTestBookDto input, CancellationToken cancellationToken = default);

    Task<TestBookDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<TestBookDto?> GetByIsbnAsync(string isbn, CancellationToken cancellationToken = default);

    Task<TestBookDto> UpdateAsync(Guid id, UpdateTestBookDto input, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

[CrestService]
public class TestBookAppService : ITestBookAppService
{
    public Task<TestBookDto> CreateAsync(CreateTestBookDto input, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TestBookDto());
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<TestBookDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<TestBookDto?>(new TestBookDto());
    }

    public Task<TestBookDto?> GetByIsbnAsync(string isbn, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<TestBookDto?>(new TestBookDto());
    }

    public Task<TestBookDto> UpdateAsync(Guid id, UpdateTestBookDto input, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TestBookDto());
    }
}

public sealed class TestBookDto
{
    public Guid Id { get; set; }
}

public sealed class CreateTestBookDto
{
    public string Name { get; set; } = string.Empty;
}

public sealed class UpdateTestBookDto
{
    public string Name { get; set; } = string.Empty;
}

public interface IInheritedBookAppService : ITestCrudAppService<Guid, InheritedBookDto, CreateInheritedBookDto, UpdateInheritedBookDto, InheritedBookListRequestDto>
{
    Task<InheritedBookDto?> GetByIsbnAsync(string isbn, CancellationToken cancellationToken = default);
}

public interface ITestCrudAppService<TKey, TDto, in TCreateDto, in TUpdateDto, in TListRequestDto>
{
    Task<TDto> CreateAsync(TCreateDto input, CancellationToken cancellationToken = default);

    Task<TDto?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);

    Task<PagedResultDto<TDto>> GetListAsync(TListRequestDto input, CancellationToken cancellationToken = default);

    Task<TDto> UpdateAsync(TKey id, TUpdateDto input, CancellationToken cancellationToken = default);

    Task DeleteAsync(TKey id, CancellationToken cancellationToken = default);
}

[CrestService]
public sealed class InheritedBookAppService : IInheritedBookAppService
{
    public Task<InheritedBookDto> CreateAsync(CreateInheritedBookDto input, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new InheritedBookDto());
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<InheritedBookDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<InheritedBookDto?>(new InheritedBookDto());
    }

    public Task<InheritedBookDto?> GetByIsbnAsync(string isbn, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<InheritedBookDto?>(new InheritedBookDto());
    }

    public Task<PagedResultDto<InheritedBookDto>> GetListAsync(InheritedBookListRequestDto input, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(PagedResultDto<InheritedBookDto>.Empty());
    }

    public Task<InheritedBookDto> UpdateAsync(Guid id, UpdateInheritedBookDto input, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new InheritedBookDto());
    }
}

public sealed class InheritedBookDto
{
    public Guid Id { get; set; }
}

public sealed class CreateInheritedBookDto
{
    public string Name { get; set; } = string.Empty;
}

public sealed class UpdateInheritedBookDto
{
    public string Name { get; set; } = string.Empty;
}

public sealed class InheritedBookListRequestDto : PagedRequestDto
{
}
