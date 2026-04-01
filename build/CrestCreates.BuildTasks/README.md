# CrestCreates.BuildTasks NuGet 包

## 概述

这是 CrestCreates 框架的 MSBuild 任务包，提供编译时模块发现和自动注册功能。

## 安装

### 通过 NuGet 包管理器

```bash
dotnet add package CrestCreates.BuildTasks
```

### 通过 PackageReference

```xml
<PackageReference Include="CrestCreates.BuildTasks" Version="1.0.0">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

## 使用

安装包后，系统会自动在构建时扫描模块。无需额外配置。

### 定义模块

```csharp
using CrestCreates.Modularity;
using CrestCreates.Domain.Shared.Attributes;

namespace MyApp.Modules;

[Module(typeof(CoreModule), Order = 0)]
public class MyModule : IModule
{
    public string Name => "MyModule";
    
    public void OnPreInitialize() { }
    public void OnInitialize() { }
    public void OnPostInitialize() { }
    public void OnConfigureServices(IServiceCollection services) { }
    public void OnApplicationInitialization(IHost host) { }
}
```

### 在应用程序中使用

```csharp
var builder = Host.CreateDefaultBuilder(args);

// 自动注册所有模块
builder = ModuleAutoInitializer.RegisterModules(builder);

var host = builder.Build();

// 自动初始化所有模块
ModuleAutoInitializer.InitializeAllModules(host.Services);

host.Run();
```

## 配置

### 禁用模块生成

```xml
<PropertyGroup>
  <EnableModuleManifestGeneration>false</EnableModuleManifestGeneration>
</PropertyGroup>
```

### 自定义输出路径

```xml
<PropertyGroup>
  <ModuleManifestOutputPath>$(MSBuildProjectDirectory)\Custom\ModuleManifest.json</ModuleManifestOutputPath>
  <AggregatedModuleCodePath>$(MSBuildProjectDirectory)\Generated\Modules.g.cs</AggregatedModuleCodePath>
</PropertyGroup>
```

## 工作原理

1. **编译时扫描**: 构建时自动扫描 `[Module]` 属性
2. **跨项目发现**: 自动发现引用项目中的模块
3. **拓扑排序**: 根据依赖关系和 Order 值排序
4. **代码生成**: 生成类型安全的注册代码
5. **自动注册**: 生成的代码自动注册到 DI 容器

## 文件结构

安装包后，以下文件会被添加到项目中：

```
packages/
└── crestcreates.buildtasks/
    ├── build/
    │   ├── CrestCreates.BuildTasks.props      # 自动导入的 props
    │   ├── CrestCreates.BuildTasks.targets    # 构建目标
    │   └── net10.0/
    │       └── CrestCreates.BuildTasks.dll    # 构建任务 DLL
    └── docs/
        └── ModuleDiscovery.md                  # 详细文档
```

## 注意事项

1. **构建顺序**: 引用项目会先构建，确保模块清单已生成
2. **清理构建**: 运行 `dotnet clean` 会清理生成的清单文件
3. **版本兼容性**: 确保所有项目使用相同版本的 BuildTasks 包

## 故障排除

### 模块未被发现

- 检查 `[Module]` 属性是否正确添加
- 确认类实现了 `IModule` 接口
- 验证类是 `public` 的

### 跨项目模块未被发现

- 确保引用项目也安装了 BuildTasks 包
- 检查引用项目的模块清单是否生成
- 验证项目引用路径正确

### 编译错误

- 确保引用了 `CrestCreates.Modularity` 包
- 检查 `IModule` 接口的所有方法是否实现

## 相关包

- `CrestCreates.Modularity` - 模块系统核心接口
- `CrestCreates.Domain.Shared` - 共享属性和类型

## 许可证

MIT License
