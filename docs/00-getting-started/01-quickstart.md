# 快速开始

本指南将帮助您在几分钟内快速上手 CrestCreates 框架。

## 前置条件

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或更高版本
- 一个支持的 IDE（Visual Studio 2022、Rider 或 VS Code）

## 步骤 1: 克隆代码库

```bash
git clone https://github.com/your-org/CrestCreates.git
cd CrestCreates
```

## 步骤 2: 恢复依赖

```bash
dotnet restore
```

## 步骤 3: 构建项目

```bash
dotnet build
```

## 步骤 4: 运行示例项目

```bash
cd samples/Ecommerce/Ecommerce.Web
dotnet run
```

访问 `http://localhost:5000` 查看运行中的示例应用。

## 创建您的第一个模块

### 1. 创建模块项目

```bash
dotnet new classlib -n MyApp.Module
cd MyApp.Module
```

### 2. 添加依赖

```bash
dotnet add package CrestCreates.Domain
dotnet add package CrestCreates.Application
dotnet add package CrestCreates.Infrastructure
```

### 3. 定义实体

```csharp
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Shared.Attributes;

namespace MyApp.Module.Entities
{
    [Entity("products")]
    public class Product : AuditedEntity<Guid>
    {
        public string Name { get; set; }
        public decimal Price { get; set; }
        public string Description { get; set; }
    }
}
```

### 4. 创建应用服务

```csharp
using CrestCreates.Application.Services;
using CrestCreates.Domain.Shared.Attributes;

namespace MyApp.Module.Services
{
    [Service(typeof(IProductService))]
    public class ProductService : ApplicationService, IProductService
    {
        private readonly IRepository<Product, Guid> _productRepository;

        public ProductService(IRepository<Product, Guid> productRepository)
        {
            _productRepository = productRepository;
        }

        public async Task<ProductDto> GetAsync(Guid id)
        {
            var product = await _productRepository.GetAsync(id);
            return ObjectMapper.Map<Product, ProductDto>(product);
        }
    }
}
```

### 5. 配置模块

```csharp
using CrestCreates.Infrastructure.Modularity;

namespace MyApp.Module
{
    [Module(typeof(CrestCreatesDomainModule))]
    [Module(typeof(CrestCreatesApplicationModule))]
    public class MyAppModule : ModuleBase
    {
        public override void ConfigureServices(IServiceCollection services)
        {
            // 注册服务
            services.AddTransient<IProductService, ProductService>();
        }
    }
}
```

## 下一步

- [安装指南](02-installation.md) - 详细的安装和配置说明
- [架构概览](../01-architecture/00-overview.md) - 深入了解架构设计
- [核心概念](../02-core-concepts/00-entities.md) - 学习核心概念
