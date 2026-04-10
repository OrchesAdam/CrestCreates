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
               using System.Reflection;
               using System.Threading.Tasks;

               namespace Microsoft.AspNetCore.Routing
               {
                   public interface IEndpointRouteBuilder
                   {
                   }
               }

               namespace CrestCreates.DynamicApi
               {
                   [AttributeUsage(AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Class)]
                   public sealed class DynamicApiIgnoreAttribute : Attribute
                   {
                   }

                   [AttributeUsage(AttributeTargets.Interface)]
                   public sealed class DynamicApiRouteAttribute : Attribute
                   {
                       public DynamicApiRouteAttribute(string template)
                       {
                           Template = template;
                       }

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
                       public DynamicApiRegistry(IReadOnlyList<DynamicApiServiceDescriptor> services)
                       {
                           Services = services;
                       }

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
                       Route,
                       Query,
                       Body,
                       CancellationToken
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
               }

               namespace CrestCreates.Domain.Shared.Attributes
               {
                   [AttributeUsage(AttributeTargets.Class)]
                   public sealed class CrestServiceAttribute : Attribute
                   {
                   }
               }

               namespace CrestCreates.Aop.Interceptors
               {
                   [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Interface)]
                   public sealed class UnitOfWorkMoAttribute : Attribute
                   {
                       public UnitOfWorkMoAttribute(bool requiresTransaction = true)
                       {
                           RequiresTransaction = requiresTransaction;
                       }

                       public bool RequiresTransaction { get; }
                   }
               }
               """;
    }
}
