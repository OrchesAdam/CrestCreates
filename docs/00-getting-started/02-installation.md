# 安装指南

本指南详细介绍如何安装和配置 CrestCreates 框架。

## 环境要求

### 必需组件

| 组件 | 版本 | 说明 |
|------|------|------|
| .NET SDK | 10.0+ | 开发框架 |
| Visual Studio / Rider / VS Code | 最新版 | 开发 IDE |
| SQL Server / PostgreSQL / MySQL | 任意 | 数据库（可选）|
| Redis | 6.0+ | 缓存（可选）|
| RabbitMQ | 3.8+ | 消息队列（可选）|

## 安装步骤

### 1. 安装 .NET SDK

下载并安装 [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。

验证安装：

```bash
dotnet --version
```

### 2. 克隆代码库

```bash
git clone https://github.com/your-org/CrestCreates.git
cd CrestCreates
```

### 3. 恢复 NuGet 包

```bash
dotnet restore
```

### 4. 构建解决方案

```bash
dotnet build
```

### 5. 运行测试

```bash
dotnet test
```

## 配置数据库

### 使用 EF Core

1. **安装 EF Core 工具**

```bash
dotnet tool install --global dotnet-ef
```

2. **配置连接字符串**

在 `appsettings.json` 中添加：

```json
{
  "ConnectionStrings": {
    "Default": "Server=localhost;Database=MyApp;Trusted_Connection=True;"
  }
}
```

3. **创建数据库迁移**

```bash
dotnet ef migrations add InitialCreate
dotnet ef database update
```

### 使用 FreeSql

1. **配置 FreeSql**

```csharp
services.AddFreeSql(options =>
{
    options.UseConnectionString(FreeSql.DataType.SqlServer, 
        "Server=localhost;Database=MyApp;Trusted_Connection=True;");
});
```

### 使用 SqlSugar

1. **配置 SqlSugar**

```csharp
services.AddSqlSugar(options =>
{
    options.ConnectionString = "Server=localhost;Database=MyApp;Trusted_Connection=True;";
    options.DbType = DbType.SqlServer;
});
```

## 配置缓存

### 内存缓存

```csharp
services.AddMemoryCache();
```

### Redis 缓存

```csharp
services.AddRedisCache(options =>
{
    options.Configuration = "localhost:6379";
});
```

## 配置日志

### Serilog 配置

在 `Program.cs` 中：

```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File("logs/myapp-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();
```

## 配置多租户

```csharp
services.AddMultiTenancy(options =>
{
    options.TenantResolvers.Add<HeaderTenantResolver>();
    options.TenantResolvers.Add<SubdomainTenantResolver>();
});
```

## 配置事件总线

### 本地事件总线

```csharp
services.AddLocalEventBus();
```

### RabbitMQ 事件总线

```csharp
services.AddRabbitMqEventBus(options =>
{
    options.HostName = "localhost";
    options.UserName = "guest";
    options.Password = "guest";
});
```

## 验证安装

1. **启动应用**

```bash
dotnet run
```

2. **访问健康检查端点**

```bash
curl http://localhost:5000/health
```

如果返回 `Healthy`，说明安装成功。

## 故障排除

### 问题 1: 构建失败

**症状**: `dotnet build` 失败

**解决方案**:
- 确保已安装 .NET 10.0 SDK
- 运行 `dotnet restore` 恢复依赖
- 检查项目文件中的包引用

### 问题 2: 数据库连接失败

**症状**: 无法连接到数据库

**解决方案**:
- 检查连接字符串是否正确
- 确保数据库服务器正在运行
- 检查防火墙设置

### 问题 3: 端口冲突

**症状**: 端口已被占用

**解决方案**:
- 修改 `launchSettings.json` 中的端口配置
- 或者使用命令行参数指定端口：`dotnet run --urls "http://localhost:5001"`

## 下一步

- [快速开始](01-quickstart.md) - 快速上手 CrestCreates
- [架构概览](../01-architecture/00-overview.md) - 深入了解架构设计
