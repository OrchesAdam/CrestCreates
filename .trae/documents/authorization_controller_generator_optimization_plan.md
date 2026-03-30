# 授权模块与控制器生成器优化计划

## 冲突分析

### 现有问题
1. **功能重叠**：两个生成器都生成控制器代码
2. **授权逻辑分离**：`AuthorizationAttributeGenerator` 处理授权特性，`ControllerSourceGenerator` 不包含授权逻辑
3. **生成方式不一致**：
   - `AuthorizationAttributeGenerator`：普通类，需要手动调用
   - `ControllerSourceGenerator`：Source Generator，自动运行
4. **代码结构差异**：生成的控制器结构和命名约定不同

### 根本原因
- 设计时未考虑两个生成器的协同工作
- 授权逻辑与控制器生成逻辑分离
- 缺乏统一的配置机制

## 优化方案

### 方案 1：将授权逻辑集成到 ControllerSourceGenerator
- **优先级**：P0
- **描述**：扩展 `ControllerSourceGenerator`，添加授权特性生成功能
- **优势**：
  - 统一生成流程
  - 减少重复代码
  - 支持自动生成授权控制器
- **劣势**：
  - 需要修改现有生成器
  - 可能影响现有功能

### 方案 2：使用 AuthorizationAttributeGenerator 作为后处理器
- **优先级**：P1
- **描述**：让 `ControllerSourceGenerator` 生成基础控制器，然后使用 `AuthorizationAttributeGenerator` 注入授权特性
- **优势**：
  - 保持现有代码结构
  - 授权逻辑独立管理
- **劣势**：
  - 增加生成步骤
  - 可能导致代码重复

### 方案 3：重构为统一的生成器
- **优先级**：P2
- **描述**：创建新的统一生成器，支持控制器和授权特性的生成
- **优势**：
  - 完全统一的生成流程
  - 更好的配置管理
- **劣势**：
  - 工作量较大
  - 可能影响现有代码

## 推荐方案

**推荐方案 1**：将授权逻辑集成到 `ControllerSourceGenerator`

### 具体实现步骤

## [ ] 任务 1：分析 ServiceAttribute 结构
- **优先级**：P0
- **描述**：分析现有的 ServiceAttribute，确定如何添加授权相关配置
- **成功标准**：了解 ServiceAttribute 的结构和使用方式
- **测试要求**：
  - `programmatic` TR-1.1：查看 ServiceAttribute 的定义和使用示例
  - `human-judgement` TR-1.2：确认授权配置可以添加到 ServiceAttribute 中

## [ ] 任务 2：扩展 ServiceAttribute 添加授权配置
- **优先级**：P0
- **描述**：在 ServiceAttribute 中添加授权相关的属性
- **成功标准**：ServiceAttribute 包含授权配置属性
- **测试要求**：
  - `programmatic` TR-2.1：ServiceAttribute 编译成功
  - `human-judgement` TR-2.2：授权配置属性设计合理

## [ ] 任务 3：修改 ControllerSourceGenerator 添加授权逻辑
- **优先级**：P0
- **描述**：修改 `ControllerSourceGenerator`，根据授权配置生成授权特性
- **成功标准**：生成的控制器包含正确的授权特性
- **测试要求**：
  - `programmatic` TR-3.1：生成的控制器代码包含授权特性
  - `programmatic` TR-3.2：授权特性配置正确
  - `human-judgement` TR-3.3：生成的代码结构清晰

## [ ] 任务 4：实现授权特性生成逻辑
- **优先级**：P1
- **描述**：实现根据 HTTP 方法和配置生成授权特性的逻辑
- **成功标准**：正确生成 CRUD 权限和自定义权限
- **测试要求**：
  - `programmatic` TR-4.1：GET 方法生成 View 权限
  - `programmatic` TR-4.2：POST 方法生成 Create 权限
  - `programmatic` TR-4.3：PUT 方法生成 Update 权限
  - `programmatic` TR-4.4：DELETE 方法生成 Delete 权限

## [ ] 任务 5：添加配置验证
- **优先级**：P1
- **描述**：添加配置验证，确保授权配置正确
- **成功标准**：配置验证逻辑正常工作
- **测试要求**：
  - `programmatic` TR-5.1：无效配置时生成警告
  - `programmatic` TR-5.2：有效配置时生成正确代码

## [ ] 任务 6：测试集成
- **优先级**：P1
- **描述**：测试生成的控制器是否包含正确的授权特性
- **成功标准**：生成的控制器代码正确，包含授权特性
- **测试要求**：
  - `programmatic` TR-6.1：编译成功
  - `human-judgement` TR-6.2：授权特性配置正确

## [ ] 任务 7：更新文档
- **优先级**：P2
- **描述**：更新文档，说明如何使用授权配置
- **成功标准**：文档更新完成
- **测试要求**：
  - `human-judgement` TR-7.1：文档内容完整
  - `human-judgement` TR-7.2：文档示例正确

## 技术实现细节

### 1. 扩展 ServiceAttribute
```csharp
[AttributeUsage(AttributeTargets.Class)]
public class ServiceAttribute : Attribute
{
    public bool GenerateController { get; set; } = true;
    public string Route { get; set; } = "";
    
    // 新增授权配置
    public bool GenerateAuthorization { get; set; } = false;
    public string ResourceName { get; set; } = "";
    public bool GenerateCrudPermissions { get; set; } = true;
    public string[] DefaultRoles { get; set; } = null;
    public bool RequireAll { get; set; } = false;
    public Dictionary<string, string> CustomPermissions { get; set; } = new();
}
```

### 2. 修改 ControllerSourceGenerator
- 在 `GenerateControllerAction` 方法中添加授权特性生成逻辑
- 根据 HTTP 方法和配置生成相应的授权特性

### 3. 生成授权特性的逻辑
- GET → View 权限
- POST → Create 权限
- PUT → Update 权限
- DELETE → Delete 权限
- 支持自定义权限映射

### 4. 配置验证
- 验证 ResourceName 是否为空
- 验证 DefaultRoles 是否有效
- 验证 CustomPermissions 是否有效

## 预期成果

1. **统一的生成流程**：控制器和授权特性由同一个生成器生成
2. **简化配置**：通过 ServiceAttribute 配置授权逻辑
3. **减少重复代码**：消除两个生成器之间的代码重复
4. **提高可维护性**：授权逻辑与控制器生成逻辑集成
5. **更好的用户体验**：开发者可以通过简单的配置生成带有授权的控制器

## 风险评估

- **风险 1**：修改现有生成器可能影响现有功能
  - **缓解措施**：进行充分的测试，确保向后兼容

- **风险 2**：授权配置可能过于复杂
  - **缓解措施**：提供合理的默认值，简化配置

- **风险 3**：生成的代码可能不符合项目规范
  - **缓解措施**：确保生成的代码符合项目的代码风格和规范

## 时间估计

| 任务 | 估计时间（小时） |
|------|-----------------|
| 任务 1 | 1 |
| 任务 2 | 1 |
| 任务 3 | 2 |
| 任务 4 | 2 |
| 任务 5 | 1 |
| 任务 6 | 2 |
| 任务 7 | 1 |
| **总计** | **10** |