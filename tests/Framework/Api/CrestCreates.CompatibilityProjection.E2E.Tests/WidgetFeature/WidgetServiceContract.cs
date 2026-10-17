using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.CompatibilityProjection.E2E.WidgetFeature.Contracts;

namespace CrestCreates.CompatibilityProjection.E2E.WidgetFeature.Services;

/// <summary>
/// Hand-written mirror of the generated CRUD service contract
/// (<c>I{Service}</c> : <c>ICrudAppService&lt;TId, TDto, TCreate, TUpdate, TListRequest&gt;</c>).
/// The generated contribution's invokers resolve this exact contract from the endpoint context's
/// service provider, never a process-global resolver.
/// </summary>
public interface IWidgetAppService
{
    Task<WidgetDto> CreateAsync(CreateWidgetDto input, CancellationToken cancellationToken = default);

    Task<WidgetDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PagedResultDto<WidgetDto>> GetListAsync(
        WidgetListRequestDto input, CancellationToken cancellationToken = default);

    Task<WidgetDto> UpdateAsync(Guid id, UpdateWidgetDto input, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default);
}
