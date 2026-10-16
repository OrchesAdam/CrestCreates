using CrestCreates.Application.Contracts.DTOs.Common;

namespace CrestCreates.CompatibilityProjection.E2E.WidgetFeature.Contracts;

// #128 S3-7 (J/H/Q, JIT leg): hand-written mirrors of the CRUD contract DTOs the emitter
// materializes (CrudArtifactEmitter.EmitReadDto/EmitCreateDto/EmitUpdateDto/EmitListRequestDto).
// The generated-code fixture with real toolchain output is S4's scope (the GeneratedCrud.AotFixture
// assemblies); this fixture exists so the five-action wire matrix can be driven over a real
// WebApplicationFactory host today, before the toolchain fixture lands.

/// <summary>Mirrors the emitted <c>{Entity}Dto</c> read contract.</summary>
public class WidgetDto
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;
}

/// <summary>Mirrors the emitted <c>Create{Entity}Dto</c> create contract.</summary>
public class CreateWidgetDto
{
    public string Title { get; set; } = string.Empty;
}

/// <summary>Mirrors the emitted <c>Update{Entity}Dto</c> update contract.</summary>
public class UpdateWidgetDto
{
    public string Title { get; set; } = string.Empty;
}

/// <summary>Mirrors the emitted <c>{Entity}ListRequestDto : PagedRequestDto</c> search request.</summary>
public class WidgetListRequestDto : PagedRequestDto
{
}
