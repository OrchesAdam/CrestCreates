using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.CompatibilityProjection.E2E.WidgetFeature.Contracts;
using CrestCreates.CompatibilityProjection.E2E.WidgetFeature.Services;

namespace CrestCreates.CompatibilityProjection.E2E.WidgetFeature;

/// <summary>
/// Per-host call ledger for the widget probe surface. Registered as a DI singleton on the host and
/// written by the scoped <see cref="WidgetAppService"/> (tests only arm/read it) through the host's
/// own provider. This is host-scoped fixture state — never a process-global static.
/// </summary>
public sealed class WidgetCallRecorder
{
    private readonly object _createGateSync = new();

    private bool _createGateArmed;
    private TaskCompletionSource? _createGate;
    private TaskCompletionSource? _createEntered;
    private bool _createCancellationObserved;
    private bool _createCompleted;

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

    // ---- opt-in create gate (S3-7 part 2a request-abort probe) ----------------------------------
    // While armed, WidgetAppService.CreateAsync parks on the gate after reporting entry, so a test
    // can abort the owning HTTP request and observe that the token handed down the generated-shaped
    // chain is the request's abort token. Every member is a no-op for the unarmed fixtures.

    /// <summary>True while the create gate is armed; <see cref="Reset"/> disarms it.</summary>
    public bool CreateGateArmed
    {
        get { lock (_createGateSync) { return _createGateArmed; } }
    }

    /// <summary>
    /// The gate an armed <see cref="WidgetAppService.CreateAsync"/> call parks on; <c>null</c> while
    /// the gate is not armed, which is how the unarmed call keeps its pre-probe shape.
    /// </summary>
    public TaskCompletionSource? CreateGate
    {
        get { lock (_createGateSync) { return _createGate; } }
    }

    /// <summary>Completed by <see cref="WidgetAppService.CreateAsync"/> immediately before it parks.</summary>
    public TaskCompletionSource? CreateEntered
    {
        get { lock (_createGateSync) { return _createEntered; } }
    }

    /// <summary>True once an armed create's gate await failed with <see cref="OperationCanceledException"/>.</summary>
    public bool CreateCancellationObserved
    {
        get { lock (_createGateSync) { return _createCancellationObserved; } }
    }

    /// <summary>True once an armed create passed the gate without cancellation.</summary>
    public bool CreateCompleted
    {
        get { lock (_createGateSync) { return _createCompleted; } }
    }

    /// <summary>Arms the create gate for the next <see cref="WidgetAppService.CreateAsync"/> call.</summary>
    public void ArmCreateGate()
    {
        lock (_createGateSync)
        {
            _createGateArmed = true;
            _createGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _createEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _createCancellationObserved = false;
            _createCompleted = false;
        }
    }

    public void SignalCreateEntered()
    {
        lock (_createGateSync)
        {
            _createEntered?.TrySetResult();
        }
    }

    public void RecordCreateCancellationObserved()
    {
        lock (_createGateSync)
        {
            _createCancellationObserved = true;
        }
    }

    public void RecordCreateCompleted()
    {
        lock (_createGateSync)
        {
            _createCompleted = true;
        }
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

        lock (_createGateSync)
        {
            _createGateArmed = false;
            _createGate = null;
            _createEntered = null;
            _createCancellationObserved = false;
            _createCompleted = false;
        }
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

    public async Task<WidgetDto> CreateAsync(CreateWidgetDto input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _recorder.RecordCreate(input, cancellationToken);

        // Opt-in request-abort probe (S3-7 part 2a): while the gate is armed the call parks after
        // reporting entry, so a test can abort the owning HTTP request and the request's own token
        // is the only way the parked await can complete. Unarmed calls skip the block entirely and
        // keep the pre-probe shape.
        var gate = _recorder.CreateGate;
        if (gate is not null)
        {
            _recorder.SignalCreateEntered();
            try
            {
                await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _recorder.RecordCreateCancellationObserved();
                throw;
            }

            _recorder.RecordCreateCompleted();
        }

        return new WidgetDto { Id = Guid.NewGuid(), Title = input.Title };
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
