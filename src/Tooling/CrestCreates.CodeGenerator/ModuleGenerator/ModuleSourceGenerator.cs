using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CrestCreates.CodeGenerator.ModuleGenerator;

[Generator]
public class ModuleSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var modules = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => GetModuleInfo(ctx))
            .Where(static module => module is not null);
        context.RegisterSourceOutput(modules.Collect().Combine(context.CompilationProvider), GenerateModuleCode);
    }

    private static ModuleInfo? GetModuleInfo(GeneratorSyntaxContext context)
    {
        var symbol = context.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)context.Node) as INamedTypeSymbol;
        if (symbol is null || !symbol.GetAttributes().Any(attribute => attribute.AttributeClass?.Name == "CrestModuleAttribute")) return null;
        var names = new Stack<string>();
        for (var containing = symbol; containing is not null; containing = containing.ContainingType) names.Push(containing.Name);
        return new ModuleInfo(
            symbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : symbol.ContainingNamespace.ToDisplayString(),
            symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), string.Join("_", names), symbol.ContainingType is not null);
    }

    private static void GenerateModuleCode(SourceProductionContext context, (ImmutableArray<ModuleInfo?>, Compilation) input)
    {
        var modules = input.Item1.Where(module => module is not null).Cast<ModuleInfo>()
            .GroupBy(module => module.FullName, System.StringComparer.Ordinal).Select(group => group.First())
            .OrderBy(module => module.FullName, System.StringComparer.Ordinal).ToList();
        if (modules.Count == 0) return;
        foreach (var module in modules)
        {
            using var hash = SHA256.Create();
            var identity = string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(module.FullName)).Select(value => value.ToString("x2")));
            context.AddSource("Module." + identity + ".Extensions.g.cs", SourceText.From(GenerateSingleModuleExtension(module, identity), Encoding.UTF8));
        }
        if (input.Item2.GetTypeByMetadataName("Microsoft.AspNetCore.Builder.WebApplication") is not null
            && input.Item2.GetTypeByMetadataName("CrestCreates.Web.Module.WebModule") is not null)
            GenerateAppLocalApplicationInitializer(context);
    }

    private static void GenerateAppLocalApplicationInitializer(SourceProductionContext context)
    {
        context.AddSource("CrestApplicationInitialization.g.cs", SourceText.From("""
            #nullable enable
            using System.Threading.Tasks;
            using CrestCreates.Modularity;
            namespace Microsoft.AspNetCore.Builder;
            internal static partial class CrestGeneratedApplicationInitializationExtensions
            {
                public static Task InitializeCrestApplicationAsync(this WebApplication app)
                {
                    return app.InitializeModulesAsync();
                }
            }
            """, Encoding.UTF8));
    }

    private static string GenerateSingleModuleExtension(ModuleInfo module, string identity)
    {
        var extensionName = module.ExtensionName + (module.IsNested ? "_" + identity.Substring(0, 16) : string.Empty);
        var extensionClass = module.ExtensionName + "Extensions_" + identity;
        var code = new StringBuilder();
        code.AppendLine("#nullable enable");
        code.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        code.AppendLine("using Microsoft.Extensions.DependencyInjection.Extensions;");
        code.AppendLine("using Microsoft.Extensions.Hosting;");
        code.AppendLine("using System;");
        if (module.Namespace.Length != 0) code.AppendLine($"namespace {module.Namespace} {{");
        code.AppendLine($"public static class {extensionClass}");
        code.AppendLine("{");
        code.AppendLine($"    public static IServiceCollection Add{extensionName}(this IServiceCollection services)");
        code.AppendLine("    {");
        code.AppendLine($"        services.TryAddSingleton<global::{module.FullName}>();");
        code.AppendLine("        return services;");
        code.AppendLine("    }");
        code.AppendLine($"    public static IHostBuilder Add{extensionName}(this IHostBuilder hostBuilder)");
        code.AppendLine($"        => hostBuilder.ConfigureServices((context, services) => {extensionClass}.Add{extensionName}(services));");
        code.AppendLine($"    public static global::{module.FullName} Get{extensionName}(this IServiceProvider services) => services.GetRequiredService<global::{module.FullName}>();");
        code.AppendLine($"    public static global::{module.FullName}? TryGet{extensionName}(this IServiceProvider services) => services.GetService<global::{module.FullName}>();");
        code.AppendLine("}");
        if (module.Namespace.Length != 0) code.AppendLine("}");
        return code.ToString();
    }

    private sealed class ModuleInfo
    {
        public ModuleInfo(string ns, string fullName, string extensionName, bool isNested)
        {
            Namespace = ns;
            FullName = fullName;
            ExtensionName = extensionName;
            IsNested = isNested;
        }

        public string Namespace { get; }
        public string FullName { get; }
        public string ExtensionName { get; }
        public bool IsNested { get; }
    }
}
