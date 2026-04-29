using CrestCreates.CodeGenerator.TenantDbContextFactoryGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.TenantDbContextFactoryGenerator;

public class TenantDbContextFactorySourceGeneratorTests
{
    private const string DbContextStubs = """
        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext { }
            public class DbContextOptions<T> where T : DbContext { }
        }
        """;

    private const string ITenantDbContextFactoryStub = """
        using Microsoft.EntityFrameworkCore;

        namespace CrestCreates.OrmProviders.EFCore.MultiTenancy
        {
            public interface ITenantDbContextFactory
            {
                TDbContext Create<TDbContext>(DbContextOptions<TDbContext> options) where TDbContext : DbContext;
            }
        }
        """;

    private const string RegistryStoreStub = """
        using Microsoft.EntityFrameworkCore;

        namespace CrestCreates.OrmProviders.EFCore.MultiTenancy
        {
            public static class TenantDbContextFactoryRegistryStore
            {
                public static void Register(ITenantDbContextFactory factory) { }
                public static ITenantDbContextFactory? GetFactory() => null;
            }
        }
        """;

    private static string[] AllStubs => new[] { DbContextStubs, ITenantDbContextFactoryStub, RegistryStoreStub };

    [Fact]
    public void GeneratesFactory_ForDbContextSubclass()
    {
        var source = """
            using Microsoft.EntityFrameworkCore;

            public class AppDbContext : DbContext { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<TenantDbContextFactorySourceGenerator>(source, additionalSources: AllStubs);

        Assert.True(result.ContainsFile("TenantDbContextFactory.g.cs"), "Expected TenantDbContextFactory.g.cs to be generated");

        var generated = result.GetSourceByFileName("TenantDbContextFactory.g.cs");
        Assert.NotNull(generated);
        Assert.Contains("GeneratedTenantDbContextFactory : ITenantDbContextFactory", generated.SourceText);
        Assert.Contains("CreateAppDbContext", generated.SourceText);
        Assert.Contains("new AppDbContext(options)", generated.SourceText);
    }

    [Fact]
    public void GeneratesFactory_ForMultipleDbContexts()
    {
        var source1 = """
            using Microsoft.EntityFrameworkCore;

            public class AppDbContext : DbContext { }
            """;

        var source2 = """
            using Microsoft.EntityFrameworkCore;

            public class TenantDbContext : DbContext { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<TenantDbContextFactorySourceGenerator>(
            new[] { source1, source2 }, additionalSources: AllStubs);

        Assert.True(result.ContainsFile("TenantDbContextFactory.g.cs"));

        var generated = result.GetSourceByFileName("TenantDbContextFactory.g.cs");
        Assert.NotNull(generated);
        Assert.Contains("CreateAppDbContext", generated.SourceText);
        Assert.Contains("CreateTenantDbContext", generated.SourceText);
        Assert.Contains("typeof(TDbContext) == typeof(AppDbContext)", generated.SourceText);
        Assert.Contains("typeof(TDbContext) == typeof(TenantDbContext)", generated.SourceText);
    }

    [Fact]
    public void GeneratesModuleInitializer_ThatRegistersFactory()
    {
        var source = """
            using Microsoft.EntityFrameworkCore;

            public class AppDbContext : DbContext { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<TenantDbContextFactorySourceGenerator>(source, additionalSources: AllStubs);

        var generated = result.GetSourceByFileName("TenantDbContextFactory.g.cs");
        Assert.NotNull(generated);
        Assert.Contains("GeneratedTenantDbContextFactoryRegistration", generated.SourceText);
        Assert.Contains("[System.Runtime.CompilerServices.ModuleInitializer]", generated.SourceText);
        Assert.Contains("TenantDbContextFactoryRegistryStore.Register(new GeneratedTenantDbContextFactory())", generated.SourceText);
    }

    [Fact]
    public void DoesNotGenerate_WhenITenantDbContextFactoryNotAvailable()
    {
        var source = """
            using Microsoft.EntityFrameworkCore;

            public class AppDbContext : DbContext { }
            """;

        // Don't include ITenantDbContextFactoryStub — simulate a project that doesn't reference EFCore
        var result = SourceGeneratorTestHelper.RunGenerator<TenantDbContextFactorySourceGenerator>(
            source, additionalSources: new[] { DbContextStubs });

        Assert.False(result.ContainsFile("TenantDbContextFactory.g.cs"));
    }

    [Fact]
    public void DoesNotGenerate_ForNonDbContextClass()
    {
        var source = """
            public class MyService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<TenantDbContextFactorySourceGenerator>(source, additionalSources: AllStubs);

        Assert.False(result.ContainsFile("TenantDbContextFactory.g.cs"));
    }

    [Fact]
    public void GeneratedFactory_UsesDirectNewInsteadOfActivator()
    {
        var source = """
            using Microsoft.EntityFrameworkCore;

            public class AppDbContext : DbContext { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<TenantDbContextFactorySourceGenerator>(source, additionalSources: AllStubs);

        var generated = result.GetSourceByFileName("TenantDbContextFactory.g.cs");
        Assert.NotNull(generated);
        Assert.DoesNotContain("Activator.CreateInstance", generated.SourceText);
        Assert.Contains("new AppDbContext(options)", generated.SourceText);
    }
}
