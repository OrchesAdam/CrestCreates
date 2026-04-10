using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.DynamicApi;
using CrestCreates.OrmProviders.Abstract;
using CrestCreates.Validation.Modules;
using CrestCreates.Validation.Validators;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CrestCreates.Web.Tests.DynamicApi;

public class GeneratedDynamicApiRuntimeTests
{
    [Fact]
    public async Task ReadBodyAsync_WithCamelCaseJson_BindsPascalCaseDto()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"name\":\"Clean Architecture\"}"));
        context.Request.ContentLength = context.Request.Body.Length;
        context.Request.ContentType = "application/json";
        context.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();

        var result = await DynamicApiGeneratedRuntime.ReadBodyAsync<BodyBindingInputDto>(context, optional: false);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Clean Architecture");
    }

    [Fact]
    public async Task EnsurePermissionAsync_WithDeniedPermission_Throws()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "admin") }, "Test"))
        };

        var action = async () => await DynamicApiGeneratedRuntime.EnsurePermissionAsync(
            context,
            new TestPermissionChecker(allGranted: false),
            new[] { "Book.Update" });

        await action.Should().ThrowAsync<CrestPermissionException>();
    }

    [Fact]
    public async Task ValidateAsync_WithInvalidDto_Throws()
    {
        var validationService = new TestValidationService(ValidationResult.Failure("Name 不能为空"));

        var action = async () => await DynamicApiGeneratedRuntime.ValidateAsync(
            validationService,
            new BodyBindingInputDto());

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Name 不能为空*");
    }

    [Fact]
    public async Task ExecuteAsync_WithTransactionalScope_CommitsTransaction()
    {
        var services = new ServiceCollection();
        var unitOfWorkManager = new TestUnitOfWorkManager();
        services.AddSingleton<IUnitOfWorkManager>(unitOfWorkManager);

        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };

        await DynamicApiGeneratedRuntime.ExecuteAsync(context, requiresTransaction: true, () => Task.CompletedTask);

        unitOfWorkManager.Scope.Should().NotBeNull();
        unitOfWorkManager.Scope!.UnitOfWork.BeginTransactionCount.Should().Be(1);
        unitOfWorkManager.Scope.UnitOfWork.CommitTransactionCount.Should().Be(1);
        unitOfWorkManager.Scope.UnitOfWork.RollbackTransactionCount.Should().Be(0);
    }

    [Fact]
    public async Task WrapGetResult_WithNullValue_ReturnsNotFoundEnvelope()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();

        await DynamicApiGeneratedRuntime.WrapGetResult<string>(null).ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }
}

public sealed class BodyBindingInputDto
{
    public string Name { get; set; } = string.Empty;
}

public sealed class TestPermissionChecker : IPermissionChecker
{
    private readonly bool _allGranted;

    public TestPermissionChecker(bool allGranted)
    {
        _allGranted = allGranted;
    }

    public Task<bool> IsGrantedAsync(string permissionName)
    {
        return Task.FromResult(_allGranted);
    }

    public Task<bool> IsGrantedAsync(ClaimsPrincipal principal, string permissionName)
    {
        return Task.FromResult(_allGranted);
    }

    public Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] permissionNames)
    {
        return Task.FromResult(CreateResult(permissionNames));
    }

    public Task<MultiplePermissionGrantResult> IsGrantedAsync(ClaimsPrincipal principal, string[] permissionNames)
    {
        return Task.FromResult(CreateResult(permissionNames));
    }

    public Task CheckAsync(string permissionName)
    {
        return Task.CompletedTask;
    }

    private MultiplePermissionGrantResult CreateResult(string[] permissionNames)
    {
        return new MultiplePermissionGrantResult(permissionNames.ToDictionary(permission => permission, _ => _allGranted));
    }
}

public sealed class TestValidationService : IValidationService
{
    private readonly ValidationResult _result;

    public TestValidationService(ValidationResult result)
    {
        _result = result;
    }

    public ValidationResult Validate<T>(T instance)
    {
        return _result;
    }

    public Task<ValidationResult> ValidateAsync<T>(T instance)
    {
        return Task.FromResult(_result);
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
