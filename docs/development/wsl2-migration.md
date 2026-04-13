# CrestCreates WSL2 迁移指南

## 目标

将 CrestCreates 的日常开发主链统一迁移到 WSL2 内，避免继续混用 Windows 本机工具链和 WSL2 工具链。

本迁移只保留一条开发主路径：

- 代码编辑：Windows IDE 连接 WSL2 工作区
- SDK 与 CLI：安装在 WSL2 发行版内
- 构建与测试：在 WSL2 内执行
- 基础设施：优先通过 Docker Desktop 的 WSL2 集成提供

## 当前仓库的实际依赖

结合当前仓库内容，开发主链至少需要以下能力：

- `.NET SDK 10.0.100`
- `git`
- `PowerShell 7`
- `Docker Desktop + WSL2 integration`
- 容器化基础设施服务
  - `SQL Server`
  - `Redis`
  - `RabbitMQ`

说明：

- `global.json` 固定了 `10.0.100`
- `CrestCreates.AppHost/Program.cs` 当前会拉起 `SQL Server`、`Redis`、`RabbitMQ`
- 仓库里只有少量 `PowerShell` 脚本，但为了统一体验，建议仍在 WSL2 内安装 `pwsh`

## 迁移原则

### 1. 统一工具归属

不要继续混用：

- Windows 上的 `dotnet`
- WSL2 里的源码路径
- Docker Desktop 的一部分能力
- 手工启动的 Windows 本地服务

迁移完成后，默认以 WSL2 内的命令行为唯一执行入口。

### 2. 统一代码位置

当前仓库位于 `/mnt/e/...`，可以运行，但不建议作为长期主链。

原因：

- 跨文件系统 IO 更慢
- `dotnet restore/build/test` 的大量小文件访问在 `/mnt/*` 下性能更差
- 文件监听、增量构建、容器挂载体验通常不如 WSL2 原生文件系统

建议迁移到类似以下位置：

```bash
~/workspace/CrestCreates
```

## 推荐最终形态

### Windows 侧

- 安装 `WSL2`
- 安装 `Docker Desktop`
- 使用 `VS Code` Remote WSL、`Rider` WSL 工具链，或支持 WSL 的 Visual Studio

### WSL2 Ubuntu 侧

- 安装 `git`
- 安装 `.NET SDK 10`
- 安装 `PowerShell 7`
- 通过 Docker Desktop WSL 集成获得 `docker`

## 执行步骤

### 1. 在 Windows 中确认 WSL2 和 Docker Desktop

需要确认：

- WSL 默认版本是 2
- Docker Desktop 已安装
- Docker Desktop 已对当前 Ubuntu 发行版开启 WSL Integration

如果未开启，打开 Docker Desktop：

- `Settings`
- `Resources`
- `WSL Integration`
- 打开当前 Ubuntu 发行版开关

### 2. 在 WSL2 中安装基础工具

在 Ubuntu 22.04 中执行：

```bash
bash scripts/setup-wsl2-dev.sh
```

这个脚本会安装：

- `.NET SDK 10`
- `PowerShell 7`
- 常用基础包

它不会安装独立 Linux Docker Engine，避免形成第二套 Docker 主链。

### 3. 将仓库迁移到 WSL2 原生目录

推荐在 WSL2 内执行：

```bash
mkdir -p ~/workspace
cp -a /mnt/e/WorkSpace/Personel/CrestCreates ~/workspace/CrestCreates
cd ~/workspace/CrestCreates
```

如果后续确定旧路径不再使用，再由你手动在图形界面中处理旧目录。

### 4. 在 WSL2 内验证工具链

```bash
dotnet --version
pwsh --version
git --version
docker --version
```

预期：

- `dotnet` 输出 `10.0.100` 或兼容版本
- `pwsh` 可用
- `docker` 可用且不再提示未开启 WSL integration

### 5. 在 WSL2 内恢复、构建、测试

```bash
dotnet restore
dotnet build
dotnet test
```

### 6. 验证基础设施主链

如果使用 Aspire AppHost 或容器依赖，继续验证：

```bash
dotnet run --project CrestCreates.AppHost
```

确认容器能正常拉起：

- SQL Server
- Redis
- RabbitMQ

## IDE 建议

### VS Code

- 安装 `Remote - WSL`
- 从 WSL2 中执行 `code ~/workspace/CrestCreates`

### Rider

- 直接打开 WSL2 内路径
- SDK 选择 WSL2 内的 `dotnet`

### Visual Studio

如果继续使用 Visual Studio，需要确认它针对 WSL 工作区的支持是否满足你的日常开发需求。
如果你的主要工作是框架开发和 CLI 驱动构建，优先建议使用 `VS Code` 或 `Rider` 连接 WSL2。

## 常见问题

### 1. `dotnet` 已安装但版本不对

检查：

```bash
dotnet --list-sdks
cat global.json
```

当前仓库要求：

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestMinor"
  }
}
```

### 2. `docker` 在 WSL2 中不可用

如果出现类似提示：

- `The command 'docker' could not be found in this WSL 2 distro`

说明不是仓库问题，而是 Docker Desktop 还没有对当前 Ubuntu 发行版开启 WSL 集成。

### 3. 仓库仍放在 `/mnt/e/...`

这不会立刻导致不可用，但会持续带来：

- 构建慢
- 恢复慢
- 文件监听不稳定

所以迁移到 WSL2 原生目录不是优化项，而是建议纳入正式主链。

## 建议的完成标准

满足以下条件，才算迁移完成：

1. 在 WSL2 内直接执行 `dotnet --version` 成功
2. 在 WSL2 内直接执行 `docker --version` 成功
3. 仓库已移动到 WSL2 原生目录
4. 在 WSL2 内完成 `dotnet restore`
5. 在 WSL2 内完成 `dotnet build`
6. 在 WSL2 内完成 `dotnet test`
7. 需要时可从 WSL2 内启动 AppHost 和容器依赖
