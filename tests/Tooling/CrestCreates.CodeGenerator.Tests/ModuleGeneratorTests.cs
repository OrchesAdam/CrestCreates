using CrestCreates.CodeGenerator.Tests.Modules;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Linq;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests
{
    public class ModuleGeneratorTests
    {
        [Fact]
        public void Should_Register_Module_Using_ServiceCollection_Extension()
        {
            var services = new ServiceCollection();

            services.AddCoreModule();
            services.AddCoreModule();
            Assert.Single(services.Where(descriptor => descriptor.ServiceType == typeof(CoreModule)));

            using var provider = services.BuildServiceProvider();
            var coreModule = provider.GetService<CoreModule>();

            Assert.NotNull(coreModule);
        }

        [Fact]
        public void Should_Register_Module_Using_HostBuilder_Extension()
        {
            var hostBuilder = Host.CreateDefaultBuilder();

            hostBuilder.AddDatabaseModule();

            using var host = hostBuilder.Build();
            var databaseModule = host.Services.GetService<DatabaseModule>();

            Assert.NotNull(databaseModule);
        }

        [Fact]
        public void Should_Generate_Get_And_TryGet_Module_Extensions()
        {
            var services = new ServiceCollection();
            services.AddApplicationModule();

            using var provider = services.BuildServiceProvider();

            Assert.NotNull(provider.GetApplicationModule());
            Assert.NotNull(provider.TryGetApplicationModule());
        }

        [Fact]
        public void SameNamedModules_GenerateDistinctOwnerHintsAndCompile()
        {
            const string source = """
                namespace One { [CrestCreates.Domain.Shared.Attributes.CrestModule] public class SharedModule {} }
                namespace Two { [CrestCreates.Domain.Shared.Attributes.CrestModule] public class SharedModule {} }
                """;
            var result = SourceGeneratorTestHelper.RunGenerator<CrestCreates.CodeGenerator.ModuleGenerator.ModuleSourceGenerator>(source);

            Assert.True(result.CompilationSuccess, string.Join(System.Environment.NewLine, result.GetErrors()));
            Assert.True(result.HasNoErrors());
            Assert.Equal(2, result.GeneratedSources.Count);
            Assert.Contains(result.GeneratedSources, generated => generated.SourceText.Contains("namespace One"));
            Assert.Contains(result.GeneratedSources, generated => generated.SourceText.Contains("namespace Two"));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NestedAndPartialModules_CompileWithoutHintCollisions(bool partial)
        {
            var source = partial ? """
                namespace Fixture {
                    [CrestCreates.Domain.Shared.Attributes.CrestModule] public partial class EntryModule {}
                    public partial class EntryModule {}
                }
                """ : """
                namespace Fixture {
                    public class First { [CrestCreates.Domain.Shared.Attributes.CrestModule] public class SharedModule {} }
                    public class Second { [CrestCreates.Domain.Shared.Attributes.CrestModule] public class SharedModule {} }
                }
                """;
            var result = SourceGeneratorTestHelper.RunGenerator<CrestCreates.CodeGenerator.ModuleGenerator.ModuleSourceGenerator>(source);

            Assert.True(result.CompilationSuccess, string.Join(System.Environment.NewLine, result.GetErrors()));
            Assert.True(result.HasNoErrors());
            Assert.Equal(partial ? 1 : 2, result.GeneratedSources.Count);
        }

        [Fact]
        public void NestedAndUnderscoreModuleNames_PreserveOrdinaryRegistrationApi()
        {
            const string source = """
                using Microsoft.Extensions.DependencyInjection;
                namespace Fixture {
                    public class First { [CrestCreates.Domain.Shared.Attributes.CrestModule] public class SharedModule {} }
                    [CrestCreates.Domain.Shared.Attributes.CrestModule] public class First_SharedModule {}
                    public static class Consumer {
                        public static void Register(IServiceCollection services) => services.AddFirst_SharedModule();
                    }
                }
                """;
            var result = SourceGeneratorTestHelper.RunGenerator<CrestCreates.CodeGenerator.ModuleGenerator.ModuleSourceGenerator>(source);

            Assert.True(result.CompilationSuccess, string.Join(System.Environment.NewLine, result.GetErrors()));
            Assert.True(result.HasNoErrors());
            Assert.Equal(2, result.GeneratedSources.Count);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OrdinaryModule_PreservesPublicExtensionClass(bool staticCall)
        {
            var source = """
                using Microsoft.Extensions.DependencyInjection;
                namespace Fixture {
                    [CrestCreates.Domain.Shared.Attributes.CrestModule] public class EntryModule {}
                    public static class Consumer {
                        public static IServiceCollection Register(IServiceCollection services) =>
                """ + (staticCall ? "Fixture.EntryModuleExtensions.AddEntryModule(services);" : "services.AddEntryModule();") + "\n} }";
            var result = SourceGeneratorTestHelper.RunGenerator<CrestCreates.CodeGenerator.ModuleGenerator.ModuleSourceGenerator>(source);

            Assert.True(result.CompilationSuccess, string.Join(System.Environment.NewLine, result.GetErrors()));
            Assert.True(result.HasNoErrors());
            if (staticCall) Assert.Contains("public static class EntryModuleExtensions" + System.Environment.NewLine, result.GeneratedSources.Single().SourceText);
        }

        [Theory]
        [InlineData("Fixture")]
        [InlineData("@event")]
        public void ExistingOrdinaryExtensionClass_DoesNotCollideWithGeneratedClass(string namespaceName)
        {
            var source = $$"""
                namespace {{namespaceName}} {
                    public static class EntryModuleExtensions {}
                    [CrestCreates.Domain.Shared.Attributes.CrestModule] public class EntryModule {}
                }
                """;
            var result = SourceGeneratorTestHelper.RunGenerator<CrestCreates.CodeGenerator.ModuleGenerator.ModuleSourceGenerator>(source);

            Assert.True(result.CompilationSuccess, string.Join(System.Environment.NewLine, result.GetErrors()));
            Assert.True(result.HasNoErrors());
        }

        [Fact]
        public void GlobalAlias_DoesNotRedirectGeneratedHostBuilderRegistration()
        {
            const string source = """
                global using EntryModuleExtensions = Foreign.Helper;
                namespace Foreign { public static class Helper {} }
                [CrestCreates.Domain.Shared.Attributes.CrestModule] public class EntryModule {}
                """;
            var result = SourceGeneratorTestHelper.RunGenerator<CrestCreates.CodeGenerator.ModuleGenerator.ModuleSourceGenerator>(source);

            Assert.True(result.CompilationSuccess, string.Join(System.Environment.NewLine, result.GetErrors()));
            Assert.True(result.HasNoErrors());
        }

        [Fact]
        public void ExistingGenericExtensionClass_DoesNotRenameOrdinaryApi()
        {
            const string source = """
                using Microsoft.Extensions.DependencyInjection;
                namespace Fixture {
                    public static class EntryModuleExtensions<T> {}
                    [CrestCreates.Domain.Shared.Attributes.CrestModule] public class EntryModule {}
                    public static class Consumer {
                        public static IServiceCollection Register(IServiceCollection services) => EntryModuleExtensions.AddEntryModule(services);
                    }
                }
                """;
            var result = SourceGeneratorTestHelper.RunGenerator<CrestCreates.CodeGenerator.ModuleGenerator.ModuleSourceGenerator>(source);

            Assert.True(result.CompilationSuccess, string.Join(System.Environment.NewLine, result.GetErrors()));
            Assert.True(result.HasNoErrors());
        }

        [Fact]
        public void ExistingNamespaceExtensionName_DoesNotCollideWithGeneratedClass()
        {
            const string source = """
                namespace Fixture {
                    namespace EntryModuleExtensions { public class Marker {} }
                    [CrestCreates.Domain.Shared.Attributes.CrestModule] public class EntryModule {}
                }
                """;
            var result = SourceGeneratorTestHelper.RunGenerator<CrestCreates.CodeGenerator.ModuleGenerator.ModuleSourceGenerator>(source);

            Assert.True(result.CompilationSuccess, string.Join(System.Environment.NewLine, result.GetErrors()));
            Assert.True(result.HasNoErrors());
        }

        [Fact]
        public void OrdinaryModulesWithSameSimpleName_KeepPublicClassesInSeparateNamespaces()
        {
            const string source = """
                namespace One { [CrestCreates.Domain.Shared.Attributes.CrestModule] public class SharedModule {} }
                namespace Two { [CrestCreates.Domain.Shared.Attributes.CrestModule] public class SharedModule {} }
                """;
            var result = SourceGeneratorTestHelper.RunGenerator<CrestCreates.CodeGenerator.ModuleGenerator.ModuleSourceGenerator>(source);

            Assert.True(result.CompilationSuccess, string.Join(System.Environment.NewLine, result.GetErrors()));
            Assert.True(result.HasNoErrors());
            Assert.All(result.GeneratedSources, generated => Assert.Contains("public static class SharedModuleExtensions" + System.Environment.NewLine, generated.SourceText));
        }

        [Fact]
        public void OrdinaryAndNestedOwnersWithSameStem_UseDistinctPublicClasses()
        {
            const string source = """
                namespace Fixture {
                    [CrestCreates.Domain.Shared.Attributes.CrestModule] public class First_SharedModule {}
                    public class First { [CrestCreates.Domain.Shared.Attributes.CrestModule] public class SharedModule {} }
                }
                """;
            var result = SourceGeneratorTestHelper.RunGenerator<CrestCreates.CodeGenerator.ModuleGenerator.ModuleSourceGenerator>(source);

            Assert.True(result.CompilationSuccess, string.Join(System.Environment.NewLine, result.GetErrors()));
            Assert.True(result.HasNoErrors());
            Assert.Contains(result.GeneratedSources, generated => generated.SourceText.Contains("public static class First_SharedModuleExtensions" + System.Environment.NewLine));
        }

        [Fact]
        public void NestedOwnersWithSameStem_UseDistinctPublicClasses()
        {
            const string source = """
                namespace Fixture {
                    public class First { public class Inner { [CrestCreates.Domain.Shared.Attributes.CrestModule] public class SharedModule {} } }
                    public class First_Inner { [CrestCreates.Domain.Shared.Attributes.CrestModule] public class SharedModule {} }
                }
                """;
            var result = SourceGeneratorTestHelper.RunGenerator<CrestCreates.CodeGenerator.ModuleGenerator.ModuleSourceGenerator>(source);

            Assert.True(result.CompilationSuccess, string.Join(System.Environment.NewLine, result.GetErrors()));
            Assert.True(result.HasNoErrors());
        }

        [Fact]
        public void NonConflictingNestedOwners_PreserveReadablePublicClassNames()
        {
            const string source = """
                namespace Fixture {
                    public class First { [CrestCreates.Domain.Shared.Attributes.CrestModule] public class SharedModule {} }
                    public class Second { [CrestCreates.Domain.Shared.Attributes.CrestModule] public class SharedModule {} }
                }
                """;
            var result = SourceGeneratorTestHelper.RunGenerator<CrestCreates.CodeGenerator.ModuleGenerator.ModuleSourceGenerator>(source);

            Assert.True(result.CompilationSuccess, string.Join(System.Environment.NewLine, result.GetErrors()));
            Assert.True(result.HasNoErrors());
            Assert.Contains(result.GeneratedSources, generated => generated.SourceText.Contains("public static class First_SharedModuleExtensions" + System.Environment.NewLine));
            Assert.Contains(result.GeneratedSources, generated => generated.SourceText.Contains("public static class Second_SharedModuleExtensions" + System.Environment.NewLine));
        }

        [Fact]
        public void Should_Not_Generate_Global_Module_Auto_Initializer()
        {
            var generatedType = typeof(ModuleGeneratorTests).Assembly
                .GetType("CrestCreates.Modularity.ModuleAutoInitializer", throwOnError: false);

            Assert.Null(generatedType);
        }

        [Fact]
        public void Should_Not_Generate_Global_Module_Descriptor_Registry()
        {
            var generatedType = typeof(ModuleGeneratorTests).Assembly
                .GetType("CrestCreates.Modularity.ModuleDescriptorRegistry", throwOnError: false);

            Assert.Null(generatedType);
        }

        [Fact]
        public void Should_Generate_InitializeCrestApplicationAsync_For_Web_Projects()
        {
            var source = @"
using CrestCreates.Domain.Shared.Attributes;

namespace TestApp
{
    [CrestModule]
    public class TestWebModule
    {
    }
}
";

            // Provide stub types so the source generator detects a web project context.
            // The generator checks for CrestCreates.Web.Module.WebModule to decide
            // whether to emit the app-local initializer.
            // Also provide a stub for InitializeModulesAsync so the generated code compiles.
            var stubSource = @"
namespace CrestCreates.Web.Module
{
    public class WebModule
    {
    }
}

namespace CrestCreates.Modularity
{
    using System.Threading.Tasks;

    public static class ModuleAutoInitializer
    {
        public static Task<object> InitializeModulesAsync(this object host) => Task.FromResult(host);
    }
}";

            var result = SourceGeneratorTestHelper.RunGenerator<CrestCreates.CodeGenerator.ModuleGenerator.ModuleSourceGenerator>(
                source,
                additionalSources: new[] { stubSource });

            var errors = result.GetErrors().ToList();
            Assert.True(result.CompilationSuccess,
                $"Compilation should succeed. Errors: {errors.Count}. " +
                $"First error: {(errors.FirstOrDefault()?.GetMessage() ?? "none")}");
            Assert.True(result.HasNoErrors(), "Should have no errors");

            var generatedFile = result.GetSourceByFileName("CrestApplicationInitialization.g.cs");
            Assert.NotNull(generatedFile);
            Assert.Contains("InitializeCrestApplicationAsync", generatedFile!.SourceText);
            Assert.Contains("WebApplication", generatedFile.SourceText);
            Assert.Contains("InitializeModulesAsync", generatedFile.SourceText);
        }

        [Fact]
        public void InitializeCrestApplicationAsync_Calls_AppLocal_InitializeModulesAsync()
        {
            var source = @"
using CrestCreates.Domain.Shared.Attributes;

namespace TestApp
{
    [CrestModule]
    public class TestWebModule
    {
    }
}
";

            var stubSource = @"
namespace CrestCreates.Web.Module
{
    public class WebModule
    {
    }
}

namespace CrestCreates.Modularity
{
    using System.Threading.Tasks;

    public static class ModuleAutoInitializer
    {
        public static Task<object> InitializeModulesAsync(this object host) => Task.FromResult(host);
    }
}";

            var result = SourceGeneratorTestHelper.RunGenerator<CrestCreates.CodeGenerator.ModuleGenerator.ModuleSourceGenerator>(
                source,
                additionalSources: new[] { stubSource });

            Assert.True(result.CompilationSuccess);

            var generatedText = result.GetSourceByFileName("CrestApplicationInitialization.g.cs")?.SourceText;
            Assert.NotNull(generatedText);
            Assert.Contains("return app.InitializeModulesAsync();", generatedText);
            Assert.Contains("namespace Microsoft.AspNetCore.Builder;", generatedText);
            Assert.Contains("internal static partial class CrestGeneratedApplicationInitializationExtensions", generatedText);
        }
    }
}
