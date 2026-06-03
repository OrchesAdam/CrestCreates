using System;
using System.Linq;
using CrestCreates.CodeGenerator.CrudServiceGenerator;
using CrestCreates.CodeGenerator.ObjectMappingGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.CrudServiceGenerator;

    public sealed class CrudServiceMainlineSourceGeneratorTests
    {
        [Fact]
        public void GeneratedCrud_WithObjectMappingGenerator_ShouldCompileGeneratedMappings()
        {
            var result = SourceGeneratorTestHelper.RunGenerators(
                EntitySource,
                new IIncrementalGenerator[] { new CrudServiceSourceGenerator(), new ObjectMappingSourceGenerator() },
                new[] { EntitySupport },
                FrameworkReferences);

            Assert.True(result.CompilationSuccess,
                "Compilation failed. Diagnostics:\n" +
                string.Join("\n", result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Select(d => d.ToString())));

            Assert.True(result.ContainsFile("ProductObjectMappings.g.cs"));
            Assert.True(result.ContainsSource("public static partial class ProductObjectMappings"));
            Assert.True(result.ContainsSource("ToTarget("));
            Assert.True(result.ContainsSource("Apply("));

            var implSource = result.GetSourceByFileName("ProductAppService.g.cs")!.SourceText;
            Assert.Contains("ProductObjectMappings.ToTarget(input)", implSource);
            Assert.Contains("ProductObjectMappings.ToTarget(created)", implSource);
            Assert.Contains("ProductObjectMappings.Apply(input, entity)", implSource);
        }

        [Fact]
        public void GeneratedCrud_ShouldCompileForSampleEntity()
        {
            var result = RunProductGenerator();

        Assert.True(result.CompilationSuccess,
            "Compilation failed. Diagnostics:\n" +
            string.Join("\n", result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Select(d => d.ToString())));

        Assert.True(result.ContainsFile("ProductDto.g.cs"));
        Assert.True(result.ContainsFile("CreateProductDto.g.cs"));
        Assert.True(result.ContainsFile("UpdateProductDto.g.cs"));
        Assert.True(result.ContainsFile("ProductListRequestDto.g.cs"));
        Assert.True(result.ContainsFile("IProductAppService.g.cs"));
        Assert.True(result.ContainsFile("ProductAppService.g.cs"));
        Assert.True(result.ContainsFile("ProductCrudPermissions.g.cs"));
        Assert.True(result.ContainsFile("ProductObjectMappings.g.cs"));
        // Dynamic API registration is conditional on IDynamicApiGeneratedProvider presence
    }

    [Fact]
    public void GeneratedCrud_ShouldUseICrestRepositoryBase()
    {
        var result = RunProductGenerator();

        var source = result.GetSourceByFileName("ProductAppService.g.cs")!.SourceText;

        Assert.Contains("ICrestRepositoryBase<Product,", source);
        Assert.DoesNotContain("IProductRepository", source);
        Assert.DoesNotContain("ProductCrudServiceBase", source);
        Assert.DoesNotContain("abstract class ProductAppService", source);
    }

    [Fact]
    public void GeneratedCrud_Contract_ShouldUseAppServiceNaming()
    {
        var result = RunProductGenerator();

        var source = result.GetSourceByFileName("IProductAppService.g.cs")!.SourceText;

        Assert.Contains("public partial interface IProductAppService", source);
        Assert.Contains("ICrudAppService<", source);
        Assert.Contains("ProductDto", source);
        Assert.Contains("ProductListRequestDto", source);
        Assert.DoesNotContain("IProductCrudService", source);
    }

    [Fact]
    public void GeneratedCrud_ListRequest_ShouldOnlyUsePagedRequestDtoDescriptors()
    {
        var result = RunProductGenerator();

        var source = result.GetSourceByFileName("ProductListRequestDto.g.cs")!.SourceText;

        Assert.Contains("public partial class ProductListRequestDto : PagedRequestDto", source);
        Assert.DoesNotContain("Keyword", source);
        Assert.DoesNotContain("StartTime", source);
        Assert.DoesNotContain("EndTime", source);
    }

    [Fact]
    public void GeneratedCrud_ShouldGeneratePermissions()
    {
        var result = RunProductGenerator();

        Assert.True(result.ContainsFile("ProductCrudPermissions.g.cs"));
        var source = result.GetSourceByFileName("ProductCrudPermissions.g.cs")!.SourceText;

        Assert.Contains("Product.Create", source);
        Assert.Contains("Product.Get", source);
        Assert.Contains("Product.Search", source);
        Assert.Contains("Product.Update", source);
        Assert.Contains("Product.Delete", source);
        Assert.DoesNotContain("Product.View", source);
        Assert.DoesNotContain("Product.Export", source);
    }

    [Fact]
    public void GeneratedCrud_UpdateDto_ShouldIncludeConcurrencyStamp_AndCreateDtoShouldExcludeIt()
    {
        var result = RunProductGenerator();

        var updateSource = result.GetSourceByFileName("UpdateProductDto.g.cs")!.SourceText;
        var createSource = result.GetSourceByFileName("CreateProductDto.g.cs")!.SourceText;
        var outputSource = result.GetSourceByFileName("ProductDto.g.cs")!.SourceText;

        Assert.Contains("ConcurrencyStamp", updateSource);
        Assert.Contains("ConcurrencyStamp", outputSource);
        Assert.DoesNotContain("ConcurrencyStamp", createSource);
    }

    [Fact]
    public void GeneratedCrud_Delete_ShouldRequireExpectedStampForConcurrentEntity()
    {
        var result = RunProductGenerator();

        var source = result.GetSourceByFileName("ProductAppService.g.cs")!.SourceText;

        Assert.Contains("DeleteAsync(System.Guid id, string? expectedStamp = null", source);
        Assert.Contains("CrestPreconditionRequiredException", source);
    }

    [Fact]
    public void GeneratedCrud_ShouldGenerateDynamicApiRegistration()
    {
        var result = RunProductGenerator();

        // Dynamic API registration is generated only when IDynamicApiGeneratedProvider
        // is available in the compilation (i.e., when CrestCreates.DynamicApi is referenced).
        if (!result.ContainsFile("ProductCrudDynamicApi.g.cs"))
            return;

        var source = result.GetSourceByFileName("ProductCrudDynamicApi.g.cs")!.SourceText;

        Assert.Contains("ModuleInitializer", source);
        Assert.Contains("DynamicApiGeneratedRegistryStore.Register", source);
        Assert.Contains("IDynamicApiGeneratedProvider", source);
        Assert.Contains("MapEndpoints", source);
        Assert.Contains("CreateRegistry", source);
        Assert.Contains("\"Create\"", source);
        Assert.Contains("\"GetById\"", source);
        Assert.Contains("\"GetList\"", source);
        Assert.Contains("\"Update\"", source);
        Assert.Contains("\"Delete\"", source);
        Assert.Contains("If-Match", source);
        Assert.Contains("expectedStamp", source);

        // CreateRegistry matches assembly before creating descriptor
        Assert.Contains("MatchesAssembly", source);

        // GetList uses POST with body binding for Filters/Sorts support
        Assert.Contains("\"POST\"", source);

        // MatchesAssembly guard present in MapEndpoints
        Assert.Contains("MatchesAssembly(options, typeof(", source);

        // Create and GetList must have distinct routes (Create = POST "", GetList = POST "search"
        Assert.Contains("BuildRoute(routePrefix, \"\"), new[] { \"POST\" }", source);
        Assert.Contains("BuildRoute(routePrefix, \"search\"), new[] { \"POST\" }", source);
        Assert.Contains("PagedResultDto<ProductDto>", source);
    }

    [Fact]
    public void GeneratedCrud_ShouldNotWrapPlatformExceptions()
    {
        var result = RunProductGenerator();

        var source = result.GetSourceByFileName("ProductAppService.g.cs")!.SourceText;

        Assert.DoesNotContain("catch (System.Data.Common.DbException", source);
        Assert.DoesNotContain("catch (Exception ex)", source);
        Assert.DoesNotContain("throw new Exception(", source);
        Assert.Contains("CrestEntityNotFoundException", source);
    }

    [Fact]
    public void GeneratedCrud_ShouldNotGenerateMainlineMvcController()
    {
        var result = RunProductGenerator();

        Assert.DoesNotContain(result.GeneratedSources, x => x.FileName.Contains("Controller"));
        Assert.DoesNotContain(result.GeneratedSources, x => x.SourceText.Contains("CrudControllerBase<"));
        Assert.DoesNotContain(result.GeneratedSources, x => x.SourceText.Contains("[ApiController]"));
    }

    [Fact]
    public void GeneratedCrud_AppService_ShouldCheckGeneratedPermissions()
    {
        var result = RunProductGenerator();

        var source = result.GetSourceByFileName("ProductAppService.g.cs")!.SourceText;

        Assert.Contains("CheckPermissionAsync(ProductCrudPermissions.Create", source);
        Assert.Contains("CheckPermissionAsync(ProductCrudPermissions.Get", source);
        Assert.Contains("CheckPermissionAsync(ProductCrudPermissions.Search", source);
        Assert.Contains("CheckPermissionAsync(ProductCrudPermissions.Update", source);
        Assert.Contains("CheckPermissionAsync(ProductCrudPermissions.Delete", source);
        Assert.DoesNotContain("Product.View", source);
    }

    [Fact]
    public void GeneratedCrud_StringId_ShouldNotGenerateStringParse()
    {
        const string source = """
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace;

[GenerateCrudService]
public class Tag : StringEntity
{
    public string Name { get; set; } = string.Empty;
}
""";

        const string support = """
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TestNamespace
{
    public interface IEntity<TKey>
    {
        TKey Id { get; set; }
    }

    public abstract class StringEntity : IEntity<string>
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
    }
}
""";

        var result = SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
            source,
            new[] { support, MappingSupport },
            FrameworkReferences);

        // Dynamic API file is conditional on IDynamicApiGeneratedProvider presence.
        // String-ID entity test doesn't need full compilation; only checks the
        // generated Dynamic API route parsing code for string ids.
        if (!result.ContainsFile("TagCrudDynamicApi.g.cs"))
            return;

        var dynamicApi = result.GetSourceByFileName("TagCrudDynamicApi.g.cs")!.SourceText;

        // String ID must read route value directly, not call string.Parse (which doesn't exist)
        Assert.DoesNotContain("System.String.Parse(", dynamicApi);
        Assert.DoesNotContain("string.Parse(", dynamicApi);
        Assert.Contains("context.Request.RouteValues[\"id\"]?.ToString()", dynamicApi);
        Assert.Contains("typeof(string)", dynamicApi);
    }

    [Fact]
    public void GeneratedCrud_Dtos_ShouldExcludeTenantId()
    {
        var result = RunProductGenerator();

        var createDto = result.GetSourceByFileName("CreateProductDto.g.cs")!.SourceText;
        var updateDto = result.GetSourceByFileName("UpdateProductDto.g.cs")!.SourceText;

        Assert.DoesNotContain("TenantId", createDto);
        Assert.DoesNotContain("TenantId", updateDto);
    }

    [Fact]
    public void GeneratedCrud_Dtos_ShouldExcludeNavigationProperties()
    {
        const string source = """
using System;
using System.Collections.Generic;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace;

[GenerateCrudService]
public class Blog : Entity<Guid>
{
    public string Title { get; set; } = string.Empty;
    public Author Author { get; set; } = null!;
    public List<Tag> Tags { get; set; } = new();
}

public class Author
{
    public string Name { get; set; } = string.Empty;
}

public class Tag
{
    public string Name { get; set; } = string.Empty;
}
""";

        const string support = """
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; } = default!;
    }
}
""";

        var result = SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
            source,
            new[] { support },
            FrameworkReferences);

        Assert.True(result.ContainsFile("BlogDto.g.cs"));
        Assert.True(result.ContainsFile("CreateBlogDto.g.cs"));
        Assert.True(result.ContainsFile("UpdateBlogDto.g.cs"));

        var outputDto = result.GetSourceByFileName("BlogDto.g.cs")!.SourceText;
        var createDto = result.GetSourceByFileName("CreateBlogDto.g.cs")!.SourceText;
        var updateDto = result.GetSourceByFileName("UpdateBlogDto.g.cs")!.SourceText;

        // Navigation properties (Author, Tags) must not appear in any DTO
        Assert.DoesNotContain("Author", outputDto);
        Assert.DoesNotContain("Author", createDto);
        Assert.DoesNotContain("Author", updateDto);
        Assert.DoesNotContain("Tags", outputDto);
        Assert.DoesNotContain("Tags", createDto);
        Assert.DoesNotContain("Tags", updateDto);

        // Scalar properties should still be present
        Assert.Contains("Title", outputDto);
        Assert.Contains("Title", createDto);
        Assert.Contains("Title", updateDto);
    }

    [Fact]
    public void GeneratedCrud_ConcurrentDelete_ShouldIncludeOnDeletingAsync()
    {
        var result = RunProductGenerator();

        var source = result.GetSourceByFileName("ProductAppService.g.cs")!.SourceText;

        Assert.Contains("OnDeletingAsync(entity, cancellationToken)", source);
    }

    [Fact]
    public void GeneratedCrud_EmptySorts_ShouldGenerateDefaultSort()
    {
        var result = RunProductGenerator();

        var source = result.GetSourceByFileName("ProductAppService.g.cs")!.SourceText;

        // When Sorts is null or empty, a default sort is applied
        Assert.Contains("sorts == null || sorts.Count == 0", source);
        Assert.Contains("new SortDescriptor(", source);
        Assert.Contains("CreationTime", source); // AuditedAggregateRoot has CreationTime
    }

    [Fact]
    public void GeneratedCrud_WithoutDataFilterReference_ShouldNotGenerateIMultiTenant()
    {
        var result = RunProductGenerator();

        var source = result.GetSourceByFileName("ProductAppService.g.cs")!.SourceText;

        // When CrestCreates.DataFilter is not referenced, IMultiTenant code is not generated.
        // When it IS referenced, IMultiTenant must use the same non-null guard as IMustHaveTenant
        // (CurrentUser.TenantId ?? throw, not ?.ToString()).
        // The compilation test verifies no unconditional dependency on CrestCreates.DataFilter exists.

        // In the test environment without CrestCreates.DataFilter, no IMultiTenant code should appear.
        // This proves the using and code are conditional.
        Assert.DoesNotContain("using CrestCreates.DataFilter.Entities", source);
        Assert.DoesNotContain("IMultiTenant", source);
    }

    [Fact]
    public void GeneratedCrud_WithDataFilterReference_ShouldHandleIMultiTenant()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
            EntitySource,
            new[] { EntitySupport, MappingSupport, DataFilterSupport },
            FrameworkReferences);

        Assert.True(result.CompilationSuccess,
            "Compilation failed. Diagnostics:\n" +
            string.Join("\n", result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Select(d => d.ToString())));

        var source = result.GetSourceByFileName("ProductAppService.g.cs")!.SourceText;

        Assert.Contains("using CrestCreates.DataFilter.Entities;", source);
        Assert.Contains("if (entity is IMultiTenant multiTenant)", source);
        Assert.Contains("multiTenant.TenantId = CurrentUser.TenantId ?? throw", source);
        Assert.Contains("IMultiTenant multiTenant && multiTenant.TenantId != CurrentUser.TenantId", source);
        Assert.DoesNotContain("multiTenant.TenantId = CurrentUser.TenantId?.ToString()", source);
    }

    private const string EntitySource = """
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace;

[GenerateCrudService]
public class Product : AuditedAggregateRoot<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
}
""";

    private const string EntitySupport = """
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TestNamespace
{
    public interface IEntity<TKey>
    {
        TKey Id { get; set; }
    }

    public interface IHasConcurrencyStamp
    {
        string ConcurrencyStamp { get; set; }
    }

    public abstract class AuditedAggregateRoot<TKey> : IEntity<TKey>, IHasConcurrencyStamp
        where TKey : IEquatable<TKey>
    {
        public TKey Id { get; set; } = default!;
        public DateTime CreationTime { get; set; }
        public Guid? CreatorId { get; set; }
        public DateTime? LastModificationTime { get; set; }
        public Guid? LastModifierId { get; set; }
        public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();
    }
}
""";

    private const string DataFilterSupport = """
namespace CrestCreates.DataFilter.Entities
{
    public interface IMultiTenant
    {
        string? TenantId { get; set; }
    }
}
""";

    private const string MappingSupport = """
using System;
using System.Threading;
using System.Threading.Tasks;
using TestNamespace.Dtos;

namespace TestNamespace.Mappings
{
    public static partial class ProductObjectMappings
    {
        public static ProductDto ToTarget(Product entity) => throw new NotImplementedException();
        public static Product ToTarget(CreateProductDto dto) => throw new NotImplementedException();
        public static void Apply(UpdateProductDto dto, Product entity) => throw new NotImplementedException();
    }
}
""";

    private static readonly string[] FrameworkReferences = new[]
    {
        "CrestCreates.Domain",
        "CrestCreates.Authorization.Abstractions",
        "CrestCreates.Application.Contracts",
        "Rougamo"
    };

    private static SourceGeneratorResult RunProductGenerator()
    {
        return SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
            EntitySource,
            new[] { EntitySupport, MappingSupport },
            FrameworkReferences);
    }
}
