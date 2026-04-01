# ORM 配置方案 - 实现计划

## [ ] 任务 1: 创建 ORM 配置类
- **优先级**: P0
- **依赖**: None
- **描述**: 
  - 创建一个 `OrmOptions` 类，用于存储 ORM 提供者的配置
  - 添加必要的属性，如默认 ORM 提供者、连接字符串等
  - 实现配置验证逻辑
- **成功标准**:
  - `OrmOptions` 类能够正确存储和验证 ORM 配置
  - 提供默认值和验证逻辑
- **测试要求**:
  - `programmatic` TR-1.1: 验证 `OrmOptions` 类的基本功能
  - `programmatic` TR-1.2: 验证配置验证逻辑
- **备注**: 配置类应该支持从 appsettings.json 读取配置

## [ ] 任务 2: 实现 ORM 配置扩展方法
- **优先级**: P0
- **依赖**: 任务 1
- **描述**: 
  - 在 `UnitOfWorkExtensions` 类中添加 `ConfigureOrm` 扩展方法
  - 允许在 Startup 中配置 ORM 提供者
  - 注册 `IOptions<OrmOptions>` 到依赖注入容器
- **成功标准**:
  - 能够在 Startup 中通过 `services.ConfigureOrm()` 配置 ORM
  - 配置能够正确存储到依赖注入容器
- **测试要求**:
  - `programmatic` TR-2.1: 验证扩展方法的功能
  - `programmatic` TR-2.2: 验证配置是否正确注册
- **备注**: 扩展方法应该支持链式调用

## [ ] 任务 3: 修改 UnitOfWorkFactory 支持配置
- **优先级**: P0
- **依赖**: 任务 1, 任务 2
- **描述**: 
  - 修改 `UnitOfWorkFactory` 构造函数，接受 `IOptions<OrmOptions>`
  - 在创建工作单元时使用配置的 ORM 提供者
  - 保持向后兼容性
- **成功标准**:
  - `UnitOfWorkFactory` 能够使用配置的 ORM 提供者
  - 当没有配置时，使用默认值
- **测试要求**:
  - `programmatic` TR-3.1: 验证使用配置的 ORM 提供者
  - `programmatic` TR-3.2: 验证默认值的使用
- **备注**: 确保与现有的工厂注册机制兼容

## [ ] 任务 4: 修改 UnitOfWorkManager 支持配置
- **优先级**: P0
- **依赖**: 任务 3
- **描述**: 
  - 修改 `UnitOfWorkManager` 构造函数，接受 `IOptions<OrmOptions>`
  - 在创建工作单元时使用配置的默认 ORM 提供者
- **成功标准**:
  - `UnitOfWorkManager` 能够使用配置的默认 ORM 提供者
  - 当没有配置时，使用默认值
- **测试要求**:
  - `programmatic` TR-4.1: 验证使用配置的默认 ORM 提供者
  - `programmatic` TR-4.2: 验证默认值的使用
- **备注**: 确保与现有的 `Begin` 方法兼容

## [ ] 任务 5: 测试和验证
- **优先级**: P1
- **依赖**: 任务 1, 任务 2, 任务 3, 任务 4
- **描述**: 
  - 创建测试项目，验证 ORM 配置功能
  - 测试不同的配置场景
  - 确保向后兼容性
- **成功标准**:
  - 所有测试通过
  - 配置能够正确生效
  - 向后兼容性得到保证
- **测试要求**:
  - `programmatic` TR-5.1: 验证配置功能的测试
  - `programmatic` TR-5.2: 验证向后兼容性的测试
- **备注**: 测试应该覆盖不同的配置场景