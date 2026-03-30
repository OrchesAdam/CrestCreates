# CrestCreates 框架 - 领域事件发布机制开发计划

## 项目状态分析

当前项目中领域事件的实现情况：
- ✅ 核心定义：`DomainEvent` 抽象类和 `IDomainEvent` 接口（继承自 `INotification`）
- ✅ `IDomainEventPublisher` 接口定义
- ✅ 实体类 `Entity<TId>` 中包含领域事件的管理方法
- ✅ `DomainEventPublisher` 实现（使用 MediatR 发布事件）
- ✅ `UnitOfWorkWithEvents` 基类（提供事件发布功能）
- ✅ `EfCoreUnitOfWork` 实现（使用反射来获取和发布领域事件）

## 问题识别

1. **性能问题**：`EfCoreUnitOfWork` 中使用反射来处理领域事件，影响性能
2. **代码重复**：不同 ORM 的工作单元实现可能需要重复相同的反射逻辑
3. **维护性**：反射代码难以维护和调试
4. **功能完整性**：需要确保所有 ORM 都支持领域事件发布

## 开发计划

### [ ] 任务 1：创建领域事件 Source Generator
- **Priority**: P0
- **Depends On**: None
- **Description**:
  - 创建一个 Source Generator 项目，用于生成领域事件相关的代码
  - 生成事件处理器注册代码
  - 生成工作单元中处理领域事件的类型安全代码
- **Success Criteria**:
  - Source Generator 能够正确生成事件处理器注册代码
  - 生成的代码类型安全，不使用反射
  - Source Generator 能够集成到项目构建流程中
- **Test Requirements**:
  - `programmatic` TR-1.1: Source Generator 能够成功编译并生成代码
  - `programmatic` TR-1.2: 生成的代码能够通过编译
  - `human-judgement` TR-1.3: 生成的代码可读性好，符合项目编码规范

### [ ] 任务 2：重构 EfCoreUnitOfWork 以使用生成的代码
- **Priority**: P0
- **Depends On**: 任务 1
- **Description**:
  - 移除 `EfCoreUnitOfWork` 中的反射代码
  - 使用 Source Generator 生成的类型安全代码替代反射
  - 确保事件发布逻辑正确执行
- **Success Criteria**:
  - `EfCoreUnitOfWork` 不再使用反射
  - 领域事件能够正确发布
  - 性能得到提升
- **Test Requirements**:
  - `programmatic` TR-2.1: `EfCoreUnitOfWork` 能够成功编译
  - `programmatic` TR-2.2: 领域事件能够正确发布到 MediatR
  - `programmatic` TR-2.3: 事件发布后实体的领域事件列表被正确清空

### [ ] 任务 3：完善 SqlSugar 和 FreeSql 工作单元的领域事件支持
- **Priority**: P1
- **Depends On**: 任务 1
- **Description**:
  - 为 `SqlSugarUnitOfWork` 添加领域事件支持
  - 为 `FreeSqlUnitOfWork` 添加领域事件支持
  - 使用 Source Generator 生成的代码确保类型安全
- **Success Criteria**:
  - `SqlSugarUnitOfWork` 能够正确发布领域事件
  - `FreeSqlUnitOfWork` 能够正确发布领域事件
  - 所有 ORM 的工作单元实现都使用类型安全的代码
- **Test Requirements**:
  - `programmatic` TR-3.1: `SqlSugarUnitOfWork` 和 `FreeSqlUnitOfWork` 能够成功编译
  - `programmatic` TR-3.2: 两种 ORM 的工作单元都能正确发布领域事件

### [ ] 任务 4：创建领域事件测试项目
- **Priority**: P1
- **Depends On**: 任务 2, 任务 3
- **Description**:
  - 创建专门的测试项目来测试领域事件发布机制
  - 测试不同场景下的事件发布
  - 测试性能对比（反射 vs 生成代码）
- **Success Criteria**:
  - 测试项目能够成功运行
  - 所有测试用例通过
  - 性能测试显示生成代码比反射更快
- **Test Requirements**:
  - `programmatic` TR-4.1: 所有测试用例通过
  - `programmatic` TR-4.2: 性能测试显示生成代码性能优于反射
  - `human-judgement` TR-4.3: 测试代码覆盖了主要场景

### [ ] 任务 5：更新文档和示例
- **Priority**: P2
- **Depends On**: 任务 4
- **Description**:
  - 更新开发指南，添加领域事件使用说明
  - 创建领域事件使用示例
  - 更新架构文档，添加领域事件发布机制的说明
- **Success Criteria**:
  - 文档更新完成
  - 示例项目能够正确演示领域事件的使用
  - 文档清晰易懂
- **Test Requirements**:
  - `human-judgement` TR-5.1: 文档内容完整准确
  - `human-judgement` TR-5.2: 示例项目能够正常运行

## 技术实现要点

1. **Source Generator 设计**:
   - 使用 Roslyn 分析器来识别领域事件和处理器
   - 生成类型安全的事件处理器注册代码
   - 生成工作单元中处理领域事件的代码

2. **工作单元重构**:
   - 移除反射代码，使用生成的类型安全代码
   - 确保事务提交时正确发布领域事件
   - 处理事件发布失败的情况

3. **性能优化**:
   - 比较反射和生成代码的性能差异
   - 优化事件发布的执行路径
   - 确保事件发布不影响事务提交的性能

4. **测试策略**:
   - 单元测试：测试事件发布的基本功能
   - 集成测试：测试完整的事件发布流程
   - 性能测试：比较反射和生成代码的性能

## 预期成果

1. 实现完整的领域事件发布机制
2. 使用 Source Generator 替代反射，提高性能和可维护性
3. 确保所有 ORM 都支持领域事件发布
4. 提供完整的测试覆盖和文档

## 风险评估

1. **技术风险**:
   - Source Generator 实现复杂度较高
   - 需要确保与不同 ORM 的兼容性

2. **时间风险**:
   - Source Generator 开发可能需要较多时间
   - 测试覆盖需要充分考虑各种场景

3. **依赖风险**:
   - 依赖 MediatR 库的稳定性
   - 依赖 Roslyn 分析器的功能

## 缓解策略

1. **技术风险**:
   - 分阶段实现 Source Generator，先实现核心功能
   - 与 ORM 团队保持沟通，确保兼容性

2. **时间风险**:
   - 制定详细的开发计划，按优先级执行
   - 并行开发测试用例，确保功能正确性

3. **依赖风险**:
   - 锁定依赖版本，确保稳定性
   - 提供降级方案，在必要时可以回退到反射实现