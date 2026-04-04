# Service 模板

## Service 实现模板

### 基本 Service 实现（继承基类）

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using YourNamespace.Application.Contracts.DTOs;
using YourNamespace.Application.Contracts.Interfaces;
using YourNamespace.Domain.Entities;
using YourNamespace.Domain.Repositories;

namespace YourNamespace.Application.Services;

public class YourEntityAppService : YourEntityCrudServiceBase, IYourEntityAppService
{
    public YourEntityAppService(
        IYourEntityRepository repository,
        IMapper mapper)
        : base(repository, mapper)
    {
    }

    // 在这里添加自定义服务方法
    // 或者重写基类方法

    /// <summary>
    /// 重写创建方法，添加自定义业务逻辑
    /// </summary>
    public override async Task<YourEntityDto> CreateAsync(CreateYourEntityDto input, CancellationToken cancellationToken = default)
    {
        // 添加自定义验证或业务逻辑
        // 例如：检查数据唯一性、调用领域服务等

        var result = await base.CreateAsync(input, cancellationToken);

        // 创建后的业务逻辑
        // 例如：发布领域事件、发送通知等

        return result;
    }

    /// <summary>
    /// 自定义服务方法
    /// </summary>
    public async Task<YourEntityDto?> GetByCustomPropertyAsync(string customValue, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByCustomPropertyAsync(customValue, cancellationToken);
        if (entity == null)
            return null;

        return _mapper.Map<YourEntityDto>(entity);
    }

    /// <summary>
    /// 重写钩子方法 - 创建前
    /// </summary>
    protected override async Task OnCreatingAsync(YourEntity entity, CancellationToken cancellationToken = default)
    {
        // 在创建实体前执行的业务逻辑
        // 例如：设置默认值、验证业务规则等

        await base.OnCreatingAsync(entity, cancellationToken);
    }

    /// <summary>
    /// 重写钩子方法 - 创建后
    /// </summary>
    protected override async Task OnCreatedAsync(YourEntity entity, CancellationToken cancellationToken = default)
    {
        // 在创建实体后执行的业务逻辑
        // 例如：发布领域事件、更新缓存、发送通知等

        await base.OnCreatedAsync(entity, cancellationToken);
    }

    /// <summary>
    /// 重写钩子方法 - 更新前
    /// </summary>
    protected override async Task OnUpdatingAsync(YourEntity entity, UpdateYourEntityDto input, CancellationToken cancellationToken = default)
    {
        // 在更新实体前执行的业务逻辑

        await base.OnUpdatingAsync(entity, input, cancellationToken);
    }

    /// <summary>
    /// 重写钩子方法 - 更新后
    /// </summary>
    protected override async Task OnUpdatedAsync(YourEntity entity, CancellationToken cancellationToken = default)
    {
        // 在更新实体后执行的业务逻辑

        await base.OnUpdatedAsync(entity, cancellationToken);
    }

    /// <summary>
    /// 重写钩子方法 - 删除前
    /// </summary>
    protected override async Task OnDeletingAsync(YourEntity entity, CancellationToken cancellationToken = default)
    {
        // 在删除实体前执行的业务逻辑
        // 例如：检查是否可以删除、记录删除日志等

        await base.OnDeletingAsync(entity, cancellationToken);
    }

    /// <summary>
    /// 重写钩子方法 - 删除后
    /// </summary>
    protected override async Task OnDeletedAsync(YourEntity entity, CancellationToken cancellationToken = default)
    {
        // 在删除实体后执行的业务逻辑
        // 例如：清理相关数据、更新统计等

        await base.OnDeletedAsync(entity, cancellationToken);
    }
}
```

### Service 接口扩展

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using YourNamespace.Application.Contracts.DTOs;

namespace YourNamespace.Application.Contracts.Interfaces;

public partial interface IYourEntityAppService : IYourEntityCrudService
{
    Task<YourEntityDto?> GetByCustomPropertyAsync(string customValue, CancellationToken cancellationToken = default);
    Task<List<YourEntityDto>> GetByStatusAsync(YourEntityStatus status, CancellationToken cancellationToken = default);
}
```

### 完整的 Service 示例（包含业务逻辑）

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using YourNamespace.Application.Contracts.DTOs;
using YourNamespace.Application.Contracts.Interfaces;
using YourNamespace.Domain.Entities;
using YourNamespace.Domain.Repositories;
using YourNamespace.Domain.DomainServices;

namespace YourNamespace.Application.Services;

public class YourEntityAppService : YourEntityCrudServiceBase, IYourEntityAppService
{
    private readonly IYourEntityDomainService _domainService;

    public YourEntityAppService(
        IYourEntityRepository repository,
        IMapper mapper,
        IYourEntityDomainService domainService)
        : base(repository, mapper)
    {
        _domainService = domainService;
    }

    /// <summary>
    /// 使用领域服务处理复杂业务逻辑
    /// </summary>
    public override async Task<YourEntityDto> CreateAsync(CreateYourEntityDto input, CancellationToken cancellationToken = default)
    {
        // 使用领域服务验证业务规则
        await _domainService.ValidateCreateAsync(input, cancellationToken);

        var result = await base.CreateAsync(input, cancellationToken);

        // 使用领域服务处理创建后的逻辑
        await _domainService.HandleCreatedAsync(result, cancellationToken);

        return result;
    }

    /// <summary>
    /// 自定义批量操作
    /// </summary>
    public async Task<List<YourEntityDto>> BatchCreateAsync(List<CreateYourEntityDto> inputs, CancellationToken cancellationToken = default)
    {
        var results = new List<YourEntityDto>();

        foreach (var input in inputs)
        {
            var result = await CreateAsync(input, cancellationToken);
            results.Add(result);
        }

        return results;
    }
}
```
