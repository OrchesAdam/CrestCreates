using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CrestCreates.CodeGenerator.Authorization;

namespace CrestCreates.CodeGenerator.Examples
{
    /// <summary>
    /// 授权特性 AOP 植入示例
    /// 演示如何通过代码生成器自动为控制器添加授权特性
    /// </summary>
    public class AuthorizationInjectionExample
    {
        /// <summary>
        /// 示例 1: 基础用法 - 为单个实体生成带授权的控制器
        /// </summary>
        public static void Example1_BasicUsage()
        {
            Console.WriteLine("=== 示例 1: 基础用法 ===\n");

            var config = new AuthorizationConfig
            {
                ResourceName = "Products",
                GenerateCrudPermissions = true,
                RequireAuthorizationForAll = true
            };

            var generator = new AuthorizationAttributeGenerator(config);
            var code = generator.GenerateController("Product", "Products");

            Console.WriteLine("生成的控制器代码:");
            Console.WriteLine(code);
            Console.WriteLine("\n" + new string('=', 80) + "\n");
        }

        /// <summary>
        /// 示例 2: 自定义权限 - 为特定方法配置自定义权限
        /// </summary>
        public static void Example2_CustomPermissions()
        {
            Console.WriteLine("=== 示例 2: 自定义权限 ===\n");

            var config = new AuthorizationConfig
            {
                ResourceName = "Orders",
                GenerateCrudPermissions = true,
                CustomPermissions = new Dictionary<string, string>
                {
                    ["Approve"] = "Orders.Approve",
                    ["Cancel"] = "Orders.Cancel",
                    ["Export"] = "Reports.Export"
                }
            };

            var generator = new AuthorizationAttributeGenerator(config);
            
            // 为自定义方法生成授权特性
            var approveAttr = generator.GenerateAuthorizationAttribute("Approve", "POST");
            var cancelAttr = generator.GenerateAuthorizationAttribute("Cancel", "POST");
            var exportAttr = generator.GenerateAuthorizationAttribute("Export", "GET");

            Console.WriteLine($"Approve 方法的授权特性: {approveAttr}");
            Console.WriteLine($"Cancel 方法的授权特性: {cancelAttr}");
            Console.WriteLine($"Export 方法的授权特性: {exportAttr}");
            Console.WriteLine("\n" + new string('=', 80) + "\n");
        }

        /// <summary>
        /// 示例 3: 角色约束 - 添加角色要求
        /// </summary>
        public static void Example3_RoleRequirements()
        {
            Console.WriteLine("=== 示例 3: 角色约束 ===\n");

            var config = new AuthorizationConfig
            {
                ResourceName = "Users",
                GenerateCrudPermissions = true,
                DefaultRoles = new[] { "Admin", "UserManager" },
                RequireAll = false // OR 逻辑
            };

            var generator = new AuthorizationAttributeGenerator(config);
            var code = generator.GenerateController("User", "Users");

            Console.WriteLine("生成的控制器（带角色约束）:");
            Console.WriteLine(code);
            Console.WriteLine("\n" + new string('=', 80) + "\n");
        }

        /// <summary>
        /// 示例 4: 批量生成 - 为多个实体批量生成控制器
        /// </summary>
        public static void Example4_BatchGeneration()
        {
            Console.WriteLine("=== 示例 4: 批量生成 ===\n");

            var configs = new List<BatchAuthorizationGenerator.EntityAuthorizationConfig>
            {
                new()
                {
                    EntityName = "Product",
                    ResourceName = "Products",
                    RequiredRoles = new[] { "Admin", "ProductManager" }
                },
                new()
                {
                    EntityName = "Order",
                    ResourceName = "Orders",
                    MethodPermissions = new Dictionary<string, string>
                    {
                        ["Approve"] = "Orders.Approve",
                        ["Ship"] = "Orders.Ship"
                    },
                    RequiredRoles = new[] { "Admin", "OrderManager" }
                },
                new()
                {
                    EntityName = "Customer",
                    ResourceName = "Customers",
                    RequiredRoles = new[] { "Admin", "SalesManager" }
                }
            };

            var batchGenerator = new BatchAuthorizationGenerator();
            var results = batchGenerator.GenerateControllers(configs);

            foreach (var (fileName, code) in results)
            {
                Console.WriteLine($"文件: {fileName}");
                Console.WriteLine($"前 20 行:");
                var lines = code.Split('\n').Take(20);
                Console.WriteLine(string.Join('\n', lines));
                Console.WriteLine("...\n");
            }

            Console.WriteLine($"共生成 {results.Count} 个控制器文件");
            Console.WriteLine("\n" + new string('=', 80) + "\n");
        }

        /// <summary>
        /// 示例 5: Roslyn 语法树注入 - 向现有代码植入授权特性
        /// </summary>
        public static void Example5_RoslynInjection()
        {
            Console.WriteLine("=== 示例 5: Roslyn 语法树注入 ===\n");

            // 现有的控制器代码（没有授权特性）
            var existingCode = @"
using Microsoft.AspNetCore.Mvc;

namespace MyApp.Controllers
{
    [ApiController]
    [Route(""api/[controller]"")]
    public class ProductsController : ControllerBase
    {
        [HttpGet]
        public IActionResult GetAll()
        {
            return Ok();
        }

        [HttpPost]
        public IActionResult Create()
        {
            return Ok();
        }

        [HttpDelete(""{id}"")]
        public IActionResult Delete(int id)
        {
            return Ok();
        }
    }
}";

            var methodPermissions = new Dictionary<string, string>
            {
                ["GetAll"] = "Products.View",
                ["Create"] = "Products.Create",
                ["Delete"] = "Products.Delete"
            };

            var injector = new RoslynAuthorizationInjector();
            var modifiedCode = injector.InjectAuthorizationAttributes(existingCode, methodPermissions);

            Console.WriteLine("注入授权特性后的代码:");
            Console.WriteLine(modifiedCode);
            Console.WriteLine("\n" + new string('=', 80) + "\n");
        }

        /// <summary>
        /// 示例 6: 从配置文件生成 - 使用 JSON 配置文件
        /// </summary>
        public static void Example6_ConfigurationFile()
        {
            Console.WriteLine("=== 示例 6: 从配置文件生成 ===\n");

            // 创建配置文件示例
            var configJson = @"{
  ""entities"": [
    {
      ""entityName"": ""Product"",
      ""resourceName"": ""Products"",
      ""requiredRoles"": [""Admin"", ""ProductManager""],
      ""methodPermissions"": {
        ""ImportFromExcel"": ""Products.Import"",
        ""ExportToExcel"": ""Products.Export""
      }
    },
    {
      ""entityName"": ""Order"",
      ""resourceName"": ""Orders"",
      ""requiredRoles"": [""Admin"", ""OrderManager""],
      ""methodPermissions"": {
        ""Approve"": ""Orders.Approve"",
        ""Reject"": ""Orders.Reject"",
        ""Ship"": ""Orders.Ship""
      }
    }
  ]
}";

            Console.WriteLine("配置文件内容:");
            Console.WriteLine(configJson);
            Console.WriteLine();

            // 解析配置并生成
            var configData = JsonSerializer.Deserialize<ConfigFile>(configJson);
            
            var batchConfigs = configData.Entities.Select(e => new BatchAuthorizationGenerator.EntityAuthorizationConfig
            {
                EntityName = e.EntityName,
                ResourceName = e.ResourceName,
                MethodPermissions = e.MethodPermissions,
                RequiredRoles = e.RequiredRoles
            }).ToList();

            var batchGenerator = new BatchAuthorizationGenerator();
            var results = batchGenerator.GenerateControllers(batchConfigs);

            Console.WriteLine($"从配置文件生成了 {results.Count} 个控制器");
            Console.WriteLine("\n" + new string('=', 80) + "\n");
        }

        /// <summary>
        /// 运行所有示例
        /// </summary>
        public static void RunAllExamples()
        {
            Example1_BasicUsage();
            Example2_CustomPermissions();
            Example3_RoleRequirements();
            Example4_BatchGeneration();
            Example5_RoslynInjection();
            Example6_ConfigurationFile();

            Console.WriteLine("\n所有示例执行完成！");
            Console.WriteLine("\n使用说明：");
            Console.WriteLine("1. 通过配置对象定义授权规则");
            Console.WriteLine("2. 调用生成器生成带授权特性的控制器代码");
            Console.WriteLine("3. 可以批量生成多个控制器");
            Console.WriteLine("4. 支持向现有代码注入授权特性（Roslyn）");
            Console.WriteLine("5. 支持从配置文件读取并生成");
        }

        // 配置文件模型
        private class ConfigFile
        {
            public List<EntityConfig> Entities { get; set; }
        }

        private class EntityConfig
        {
            public string EntityName { get; set; }
            public string ResourceName { get; set; }
            public string[] RequiredRoles { get; set; }
            public Dictionary<string, string> MethodPermissions { get; set; }
        }
    }

    /// <summary>
    /// 集成到构建流程的示例
    /// </summary>
    public class BuildIntegrationExample
    {
        /// <summary>
        /// MSBuild Task 集成示例
        /// </summary>
        public static void GenerateDuringBuild(string projectPath, string outputPath)
        {
            Console.WriteLine("=== 构建时自动生成授权控制器 ===\n");

            // 1. 读取项目中的实体定义
            var entityFiles = Directory.GetFiles(
                Path.Combine(projectPath, "Entities"),
                "*.cs",
                SearchOption.AllDirectories);

            Console.WriteLine($"发现 {entityFiles.Length} 个实体文件");

            // 2. 为每个实体生成控制器
            var configs = new List<BatchAuthorizationGenerator.EntityAuthorizationConfig>();

            foreach (var file in entityFiles)
            {
                var entityName = Path.GetFileNameWithoutExtension(file);
                
                configs.Add(new BatchAuthorizationGenerator.EntityAuthorizationConfig
                {
                    EntityName = entityName,
                    ResourceName = $"{entityName}s",
                    RequiredRoles = new[] { "Admin" }
                });
            }

            // 3. 批量生成
            var batchGenerator = new BatchAuthorizationGenerator();
            var results = batchGenerator.GenerateControllers(configs);

            // 4. 写入输出目录
            var controllersPath = Path.Combine(outputPath, "Controllers", "Generated");
            Directory.CreateDirectory(controllersPath);

            foreach (var (fileName, code) in results)
            {
                var filePath = Path.Combine(controllersPath, fileName);
                File.WriteAllText(filePath, code);
                Console.WriteLine($"生成: {filePath}");
            }

            Console.WriteLine($"\n共生成 {results.Count} 个控制器文件");
        }
    }
}
