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
            Assert.Contains("public static partial class CrestGeneratedApplicationInitializationExtensions", generatedText);
        }
    }
}
