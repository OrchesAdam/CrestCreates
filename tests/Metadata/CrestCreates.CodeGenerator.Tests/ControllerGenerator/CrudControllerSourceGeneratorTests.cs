using CrestCreates.CodeGenerator.ControllerGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.ControllerGenerator
{
    public class CrudControllerSourceGeneratorTests
    {
        [Fact]
        public void Should_Generate_Thin_Controller_From_Crud_Service_Metadata()
        {
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Application.Contracts.Interfaces;

namespace CrestCreates.Domain.Shared.Attributes
{
    [AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
    public sealed class CrestCrudApiControllerAttribute : Attribute
    {
        public CrestCrudApiControllerAttribute(string controllerName, string route)
        {
            ControllerName = controllerName;
            Route = route;
        }

        public string ControllerName { get; }
        public string Route { get; }
    }
}

namespace CrestCreates.Application.Contracts.Interfaces
{
    public interface ICrudAppService<TKey, TDto, TCreateDto, TUpdateDto, TListRequestDto>
        where TKey : IEquatable<TKey>
    {
        TDto UpdateAsync(TKey id, TUpdateDto input);
    }
}

namespace Microsoft.AspNetCore.Mvc
{
    public class ControllerBase
    {
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ApiControllerAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class RouteAttribute : Attribute
    {
        public RouteAttribute(string template)
        {
        }
    }
}

namespace CrestCreates.AspNetCore.Controllers
{
    public abstract class CrudControllerBase<TService, TKey, TDto, TCreateDto, TUpdateDto, TListRequestDto>
        : Microsoft.AspNetCore.Mvc.ControllerBase
        where TService : CrestCreates.Application.Contracts.Interfaces.ICrudAppService<TKey, TDto, TCreateDto, TUpdateDto, TListRequestDto>
        where TKey : IEquatable<TKey>
    {
        protected CrudControllerBase(TService service)
        {
        }
    }
}

namespace TestNamespace.Services
{
    [CrestCrudApiController(""Book"", ""api/books"")]
    public interface IBookCrudService : ICrudAppService<Guid, BookDto, CreateBookDto, UpdateBookDto, BookListRequestDto>
    {
    }

    public sealed class BookDto { }
    public sealed class CreateBookDto { }
    public sealed class UpdateBookDto { }
    public sealed class BookListRequestDto { }
}
";

            var result = SourceGeneratorTestHelper.RunGenerator<CrudControllerSourceGenerator>(source);

            Assert.True(result.ContainsFile("BookController"));
            var controllerSource = result.GetSourceByFileName("BookController");
            Assert.NotNull(controllerSource);
            Assert.Contains("namespace TestNamespace.Controllers", controllerSource!.SourceText);
            Assert.Contains("[Route(\"api/books\")]", controllerSource.SourceText);
            Assert.Contains("public sealed partial class BookController", controllerSource.SourceText);
            Assert.Contains("CrudControllerBase<global::TestNamespace.Services.IBookCrudService, global::System.Guid", controllerSource.SourceText);
            Assert.True(result.CompilationSuccess);
        }
    }
}
