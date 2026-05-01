using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
            string[]? additionalReferences = null)
            where TGenerator : IIncrementalGenerator, new()
        {
            return RunGenerator<TGenerator>(new[] { source }, additionalSources, additionalReferences);
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
            // 创建编译
            var compilation = CreateCompilation(sources, additionalSources, additionalReferences);

            // 创建生成器
            var generator = new TGenerator();
            var driver = CSharpGeneratorDriver.Create(generator);

            // 运行生成器
            driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out var outputCompilation,
                out var diagnostics);

            // 获取生成的源代码 - 优先从 runResult 获取
            var runResult = driver.GetRunResult();

            // 将 runResult 的诊断信息也加入
            var allDiagnostics = diagnostics.Concat(runResult.Diagnostics);

            var generatedTrees = runResult.GeneratedTrees;

            // 如果 runResult 没有生成的树，则从 outputCompilation 中提取
            if (generatedTrees.IsEmpty)
            {
                generatedTrees = outputCompilation.SyntaxTrees
                    .Where(tree => !sources.Any(s => tree.ToString().Contains(s.Substring(0, Math.Min(100, s.Length)))))
                    .Where(tree => !IsSystemFile(tree.FilePath))
                    .ToImmutableArray();
            }

            var generatedSources = generatedTrees
                .Select(tree => new GeneratedSource(
                    tree.FilePath,
                    tree.ToString(),
                    tree.GetText()))
                .ToList();

            // 尝试编译生成的代码
            var memoryStream = new System.IO.MemoryStream();
            var emitResult = outputCompilation.Emit(memoryStream);
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
            string[] sources,
            string[]? additionalSources = null,
            string[]? additionalReferences = null)
        {
            // 获取引用
            var references = new List<MetadataReference>();

            // 添加默认引用
            foreach (var assemblyName in DefaultReferences)
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
            TryAddReference(references, "Microsoft.AspNetCore.Routing");

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
                "TestCompilation",
                syntaxTrees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

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
}
