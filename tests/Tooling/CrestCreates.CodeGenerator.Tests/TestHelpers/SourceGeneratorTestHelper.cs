using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.CodeGenerator.Tests.TestHelpers
{
    /// <summary>
    /// 源代码生成器测试结果
    /// </summary>
    public class SourceGeneratorResult
    {
        /// <summary>
        /// 生成的源代码列表
        /// </summary>
        public IReadOnlyList<GeneratedSource> GeneratedSources { get; }

        /// <summary>
        /// 编译诊断信息
        /// </summary>
        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        /// <summary>
        /// 是否编译成功
        /// </summary>
        public bool CompilationSuccess { get; }

        /// <summary>
        /// 编译后的程序集
        /// </summary>
        public byte[]? CompiledAssembly { get; }

        public SourceGeneratorResult(
            IReadOnlyList<GeneratedSource> generatedSources,
            IReadOnlyList<Diagnostic> diagnostics,
            bool compilationSuccess,
            byte[]? compiledAssembly = null)
        {
            GeneratedSources = generatedSources;
            Diagnostics = diagnostics;
            CompilationSuccess = compilationSuccess;
            CompiledAssembly = compiledAssembly;
        }
    }

    /// <summary>
    /// 生成的源代码
    /// </summary>
    public class GeneratedSource
    {
        /// <summary>
        /// 文件名
        /// </summary>
        public string FileName { get; }

        /// <summary>
        /// 源代码内容
        /// </summary>
        public string SourceText { get; }

        /// <summary>
        /// 源代码文本对象
        /// </summary>
        public SourceText Text { get; }

        public GeneratedSource(string fileName, string sourceText, SourceText text)
        {
            FileName = fileName;
            SourceText = sourceText;
            Text = text;
        }
    }

    /// <summary>
    /// 源代码生成器测试辅助类
    /// </summary>
    public static class SourceGeneratorTestHelper
    {
        /// <summary>
        /// 默认的引用程序集
        /// </summary>
        private static readonly string[] DefaultReferences = new[]
        {
            "System.Runtime",
            "netstandard",
            "System.Collections",
            "System.Collections.Concurrent",
            "System.Linq",
            "System.Linq.Expressions",
            "System.Threading.Tasks",
            "System.ComponentModel",
            "System.ComponentModel.Annotations"
        };

        /// <summary>
        /// 缺失的测试特性源代码（从 Domain.Shared 中移除）
        /// 这些特性在源代码中查找需要使用，但已被从框架中移除
        /// </summary>
        internal const string TestAttributesSource = @"
using System;

namespace CrestCreates.Domain.Shared.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class GenerateCrudServiceAttribute : Attribute
    {
        public bool GenerateDto { get; set; } = true;
        public bool GenerateController { get; set; } = false;
        public string ServiceRoute { get; set; } = string.Empty;

        public GenerateCrudServiceAttribute() { }
        public GenerateCrudServiceAttribute(string serviceRoute) { ServiceRoute = serviceRoute; }
        public GenerateCrudServiceAttribute(bool generateController) { GenerateController = generateController; }
        public GenerateCrudServiceAttribute(bool generateController, string serviceRoute) { GenerateController = generateController; ServiceRoute = serviceRoute; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class GenerateRepositoryAttribute : Attribute
    {
        public CrestCreates.Domain.Shared.Enums.OrmProvider OrmProvider { get; set; } = CrestCreates.Domain.Shared.Enums.OrmProvider.EfCore;
        public bool GenerateInterface { get; set; } = true;
        public bool GenerateImplementation { get; set; } = true;

        public GenerateRepositoryAttribute() { }
        public GenerateRepositoryAttribute(CrestCreates.Domain.Shared.Enums.OrmProvider ormProvider) { OrmProvider = ormProvider; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class GenerateEntityAttribute : Attribute
    {
        public bool GenerateRepository { get; set; } = true;
        public bool GenerateRepositoryInterface { get; set; } = true;
        public bool GenerateRepositoryImplementation { get; set; } = true;
        public CrestCreates.Domain.Shared.Enums.OrmProvider OrmProvider { get; set; } = CrestCreates.Domain.Shared.Enums.OrmProvider.EfCore;
        public bool GenerateCrudService { get; set; } = true;
        public bool GenerateDto { get; set; } = true;
        public string[]? ExcludeProperties { get; set; }
        public bool GenerateQueryExtensions { get; set; } = true;
        public string[]? FilterableProperties { get; set; }
        public string[]? SortableProperties { get; set; }
        public bool GenerateController { get; set; } = false;
        public string? ControllerRoute { get; set; }
        public bool GenerateAsBaseClass { get; set; } = true;
        public bool EnableTransaction { get; set; } = true;
        public bool EnableLogging { get; set; } = true;
        public bool EnableValidation { get; set; } = true;
        public bool EnableCaching { get; set; } = false;
        public Type[]? CustomMoAttributes { get; set; }

        public GenerateEntityAttribute() { }
        public GenerateEntityAttribute(CrestCreates.Domain.Shared.Enums.OrmProvider ormProvider) { OrmProvider = ormProvider; }
    }
}
";

        /// <summary>
        /// 运行源代码生成器并返回结果
        /// </summary>
        /// <typeparam name="TGenerator">源代码生成器类型</typeparam>
        /// <param name="source">输入源代码</param>
        /// <param name="additionalSources">额外的源代码文件</param>
        /// <param name="additionalReferences">额外的程序集引用</param>
        /// <returns>测试结果</returns>
        public static SourceGeneratorResult RunGenerator<TGenerator>(
            string source,
            string[]? additionalSources = null,
            string[]? additionalReferences = null,
            Dictionary<string, string>? globalAnalyzerOptions = null)
            where TGenerator : IIncrementalGenerator, new()
        {
            return RunGenerators(new[] { source }, new IIncrementalGenerator[] { new TGenerator() }, additionalSources, additionalReferences, globalAnalyzerOptions);
        }

        /// <summary>
        /// 运行源代码生成器并返回结果（多文件输入）
        /// </summary>
        /// <typeparam name="TGenerator">源代码生成器类型</typeparam>
        /// <param name="sources">输入源代码列表</param>
        /// <param name="additionalSources">额外的源代码文件</param>
        /// <param name="additionalReferences">额外的程序集引用</param>
        /// <returns>测试结果</returns>
        public static SourceGeneratorResult RunGenerator<TGenerator>(
            string[] sources,
            string[]? additionalSources = null,
            string[]? additionalReferences = null)
            where TGenerator : IIncrementalGenerator, new()
        {
            return RunGenerators(sources, new IIncrementalGenerator[] { new TGenerator() }, additionalSources, additionalReferences);
        }

        /// <summary>
        /// 运行多个源代码生成器并返回结果
        /// </summary>
        public static SourceGeneratorResult RunGenerators(
            string source,
            IEnumerable<IIncrementalGenerator> generators,
            string[]? additionalSources = null,
            string[]? additionalReferences = null,
            Dictionary<string, string>? globalAnalyzerOptions = null)
        {
            return RunGenerators(new[] { source }, generators, additionalSources, additionalReferences, globalAnalyzerOptions);
        }

        /// <summary>
        /// 运行多个源代码生成器并返回结果（多文件输入）
        /// </summary>
        public static SourceGeneratorResult RunGenerators(
            string[] sources,
            IEnumerable<IIncrementalGenerator> generators,
            string[]? additionalSources = null,
            string[]? additionalReferences = null,
            Dictionary<string, string>? globalAnalyzerOptions = null)
            => RunGenerators("TestCompilation", sources, generators, additionalSources, additionalReferences, globalAnalyzerOptions, additionalMetadataReferences: null);

        /// <summary>
        /// 运行多个源代码生成器并返回结果，允许指定编译的程序集名与额外的元数据引用镜像。
        /// 用于跨程序集场景：被测程序集通过 <see cref="EmitAssemblyImage"/> 产出的其它程序集镜像引用事件类型，
        /// 从而让生成器区分「事件所属程序集」与「仅引用事件的 handler 程序集」。
        /// </summary>
        public static SourceGeneratorResult RunGenerators(
            string assemblyName,
            string[] sources,
            IEnumerable<IIncrementalGenerator> generators,
            string[]? additionalSources = null,
            string[]? additionalReferences = null,
            Dictionary<string, string>? globalAnalyzerOptions = null,
            IEnumerable<MetadataReference>? additionalMetadataReferences = null)
        {
            var compilation = CreateCompilation(assemblyName, sources, additionalSources, additionalReferences, additionalMetadataReferences);
            var allDiagnostics = new List<Diagnostic>();
            var generatedSources = new List<GeneratedSource>();

            foreach (var generator in generators)
            {
                GeneratorDriver driver = CSharpGeneratorDriver.Create(generator.AsSourceGenerator());
                if (globalAnalyzerOptions is not null)
                {
                    driver = driver.WithUpdatedAnalyzerConfigOptions(
                        new SimpleAnalyzerConfigOptionsProvider(globalAnalyzerOptions));
                }

                driver.RunGeneratorsAndUpdateCompilation(
                    compilation,
                    out var outputCompilation,
                    out var diagnostics);

                var runResult = driver.GetRunResult();
                allDiagnostics.AddRange(diagnostics);
                allDiagnostics.AddRange(runResult.Diagnostics);

                var generatedTrees = outputCompilation.SyntaxTrees
                    .Where(tree => !string.IsNullOrWhiteSpace(tree.FilePath))
                    .Where(tree => !IsSystemFile(tree.FilePath))
                    .ToImmutableArray();

                generatedSources.AddRange(generatedTrees.Select(tree => new GeneratedSource(
                    tree.FilePath,
                    tree.ToString(),
                    tree.GetText())));

                compilation = outputCompilation;
            }

            var memoryStream = new System.IO.MemoryStream();
            var emitResult = compilation.Emit(memoryStream);
            byte[]? compiledAssembly = null;
            if (emitResult.Success)
            {
                memoryStream.Position = 0;
                compiledAssembly = memoryStream.ToArray();
            }

            return new SourceGeneratorResult(
                generatedSources,
                allDiagnostics.Concat(emitResult.Diagnostics).ToImmutableList(),
                emitResult.Success,
                compiledAssembly);
        }

        /// <summary>
        /// 将一个程序集编译为 PE 镜像字节（不运行任何生成器），供跨程序集测试作为 MetadataReference 引用。
        /// 编译失败时抛出，附带诊断，避免把编译错误静默当作"引用缺失"。
        /// </summary>
        public static byte[] EmitAssemblyImage(
            string assemblyName,
            string[] sources,
            string[]? additionalReferences = null)
        {
            var compilation = CreateCompilation(assemblyName, sources, additionalSources: null, additionalReferences);
            using var memoryStream = new System.IO.MemoryStream();
            var emitResult = compilation.Emit(memoryStream);
            if (!emitResult.Success)
            {
                var errors = string.Join("\n", emitResult.Diagnostics
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(diagnostic => diagnostic.ToString()));
                throw new InvalidOperationException($"Failed to emit assembly '{assemblyName}':\n{errors}");
            }

            return memoryStream.ToArray();
        }

        /// <summary>
        /// 运行源代码生成器（异步版本）
        /// </summary>
        /// <typeparam name="TGenerator">源代码生成器类型</typeparam>
        /// <param name="source">输入源代码</param>
        /// <param name="additionalSources">额外的源代码文件</param>
        /// <param name="additionalReferences">额外的程序集引用</param>
        /// <returns>测试结果</returns>
        public static Task<SourceGeneratorResult> RunGeneratorAsync<TGenerator>(
            string source,
            string[]? additionalSources = null,
            string[]? additionalReferences = null)
            where TGenerator : IIncrementalGenerator, new()
        {
            return Task.FromResult(RunGenerator<TGenerator>(source, additionalSources, additionalReferences));
        }

        /// <summary>
        /// 创建编译对象
        /// </summary>
        private static Compilation CreateCompilation(
            string assemblyName,
            string[] sources,
            string[]? additionalSources = null,
            string[]? additionalReferences = null,
            IEnumerable<MetadataReference>? additionalMetadataReferences = null)
        {
            // 获取引用
            var references = new List<MetadataReference>();

            // 添加默认引用
            foreach (var defaultReferenceName in DefaultReferences)
            {
                try
                {
                    var assembly = System.Reflection.Assembly.Load(defaultReferenceName);
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
                catch
                {
                    // 忽略加载失败的程序集
                }
            }

            // 添加当前程序集的引用
            references.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

            // 添加额外引用
            if (additionalReferences != null)
            {
                foreach (var refName in additionalReferences)
                {
                    try
                    {
                        var assembly = System.Reflection.Assembly.Load(refName);
                        references.Add(MetadataReference.CreateFromFile(assembly.Location));
                    }
                    catch
                    {
                        // 忽略加载失败的程序集
                    }
                }
            }

            // 添加 Domain.Shared 引用
            try
            {
                var domainSharedAssembly = System.Reflection.Assembly.Load("CrestCreates.Domain.Shared");
                references.Add(MetadataReference.CreateFromFile(domainSharedAssembly.Location));
            }
            catch
            {
                // 如果加载失败，尝试从类型获取
                try
                {
                    var domainSharedType = typeof(CrestCreates.Domain.Shared.Attributes.EntityAttribute);
                    references.Add(MetadataReference.CreateFromFile(domainSharedType.Assembly.Location));
                }
                catch
                {
                    // 忽略
                }
            }

            TryAddReference(references, "CrestCreates.DynamicApi");
            TryAddReference(references, "CrestCreates.Aop");
            TryAddReference(references, "CrestCreates.Web");
            TryAddReference(references, "Microsoft.AspNetCore.Routing");
            TryAddReference(references, "Microsoft.AspNetCore.Http.Abstractions");
            TryAddReference(references, "Microsoft.AspNetCore.Http.Features");
            TryAddReference(references, "Microsoft.Extensions.Primitives");
            TryAddReference(references, "Microsoft.Extensions.DependencyInjection");
            TryAddReference(references, "Microsoft.Extensions.DependencyInjection.Abstractions");
            TryAddReference(references, "Microsoft.Extensions.Hosting");
            TryAddReference(references, "Microsoft.Extensions.Hosting.Abstractions");

            // For tests that need WebApplication, load Microsoft.AspNetCore from the ASP.NET Core ref pack
            AddAspNetCoreReference(references);

            // 额外的元数据引用镜像（如跨程序集测试中由 EmitAssemblyImage 产出的引用程序集）
            if (additionalMetadataReferences != null)
            {
                references.AddRange(additionalMetadataReferences);
            }

            // 创建语法树
            var syntaxTrees = new List<SyntaxTree>();

            // 包含缺失的测试特性源代码
            syntaxTrees.Add(CSharpSyntaxTree.ParseText(TestAttributesSource));

            foreach (var source in sources)
            {
                syntaxTrees.Add(CSharpSyntaxTree.ParseText(source));
            }

            if (additionalSources != null)
            {
                foreach (var source in additionalSources)
                {
                    syntaxTrees.Add(CSharpSyntaxTree.ParseText(source));
                }
            }

            // 创建编译
            var compilation = CSharpCompilation.Create(
                assemblyName,
                syntaxTrees,
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));

            return compilation;
        }

        /// <summary>
        /// 判断是否为系统文件（仅过滤 Microsoft.NET 内部文件）
        /// </summary>
        private static bool IsSystemFile(string filePath)
        {
            return filePath.Contains("Microsoft.NET");
        }

        private static void TryAddReference(List<MetadataReference> references, string assemblyName)
        {
            try
            {
                var assembly = System.Reflection.Assembly.Load(assemblyName);
                references.Add(MetadataReference.CreateFromFile(assembly.Location));
            }
            catch
            {
                // 忽略加载失败的程序集
            }
        }

        /// <summary>
        /// Add Microsoft.AspNetCore reference assembly so that source generators
        /// can detect WebApplication type availability.
        /// </summary>
        private static void AddAspNetCoreReference(List<MetadataReference> references)
        {
            try
            {
                var assembly = System.Reflection.Assembly.Load("Microsoft.AspNetCore");
                references.Add(MetadataReference.CreateFromFile(assembly.Location));
            }
            catch
            {
                // Fallback: locate the reference assembly from the .NET SDK packs directory
                try
                {
                    var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
                    var packsDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(runtimeDir, "..", "..", "packs"));
                    if (System.IO.Directory.Exists(packsDir))
                    {
                        var refDirs = System.IO.Directory.GetDirectories(packsDir, "Microsoft.AspNetCore.App.Ref");
                        if (refDirs.Length > 0)
                        {
                            var latestRef = refDirs.OrderDescending().First();
                            var dllPath = System.IO.Path.Combine(latestRef, "ref", "net10.0", "Microsoft.AspNetCore.dll");
                            if (System.IO.File.Exists(dllPath))
                            {
                                references.Add(MetadataReference.CreateFromFile(dllPath));
                            }
                        }
                    }
                }
                catch
                {
                    // 忽略加载失败
                }
            }
        }

        /// <summary>
        /// 验证生成的源代码包含指定内容
        /// </summary>
        public static bool ContainsSource(this SourceGeneratorResult result, string content)
        {
            return result.GeneratedSources.Any(s =>
                s.SourceText.Contains(content, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 验证生成的源代码包含指定文件名
        /// </summary>
        public static bool ContainsFile(this SourceGeneratorResult result, string fileName)
        {
            return result.GeneratedSources.Any(s => MatchesFileName(s, fileName));
        }

        /// <summary>
        /// 获取指定文件名的源代码
        /// </summary>
        public static GeneratedSource? GetSourceByFileName(this SourceGeneratorResult result, string fileName)
        {
            return result.GeneratedSources.FirstOrDefault(s => MatchesFileName(s, fileName));
        }

        /// <summary>
        /// 检查文件名是否匹配（支持精确匹配、路径结尾匹配、以及前缀匹配用于带 hash 后缀的文件名）
        /// </summary>
        private static bool MatchesFileName(GeneratedSource s, string fileName)
        {
            // 精确匹配
            if (s.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                return true;

            // 路径结尾匹配（如 \BookController.g.cs）
            if (s.FileName.EndsWith("\\" + fileName, StringComparison.OrdinalIgnoreCase) ||
                s.FileName.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase))
                return true;

            // 如果 fileName 不以 .g.cs 结尾，尝试前缀匹配以支持带 hash 后缀的文件名
            // 如 BookController.ABCD1234.g.cs 匹配 BookController
            if (!fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            {
                var lastSegment = s.FileName;
                var lastSlash = s.FileName.LastIndexOfAny(new[] { '\\', '/' });
                if (lastSlash >= 0)
                    lastSegment = s.FileName.Substring(lastSlash + 1);

                if (lastSegment.StartsWith(fileName + ".", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 获取包含指定内容的源代码
        /// </summary>
        public static GeneratedSource? GetSourceByContent(this SourceGeneratorResult result, string content)
        {
            return result.GeneratedSources.FirstOrDefault(s =>
                s.SourceText.Contains(content, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 验证没有错误诊断信息
        /// </summary>
        public static bool HasNoErrors(this SourceGeneratorResult result)
        {
            return !result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// 获取所有错误诊断信息
        /// </summary>
        public static IEnumerable<Diagnostic> GetErrors(this SourceGeneratorResult result)
        {
            return result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// 获取所有警告诊断信息
        /// </summary>
        public static IEnumerable<Diagnostic> GetWarnings(this SourceGeneratorResult result)
        {
            return result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning);
        }
    }

    internal sealed class SimpleAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly SimpleAnalyzerConfigOptions _globalOptions;
        private static readonly SimpleAnalyzerConfigOptions Empty = new(new Dictionary<string, string>());

        public SimpleAnalyzerConfigOptionsProvider(Dictionary<string, string> globalOptions)
        {
            _globalOptions = new SimpleAnalyzerConfigOptions(globalOptions);
        }

        public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(Microsoft.CodeAnalysis.SyntaxTree tree) => Empty;

        public override AnalyzerConfigOptions GetOptions(Microsoft.CodeAnalysis.AdditionalText textFile) => Empty;
    }

    internal sealed class SimpleAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly Dictionary<string, string> _options;

        public SimpleAnalyzerConfigOptions(Dictionary<string, string> options) => _options = options;

        public override bool TryGetValue(string key, out string value) => _options.TryGetValue(key, out value!);
    }
}
