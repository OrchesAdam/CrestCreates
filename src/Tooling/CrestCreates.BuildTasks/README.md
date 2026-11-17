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

[CrestModule(typeof(CoreModule), Order = 0)]
public class MyModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services) { }
}
```

Only `CrestCreates.Domain.Shared.Attributes.CrestModuleAttribute` (or an alias resolving to it) is recognized. Roslyn binds attribute and dependency types against the evaluated `Compile` and resolved `ReferencePath` inputs, preserving reference `Aliases`, including references visible only through `extern alias`. Same-named attributes with another bound identity do not create modules. Module and `typeof` dependency identities share the same symbol-based C# type-name format; unresolved dependency types fail the build. `Order` and `AutoRegisterServices` consume compiler-bound typed constants, including valid implicit constant conversions. Direct task callers must supply their resolved references and alias metadata through `ReferencePaths`. Unresolved or ambiguous attribute types on class declarations fail with `CCMB001` and a source location when the attribute name could denote the formal module attribute (rightmost `CrestModule`/`CrestModuleAttribute`, or a using-alias chain resolved in its visible namespace scope, with global usings shared across files); failure does not overwrite an existing manifest. Attribute alias lookup includes the `Attribute` suffix except for verbatim identifiers. A using directive's target excludes aliases declared in its own scope, including global aliases for compilation-unit targets; aliases from enclosing scopes remain visible. Scanning runs before source generators, so other unbound attributes, including invalid peer-alias chains with no formal candidate, are left to CoreCompile, which rejects genuinely missing types and accepts generator-supplied ones. An empty module set writes `[]` manifests and a dependency-free empty initializer.

### 在应用程序中使用

```csharp
var builder = Host.CreateDefaultBuilder(args);

// 自动注册所有模块
builder = ModuleAutoInitializer.RegisterModules(builder);

var host = builder.Build();

// 执行 PreInit、Init、PostInit 和 AppInit
await host.InitializeModulesAsync();

host.Run();
```

`RegisterAllModules` 对同一 `IServiceCollection` 的同一模块只注册并执行一次 `OnConfigureServices`，另一个 collection 有独立配置状态。复制 service descriptors 时，配置历史、失败事实和健康检查注册标记迁移为新 collection 的独立快照；已经带入的注册不重放，后续新增配置彼此隔离。正在执行的配置回调尚未完成时不允许复制配置快照。初始化 Task 始终由各 provider 持有，不迁移 Task，也不在配置期执行用户 DI 工厂。配置失败会保留异常，重复调用不会重放已发生的副作用。`InitializeAllModulesAsync` 执行前三个阶段，`InitializeModulesAsync` 再执行 AppInit；各阶段在同一个根 provider 内按模块类型共享执行 Task，包括失败和取消结果。多个生成 initializer 重叠引用同一模块时也共享阶段状态；不同 provider 独立执行，即使来自同一个 collection。

诊断通过 DI 中的 `IModuleDiagnosticsStore` 读取，不再提供进程静态 `Store`。诊断注册描述符捕获当时的固定配置快照，每个 provider 再复制为自己的 Store；已经构建的 provider 即使延迟解析诊断，也不会读到 collection 后续新增的配置事实，再由各自 Store 记录初始化阶段；记录、计数和快照读取在同一把锁下完成。`RegisteredModules` 仅为不可变的生成描述符元数据，不持有 Host 运行状态。Source Generator 只生成注册扩展及应用本地入口，hint 以完整类型身份区分，聚合初始化代码仍由 BuildTasks 唯一生成。普通模块保留 `XExtensions` 公开类名及 `AddX` / `GetX` / `TryGetX` 方法，既有静态调用继续有效；只有当前编译中已有同名非泛型类型/命名空间占用名称，或嵌套 owner 的扩展类名称发生冲突时，冲突扩展类才追加完整 owner 哈希。普通模块与嵌套模块名称冲突时优先保留普通模块公开类名；不同 namespace 的同名模块不视为冲突。嵌套模块的方法名仍包含容器路径及稳定 owner 后缀，避免与普通含下划线的模块名称碰撞。生成的 HostBuilder 静态转发使用 `global::` 全限定目标，避免同名 global alias 重定向注册调用。

构建目前每次消费实际 evaluated Compile、解析后的引用、DefineConstants 和导入配置，不使用可能遗漏依赖的 Inputs/Outputs 跳过缓存。三个模块输出（本地清单、聚合清单、initializer）仅在内容改变时写入；无语义变化的构建保留原时间戳。该策略保证重算输入的正确性，不声明已经实现跳过扫描的构建缓存，也不新增 NativeAOT 验证级别。

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

1. **编译时扫描**: 构建时自动扫描 `[CrestModule]` 属性
2. **跨项目发现**: 通过引用项目的 `GetCrestCreatesModuleManifestPath` 目标输出收集清单（包含传递引用）；普通未导入此目标的项目不贡献清单。显式贡献的清单必须存在且有效，不能猜测 `obj/Debug` 路径。
3. **拓扑排序**: 依赖优先，可用节点按 Order、FullName（Ordinal）排序；未知或歧义依赖、循环和冲突身份使构建失败
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

- 检查 `[CrestModule]` 属性是否正确添加
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
