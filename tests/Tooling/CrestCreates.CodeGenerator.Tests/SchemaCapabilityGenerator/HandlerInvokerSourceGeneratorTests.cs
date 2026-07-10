using CrestCreates.CodeGenerator.SchemaCapabilityGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.SchemaCapabilityGenerator;

public class HandlerInvokerSourceGeneratorTests
{
    #region Stubs

    /// <summary>
    /// Provides stubs for types in the CrestCreates.Capability namespace that the
    /// generated code references but may not be available as loaded assemblies.
    /// </summary>
    private static string BuildCapabilityStubs()
    {
        return @"
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability.Abstractions
{
    public class CapabilityHandlerResolver
    {
        public void Register(string capabilityId, ICapabilityHandlerInvoker invoker) { }
    }

    public static class CapabilityHandlerResolverProvider
    {
        public static void Register(string capabilityId, ICapabilityHandlerInvoker invoker) { }

        [System.Obsolete]
        public static void SetResolver(ICapabilityHandlerResolver resolver) { }
    }
}

namespace MyApp
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class CapabilityNameAttribute : System.Attribute
    {
        public CapabilityNameAttribute(string name) { }
    }
}
";
    }

    #endregion

    /// <summary>
    /// Helper that runs the generator with source + stubs and returns the result.
    /// </summary>
    private static SourceGeneratorResult Run(string source)
    {
        return SourceGeneratorTestHelper.RunGenerator<HandlerInvokerSourceGenerator>(
            source,
            additionalSources: new[] { BuildCapabilityStubs() },
            additionalReferences: new[] { "CrestCreates.Capability.Abstractions" });
    }

    [Fact]
    public void GeneratedCode_UsesRegisterNotSetResolver()
    {
        var source = """
            using CrestCreates.Capability.Abstractions;
            using MyApp;

            namespace MyApp;

            [CapabilityName("test-capability")]
            public class TestHandler : ICapabilityHandler<string, string>
            {
                public Task<string> ExecuteAsync(string input, CancellationToken ct) => Task.FromResult(input);
            }
            """;

        var result = Run(source);
        var generated = result.GetSourceByFileName("GeneratedHandlerRegistry.g.cs");
        generated.Should().NotBeNull();
        generated!.SourceText.Should().Contain("CapabilityHandlerResolverProvider.Register(");
        generated.SourceText.Should().NotContain("CapabilityHandlerResolverProvider.SetResolver(");
        generated.SourceText.Should().NotContain("new CapabilityHandlerResolver()");
    }
}
