using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.CodeGenerator.ServiceGenerator
{
    [Generator]
    public class ServiceSourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // 创建增量数据源：查找带有ServiceAttribute的类
            var serviceClasses = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsServiceCandidate(node),
                    transform: static (ctx, _) => GetServiceClass(ctx))
                .Where(static x => x is not null)
                .Collect();

            // 注册源代码生成
            context.RegisterSourceOutput(serviceClasses, ExecuteGeneration);
        }

        private static bool IsServiceCandidate(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax classDeclaration &&
                   classDeclaration.AttributeLists.Count > 0;
        }

        private static INamedTypeSymbol? GetServiceClass(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
            
            if (symbol != null && HasServiceAttribute(symbol))
            {
                return symbol;
            }
            
            return null;
        }        private static bool HasServiceAttribute(INamedTypeSymbol symbol)
        {
            return symbol.GetAttributes().Any(attr =>
                attr.AttributeClass?.Name == "ServiceAttribute" ||
                attr.AttributeClass?.Name == "Service");
        }        private void ExecuteGeneration(SourceProductionContext context, ImmutableArray<INamedTypeSymbol> serviceClasses)
        {
            if (serviceClasses.IsDefaultOrEmpty)
                return;

            // 去重处理
            var uniqueServices = serviceClasses
                .Where(s => s != null)
                .Distinct(SymbolEqualityComparer.Default)
                .ToList();

            // 生成服务注册代码
            var serviceArray = uniqueServices.Cast<INamedTypeSymbol>().ToArray();
            GenerateServiceRegistration(context, serviceArray);            // 生成API控制器
            foreach (var service in uniqueServices)
            {
                if (service != null)
                {
                    var namedTypeSymbol = service as INamedTypeSymbol;
                    if (namedTypeSymbol != null && GetAttributeProperty(namedTypeSymbol, "GenerateController", true))
                    {
                        GenerateApiController(context, namedTypeSymbol);
                    }
                }
            }
        }        private void GenerateServiceRegistration(SourceProductionContext context, INamedTypeSymbol[] serviceClasses)
        {
            var builder = new StringBuilder();
            builder.AppendLine("using System;");
            builder.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            builder.AppendLine();
            builder.AppendLine("namespace CrestCreates.Infrastructure.DependencyInjection");
            builder.AppendLine("{");
            builder.AppendLine("    public static class AutoServiceRegistration");
            builder.AppendLine("    {");
            builder.AppendLine("        public static IServiceCollection AddGeneratedServices(this IServiceCollection services)");
            builder.AppendLine("        {");

            foreach (var service in serviceClasses)
            {
                var serviceType = service.ToDisplayString();
                string? interfaceType = null;
                
                // 查找该服务的接口
                foreach (var interfaceSymbol in service.Interfaces)
                {
                    if (interfaceSymbol.Name.StartsWith("I") && 
                        interfaceSymbol.Name.Substring(1) == service.Name)
                    {
                        interfaceType = interfaceSymbol.ToDisplayString();
                        break;
                    }
                }

                var lifetime = GetAttributeProperty(service, "Lifetime", "Scoped");
                
                if (interfaceType != null)
                {
                    builder.AppendLine($"            services.Add{lifetime}<{interfaceType}, {serviceType}>();");
                }
                else
                {
                    builder.AppendLine($"            services.Add{lifetime}<{serviceType}>();");
                }
            }

            builder.AppendLine("            return services;");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource("AutoServiceRegistration.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }        private void GenerateApiController(SourceProductionContext context, INamedTypeSymbol serviceClass)
        {
            if (serviceClass == null) return;
            
            var serviceName = serviceClass.Name;
            var namespaceName = serviceClass.ContainingNamespace.ToDisplayString();
            var route = GetAttributeProperty(serviceClass, "Route", $"api/{serviceName.ToLowerInvariant()}");
            
            // 查找该服务的接口
            INamedTypeSymbol? interfaceSymbol = null;
            foreach (var iface in serviceClass.Interfaces)
            {
                if (iface.Name.StartsWith("I") && 
                    iface.Name.Substring(1) == serviceName)
                {
                    interfaceSymbol = iface;
                    break;
                }
            }

            if (interfaceSymbol == null)
                return;
            
            var builder = new StringBuilder();
            builder.AppendLine("using System;");
            builder.AppendLine("using System.Threading.Tasks;");
            builder.AppendLine("using Microsoft.AspNetCore.Mvc;");
            builder.AppendLine($"using {namespaceName};");
            
            builder.AppendLine();
            builder.AppendLine($"namespace {namespaceName}.Controllers");
            builder.AppendLine("{");
            builder.AppendLine("    [ApiController]");
            builder.AppendLine($"    [Route(\"{route}\")]");
            builder.AppendLine($"    public class {serviceName}Controller : ControllerBase");
            builder.AppendLine("    {");
            builder.AppendLine($"        private readonly {interfaceSymbol.ToDisplayString()} _{serviceName.ToLowerInvariant()};");
            builder.AppendLine();
            
            builder.AppendLine($"        public {serviceName}Controller({interfaceSymbol.ToDisplayString()} {serviceName.ToLowerInvariant()})");
            builder.AppendLine("        {");
            builder.AppendLine($"            _{serviceName.ToLowerInvariant()} = {serviceName.ToLowerInvariant()};");
            builder.AppendLine("        }");
            builder.AppendLine();
            
            // 为每个公共方法生成API端点
            foreach (var method in interfaceSymbol.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.DeclaredAccessibility != Accessibility.Public)
                    continue;

                var methodName = method.Name;
                var returnType = method.ReturnType.ToDisplayString();
                var httpVerb = DetermineHttpVerb(methodName);
                var routeTemplate = GenerateRouteTemplate(methodName);
                
                builder.AppendLine($"        [Http{httpVerb}(\"{routeTemplate}\")]");
                
                // 方法签名
                builder.Append($"        public async Task<IActionResult> {methodName}(");
                
                // 参数列表
                var parameters = new List<string>();
                foreach (var parameter in method.Parameters)
                {
                    string paramAttr = string.Empty;
                    if (parameter.Type.ToDisplayString().Contains("Dto") || parameter.Type.SpecialType == SpecialType.None)
                    {
                        paramAttr = "[FromBody] ";
                    }
                    parameters.Add($"{paramAttr}{parameter.Type.ToDisplayString()} {parameter.Name}");
                }
                
                builder.Append(string.Join(", ", parameters));
                builder.AppendLine(")");
                builder.AppendLine("        {");
                
                // 方法实现
                builder.Append($"            var result = await _{serviceName.ToLowerInvariant()}.{methodName}(");
                builder.Append(string.Join(", ", method.Parameters.Select(p => p.Name)));
                builder.AppendLine(");");
                builder.AppendLine("            return Ok(result);");
                
                builder.AppendLine("        }");
                builder.AppendLine();
            }
            
            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"{serviceName}Controller.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        private string DetermineHttpVerb(string methodName)
        {
            if (methodName.StartsWith("Get"))
                return "Get";
            if (methodName.StartsWith("Create") || methodName.StartsWith("Add") || methodName.StartsWith("Insert"))
                return "Post";
            if (methodName.StartsWith("Update"))
                return "Put";
            if (methodName.StartsWith("Delete") || methodName.StartsWith("Remove"))
                return "Delete";
            if (methodName.StartsWith("Patch"))
                return "Patch";
            
            return "Post"; // 默认
        }

        private string GenerateRouteTemplate(string methodName)
        {
            if (methodName.StartsWith("Get"))
            {
                // GetById -> {id}
                if (methodName.EndsWith("ById"))
                    return "{id}";
                // GetAll -> 空路由
                if (methodName.EndsWith("All"))
                    return "";
            }
            
            // 其他情况，使用方法名的小写形式
            return methodName.ToLowerInvariant();
        }        private T GetAttributeProperty<T>(ISymbol symbol, string propertyName, T defaultValue)
        {
            if (symbol == null)
                return defaultValue;
                
            var attr = symbol.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass?.Name == "ServiceAttribute" || 
                                     attr.AttributeClass?.Name == "Service");

            if (attr == null)
                return defaultValue;

            var namedArg = attr.NamedArguments
                .FirstOrDefault(arg => arg.Key == propertyName);

            if (namedArg.Key == propertyName && namedArg.Value.Value is T value)
                return value;

            return defaultValue;
        }}
}
