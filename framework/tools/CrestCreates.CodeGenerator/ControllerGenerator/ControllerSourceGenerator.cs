using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using CrestCreates.CodeGenerator.Authorization;

namespace CrestCreates.CodeGenerator.ControllerGenerator
{
    [Generator]
    public class ControllerSourceGenerator : IIncrementalGenerator
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

        private static bool HasServiceAttribute(INamedTypeSymbol symbol)
        {
            return symbol.GetAttributes().Any(attr =>
                attr.AttributeClass != null && (
                    attr.AttributeClass.Name == "CrestServiceAttribute" ||
                    attr.AttributeClass.Name == "Service" ||
                    attr.AttributeClass.ToDisplayString().EndsWith(".CrestServiceAttribute") ||
                    attr.AttributeClass.ToDisplayString().EndsWith(".Service")
                ));
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
        }

        private void ExecuteGeneration(SourceProductionContext context, ImmutableArray<INamedTypeSymbol?> serviceClasses)
        {
            if (serviceClasses.IsDefaultOrEmpty)
                return;

            // 去重处理
            var uniqueServices = serviceClasses
                .Where(s => s != null)
                .Distinct(SymbolEqualityComparer.Default)
                .Cast<INamedTypeSymbol>()
                .ToList();

            try
            {
                // 生成API控制器
                foreach (var service in uniqueServices)
                {
                    if (GetAttributeProperty(service, "GenerateController", false))
                    {
                        GenerateApiController(context, service);
                    }
                }
            }
            catch (Exception ex)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor("CCCG003", "Controller generation error",
                        $"Error generating controller code: {ex.Message}",
                        "CodeGeneration", DiagnosticSeverity.Warning, true),
                    Location.None));
            }
        }

        private void GenerateApiController(SourceProductionContext context, INamedTypeSymbol serviceClass)
        {
            if (serviceClass == null) return;
            
            var serviceName = serviceClass.Name;
            var namespaceName = serviceClass.ContainingNamespace.ToDisplayString();
            var routeAttr = GetAttributeProperty(serviceClass, "Route", "");
            var route = !string.IsNullOrEmpty(routeAttr) 
                ? routeAttr 
                : $"api/{serviceName.Replace("Service", "").ToLowerInvariant()}";
            
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

            // 如果没有接口，使用生成的接口
            var serviceInterface = interfaceSymbol?.ToDisplayString() ?? $"{namespaceName}.I{serviceName}";
            var controllerName = serviceName.Replace("Service", "");

            // 获取授权配置
            var generateAuthorization = GetAttributeProperty(serviceClass, "GenerateAuthorization", false);
            var resourceName = GetAttributeProperty(serviceClass, "ResourceName", "");
            var generateCrudPermissions = GetAttributeProperty(serviceClass, "GenerateCrudPermissions", true);
            var defaultRoles = GetAttributeProperty<string[]>(serviceClass, "DefaultRoles", Array.Empty<string>());
            var requireAll = GetAttributeProperty(serviceClass, "RequireAll", false);
            var requireAuthorizationForAll = GetAttributeProperty(serviceClass, "RequireAuthorizationForAll", true);

            // 验证授权配置
            ValidateAuthorizationConfig(context, serviceClass, generateAuthorization, resourceName);
            
            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("#nullable enable");
            builder.AppendLine("using System;");
            builder.AppendLine("using System.Threading.Tasks;");
            builder.AppendLine("using Microsoft.AspNetCore.Authorization;");
            builder.AppendLine("using Microsoft.AspNetCore.Mvc;");
            builder.AppendLine("using CrestCreates.Infrastructure.Authorization;");
            builder.AppendLine($"using {namespaceName};");
            
            builder.AppendLine();
            builder.AppendLine($"namespace {namespaceName}.Controllers");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>");
            builder.AppendLine($"    /// {controllerName} API 控制器");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine("    [ApiController]");
            builder.AppendLine($"    [Route(\"{route}\")]");
            if (generateAuthorization && requireAuthorizationForAll)
            {
                builder.AppendLine("    [Authorize]");
            }
            builder.AppendLine($"    public partial class {controllerName}Controller : ControllerBase");

            builder.AppendLine("    {");
            builder.AppendLine($"        private readonly {serviceInterface} _service;");
            builder.AppendLine();
            
            builder.AppendLine($"        public {controllerName}Controller({serviceInterface} service)");
            builder.AppendLine("        {");
            builder.AppendLine("            _service = service ?? throw new ArgumentNullException(nameof(service));");
            builder.AppendLine("        }");
            builder.AppendLine();
            
            // 为每个公共方法生成API端点
            var methods = serviceClass.GetMembers().OfType<IMethodSymbol>()
                .Where(m => m.DeclaredAccessibility == Accessibility.Public && 
                           !m.IsStatic &&
                           m.MethodKind == MethodKind.Ordinary);
                
            foreach (var method in methods)
            {
                GenerateControllerAction(builder, method, generateAuthorization, resourceName, generateCrudPermissions, defaultRoles, requireAll);
            }
            
            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"{controllerName}Controller.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        private void GenerateControllerAction(StringBuilder builder, IMethodSymbol method, bool generateAuthorization, string resourceName, bool generateCrudPermissions, string[] defaultRoles, bool requireAll)
        {
            var methodName = method.Name;
            var httpVerb = DetermineHttpVerb(methodName);
            var routeTemplate = GenerateRouteTemplate(methodName, method.Parameters);
            
            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// {methodName}");
            builder.AppendLine("        /// </summary>");
            
            // 生成授权特性
            if (generateAuthorization)
            {
                GenerateAuthorizationAttributes(builder, methodName, httpVerb, resourceName, generateCrudPermissions, defaultRoles, requireAll);
            }
            
            builder.AppendLine($"        [Http{httpVerb}(\"{routeTemplate}\")]");
            builder.AppendLine("        [ProducesResponseType(200)]");
            
            var isAwaitableReturn = TryGetAwaitableReturn(method.ReturnType, out var hasResult);

            // 方法签名
            var actionReturnType = isAwaitableReturn ? "async Task<IActionResult>" : "IActionResult";
            builder.Append($"        public {actionReturnType} {methodName}(");
            
            // 参数列表
            var parameters = new List<string>();
            foreach (var parameter in method.Parameters)
            {
                string paramAttr = DetermineParameterAttribute(parameter, httpVerb, routeTemplate);
                parameters.Add($"{paramAttr}{parameter.Type.ToDisplayString()} {parameter.Name}");
            }
            
            builder.Append(string.Join(", ", parameters));
            builder.AppendLine(")");
            builder.AppendLine("        {");

            var serviceArguments = string.Join(", ", method.Parameters.Select(p => p.Name));
            if (isAwaitableReturn)
            {
                if (hasResult)
                {
                    builder.AppendLine($"            var result = await _service.{methodName}({serviceArguments});");
                    builder.AppendLine("            return Ok(result);");
                }
                else
                {
                    builder.AppendLine($"            await _service.{methodName}({serviceArguments});");
                    builder.AppendLine("            return Ok();");
                }
            }
            else if (method.ReturnsVoid)
            {
                builder.AppendLine($"            _service.{methodName}({serviceArguments});");
                builder.AppendLine("            return Ok();");
            }
            else
            {
                builder.AppendLine($"            var result = _service.{methodName}({serviceArguments});");
                builder.AppendLine("            return Ok(result);");
            }

            builder.AppendLine("        }");
            builder.AppendLine();
        }

        private string DetermineParameterAttribute(IParameterSymbol parameter, string httpVerb, string routeTemplate)
        {
            var typeName = parameter.Type.ToDisplayString();

            if (typeName == "System.Threading.CancellationToken")
            {
                return string.Empty;
            }

            if (routeTemplate.IndexOf($"{{{parameter.Name}}}", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "[FromRoute] ";
            }
            
            // 简单类型默认从查询字符串获取；只有出现在 route 模板中的参数才走 FromRoute。
            if (parameter.Type.SpecialType != SpecialType.None ||
                typeName == "string" || 
                typeName == "System.Guid" ||
                typeName.StartsWith("System."))
            {
                return "[FromQuery] ";
            }
            
            // GET/DELETE 的复杂查询对象走查询字符串，其它写操作走请求体。
            if (httpVerb == "Get" || httpVerb == "Delete")
            {
                return "[FromQuery] ";
            }
            
            return "[FromBody] ";
        }

        private bool TryGetAwaitableReturn(ITypeSymbol returnType, out bool hasResult)
        {
            hasResult = false;

            if (returnType is not INamedTypeSymbol namedType)
            {
                return false;
            }

            var originalType = namedType.OriginalDefinition.ToDisplayString();
            if (originalType == "System.Threading.Tasks.Task")
            {
                return true;
            }

            if (originalType == "System.Threading.Tasks.Task<TResult>" ||
                originalType == "System.Threading.Tasks.ValueTask<TResult>")
            {
                hasResult = true;
                return true;
            }

            if (originalType == "System.Threading.Tasks.ValueTask")
            {
                return true;
            }

            return false;
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

        private string GenerateRouteTemplate(string methodName, ImmutableArray<IParameterSymbol> parameters)
        {
            var routeMethodName = TrimAsyncSuffix(methodName);
            var firstRouteParameter = parameters.FirstOrDefault(IsRouteParameterCandidate);

            if (routeMethodName.StartsWith("Get"))
            {
                // GetById -> {id}
                if (routeMethodName.EndsWith("ById") && firstRouteParameter != null)
                    return $"{{{firstRouteParameter.Name}}}";
                // GetAll -> 空路由
                if (routeMethodName.EndsWith("All") || routeMethodName.StartsWith("GetList") || routeMethodName == "Get")
                    return "";
                // GetByXxx -> byxxx
                if (routeMethodName.StartsWith("GetBy") && firstRouteParameter != null)
                    return $"by{routeMethodName.Substring(5).ToLowerInvariant()}/{{{firstRouteParameter.Name}}}";
            }
            
            if (routeMethodName.StartsWith("Create") || routeMethodName.StartsWith("Add"))
            {
                return "";
            }
            
            if (routeMethodName.StartsWith("Update") && firstRouteParameter != null)
            {
                return $"{{{firstRouteParameter.Name}}}";
            }
            
            if (routeMethodName.StartsWith("Delete") && firstRouteParameter != null)
            {
                return $"{{{firstRouteParameter.Name}}}";
            }
            
            // 其他情况，使用方法名的小写形式
            return routeMethodName.ToLowerInvariant();
        }

        private static string TrimAsyncSuffix(string methodName)
        {
            return methodName.EndsWith("Async", StringComparison.Ordinal)
                ? methodName.Substring(0, methodName.Length - "Async".Length)
                : methodName;
        }

        private static bool IsRouteParameterCandidate(IParameterSymbol parameter)
        {
            var typeName = parameter.Type.ToDisplayString();
            if (typeName == "System.Threading.CancellationToken")
            {
                return false;
            }

            return parameter.Type.SpecialType != SpecialType.None ||
                   parameter.Type.TypeKind == TypeKind.Enum ||
                   typeName == "string" ||
                   typeName == "System.Guid";
        }
        
        private void GenerateAuthorizationAttributes(StringBuilder builder, string methodName, string httpVerb, string resourceName, bool generateCrudPermissions, string[] defaultRoles, bool requireAll)
        {
            if (string.IsNullOrEmpty(resourceName))
            {
                // 如果没有指定资源名称，使用方法名的前缀
                resourceName = AuthorizationHelper.GetResourceNameFromMethodName(methodName);
            }
            
            var attributes = AuthorizationHelper.GenerateAuthorizationAttributes(
                methodName, 
                httpVerb, 
                resourceName, 
                generateCrudPermissions, 
                defaultRoles, 
                requireAll);
            
            if (!string.IsNullOrEmpty(attributes))
            {
                builder.AppendLine("        " + attributes);
            }
        }
        
        private void ValidateAuthorizationConfig(SourceProductionContext context, INamedTypeSymbol serviceClass, bool generateAuthorization, string resourceName)
        {
            if (generateAuthorization)
            {
                if (string.IsNullOrEmpty(resourceName))
                {
                    // 生成警告：资源名称为空
                    context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor("CCCG004", "Authorization configuration warning",
                            $"GenerateAuthorization is true but ResourceName is empty for service {serviceClass.Name}. Resource name will be inferred from method names.",
                            "CodeGeneration", DiagnosticSeverity.Warning, true),
                        serviceClass.Locations[0]));
                }
            }
        }
        
        private T GetAttributeProperty<T>(ISymbol symbol, string propertyName, T defaultValue)
        {
            if (symbol == null)
                return defaultValue;
                
            var attr = symbol.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass?.Name == "CrestServiceAttribute" || 
                                     attr.AttributeClass?.Name == "Service");

            if (attr == null)
                return defaultValue;

            var namedArg = attr.NamedArguments
                .FirstOrDefault(arg => arg.Key == propertyName);

            if (namedArg.Key == propertyName)
            {
                if (typeof(T).IsArray && namedArg.Value.Kind == TypedConstantKind.Array)
                {
                    var elementType = typeof(T).GetElementType();
                    if (elementType == typeof(string))
                    {
                        var stringArray = namedArg.Value.Values
                            .Select(value => value.Value?.ToString())
                            .Where(value => value != null)
                            .Cast<string>()
                            .ToArray();

                        return (T)(object)stringArray;
                    }
                }

                if (namedArg.Value.Value is T value)
                    return value;
            }

            return defaultValue;
        }

    }
}
