# DataFilter.Entities 重构计划（解决与 Domain 实体的冲突）

## [ ] 任务 1: 统一实体基类，修改为泛型继承
- **Priority**: P0
- **Depends On**: None
- **Description**:
  - 修改 DataFilter.Entities 中的实体，使其继承自泛型的 `Entity<TId>`
  - 包括 `MultiTenantEntity`、`SoftDeleteEntity`、`MultiTenantSoftDeleteEntity`
- **Success Criteria**:
  - 所有实体都正确继承自泛型的 `Entity<TId>`
  - 能够成功编译
- **Test Requirements**:
  - `programmatic` TR-1.1: 项目能够成功编译
  - `human-judgement` TR-1.2: 代码结构清晰，泛型继承正确
- **Notes**: 确保所有实体类都使用正确的泛型参数

## [ ] 任务 2: 统一 ISoftDelete 接口，使用 Domain.Shared 中的定义
- **Priority**: P0
- **Depends On**: 任务 1
- **Description**:
  - 移除 DataFilter.Entities 中的 `ISoftDelete` 接口
  - 修改相关实体，使用 Domain.Shared 中已定义的 `ISoftDelete` 接口
  - 确保 `DeleterId` 类型从 `string?` 改为 `Guid?`
- **Success Criteria**:
  - DataFilter.Entities 不再包含 `ISoftDelete` 接口
  - 所有实体正确实现 Domain.Shared 中的 `ISoftDelete` 接口
- **Test Requirements**:
  - `programmatic` TR-2.1: 项目能够成功编译
  - `human-judgement` TR-2.2: 接口使用正确，无重复定义
- **Notes**: 确保所有使用 ISoftDelete 的地方都更新为正确的命名空间

## [ ] 任务 3: 优化实体设计，避免功能重复
- **Priority**: P1
- **Depends On**: 任务 2
- **Description**:
  - 审查 DataFilter.Entities 中的实体，确保与 Domain 中的实体功能不重复
  - 明确 DataFilter 模块的职责，专注于数据过滤逻辑
- **Success Criteria**:
  - 实体设计清晰，职责明确
  - 与 Domain 模块的功能无重复
- **Test Requirements**:
  - `programmatic` TR-3.1: 项目能够成功编译
  - `human-judgement` TR-3.2: 实体设计合理，职责明确
- **Notes**: 确保 DataFilter 模块专注于数据过滤，而不是重复实现业务逻辑

## [ ] 任务 4: 统一命名空间和命名规范
- **Priority**: P1
- **Depends On**: 任务 3
- **Description**:
  - 确保所有接口和类的命名在整个项目中保持一致
  - 避免在不同模块中使用相同的接口名称
- **Success Criteria**:
  - 命名规范一致
  - 无命名冲突
- **Test Requirements**:
  - `programmatic` TR-4.1: 项目能够成功编译
  - `human-judgement` TR-4.2: 命名规范一致，无冲突
- **Notes**: 确保所有文件都使用正确的命名空间

## [ ] 任务 5: 测试重构后的实现
- **Priority**: P0
- **Depends On**: 任务 4
- **Description**:
  - 编译项目，确保所有代码都能正常编译
  - 验证重构后的实体能够正常使用
- **Success Criteria**:
  - 项目能够成功编译
  - 实体功能正常
- **Test Requirements**:
  - `programmatic` TR-5.1: 项目能够成功编译
  - `human-judgement` TR-5.2: 重构后的代码结构清晰
- **Notes**: 确保所有重构后的代码都能正常工作