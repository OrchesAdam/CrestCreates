# 中央包管理器使用说明

## 概述
本项目已启用 .NET 中央包管理器（Central Package Management），通过 `Directory.Packages.props` 文件统一管理所有 NuGet 包的版本。

## 文件结构
```
├── Directory.Build.props    # 全局项目属性设置
├── Directory.Packages.props # 中央包版本管理
└── global.json             # SDK 版本设置
```

## 配置说明

### Directory.Build.props
启用中央包管理的关键属性：
```xml
<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
<CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
```

### Directory.Packages.props
包含所有 NuGet 包的版本定义，按类别组织：
- Microsoft 核心包
- Entity Framework Core
- ORM 提供程序（SqlSugar, FreeSql）
- 中介者模式（MediatR）
- 验证框架（FluentValidation）
- 映射工具（AutoMapper）
- 测试框架（xUnit, Moq, FluentAssertions）
- 代码分析工具
- Web 相关包
- 缓存相关包
- 日志框架（Serilog）

## 使用方式

### 在项目文件中添加包引用
项目文件中只需指定包名，不需要指定版本：
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.EntityFrameworkCore" />
  <PackageReference Include="MediatR" />
  <PackageReference Include="FluentValidation" />
</ItemGroup>
```

### 添加新包
1. 在 `Directory.Packages.props` 中添加包版本：
```xml
<PackageVersion Include="新包名" Version="版本号" />
```

2. 在需要的项目文件中添加包引用：
```xml
<PackageReference Include="新包名" />
```

### 更新包版本
只需在 `Directory.Packages.props` 中修改版本号，所有使用该包的项目将自动使用新版本。

## 优点
1. **版本一致性**：确保整个解决方案中同一包的版本保持一致
2. **简化管理**：只需在一个地方管理包版本
3. **减少冲突**：避免版本冲突问题
4. **提升性能**：减少包版本解析时间
5. **安全性**：通过 `CentralPackageTransitivePinningEnabled` 固定传递依赖版本

## 注意事项
1. 所有项目必须遵循中央包管理模式
2. 项目文件中不应再指定包版本号
3. 添加新包时需要先在 `Directory.Packages.props` 中定义版本
4. 特殊属性（如 `PrivateAssets`）仍可在项目文件中指定

## 包版本更新策略
- **Microsoft 核心包**：跟随 .NET 6.0 版本
- **第三方包**：使用稳定的最新版本
- **测试框架**：使用最新稳定版本
- **代码分析工具**：使用与 .NET SDK 兼容的版本

## 故障排除
如果遇到版本问题：
1. 检查 `Directory.Packages.props` 中是否定义了包版本
2. 确认项目文件中没有指定版本号
3. 清理并重新构建解决方案：`dotnet clean && dotnet restore && dotnet build`
