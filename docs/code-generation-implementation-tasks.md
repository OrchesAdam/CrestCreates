# CrestCreates 代码生成优化实施任务

## 概述

本文档基于《CrestCreates 代码生成方案优化建议》，提供详细的实施任务计划。

---

## 任务总览

| 阶段 | 任务数 | 优先级 | 预计工作量 |
|------|--------|--------|------------|
| 阶段一：统一特性和生成器 | 3 | 高 | 3-4 天 |
| 阶段二：基类模式改造 | 5 | 高 | 4-5 天 |
| 阶段三：查询层简化 | 4 | 中 | 2-3 天 |
| 阶段四：服务层优化 | 4 | 中 | 2-3 天 |
| 阶段五：迁移和文档 | 3 | 中 | 2-3 天 |

---

## 阶段一：统一特性和生成器

### 任务 1.1：创建统一特性

**优先级**: 高
**预计时间**: 4-6 小时

**描述**: 创建 `GenerateEntityAttribute` 统一特性，整合现有的多个特性。

**具体步骤**:
1. 在 `CrestCreates.Domain.Shared/Attributes` 目录下创建新文件
2. 定义 `GenerateEntityAttribute` 类，包含以下属性：
   - `GenerateRepository` (bool, 默认 true)
   - `GenerateCrudService` (bool, 默认 true)
   - `GenerateQueryExtensions` (bool, 默认 true)
   - `GenerateController` (bool, 默认 false)
   - `ControllerRoute` (string?)
   - `OrmProvider` (OrmProvider, 默认 EfCore)
   - `ExcludeProperties` (string[]?)
   - `GenerateAsBaseClass` (bool, 默认 true)
3. 保留旧特性的向后兼容（标记为 Obsolete）
4. 添加 XML 文档注释

**验收标准**:
- 特性编译成功
- 可以应用到实体类
- 旧特性仍可使用但有警告

---

### 任务 1.2：创建统一的 EntityInfo 模型

**优先级**: 高
**预计时间**: 2-3 小时

**描述**: 创建一个用于在生成器之间传递实体信息的模型。

**具体步骤**:
1. 在 `CrestCreates.CodeGenerator` 项目中创建 `Models` 文件夹
2. 创建 `EntityInfo` 类，包含：
   - 实体名称
   - 命名空间
   - ID 类型
   - 所有属性列表
   - 特性配置
   - 基类信息

**验收标准**:
- 模型可以正确存储实体信息
- 包含生成器所需的所有数据

---

### 任务 1.3：创建统一源生成器

**优先级**: 高
**预计时间**: 1.5-2 天

**描述**: 创建 `UnifiedSourceGenerator`，统一管理所有生成逻辑。

**具体步骤**:
1. 创建 `UnifiedSourceGenerator.cs`
2. 实现 `IIncrementalGenerator` 接口
3. 从现有生成器中提取核心逻辑：
   - Repository 生成
   - DTO 生成
   - CRUD Service 生成
   - Query 相关生成
4. 根据特性配置选择性生成
5. 保留现有生成器作为过渡（标记为 Obsolete）

**验收标准**:
- 可以正确识别带特性的实体
- 根据配置选择性生成
- 与现有代码兼容

---

## 阶段二：基类模式改造

### 任务 2.1：改造 Repository 生成器

**优先级**: 高
**预计时间**: 1 天

**描述**: 修改 Repository 生成器，生成基类而非完整实现。

**具体步骤**:
1. 修改 `RepositorySourceGenerator`
2. 添加 `GenerateAsBaseClass` 配置支持
3. 生成 `{EntityName}RepositoryBase` 抽象类
4. 基类方法标记为 `virtual`
5. 接口改为 `partial`
6. 更新示例代码

**验收标准**:
- 生成的基类可以被继承
- 可以重写任何方法
- 向后兼容（配置控制）

---

### 任务 2.2：创建 Repository 模板

**优先级**: 中
**预计时间**: 3-4 小时

**描述**: 创建 Repository 的手写实现模板，方便开发者使用。

**具体步骤**:
1. 创建模板文件或代码片段
2. 模板包含：
   - 继承基类
   - 构造函数
   - 注释说明
3. 提供 VS 代码片段或 CLI 工具

**验收标准**:
- 模板可以直接使用
- 包含必要的注释和示例

---

### 任务 2.3：改造 DTO 生成器

**优先级**: 中
**预计时间**: 4-6 小时

**描述**: 改进 DTO 生成，支持更多配置和扩展点。

**具体步骤**:
1. 支持 `ExcludeProperties` 配置
2. DTO 类改为 `partial`
3. 生成 `{EntityName}Dto.g.cs` 等文件
4. 添加扩展点（partial 方法）

**验收标准**:
- 可以排除指定属性
- 可以通过 partial 扩展 DTO
- 保持向后兼容

---

### 任务 2.4：改造 CRUD Service 生成器

**优先级**: 高
**预计时间**: 1 天

**描述**: 修改 CRUD Service 生成器，生成基类。

**具体步骤**:
1. 修改 `CrudServiceSourceGenerator`
2. 生成 `{EntityName}CrudServiceBase` 抽象类
3. 所有方法标记为 `virtual`
4. 添加钩子方法：
   - `OnCreatingAsync`
   - `OnCreatedAsync`
   - `OnUpdatingAsync`
   - `OnUpdatedAsync`
   - `OnDeletingAsync`
   - `OnDeletedAsync`
5. 接口改为 `partial`

**验收标准**:
- 生成的基类可以被继承
- 可以重写任何方法
- 钩子方法可以被覆盖

---

### 任务 2.5：更新 Book 实体和示例

**优先级**: 高
**预计时间**: 4-6 小时

**描述**: 更新示例项目，使用新的基类模式。

**具体步骤**:
1. 更新 `Book.cs` 实体，使用新特性
2. 创建 `BookRepository.cs`，继承基类
3. 创建 `BookAppService.cs`，继承基类
4. 更新 `LibraryManagement.Application` 项目
5. 确保示例可以正常编译运行

**验收标准**:
- 示例项目可以正常编译
- 功能与之前一致
- 代码更简洁清晰

---

## 阶段三：查询层简化

### 任务 3.1：设计查询扩展方法生成器

**优先级**: 中
**预计时间**: 4-6 小时

**描述**: 设计查询扩展方法的生成规则。

**具体步骤**:
1. 分析实体属性，确定需要生成的扩展方法
2. 设计命名规范：
   - `Where{PropertyName}` - 等值过滤
   - `Where{PropertyName}Contains` - 包含（字符串）
   - `Where{PropertyName}StartsWith` - 开头匹配（字符串）
   - `Where{PropertyName}EndsWith` - 结尾匹配（字符串）
   - `Where{PropertyName}GreaterThan` - 大于（数值/日期）
   - `Where{PropertyName}LessThan` - 小于（数值/日期）
   - `Where{PropertyName}Between` - 范围（数值/日期）
   - `OrderBy{PropertyName}` - 排序
   - `OrderBy{PropertyName}Descending` - 降序排序
3. 确定哪些属性需要生成哪些方法

**验收标准**:
- 设计文档完整
- 命名规范清晰
- 覆盖常见查询场景

---

### 任务 3.2：实现查询扩展方法生成器

**优先级**: 中
**预计时间**: 1 天

**描述**: 实现查询扩展方法的生成器。

**具体步骤**:
1. 创建 `QueryExtensionsGenerator`（或整合到统一生成器）
2. 根据设计文档生成扩展方法
3. 生成 `{EntityName}QueryExtensions.g.cs`
4. 确保扩展方法是类型安全的
5. 添加 XML 文档注释

**验收标准**:
- 生成的扩展方法可以直接使用
- 类型安全，无编译错误
- 包含完整的文档注释

---

### 任务 3.3：更新查询执行器（可选）

**优先级**: 低
**预计时间**: 4-6 小时

**描述**: 如果需要，简化或移除 QueryExecutor。

**具体步骤**:
1. 评估是否需要保留 QueryExecutor
2. 如果保留，简化实现
3. 如果移除，更新相关代码

**验收标准**:
- 查询功能正常
- 代码更简洁

---

### 任务 3.4：更新查询使用示例

**优先级**: 中
**预计时间**: 3-4 小时

**描述**: 更新示例代码，展示新的查询方式。

**具体步骤**:
1. 更新 `BookAppService.cs` 中的查询逻辑
2. 使用查询扩展方法替代 QueryBuilder
3. 展示多种查询场景
4. 添加注释说明

**验收标准**:
- 示例代码可以正常工作
- 代码更简洁易读
- 展示新的查询方式优势

---

## 阶段四：服务层优化

### 任务 4.1：完善钩子方法

**优先级**: 中
**预计时间**: 4-6 小时

**描述**: 完善服务基类的钩子方法。

**具体步骤**:
1. 在基类中定义所有钩子方法
2. 钩子方法默认返回 `Task.CompletedTask`
3. 在适当位置调用钩子方法
4. 添加 XML 文档注释说明使用场景

**验收标准**:
- 所有钩子方法都有定义
- 在正确的时机被调用
- 可以被子类覆盖

---

### 任务 4.2：创建服务模板

**优先级**: 中
**预计时间**: 3-4 小时

**描述**: 创建服务的手写实现模板。

**具体步骤**:
1. 创建模板文件或代码片段
2. 模板包含：
   - 继承基类
   - 构造函数
   - 常用重写示例
   - 钩子方法使用示例
3. 提供注释说明

**验收标准**:
- 模板可以直接使用
- 包含完整示例
- 注释清晰

---

### 任务 4.3：更新依赖注入注册

**优先级**: 中
**预计时间**: 3-4 小时

**描述**: 确保生成的服务和仓储可以正确注册到 DI 容器。

**具体步骤**:
1. 更新 Module 生成器（如果有）
2. 确保基类不被注册（注册具体实现）
3. 提供注册扩展方法或模板

**验收标准**:
- 服务和仓储可以正确注入
- 基类不会被错误注册

---

### 任务 4.4：添加验证支持

**优先级**: 低
**预计时间**: 4-6 小时

**描述**: 可选地添加验证支持。

**具体步骤**:
1. 考虑是否需要在服务基类中集成验证
2. 设计验证钩子
3. 实现验证逻辑（可选）

**验收标准**:
- （如果实现）验证功能正常工作
- 可以自定义验证逻辑

---

## 阶段五：迁移和文档

### 任务 5.1：创建迁移指南

**优先级**: 中
**预计时间**: 4-6 小时

**描述**: 创建详细的迁移指南文档。

**具体步骤**:
1. 编写迁移步骤说明
2. 提供代码对比示例
3. 列出常见问题和解决方案
4. 提供回滚方案

**验收标准**:
- 迁移指南清晰易懂
- 包含完整示例
- 覆盖常见场景

---

### 任务 5.2：更新 API 文档

**优先级**: 中
**预计时间**: 3-4 小时

**描述**: 更新所有相关的 API 文档。

**具体步骤**:
1. 更新特性文档
2. 更新生成器文档
3. 添加新 API 的文档
4. 更新示例文档

**验收标准**:
- 文档完整准确
- 与代码一致
- 易于理解

---

### 任务 5.3：完整测试和验收

**优先级**: 高
**预计时间**: 1-2 天

**描述**: 完整测试所有功能，确保一切正常。

**具体步骤**:
1. 编译整个解决方案
2. 运行所有单元测试
3. 手动测试示例项目
4. 性能测试（可选）
5. 检查向后兼容性

**验收标准**:
- 所有项目编译成功
- 所有测试通过
- 示例项目正常运行
- 向后兼容性保持

---

## 风险和注意事项

### 向后兼容性
- 保留旧特性，标记为 Obsolete
- 提供配置开关，默认行为可以调整
- 分阶段迁移，不要一次性删除旧代码

### 性能考虑
- 查询扩展方法编译时生成，无运行时开销
- 避免在基类中添加不必要的逻辑
- 保持生成的代码简洁高效

### 测试策略
- 每个阶段完成后进行测试
- 保持测试覆盖率
- 添加新功能的测试用例

---

## 工具和资源

### 推荐工具
- Visual Studio 2022 或更高版本
- .NET 8.0 SDK
- Roslyn 分析器（用于调试源生成器）

### 参考资料
- Roslyn Source Generators 官方文档
- C# 9.0+ 新特性文档
- 现有代码库作为参考

---

## 附录：AOP 架构设计说明

### 分层 AOP 策略

| 层级 | 拦截类型 | 实现方式 |
|------|----------|----------|
| **细粒度业务逻辑** | 单个实体/服务的特定业务 | 钩子方法（基类设计） |
| **中粒度实体级拦截** | 某个实体的所有 CRUD 操作 | 通过特性在基类上应用 Rougamo |
| **粗粒度横切关注点** | 事务、日志、缓存、权限等 | 全局配置或程序集级别应用 Rougamo |

### 架构图示

```
┌─────────────────────────────────────────────────────────┐
│                    Rougamo AOP 层                        │
│  (全局横切关注点: 事务、日志、缓存、权限、性能监控)      │
└─────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────┐
│              CrestCreates 生成基类层                      │
│  (生成的 RepositoryBase、CrudServiceBase)                │
│  - 提供基础 CRUD 方法                                     │
│  - 提供钩子方法: OnCreating、OnCreated 等                │
└─────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────┐
│              开发者手写实现层                              │
│  (继承基类，重写方法，实现业务逻辑)                       │
└─────────────────────────────────────────────────────────┘
```

### Rougamo 切面类型

| 切面名称 | 功能描述 | 默认启用 |
|----------|----------|----------|
| `TransactionMoAttribute` | 事务管理 | 是 |
| `LoggingMoAttribute` | 日志记录 | 是 |
| `ValidationMoAttribute` | 参数验证 | 是 |
| `CacheMoAttribute` | 缓存 | 否 |
| `PerformanceMoAttribute` | 性能监控 | 否 |

### 使用示例

**实体配置**：
```csharp
[GenerateEntity(
    GenerateRepository = true,
    GenerateCrudService = true,
    EnableTransaction = true,
    EnableLogging = true,
    EnableValidation = true,
    EnableCaching = false)]
public class Book : AuditedEntity<Guid>
{
    // ...
}
```

**生成的基类**：
```csharp
[TransactionMo]
[LoggingMo]
[ValidationMo]
public abstract class BookCrudServiceBase : IBookCrudService
{
    // 所有方法都会被 Rougamo 拦截
    public virtual async Task<BookDto> CreateAsync(CreateBookDto input, CancellationToken ct = default)
    {
        var entity = _mapper.Map<Book>(input);
        await OnCreatingAsync(entity, ct); // 钩子方法
        entity = await _repository.AddAsync(entity, ct);
        await OnCreatedAsync(entity, ct); // 钩子方法
        return _mapper.Map<BookDto>(entity);
    }
    
    protected virtual Task OnCreatingAsync(Book entity, CancellationToken ct) 
        => Task.CompletedTask;
}
```

**开发者实现**：
```csharp
public class BookAppService : BookCrudServiceBase, IBookAppService
{
    // 可以选择性地在方法上应用额外切面
    [CacheMo(ExpirationMinutes = 30)]
    public override async Task<BookDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return await base.GetAsync(id, ct);
    }
    
    // 钩子方法用于业务逻辑
    protected override Task OnCreatingAsync(Book entity, CancellationToken ct)
    {
        // 自定义业务逻辑
        return base.OnCreatingAsync(entity, ct);
    }
}
```

---

## 总结

这个任务计划分为 **6 个阶段**，共 **24 个主要任务**，预计总工作量约 **3-4 周**。建议按照阶段顺序执行，每个阶段完成后进行充分测试，确保质量和稳定性。

关键要点：
1. 保持向后兼容性
2. 分阶段实施，降低风险
3. 充分测试，确保质量
4. 文档完善，便于使用
5. Rougamo 集成提供编译时 AOP，性能优秀
6. 钩子方法和 AOP 协同工作，职责清晰
