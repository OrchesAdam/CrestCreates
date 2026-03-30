using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CrestCreates.CodeGenerator.Authorization
{
    /// <summary>
    /// 授权特性配置
    /// </summary>
    public class AuthorizationConfig
    {
        /// <summary>
        /// 资源名称（如 "Products"）
        /// </summary>
        public string ResourceName { get; set; }

        /// <summary>
        /// 是否生成 CRUD 权限
        /// </summary>
        public bool GenerateCrudPermissions { get; set; } = true;

        /// <summary>
        /// 自定义权限映射
        /// Key: 方法名，Value: 权限名称
        /// </summary>
        public Dictionary<string, string> CustomPermissions { get; set; } = new();

        /// <summary>
        /// 是否对所有方法应用授权
        /// </summary>
        public bool RequireAuthorizationForAll { get; set; } = true;

        /// <summary>
        /// 默认角色要求
        /// </summary>
        public string[] DefaultRoles { get; set; }

        /// <summary>
        /// 是否要求所有权限（AND 逻辑）
        /// </summary>
        public bool RequireAll { get; set; } = false;
    }

    /// <summary>
    /// 授权特性代码生成器
    /// 通过 AOP 方式将授权特性植入到生成的控制器中
    /// </summary>
    public class AuthorizationAttributeGenerator
    {
        private readonly AuthorizationConfig _config;

        public AuthorizationAttributeGenerator(AuthorizationConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// 为控制器方法生成授权特性代码
        /// </summary>
        public string GenerateAuthorizationAttribute(string methodName, string httpMethod)
        {
            var attributes = new List<string>();

            // 1. 检查是否有自定义权限
            if (_config.CustomPermissions.TryGetValue(methodName, out var customPermission))
            {
                attributes.Add(AuthorizationHelper.GeneratePermissionAttribute(customPermission, _config.RequireAll));
            }
            // 2. 根据 HTTP 方法生成 CRUD 权限
            else if (_config.GenerateCrudPermissions)
            {
                var permission = AuthorizationHelper.MapHttpMethodToPermission(httpMethod);
                if (!string.IsNullOrEmpty(permission))
                {
                    attributes.Add(AuthorizationHelper.GeneratePermissionAttribute($"{_config.ResourceName}.{permission}", _config.RequireAll));
                }
            }

            // 3. 添加角色要求
            if (_config.DefaultRoles != null && _config.DefaultRoles.Length > 0)
            {
                attributes.Add(AuthorizationHelper.GenerateRoleAttribute(_config.DefaultRoles));
            }

            return string.Join("\r\n        ", attributes);
        }

        /// <summary>
        /// 生成权限特性代码
        /// </summary>
        private string GeneratePermissionAttribute(string permission)
        {
            var requireAllParam = _config.RequireAll ? ", RequireAll = true" : "";
            return $"[AuthorizePermission(\"{permission}\"{requireAllParam})]";
        }

        /// <summary>
        /// 生成角色特性代码
        /// </summary>
        private string GenerateRoleAttribute(string[] roles)
        {
            var roleList = string.Join("\", \"", roles);
            return $"[AuthorizeRoles(\"{roleList}\")]";
        }

        /// <summary>
        /// 映射 HTTP 方法到 CRUD 权限
        /// </summary>
        private string MapHttpMethodToPermission(string httpMethod)
        {
            return httpMethod?.ToUpperInvariant() switch
            {
                "POST" => "Create",
                "PUT" => "Update",
                "PATCH" => "Update",
                "DELETE" => "Delete",
                "GET" => "View",
                _ => null
            };
        }

        /// <summary>
        /// 生成完整的控制器代码（包含授权特性）
        /// </summary>
        public string GenerateController(string entityName, string entityNamePlural)
        {
            var sb = new StringBuilder();

            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using Microsoft.AspNetCore.Mvc;");
            sb.AppendLine("using CrestCreates.Infrastructure.Authorization;");
            sb.AppendLine();
            sb.AppendLine($"namespace YourNamespace.Controllers");
            sb.AppendLine("{");
            sb.AppendLine($"    [ApiController]");
            sb.AppendLine($"    [Route(\"api/[controller]\")]");
            
            // 可选：控制器级别的授权
            if (_config.RequireAuthorizationForAll)
            {
                sb.AppendLine($"    [Authorize]");
            }

            sb.AppendLine($"    public class {entityNamePlural}Controller : ControllerBase");
            sb.AppendLine("    {");
            sb.AppendLine($"        private readonly I{entityName}Service _{ToCamelCase(entityName)}Service;");
            sb.AppendLine();
            sb.AppendLine($"        public {entityNamePlural}Controller(I{entityName}Service {ToCamelCase(entityName)}Service)");
            sb.AppendLine("        {");
            sb.AppendLine($"            _{ToCamelCase(entityName)}Service = {ToCamelCase(entityName)}Service;");
            sb.AppendLine("        }");
            sb.AppendLine();

            // GET All
            sb.AppendLine("        [HttpGet]");
            sb.AppendLine($"        {GenerateAuthorizationAttribute("GetAll", "GET")}");
            sb.AppendLine($"        public async Task<IActionResult> GetAll()");
            sb.AppendLine("        {");
            sb.AppendLine($"            var items = await _{ToCamelCase(entityName)}Service.GetAllAsync();");
            sb.AppendLine("            return Ok(items);");
            sb.AppendLine("        }");
            sb.AppendLine();

            // GET by ID
            sb.AppendLine("        [HttpGet(\"{id}\")]");
            sb.AppendLine($"        {GenerateAuthorizationAttribute("GetById", "GET")}");
            sb.AppendLine($"        public async Task<IActionResult> GetById(Guid id)");
            sb.AppendLine("        {");
            sb.AppendLine($"            var item = await _{ToCamelCase(entityName)}Service.GetByIdAsync(id);");
            sb.AppendLine("            if (item == null) return NotFound();");
            sb.AppendLine("            return Ok(item);");
            sb.AppendLine("        }");
            sb.AppendLine();

            // POST
            sb.AppendLine("        [HttpPost]");
            sb.AppendLine($"        {GenerateAuthorizationAttribute("Create", "POST")}");
            sb.AppendLine($"        public async Task<IActionResult> Create([FromBody] Create{entityName}Dto dto)");
            sb.AppendLine("        {");
            sb.AppendLine($"            var item = await _{ToCamelCase(entityName)}Service.CreateAsync(dto);");
            sb.AppendLine("            return CreatedAtAction(nameof(GetById), new { id = item.Id }, item);");
            sb.AppendLine("        }");
            sb.AppendLine();

            // PUT
            sb.AppendLine("        [HttpPut(\"{id}\")]");
            sb.AppendLine($"        {GenerateAuthorizationAttribute("Update", "PUT")}");
            sb.AppendLine($"        public async Task<IActionResult> Update(Guid id, [FromBody] Update{entityName}Dto dto)");
            sb.AppendLine("        {");
            sb.AppendLine($"            var item = await _{ToCamelCase(entityName)}Service.UpdateAsync(id, dto);");
            sb.AppendLine("            if (item == null) return NotFound();");
            sb.AppendLine("            return Ok(item);");
            sb.AppendLine("        }");
            sb.AppendLine();

            // DELETE
            sb.AppendLine("        [HttpDelete(\"{id}\")]");
            sb.AppendLine($"        {GenerateAuthorizationAttribute("Delete", "DELETE")}");
            sb.AppendLine($"        public async Task<IActionResult> Delete(Guid id)");
            sb.AppendLine("        {");
            sb.AppendLine($"            var result = await _{ToCamelCase(entityName)}Service.DeleteAsync(id);");
            sb.AppendLine("            if (!result) return NotFound();");
            sb.AppendLine("            return NoContent();");
            sb.AppendLine("        }");

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private string ToCamelCase(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return char.ToLowerInvariant(text[0]) + text.Substring(1);
        }
    }

    /// <summary>
    /// Roslyn 语法树方式植入授权特性（更高级）
    /// </summary>
    public class RoslynAuthorizationInjector
    {
        /// <summary>
        /// 向现有类添加授权特性
        /// </summary>
        public string InjectAuthorizationAttributes(
            string sourceCode,
            Dictionary<string, string> methodPermissions)
        {
            var tree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = tree.GetRoot();

            var rewriter = new AuthorizationAttributeRewriter(methodPermissions);
            var newRoot = rewriter.Visit(root);

            return newRoot.ToFullString();
        }

        private class AuthorizationAttributeRewriter : CSharpSyntaxRewriter
        {
            private readonly Dictionary<string, string> _methodPermissions;

            public AuthorizationAttributeRewriter(Dictionary<string, string> methodPermissions)
            {
                _methodPermissions = methodPermissions;
            }

            public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                var methodName = node.Identifier.Text;

                // 检查是否需要添加授权特性
                if (_methodPermissions.TryGetValue(methodName, out var permission))
                {
                    // 检查是否已有授权特性
                    var hasAuthAttribute = node.AttributeLists
                        .SelectMany(al => al.Attributes)
                        .Any(a => a.Name.ToString().Contains("Authorize"));

                    if (!hasAuthAttribute)
                    {
                        // 创建授权特性
                        var attributeList = SyntaxFactory.AttributeList(
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.Attribute(
                                    SyntaxFactory.IdentifierName("AuthorizePermission"),
                                    SyntaxFactory.AttributeArgumentList(
                                        SyntaxFactory.SingletonSeparatedList(
                                            SyntaxFactory.AttributeArgument(
                                                SyntaxFactory.LiteralExpression(
                                                    SyntaxKind.StringLiteralExpression,
                                                    SyntaxFactory.Literal(permission))))))));

                        // 添加特性到方法
                        node = node.AddAttributeLists(attributeList);
                    }
                }

                return base.VisitMethodDeclaration(node);
            }
        }
    }

    /// <summary>
    /// 基于配置的批量生成器
    /// </summary>
    public class BatchAuthorizationGenerator
    {
        public class EntityAuthorizationConfig
        {
            public string EntityName { get; set; }
            public string ResourceName { get; set; }
            public Dictionary<string, string> MethodPermissions { get; set; } = new();
            public string[] RequiredRoles { get; set; }
        }

        /// <summary>
        /// 从配置批量生成控制器
        /// </summary>
        public Dictionary<string, string> GenerateControllers(
            List<EntityAuthorizationConfig> configs)
        {
            var result = new Dictionary<string, string>();

            foreach (var config in configs)
            {
                var authConfig = new AuthorizationConfig
                {
                    ResourceName = config.ResourceName ?? config.EntityName,
                    GenerateCrudPermissions = true,
                    CustomPermissions = config.MethodPermissions,
                    DefaultRoles = config.RequiredRoles
                };

                var generator = new AuthorizationAttributeGenerator(authConfig);
                var code = generator.GenerateController(
                    config.EntityName,
                    $"{config.EntityName}s");

                result[$"{config.EntityName}sController.cs"] = code;
            }

            return result;
        }
    }
}
