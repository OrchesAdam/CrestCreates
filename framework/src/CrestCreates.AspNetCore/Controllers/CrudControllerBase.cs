using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Application.Contracts.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace CrestCreates.AspNetCore.Controllers;

/// <summary>
/// 标准 CRUD 控制器基类，实体 SourceGenerator 只需要生成闭合泛型的薄控制器。
/// </summary>
[ApiController]
public abstract class CrudControllerBase<TService, TKey, TDto, TCreateDto, TUpdateDto, TListRequestDto> : ControllerBase
    where TService : ICrudAppService<TKey, TDto, TCreateDto, TUpdateDto, TListRequestDto>
    where TKey : IEquatable<TKey>
{
    protected CrudControllerBase(TService service)
    {
        Service = service ?? throw new ArgumentNullException(nameof(service));
    }

    protected TService Service { get; }

    [HttpGet("{id}")]
    public virtual async Task<ActionResult<TDto>> GetByIdAsync([FromRoute] TKey id, CancellationToken cancellationToken = default)
    {
        var result = await Service.GetByIdAsync(id, cancellationToken);
        if (result == null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    [HttpGet]
    public virtual async Task<ActionResult<PagedResultDto<TDto>>> GetListAsync([FromQuery] TListRequestDto input, CancellationToken cancellationToken = default)
    {
        var result = await Service.GetListAsync(input, cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    public virtual async Task<ActionResult<TDto>> CreateAsync([FromBody] TCreateDto input, CancellationToken cancellationToken = default)
    {
        var result = await Service.CreateAsync(input, cancellationToken);
        return Ok(result);
    }

    [HttpPut("{id}")]
    public virtual async Task<ActionResult<TDto>> UpdateAsync([FromRoute] TKey id, [FromBody] TUpdateDto input, CancellationToken cancellationToken = default)
    {
        var result = await Service.UpdateAsync(id, input, cancellationToken);
        return Ok(result);
    }

    [HttpDelete("{id}")]
    public virtual async Task<IActionResult> DeleteAsync([FromRoute] TKey id, CancellationToken cancellationToken = default)
    {
        await Service.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
