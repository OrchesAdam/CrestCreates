using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace CrestCreates.CodeGenerator.DynamicApiGenerator;

[Generator]
public class DynamicApiSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var dynamicApisProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is InterfaceDeclarationSyntax,
                transform: static (ctx, _) => GetDynamicApiInfo(ctx))
            .Where(static m => m is not null);

        context.RegisterSourceOutput(dynamicApisProvider.Collect(), GenerateApiCode);
    }

    private static DynamicApiInfo? GetDynamicApiInfo(GeneratorSyntaxContext context)
    {
        var interfaceDeclaration = (InterfaceDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(interfaceDeclaration);
        if (symbol is null) return null;

        var dynamicApiAttribute = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "DynamicApiAttribute");
        if (dynamicApiAttribute is null) return null;

        var route = "";
        var controllerName = "";
        var area = "";
        var enableAuthorization = false;

        foreach (var namedArg in dynamicApiAttribute.NamedArguments)
        {
            if (namedArg.Key == "Route" && namedArg.Value.Value is string routeValue)
            {
                route = routeValue;
            }
            else if (namedArg.Key == "ControllerName" && namedArg.Value.Value is string controllerValue)
            {
                controllerName = controllerValue;
            }
            else if (namedArg.Key == "Area" && namedArg.Value.Value is string areaValue)
            {
                area = areaValue;
            }
            else if (namedArg.Key == "EnableAuthorization" && namedArg.Value.Value is bool authValue)
            {
                enableAuthorization = authValue;
            }
        }

        var methods = new List<DynamicApiMethodInfo>();
        var interfaceSymbol = symbol as INamedTypeSymbol;
        if (interfaceSymbol != null)
        {
            foreach (var member in interfaceSymbol.GetMembers())
            {
                if (member is IMethodSymbol methodSymbol)
                {
                var methodInfo = new DynamicApiMethodInfo
                {
                    Name = methodSymbol.Name,
                    ReturnType = methodSymbol.ReturnType.ToDisplayString(),
                    Parameters = methodSymbol.Parameters.Select(p => new DynamicApiParameterInfo
                    {
                        Name = p.Name,
                        Type = p.Type.ToDisplayString()
                    }).ToList()
                };

                var httpMethodAttr = methodSymbol.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == "HttpMethodAttribute");
                if (httpMethodAttr != null && httpMethodAttr.ConstructorArguments.Length > 0)
                {
                    methodInfo.HttpMethod = httpMethodAttr.ConstructorArguments[0].Value?.ToString() ?? "GET";
                }

                var routeAttr = methodSymbol.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == "RouteAttribute");
                if (routeAttr != null && routeAttr.ConstructorArguments.Length > 0)
                {
                    methodInfo.RouteTemplate = routeAttr.ConstructorArguments[0].Value?.ToString() ?? "";
                }

                var authorizeAttr = methodSymbol.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == "AuthorizeAttribute");
                if (authorizeAttr != null)
                {
                    methodInfo.RequireAuthorization = true;
                    foreach (var namedArg in authorizeAttr.NamedArguments)
                    {
                        if (namedArg.Key == "Roles" && namedArg.Value.Value is string roles)
                        {
                            methodInfo.Roles = roles;
                        }
                        else if (namedArg.Key == "Policy" && namedArg.Value.Value is string policy)
                        {
                            methodInfo.Policy = policy;
                        }
                    }
                }

                methods.Add(methodInfo);
            }
        }
        }

        return new DynamicApiInfo(
            symbol.Name,
            symbol.ContainingNamespace.ToDisplayString(),
            route,
            controllerName,
            area,
            enableAuthorization,
            methods);
    }

    private static void GenerateApiCode(SourceProductionContext context, ImmutableArray<DynamicApiInfo?> apis)
    {
        var validApis = apis.Where(a => a is not null).Cast<DynamicApiInfo>().ToList();
        if (validApis.Count == 0) return;

        foreach (var api in validApis)
        {
            GenerateControllerCode(context, api);
        }
    }

    private static void GenerateControllerCode(SourceProductionContext context, DynamicApiInfo api)
    {
        var sb = new StringBuilder();
        var controllerName = string.IsNullOrEmpty(api.ControllerName)
            ? api.Name.Replace("I", "").Replace("Service", "")
            : api.ControllerName;
        var route = string.IsNullOrEmpty(api.Route)
            ? $"api/{controllerName.ToLowerInvariant()}"
            : api.Route;

        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.AspNetCore.Mvc;");
        sb.AppendLine("using Microsoft.Extensions.Logging;");
        sb.AppendLine("using CrestCreates.DynamicApi.Services;");
        sb.AppendLine();
        sb.AppendLine($"namespace {api.Namespace}.Controllers");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine($"    /// {controllerName} API Controller");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    [ApiController]");
        sb.AppendLine($"    [Route(\"{route}\")]");

        if (api.EnableAuthorization)
        {
            sb.AppendLine("    [Authorize]");
        }

        if (!string.IsNullOrEmpty(api.Area))
        {
            sb.AppendLine($"    [Area(\"{api.Area}\")]");
        }

        sb.AppendLine($"    public partial class {controllerName}Controller : ControllerBase");
        sb.AppendLine("    {");
        sb.AppendLine($"        private readonly {api.FullName} _service;");
        sb.AppendLine($"        private readonly ILogger<{controllerName}Controller> _logger;");
        sb.AppendLine();
        sb.AppendLine($"        public {controllerName}Controller(");
        sb.AppendLine($"            {api.FullName} service,");
        sb.AppendLine($"            ILogger<{controllerName}Controller> logger)");
        sb.AppendLine("        {");
        sb.AppendLine("            _service = service ?? throw new ArgumentNullException(nameof(service));");
        sb.AppendLine("            _logger = logger ?? throw new ArgumentNullException(nameof(logger));");
        sb.AppendLine("        }");
        sb.AppendLine();

        foreach (var method in api.Methods)
        {
            GenerateActionMethod(sb, method, controllerName, api.Namespace);
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource($"{controllerName}Controller.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static void GenerateActionMethod(StringBuilder sb, DynamicApiMethodInfo method, string controllerName, string namespaceName)
    {
        var httpMethod = string.IsNullOrEmpty(method.HttpMethod) ? "Get" : method.HttpMethod;
        var routeTemplate = string.IsNullOrEmpty(method.RouteTemplate) ? "" : method.RouteTemplate;

        sb.AppendLine("        /// <summary>");
        sb.AppendLine($"        /// {method.Name}");
        sb.AppendLine("        /// </summary>");

        if (method.RequireAuthorization)
        {
            sb.Append("        [Authorize");
            if (!string.IsNullOrEmpty(method.Roles))
            {
                sb.Append($"(Roles=\"{method.Roles}\")");
            }
            else if (!string.IsNullOrEmpty(method.Policy))
            {
                sb.Append($"(Policy=\"{method.Policy}\")");
            }
            sb.AppendLine("]");
        }

        sb.AppendLine($"        [Http{httpMethod}(\"{routeTemplate}\")]");
        sb.AppendLine("        [ProducesResponseType(200)]");
        sb.AppendLine("        [ProducesResponseType(400)]");
        sb.AppendLine("        [ProducesResponseType(500)]");

        sb.Append($"        public async Task<IActionResult> {method.Name}(");

        var parameters = method.Parameters.Select(p => $"{p.Type} {p.Name}").ToList();
        sb.Append(string.Join(", ", parameters));
        sb.AppendLine(")");
        sb.AppendLine("        {");
        sb.AppendLine("            try");
        sb.AppendLine("            {");

        if (method.ReturnType == "void" || method.ReturnType == "System.Threading.Tasks.Task")
        {
            sb.Append($"                await _service.{method.Name}(");
            sb.Append(string.Join(", ", method.Parameters.Select(p => p.Name)));
            sb.AppendLine(");");
            sb.AppendLine("                return Ok();");
        }
        else
        {
            sb.Append($"                var result = await _service.{method.Name}(");
            sb.Append(string.Join(", ", method.Parameters.Select(p => p.Name)));
            sb.AppendLine(");");
            sb.AppendLine("                return Ok(result);");
        }

        sb.AppendLine("            }");
        sb.AppendLine("            catch (Exception ex)");
        sb.AppendLine("            {");
        sb.AppendLine($"                _logger.LogError(ex, \"Error in {method.Name}\");");
        sb.AppendLine("                return StatusCode(500, ex.Message);");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private class DynamicApiInfo
    {
        public DynamicApiInfo(string name, string ns, string route, string controllerName, string area, bool enableAuthorization, List<DynamicApiMethodInfo> methods)
        {
            Name = name;
            Namespace = ns;
            Route = route;
            ControllerName = controllerName;
            Area = area;
            EnableAuthorization = enableAuthorization;
            Methods = methods;
        }

        public string Name { get; }
        public string Namespace { get; }
        public string Route { get; }
        public string ControllerName { get; }
        public string Area { get; }
        public bool EnableAuthorization { get; }
        public List<DynamicApiMethodInfo> Methods { get; }
        public string FullName => $"{Namespace}.{Name}";
    }

    private class DynamicApiMethodInfo
    {
        public string Name { get; set; } = "";
        public string ReturnType { get; set; } = "";
        public string HttpMethod { get; set; } = "";
        public string RouteTemplate { get; set; } = "";
        public bool RequireAuthorization { get; set; }
        public string? Roles { get; set; }
        public string? Policy { get; set; }
        public List<DynamicApiParameterInfo> Parameters { get; set; } = new List<DynamicApiParameterInfo>();
    }

    private class DynamicApiParameterInfo
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
    }
}