using System;
using CrestCreates.CodeGenerator.DynamicApiGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.DynamicApiGenerator;

public class DynamicApiAotSourceGeneratorTests
{
    [Fact]
    public void RunGenerator_WithCrestServiceAppService_GeneratesRegistryAndEndpoints()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<DynamicApiAotSourceGenerator>(
            BuildDynamicApiSource(),
            additionalSources: new[] { BuildDynamicApiStubs() });

        result.HasNoErrors().Should().BeTrue(string.Join(Environment.NewLine, result.GetErrors()));
        result.ContainsFile("GeneratedDynamicApiRegistry.g.cs").Should().BeTrue();
        result.ContainsFile("GeneratedDynamicApiEndpoints.g.cs").Should().BeTrue();

        var registrySource = result.GetSourceByFileName("GeneratedDynamicApiRegistry.g.cs");
        registrySource.Should().NotBeNull();
        registrySource!.SourceText.Should().Contain("ServiceName = \"TestBook\"");
        registrySource.SourceText.Should().Contain("RoutePrefix = routePrefix");
        registrySource.SourceText.Should().Contain("ResolveRoutePrefix(options, \"test-book\", false)");
        registrySource.SourceText.Should().Contain("RelativeRoute = \"{id}\"");
        registrySource.SourceText.Should().Contain("HttpMethod = \"PUT\"");
        registrySource.SourceText.Should().Contain("Permissions = new[] { \"TestBook.Update\" }");

        var endpointSource = result.GetSourceByFileName("GeneratedDynamicApiEndpoints.g.cs");
        endpointSource.Should().NotBeNull();
        endpointSource!.SourceText.Should().Contain("BuildRoute(routePrefix_0, \"{id}\")");
        endpointSource.SourceText.Should().Contain("await DynamicApiGeneratedRuntime.EnsurePermissionAsync");
        endpointSource.SourceText.Should().Contain("await DynamicApiGeneratedRuntime.ReadBodyAsync<global::TestContracts.CreateTestBookDto>(context, false)");
        endpointSource.SourceText.Should().Contain("await DynamicApiGeneratedRuntime.ValidateAsync");
        endpointSource.SourceText.Should().Contain("var input = new global::TestContracts.TestBookListRequestDto();");
        endpointSource.SourceText.Should().Contain("input.Keyword = string.IsNullOrWhiteSpace(context.Request.Query[\"Keyword\"].ToString()) ? null : context.Request.Query[\"Keyword\"].ToString()");
        endpointSource.SourceText.Should().Contain("await DynamicApiGeneratedRuntime.ExecuteAsync(context, false, () => service.UpdateAsync(id, input, context.RequestAborted))");
        endpointSource.SourceText.Should().Contain("return DynamicApiGeneratedRuntime.WrapGetResult(result);");
        endpointSource.SourceText.Should().Contain("return DynamicApiGeneratedRuntime.WrapResult(result);");
    }

    [Fact]
    public void RunGenerator_WithInheritedContractAndIgnoredMethod_GeneratesExpectedActions()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<DynamicApiAotSourceGenerator>(
            BuildDynamicApiSource(),
            additionalSources: new[] { BuildDynamicApiStubs() });

        result.HasNoErrors().Should().BeTrue(string.Join(Environment.NewLine, result.GetErrors()));

        var registrySource = result.GetSourceByFileName("GeneratedDynamicApiRegistry.g.cs");
        registrySource.Should().NotBeNull();
        registrySource!.SourceText.Should().Contain("ActionName = \"GetByIsbn\"");
        registrySource.SourceText.Should().Contain("RelativeRoute = \"by-isbn/{isbn}\"");
        registrySource.SourceText.Should().Contain("HttpMethod = \"DELETE\"");
        registrySource.SourceText.Should().NotContain("InternalPing");
    }

    private static string BuildDynamicApiSource()
    {
        return """
               using System;
               using System.Threading;
               using System.Threading.Tasks;
               using CrestCreates.Aop.Interceptors;
               using CrestCreates.Domain.Shared.Attributes;
               using CrestCreates.DynamicApi;

               namespace TestContracts;

               public interface ITestCrudAppService<TKey, TDto, in TCreateDto, in TUpdateDto, in TListRequestDto>
               {
                   Task<TDto> CreateAsync(TCreateDto input, CancellationToken cancellationToken = default);
                   Task<TDto?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);
                   Task<TDto> UpdateAsync(TKey id, TUpdateDto input, CancellationToken cancellationToken = default);
                   Task DeleteAsync(TKey id, CancellationToken cancellationToken = default);
                   Task<TDto[]> GetListAsync(TListRequestDto input, CancellationToken cancellationToken = default);
               }

               public interface ITestBookAppService : ITestCrudAppService<Guid, TestBookDto, CreateTestBookDto, UpdateTestBookDto, TestBookListRequestDto>
               {
                   Task<TestBookDto?> GetByIsbnAsync(string isbn, CancellationToken cancellationToken = default);

                   [DynamicApiIgnore]
                   Task<string> InternalPingAsync(CancellationToken cancellationToken = default);
               }

               [CrestService]
               public class TestBookAppService : ITestBookAppService
               {
                   public Task<TestBookDto> CreateAsync(CreateTestBookDto input, CancellationToken cancellationToken = default) => Task.FromResult(new TestBookDto());
                   public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
                   public Task<TestBookDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<TestBookDto?>(new TestBookDto());
                   public Task<TestBookDto?> GetByIsbnAsync(string isbn, CancellationToken cancellationToken = default) => Task.FromResult<TestBookDto?>(new TestBookDto());
                   public Task<string> InternalPingAsync(CancellationToken cancellationToken = default) => Task.FromResult("pong");
                   public Task<TestBookDto[]> GetListAsync(TestBookListRequestDto input, CancellationToken cancellationToken = default) => Task.FromResult(Array.Empty<TestBookDto>());

                   [UnitOfWorkMo(false)]
                   public Task<TestBookDto> UpdateAsync(Guid id, UpdateTestBookDto input, CancellationToken cancellationToken = default) => Task.FromResult(new TestBookDto());
               }

               public class TestBookDto
               {
                   public Guid Id { get; set; }
               }

               public class CreateTestBookDto
               {
                   public string Name { get; set; } = string.Empty;
               }

               public class UpdateTestBookDto
               {
                   public string Name { get; set; } = string.Empty;
               }

               public class TestBookListRequestDto
               {
                   public string? Keyword { get; set; }
                   public int SkipCount { get; set; }
                   public object? Complex { get; set; }
               }
               """;
    }

    private static string BuildDynamicApiStubs()
    {
        return """
               using System;
               using System.Collections.Generic;
               using System.Linq;
               using System.IO;
               using System.Reflection;
               using System.Threading;
               using System.Threading.Tasks;
               using Microsoft.AspNetCore.Http;
               using Microsoft.AspNetCore.Routing;
               using CrestCreates.Authorization.Abstractions;
               using CrestCreates.Validation.Modules;

               namespace Microsoft.AspNetCore.Routing
               {
                   public interface IEndpointRouteBuilder
                   {
                   }

                   public sealed class RouteHandlerBuilder
                   {
                       public RouteHandlerBuilder WithDisplayName(string name) => this;
                       public RouteHandlerBuilder WithMetadata(object metadata) => this;
                       public RouteHandlerBuilder ExcludeFromDescription() => this;
                   }

                   public static class RoutingEndpointExtensions
                   {
                       public static RouteHandlerBuilder MapMethods(this IEndpointRouteBuilder endpoints, string route, IEnumerable<string> httpMethods, Delegate handler) => new();
                   }
               }

               namespace Microsoft.AspNetCore.Http
               {
                   public class HttpContext
                   {
                       public HttpRequest Request { get; set; } = new();
                       public HttpResponse Response { get; set; } = new();
                       public CancellationToken RequestAborted { get; set; }
                       public IServiceProvider RequestServices { get; set; } = new ServiceProviderStub();
                   }

                   public class HttpRequest
                   {
                       public IQueryCollection Query { get; set; } = new QueryCollection();
                       public IDictionary<string, string?> RouteValues { get; set; } = new Dictionary<string, string?>();
                       public Stream Body { get; set; } = new MemoryStream();
                       public long? ContentLength { get; set; }
                       public IServiceProvider RequestServices { get; set; } = new ServiceProviderStub();

                       public void EnableBuffering() { }
                   }

                   public class HttpResponse
                   {
                   }

                   public interface IQueryCollection
                   {
                       string? this[string key] { get; }
                   }

                   public class QueryCollection : IQueryCollection
                   {
                       private readonly Dictionary<string, string> _data = new();
                       public string? this[string key] => _data.GetValueOrDefault(key);
                   }

                   public interface IResult { }

                   public sealed class ResultStub : IResult { }

                   public static class Results
                   {
                       public static IResult Ok(object? value) => new ResultStub();
                       public static IResult NotFound(object? value) => new ResultStub();
                   }

                   public sealed class BadHttpRequestException : Exception
                   {
                       public BadHttpRequestException(string message) : base(message) { }
                   }

                   public static class StatusCodes
                   {
                       public const int Status200OK = 200;
                       public const int Status404NotFound = 404;
                   }
               }

               namespace Microsoft.AspNetCore.Mvc
               {
                   [AttributeUsage(AttributeTargets.Parameter)]
                   public sealed class FromServicesAttribute : Attribute { }
               }

               namespace CrestCreates.DynamicApi
               {
                   [AttributeUsage(AttributeTargets.Interface)]
                   public sealed class DynamicApiRouteAttribute : Attribute
                   {
                       public DynamicApiRouteAttribute(string template) { Template = template; }
                       public string Template { get; }
                   }

                   public interface IDynamicApiGeneratedProvider
                   {
                       IReadOnlyCollection<Assembly> ServiceAssemblies { get; }
                       DynamicApiRegistry CreateRegistry(DynamicApiOptions options);
                       void MapEndpoints(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, DynamicApiOptions options);
                   }

                   public sealed class DynamicApiOptions
                   {
                       public IReadOnlyCollection<Assembly> ServiceAssemblies => Array.Empty<Assembly>();
                       public string DefaultRoutePrefix { get; set; } = "api";
                   }

                   public sealed class DynamicApiRegistry
                   {
                       public DynamicApiRegistry(IReadOnlyList<DynamicApiServiceDescriptor> services) { Services = services; }
                       public IReadOnlyList<DynamicApiServiceDescriptor> Services { get; }
                   }

                   public sealed class DynamicApiServiceDescriptor
                   {
                       public string ServiceName { get; init; } = string.Empty;
                       public string RoutePrefix { get; init; } = string.Empty;
                       public Type ServiceType { get; init; } = typeof(object);
                       public Type ImplementationType { get; init; } = typeof(object);
                       public IReadOnlyList<DynamicApiActionDescriptor> Actions { get; init; } = Array.Empty<DynamicApiActionDescriptor>();
                   }

                   public sealed class DynamicApiActionDescriptor
                   {
                       public string ActionName { get; init; } = string.Empty;
                       public string DeclaringTypeName { get; init; } = string.Empty;
                       public string OperationId { get; init; } = string.Empty;
                       public string RelativeRoute { get; init; } = string.Empty;
                       public string HttpMethod { get; init; } = string.Empty;
                       public string RoutePrefix { get; init; } = string.Empty;
                       public DynamicApiReturnDescriptor ReturnDescriptor { get; init; } = new();
                       public IReadOnlyList<DynamicApiParameterDescriptor> Parameters { get; init; } = Array.Empty<DynamicApiParameterDescriptor>();
                       public DynamicApiPermissionMetadata Permission { get; init; } = new();
                   }

                   public sealed class DynamicApiParameterDescriptor
                   {
                       public string Name { get; init; } = string.Empty;
                       public Type ParameterType { get; init; } = typeof(object);
                       public DynamicApiParameterSource Source { get; init; }
                       public bool IsOptional { get; init; }
                   }

                   public enum DynamicApiParameterSource
                   {
                       Route, Query, Body, CancellationToken
                   }

                   public sealed class DynamicApiReturnDescriptor
                   {
                       public Type DeclaredType { get; init; } = typeof(void);
                       public Type? PayloadType { get; init; }
                       public bool IsVoid { get; init; }
                   }

                   public sealed class DynamicApiPermissionMetadata
                   {
                       public string[] Permissions { get; init; } = Array.Empty<string>();
                       public bool RequireAll { get; init; }
                   }

                   public static class DynamicApiGeneratedRegistryStore
                   {
                       private static readonly List<IDynamicApiGeneratedProvider> _providers = new();
                       public static void Register(IDynamicApiGeneratedProvider provider) => _providers.Add(provider);
                       public static IReadOnlyCollection<IDynamicApiGeneratedProvider> GetProviders() => _providers;
                       public static DynamicApiRegistry? BuildRegistry(DynamicApiOptions options) => null;
                       public static DynamicApiRegistry BuildRequiredRegistry(DynamicApiOptions options) => new(Array.Empty<DynamicApiServiceDescriptor>());
                       public static bool MapGeneratedEndpoints(IEndpointRouteBuilder endpoints, DynamicApiOptions options) => false;
                   }

                   public static class DynamicApiGeneratedRuntime
                   {
                       public static Task<T> ReadBodyAsync<T>(HttpContext context, bool optional) where T : new() => Task.FromResult(default(T));
                       public static Task EnsurePermissionAsync(HttpContext context, IPermissionChecker permissionChecker, IReadOnlyCollection<string> permissions) => Task.CompletedTask;
                       public static Task ValidateAsync<T>(IValidationService validationService, T instance) => Task.CompletedTask;
                       public static Task ExecuteAsync(HttpContext context, bool requiresTransaction, Func<Task> action) => Task.CompletedTask;
                       public static Task<T> ExecuteAsync<T>(HttpContext context, bool requiresTransaction, Func<Task<T>> action) => Task.FromResult(default(T));
                       public static IResult WrapResult<T>(T value) => new ResultStub();
                       public static IResult WrapVoidResult() => new ResultStub();
                       public static IResult WrapGetResult<T>(T value) => new ResultStub();
                   }

                   public static class DynamicApiRouteConvention
                   {
                       public static bool IsScalar(Type type) => false;
                   }

                   public class DynamicApiResponse { }
                   public sealed class DynamicApiResponse<T> : DynamicApiResponse { }

                   public sealed class CrestPermissionException : Exception
                   {
                       public CrestPermissionException(string message) : base(message) { }
                   }
               }

               namespace CrestCreates.Validation.Modules
               {
                   public interface IValidationService
                   {
                       Task<ValidationResult> ValidateAsync<T>(T instance);
                   }

                   public sealed class ValidationResult
                   {
                       public bool IsValid => true;
                       public IReadOnlyList<string> Errors => Array.Empty<string>();
                   }
               }

               namespace CrestCreates.Authorization.Abstractions
               {
                   public interface IPermissionChecker
                   {
                       Task<PermissionGrantResult> IsGrantedAsync(string[] permissions);
                   }

                   public sealed class PermissionGrantResult
                   {
                       public bool AllProhibited => false;
                   }
               }

               namespace CrestCreates.Domain.Shared.Attributes
               {
                   [AttributeUsage(AttributeTargets.Class)]
                   public sealed class CrestServiceAttribute : Attribute { }
               }

               namespace CrestCreates.Aop.Interceptors
               {
                   [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Interface)]
                   public sealed class UnitOfWorkMoAttribute : Attribute
                   {
                       public UnitOfWorkMoAttribute(bool requiresTransaction = true) { RequiresTransaction = requiresTransaction; }
                       public bool RequiresTransaction { get; }
                   }
               }

               namespace CrestCreates.OrmProviders.Abstract
               {
                   public interface IUnitOfWorkManager
                   {
                       IUnitOfWorkScope BeginScope(bool isTransactional = false);
                   }

                   public interface IUnitOfWorkScope : IDisposable
                   {
                       bool IsOwner { get; }
                       bool IsTransactional { get; }
                       IUnitOfWork UnitOfWork { get; }
                   }

                   public interface IUnitOfWork
                   {
                       Task BeginTransactionAsync();
                       Task CommitTransactionAsync();
                       Task RollbackTransactionAsync();
                       Task SaveChangesAsync();
                   }
               }

               public class ServiceProviderStub : IServiceProvider
               {
                   public object? GetService(Type serviceType) => null;
               }
               """;
    }
}
