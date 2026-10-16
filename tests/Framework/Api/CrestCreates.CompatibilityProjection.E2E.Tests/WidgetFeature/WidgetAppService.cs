using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.CompatibilityProjection.E2E.WidgetFeature.Contracts;
using CrestCreates.CompatibilityProjection.E2E.WidgetFeature.Services;

namespace CrestCreates.CompatibilityProjection.E2E.WidgetFeature;

/// <summary>
/// Per-host call ledger for the widget probe surface. Registered as a DI singleton on the host and
/// written only by the scoped <see cref="WidgetAppService"/>; tests read it back through the host's
/// own provider. This is host-scoped fixture state — never a process-global static.
/// </summary>
public sealed class WidgetCallRecorder
{
    public int CreateCalls { get; private set; }

    public int GetCalls { get; private set; }

    public int SearchCalls { get; private set; }

    public int UpdateCalls { get; private set; }

    public int DeleteCalls { get; private set; }

    public CreateWidgetDto? LastCreateInput { get; private set; }

    public CancellationToken LastCreateToken { get; private set; }

    public Guid? LastGetId { get; private set; }

    public WidgetListRequestDto? LastSearchInput { get; private set; }

    public Guid? LastUpdateId { get; private set; }

    public UpdateWidgetDto? LastUpdateInput { get; private set; }

    public Guid? LastDeleteId { get; private set; }

    public string? LastDeleteStamp { get; private set; }

    public void RecordCreate(CreateWidgetDto input, CancellationToken cancellationToken)
    {
        CreateCalls++;
        LastCreateInput = input;
        LastCreateToken = cancellationToken;
    }

    public void RecordGet(Guid id)
    {
        GetCalls++;
        LastGetId = id;
    }

    public void RecordSearch(WidgetListRequestDto input)
    {
        SearchCalls++;
        LastSearchInput = input;
    }

    public void RecordUpdate(Guid id, UpdateWidgetDto input)
    {
        UpdateCalls++;
        LastUpdateId = id;
        LastUpdateInput = input;
    }

    public void RecordDelete(Guid id, string? expectedStamp)
    {
        DeleteCalls++;
        LastDeleteId = id;
        LastDeleteStamp = expectedStamp;
    }

    public void Reset()
    {
        CreateCalls = 0;
        GetCalls = 0;
        SearchCalls = 0;
        UpdateCalls = 0;
        DeleteCalls = 0;
        LastCreateInput = null;
        LastCreateToken = default;
        LastGetId = null;
        LastSearchInput = null;
        LastUpdateId = null;
        LastUpdateInput = null;
        LastDeleteId = null;
        LastDeleteStamp = null;
    }
}

/// <summary>
/// The small scoped, memory-backed probe service exposing exactly the five operations the generated
/// CRUD AppService contract exposes (same names and signatures as
/// CrudArtifactEmitter.EmitServiceImplementation / EmitServiceContract). It performs no identity,
/// permission or UoW work of its own — those facilities belong to the Capability/AppService core this
/// fixture deliberately does not re-implement. Every call is recorded and the managed request token
/// is observed.
/// </summary>
public sealed class WidgetAppService : IWidgetAppService
{
    /// <summary>Id the memory store answers <c>null</c> for, so the typed 404 envelope is reachable.</summary>
    public static readonly Guid UnknownWidgetId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private readonly WidgetCallRecorder _recorder;

    public WidgetAppService(WidgetCallRecorder recorder)
    {
        _recorder = recorder;
    }

    public Task<WidgetDto> CreateAsync(CreateWidgetDto input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _recorder.RecordCreate(input, cancellationToken);
        return Task.FromResult(new WidgetDto { Id = Guid.NewGuid(), Title = input.Title });
    }

    public Task<WidgetDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _recorder.RecordGet(id);
        return Task.FromResult(id == UnknownWidgetId
            ? null
            : new WidgetDto { Id = id, Title = "Existing Widget" });
    }

    public Task<PagedResultDto<WidgetDto>> GetListAsync(
        WidgetListRequestDto input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _recorder.RecordSearch(input);
        var items = new List<WidgetDto>
        {
            new() { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Title = "Widget One" },
            new() { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Title = "Widget Two" },
        };
        return Task.FromResult(new PagedResultDto<WidgetDto>(items, items.Count, input.PageIndex, input.PageSize));
    }

    public Task<WidgetDto> UpdateAsync(Guid id, UpdateWidgetDto input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _recorder.RecordUpdate(id, input);
        return Task.FromResult(new WidgetDto { Id = id, Title = input.Title });
    }

    public Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _recorder.RecordDelete(id, expectedStamp);
        return Task.CompletedTask;
    }
}
