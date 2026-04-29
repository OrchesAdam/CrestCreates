using System;
using System.Linq;
using CrestCreates.CodeGenerator.TenantFilterGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.TenantFilterGenerator;

public class TenantFilterSourceGeneratorTests
{
    private const string IMultiTenantStub = """
        namespace CrestCreates.OrmProviders.EFCore.MultiTenancy
        {
            public interface IMultiTenant
            {
                string? TenantId { get; set; }
            }
        }
        """;

    private const string ICurrentTenantStub = """
        namespace CrestCreates.MultiTenancy.Abstract
        {
            public interface ICurrentTenant
            {
                ITenantInfo? Tenant { get; }
                string? Id { get; }
            }
            public interface ITenantInfo
            {
                string Name { get; }
            }
        }
        """;

    private const string TenantFilterRegistryStoreStub = """
        using Microsoft.EntityFrameworkCore;
        using CrestCreates.MultiTenancy.Abstract;

        namespace CrestCreates.OrmProviders.EFCore.MultiTenancy
        {
            public static class TenantFilterRegistryStore
            {
                public delegate void ApplyAllDelegate(ModelBuilder modelBuilder, ICurrentTenant currentTenant);
                public static void Register(ApplyAllDelegate applyAll) { }
                public static ApplyAllDelegate? GetApplyAll() => null;
            }
        }
        """;

    private const string ModelBuilderStub = """
        namespace Microsoft.EntityFrameworkCore
        {
            public class ModelBuilder
            {
                public EntityTypeBuilder<T> Entity<T>() => new EntityTypeBuilder<T>();
            }
            public class DbContext { }
            public class DbSet<T> { }
            public class EntityTypeBuilder<T>
            {
                public EntityTypeBuilder<T> HasQueryFilter(System.Linq.Expressions.Expression<System.Func<T, bool>> filter) => this;
                public IndexBuilder HasIndex(System.Linq.Expressions.Expression<System.Func<T, object?>> indexExpression) => new IndexBuilder();
            }
            public class IndexBuilder
            {
                public IndexBuilder HasDatabaseName(string name) => this;
            }
        }
        """;

    private static string[] AllStubs => new[] { IMultiTenantStub, ICurrentTenantStub, TenantFilterRegistryStoreStub, ModelBuilderStub };

    [Fact]
    public void GeneratesTenantFilter_ForEntityWithIMultiTenant()
    {
        var source = """
            using CrestCreates.Domain.Shared.Attributes;
            using CrestCreates.OrmProviders.EFCore.MultiTenancy;

            [Entity]
            public class Book : IMultiTenant
            {
                public string? TenantId { get; set; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<TenantFilterSourceGenerator>(source, additionalSources: AllStubs);

        Assert.True(result.ContainsFile("TenantFilter.g.cs"), "Expected TenantFilter.g.cs to be generated");
        Assert.True(result.CompilationSuccess, string.Join(Environment.NewLine, result.GetErrors()));

        var generated = result.GetSourceByFileName("TenantFilter.g.cs");
        Assert.NotNull(generated);
        Assert.Contains("ConfigureBookFilter", generated.SourceText);
        Assert.Contains("HasQueryFilter", generated.SourceText);
        Assert.Contains("TenantFilterRegistryStore.Register(ApplyAll)", generated.SourceText);
        Assert.Contains("[System.Runtime.CompilerServices.ModuleInitializer]", generated.SourceText);
    }

    [Fact]
    public void GeneratesTenantFilter_ForMultipleMultiTenantEntities()
    {
        var source1 = """
            using CrestCreates.Domain.Shared.Attributes;
            using CrestCreates.OrmProviders.EFCore.MultiTenancy;

            [Entity]
            public class Book : IMultiTenant
            {
                public string? TenantId { get; set; }
            }
            """;

        var source2 = """
            using CrestCreates.Domain.Shared.Attributes;
            using CrestCreates.OrmProviders.EFCore.MultiTenancy;

            [Entity]
            public class Order : IMultiTenant
            {
                public string? TenantId { get; set; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<TenantFilterSourceGenerator>(
            new[] { source1, source2 }, additionalSources: AllStubs);

        Assert.True(result.ContainsFile("TenantFilter.g.cs"));
        Assert.True(result.CompilationSuccess, string.Join(Environment.NewLine, result.GetErrors()));

        var generated = result.GetSourceByFileName("TenantFilter.g.cs");
        Assert.NotNull(generated);
        Assert.Contains("ConfigureBookFilter", generated.SourceText);
        Assert.Contains("ConfigureOrderFilter", generated.SourceText);
        Assert.Contains("ApplyAll", generated.SourceText);
    }

    [Fact]
    public void DoesNotGenerate_ForEntityWithoutIMultiTenant()
    {
        var source = """
            using CrestCreates.Domain.Shared.Attributes;

            [Entity]
            public class Book
            {
                public string Title { get; set; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<TenantFilterSourceGenerator>(source, additionalSources: AllStubs);

        Assert.False(result.ContainsFile("TenantFilter.g.cs"));
    }

    [Fact]
    public void DoesNotGenerate_WhenTenantFilterRegistryStoreNotAvailable()
    {
        var source = """
            using CrestCreates.Domain.Shared.Attributes;
            using CrestCreates.OrmProviders.EFCore.MultiTenancy;

            [Entity]
            public class Book : IMultiTenant
            {
                public string? TenantId { get; set; }
            }
            """;

        // Don't include TenantFilterRegistryStoreStub — simulate a project that doesn't reference EFCore
        var result = SourceGeneratorTestHelper.RunGenerator<TenantFilterSourceGenerator>(
            source, additionalSources: new[] { IMultiTenantStub, ICurrentTenantStub, ModelBuilderStub });

        Assert.False(result.ContainsFile("TenantFilter.g.cs"));
    }

    [Fact]
    public void GeneratesTenantFilter_ForTenantEntityReferencedByDbContextDbSet()
    {
        var entitySource = """
            using CrestCreates.OrmProviders.EFCore.MultiTenancy;

            namespace External.Domain
            {
                public class Book : IMultiTenant
                {
                    public string? TenantId { get; set; }
                }
            }
            """;

        var dbContextSource = """
            using External.Domain;
            using Microsoft.EntityFrameworkCore;

            public class AppDbContext : DbContext
            {
                public DbSet<Book> Books { get; set; } = null!;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<TenantFilterSourceGenerator>(
            new[] { dbContextSource }, additionalSources: AllStubs.Concat(new[] { entitySource }).ToArray());

        Assert.True(result.ContainsFile("TenantFilter.g.cs"), "Expected TenantFilter.g.cs to be generated for DbSet<T> tenant entity");
        Assert.True(result.CompilationSuccess, string.Join(Environment.NewLine, result.GetErrors()));

        var generated = result.GetSourceByFileName("TenantFilter.g.cs");
        Assert.NotNull(generated);
        Assert.Contains("ConfigureBookFilter", generated.SourceText);
        Assert.Contains("External.Domain.Book", generated.SourceText);
    }

    [Fact]
    public void GeneratedFilter_UsesCurrentTenantId()
    {
        var source = """
            using CrestCreates.Domain.Shared.Attributes;
            using CrestCreates.OrmProviders.EFCore.MultiTenancy;

            [Entity]
            public class Book : IMultiTenant
            {
                public string? TenantId { get; set; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<TenantFilterSourceGenerator>(source, additionalSources: AllStubs);

        Assert.True(result.CompilationSuccess, string.Join(Environment.NewLine, result.GetErrors()));

        var generated = result.GetSourceByFileName("TenantFilter.g.cs");
        Assert.NotNull(generated);
        Assert.Contains("currentTenant.Id == null || e.TenantId == currentTenant.Id", generated.SourceText);
    }
}
