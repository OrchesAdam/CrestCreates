using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Aop.Interceptors;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.DynamicApi;
using CrestCreates.OrmProviders.Abstract;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.Web.Tests.DynamicApi;

public class DynamicApiEndpointExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_WithCamelCaseBody_BindsToPascalCaseDto()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBodyBindingAppService, BodyBindingAppService>();
        services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNameCaseInsensitive = false;
            options.SerializerOptions.PropertyNamingPolicy = null;
        });

        await using var serviceProvider = services.BuildServiceProvider();
        var executor = new DynamicApiEndpointExecutor(
            serviceProvider,
            serviceProvider.GetRequiredService<IOptions<JsonOptions>>());

        var requestBody = JsonSerializer.Serialize(new
        {
            name = "Clean Architecture"
        });

        var httpContext = new DefaultHttpContext();
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));
        httpContext.Request.ContentLength = httpContext.Request.Body.Length;

        var serviceMethod = typeof(IBodyBindingAppService).GetMethod(nameof(IBodyBindingAppService.CreateAsync))!;
        var actionDescriptor = new DynamicApiActionDescriptor
        {
            ActionName = "Create",
            HttpMethod = HttpMethods.Post,
            RoutePrefix = "api/body-binding",
            ServiceMethod = serviceMethod,
            ImplementationMethod = typeof(BodyBindingAppService).GetMethod(nameof(BodyBindingAppService.CreateAsync))!,
            Parameters = new DynamicApiRouteConvention().ResolveParameters(serviceMethod, string.Empty, HttpMethods.Post),
            ReturnDescriptor = DynamicApiRouteConvention.ResolveReturnDescriptor(serviceMethod),
            Permission = new DynamicApiPermissionMetadata()
        };

        var serviceDescriptor = new DynamicApiServiceDescriptor
        {
            ServiceName = "BodyBinding",
            RoutePrefix = "api/body-binding",
            ServiceType = typeof(IBodyBindingAppService),
            ImplementationType = typeof(BodyBindingAppService),
            Actions = new[] { actionDescriptor }
        };

        await executor.ExecuteAsync(httpContext, serviceDescriptor, actionDescriptor);

        var service = (BodyBindingAppService)serviceProvider.GetRequiredService<IBodyBindingAppService>();
        service.LastInput.Should().NotBeNull();
        service.LastInput!.Name.Should().Be("Clean Architecture");
    }

    [Fact]
    public async Task ExecuteAsync_WithAlreadyReadBody_RewindsAndBindsInput()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBodyBindingAppService, BodyBindingAppService>();
        services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNameCaseInsensitive = false;
            options.SerializerOptions.PropertyNamingPolicy = null;
        });

        await using var serviceProvider = services.BuildServiceProvider();
        var executor = new DynamicApiEndpointExecutor(
            serviceProvider,
            serviceProvider.GetRequiredService<IOptions<JsonOptions>>());

        var requestBody = JsonSerializer.Serialize(new
        {
            name = "Refactoring"
        });

        var httpContext = new DefaultHttpContext();
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));
        httpContext.Request.ContentLength = httpContext.Request.Body.Length;
        httpContext.Request.EnableBuffering();
        _ = await new StreamReader(httpContext.Request.Body, Encoding.UTF8, leaveOpen: true).ReadToEndAsync();

        var serviceMethod = typeof(IBodyBindingAppService).GetMethod(nameof(IBodyBindingAppService.CreateAsync))!;
        var actionDescriptor = new DynamicApiActionDescriptor
        {
            ActionName = "Create",
            HttpMethod = HttpMethods.Post,
            RoutePrefix = "api/body-binding",
            ServiceMethod = serviceMethod,
            ImplementationMethod = typeof(BodyBindingAppService).GetMethod(nameof(BodyBindingAppService.CreateAsync))!,
            Parameters = new DynamicApiRouteConvention().ResolveParameters(serviceMethod, string.Empty, HttpMethods.Post),
            ReturnDescriptor = DynamicApiRouteConvention.ResolveReturnDescriptor(serviceMethod),
            Permission = new DynamicApiPermissionMetadata()
        };

        var serviceDescriptor = new DynamicApiServiceDescriptor
        {
            ServiceName = "BodyBinding",
            RoutePrefix = "api/body-binding",
            ServiceType = typeof(IBodyBindingAppService),
            ImplementationType = typeof(BodyBindingAppService),
            Actions = new[] { actionDescriptor }
        };

        await executor.ExecuteAsync(httpContext, serviceDescriptor, actionDescriptor);

        var service = (BodyBindingAppService)serviceProvider.GetRequiredService<IBodyBindingAppService>();
        service.LastInput.Should().NotBeNull();
        service.LastInput!.Name.Should().Be("Refactoring");
    }

    [Fact]
    public async Task ExecuteAsync_WithNullContentLength_BindsInputFromBody()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBodyBindingAppService, BodyBindingAppService>();
        services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNameCaseInsensitive = false;
            options.SerializerOptions.PropertyNamingPolicy = null;
        });

        await using var serviceProvider = services.BuildServiceProvider();
        var executor = new DynamicApiEndpointExecutor(
            serviceProvider,
            serviceProvider.GetRequiredService<IOptions<JsonOptions>>());

        var requestBody = JsonSerializer.Serialize(new
        {
            name = "Patterns of Enterprise Application Architecture"
        });

        var httpContext = new DefaultHttpContext();
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));
        httpContext.Request.ContentLength = null;

        var serviceMethod = typeof(IBodyBindingAppService).GetMethod(nameof(IBodyBindingAppService.CreateAsync))!;
        var actionDescriptor = new DynamicApiActionDescriptor
        {
            ActionName = "Create",
            HttpMethod = HttpMethods.Post,
            RoutePrefix = "api/body-binding",
            ServiceMethod = serviceMethod,
            ImplementationMethod = typeof(BodyBindingAppService).GetMethod(nameof(BodyBindingAppService.CreateAsync))!,
            Parameters = new DynamicApiRouteConvention().ResolveParameters(serviceMethod, string.Empty, HttpMethods.Post),
            ReturnDescriptor = DynamicApiRouteConvention.ResolveReturnDescriptor(serviceMethod),
            Permission = new DynamicApiPermissionMetadata()
        };

        var serviceDescriptor = new DynamicApiServiceDescriptor
        {
            ServiceName = "BodyBinding",
            RoutePrefix = "api/body-binding",
            ServiceType = typeof(IBodyBindingAppService),
            ImplementationType = typeof(BodyBindingAppService),
            Actions = new[] { actionDescriptor }
        };

        await executor.ExecuteAsync(httpContext, serviceDescriptor, actionDescriptor);

        var service = (BodyBindingAppService)serviceProvider.GetRequiredService<IBodyBindingAppService>();
        service.LastInput.Should().NotBeNull();
        service.LastInput!.Name.Should().Be("Patterns of Enterprise Application Architecture");
    }

    [Fact]
    public async Task ExecuteAsync_WithUnitOfWorkAction_CommitsTransaction()
    {
        var services = new ServiceCollection();
        var unitOfWorkManager = new TestUnitOfWorkManager();
        services.AddSingleton<IUnitOfWorkAppService, UnitOfWorkAppService>();
        services.AddSingleton<IUnitOfWorkManager>(unitOfWorkManager);
        services.Configure<JsonOptions>(options => options.SerializerOptions.PropertyNamingPolicy = null);

        await using var serviceProvider = services.BuildServiceProvider();
        var executor = new DynamicApiEndpointExecutor(
            serviceProvider,
            serviceProvider.GetRequiredService<IOptions<JsonOptions>>());

        var httpContext = CreateJsonHttpContext(new
        {
            name = "Transactional Book"
        });

        var serviceMethod = typeof(IUnitOfWorkAppService).GetMethod(nameof(IUnitOfWorkAppService.CreateAsync))!;
        var actionDescriptor = CreateActionDescriptor<IUnitOfWorkAppService, UnitOfWorkAppService>(serviceMethod);
        var serviceDescriptor = CreateServiceDescriptor<IUnitOfWorkAppService, UnitOfWorkAppService>(actionDescriptor);

        await executor.ExecuteAsync(httpContext, serviceDescriptor, actionDescriptor);

        unitOfWorkManager.Scope.Should().NotBeNull();
        unitOfWorkManager.Scope!.UnitOfWork.BeginTransactionCount.Should().Be(1);
        unitOfWorkManager.Scope.UnitOfWork.CommitTransactionCount.Should().Be(1);
        unitOfWorkManager.Scope.UnitOfWork.RollbackTransactionCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenUnitOfWorkActionThrows_RollsBackTransaction()
    {
        var services = new ServiceCollection();
        var unitOfWorkManager = new TestUnitOfWorkManager();
        services.AddSingleton<IFailingUnitOfWorkAppService, FailingUnitOfWorkAppService>();
        services.AddSingleton<IUnitOfWorkManager>(unitOfWorkManager);
        services.Configure<JsonOptions>(options => options.SerializerOptions.PropertyNamingPolicy = null);

        await using var serviceProvider = services.BuildServiceProvider();
        var executor = new DynamicApiEndpointExecutor(
            serviceProvider,
            serviceProvider.GetRequiredService<IOptions<JsonOptions>>());

        var httpContext = CreateJsonHttpContext(new
        {
            name = "Broken Book"
        });

        var serviceMethod = typeof(IFailingUnitOfWorkAppService).GetMethod(nameof(IFailingUnitOfWorkAppService.CreateAsync))!;
        var actionDescriptor = CreateActionDescriptor<IFailingUnitOfWorkAppService, FailingUnitOfWorkAppService>(serviceMethod);
        var serviceDescriptor = CreateServiceDescriptor<IFailingUnitOfWorkAppService, FailingUnitOfWorkAppService>(actionDescriptor);

        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(httpContext, serviceDescriptor, actionDescriptor));

        unitOfWorkManager.Scope.Should().NotBeNull();
        unitOfWorkManager.Scope!.UnitOfWork.BeginTransactionCount.Should().Be(1);
        unitOfWorkManager.Scope.UnitOfWork.CommitTransactionCount.Should().Be(0);
        unitOfWorkManager.Scope.UnitOfWork.RollbackTransactionCount.Should().Be(1);
    }

    private static DefaultHttpContext CreateJsonHttpContext(object requestBody)
    {
        var payload = JsonSerializer.Serialize(requestBody);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        httpContext.Request.ContentLength = httpContext.Request.Body.Length;
        return httpContext;
    }

    private static DynamicApiActionDescriptor CreateActionDescriptor<TService, TImplementation>(System.Reflection.MethodInfo serviceMethod)
    {
        return new DynamicApiActionDescriptor
        {
            ActionName = "Create",
            HttpMethod = HttpMethods.Post,
            RoutePrefix = "api/test",
            ServiceMethod = serviceMethod,
            ImplementationMethod = typeof(TImplementation).GetMethod(serviceMethod.Name)!,
            Parameters = new DynamicApiRouteConvention().ResolveParameters(serviceMethod, string.Empty, HttpMethods.Post),
            ReturnDescriptor = DynamicApiRouteConvention.ResolveReturnDescriptor(serviceMethod),
            Permission = new DynamicApiPermissionMetadata()
        };
    }

    private static DynamicApiServiceDescriptor CreateServiceDescriptor<TService, TImplementation>(DynamicApiActionDescriptor actionDescriptor)
    {
        return new DynamicApiServiceDescriptor
        {
            ServiceName = "Test",
            RoutePrefix = "api/test",
            ServiceType = typeof(TService),
            ImplementationType = typeof(TImplementation),
            Actions = new[] { actionDescriptor }
        };
    }
}

public interface IBodyBindingAppService
{
    Task<BodyBindingOutputDto> CreateAsync(BodyBindingInputDto input, CancellationToken cancellationToken = default);
}

public sealed class BodyBindingAppService : IBodyBindingAppService
{
    public BodyBindingInputDto? LastInput { get; private set; }

    public Task<BodyBindingOutputDto> CreateAsync(BodyBindingInputDto input, CancellationToken cancellationToken = default)
    {
        LastInput = input;
        return Task.FromResult(new BodyBindingOutputDto
        {
            Name = input.Name
        });
    }
}

public sealed class BodyBindingInputDto
{
    public string Name { get; set; } = string.Empty;
}

public sealed class BodyBindingOutputDto
{
    public string Name { get; set; } = string.Empty;
}

public interface IUnitOfWorkAppService
{
    Task<BodyBindingOutputDto> CreateAsync(BodyBindingInputDto input, CancellationToken cancellationToken = default);
}

public sealed class UnitOfWorkAppService : IUnitOfWorkAppService
{
    [UnitOfWorkMo]
    public Task<BodyBindingOutputDto> CreateAsync(BodyBindingInputDto input, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new BodyBindingOutputDto
        {
            Name = input.Name
        });
    }
}

public interface IFailingUnitOfWorkAppService
{
    Task<BodyBindingOutputDto> CreateAsync(BodyBindingInputDto input, CancellationToken cancellationToken = default);
}

public sealed class FailingUnitOfWorkAppService : IFailingUnitOfWorkAppService
{
    [UnitOfWorkMo]
    public Task<BodyBindingOutputDto> CreateAsync(BodyBindingInputDto input, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Boom");
    }
}

public sealed class TestUnitOfWorkManager : IUnitOfWorkManager
{
    public TestUnitOfWorkScope? Scope { get; private set; }

    public IUnitOfWork? CurrentOrNull => Scope?.UnitOfWork;

    public IUnitOfWork Current => Scope?.UnitOfWork ?? throw new InvalidOperationException("No active unit of work.");

    public IUnitOfWorkScope BeginScope(bool isTransactional = true, bool requiresNew = false, OrmProvider? provider = null)
    {
        Scope = new TestUnitOfWorkScope(new TestUnitOfWork(), isOwner: true, isTransactional: isTransactional);
        return Scope;
    }

    public IUnitOfWork Begin(OrmProvider? provider = null)
    {
        throw new NotSupportedException();
    }

    public TResult Execute<TResult>(Func<IUnitOfWork, TResult> action, OrmProvider? provider = null)
    {
        throw new NotSupportedException();
    }

    public Task<TResult> ExecuteAsync<TResult>(Func<IUnitOfWork, Task<TResult>> action, OrmProvider? provider = null)
    {
        throw new NotSupportedException();
    }
}

public sealed class TestUnitOfWorkScope : IUnitOfWorkScope
{
    public TestUnitOfWorkScope(TestUnitOfWork unitOfWork, bool isOwner, bool isTransactional)
    {
        UnitOfWork = unitOfWork;
        IsOwner = isOwner;
        IsTransactional = isTransactional;
    }

    public TestUnitOfWork UnitOfWork { get; }

    IUnitOfWork IUnitOfWorkScope.UnitOfWork => UnitOfWork;

    public bool IsOwner { get; }

    public bool IsTransactional { get; }

    public void Dispose()
    {
    }
}

public sealed class TestUnitOfWork : IUnitOfWork
{
    public int BeginTransactionCount { get; private set; }

    public int CommitTransactionCount { get; private set; }

    public int RollbackTransactionCount { get; private set; }

    public int SaveChangesCount { get; private set; }

    public Task BeginTransactionAsync()
    {
        BeginTransactionCount++;
        return Task.CompletedTask;
    }

    public Task CommitTransactionAsync()
    {
        CommitTransactionCount++;
        return Task.CompletedTask;
    }

    public Task RollbackTransactionAsync()
    {
        RollbackTransactionCount++;
        return Task.CompletedTask;
    }

    public Task<int> SaveChangesAsync()
    {
        SaveChangesCount++;
        return Task.FromResult(0);
    }

    public void Dispose()
    {
    }
}
