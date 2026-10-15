#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CrestCreates.Tooling.CrudGeneration;

/// <summary>
/// One materialized CRUD source file: a stable, unique file-like name plus the ordinary C# text the
/// precompile stage feeds to the compiler as Compile input.
/// </summary>
public sealed record CrudArtifact(string HintName, string FileName, string Source);

/// <summary>
/// #128 S2: turns the frozen <see cref="CrudGenerationModel"/> into the materialized CRUD surface —
/// contracts DTOs, permission definitions, mapping seeds, service contract, service implementation and
/// the capability input envelopes. Every emitted name, namespace and member list is read from the model,
/// so the emitter owns no second naming rule, and the analyzer plus the build-time tool compile this same
/// implementation.
/// The surface deliberately contains no HTTP projection, no registry activation and no runtime
/// reflection: it must compile as ordinary input inside the same CoreCompile as the official STJ
/// generator and the sibling generators.
/// Imports are emitted inside the namespace block, so one artifact stays compilable alone and the whole
/// artifact set stays compilable when concatenated into a single bundle file — a file-level import after
/// the first namespace declaration would break the bundle.
/// </summary>
public static class CrudArtifactEmitter
{
    /// <summary>Separator written between artifacts inside one bundle, so every block stays attributable.</summary>
    private const string ArtifactSeparator = "// ---- artifact: ";

    private const string Indent = "    ";

    private const string NestedIndent = Indent + Indent;

    private const string ObjectMappingNamespace = "CrestCreates.Domain.Shared.ObjectMapping";

    private const string PermissionNameNamespace = "CrestCreates.Core.Abstractions.Identity";

    /// <summary>
    /// Tenant marker the ported audit/ownership branches stamp. The model flag <c>EntityHasTenantId</c>
    /// keys on the Domain.Shared auditing interface name, while the framework's tenant entities implement
    /// this one; the branch is emitted fully qualified so no imported namespace can make the short name
    /// ambiguous. The mismatch itself has to be closed in the model (flagged in the S2 handover).
    /// </summary>
    private const string MultiTenantInterface = "global::CrestCreates.DataFilter.Entities.IMultiTenant";

    private static readonly string[] PermissionActionOrder = ["Create", "Get", "Search", "Update", "Delete"];

    private static readonly IReadOnlyList<string> ReadDtoUsings = ["using System;"];

    private static readonly IReadOnlyList<string> ServiceContractUsings =
    [
        "using System;",
        "using CrestCreates.Application.Contracts.Interfaces;",
    ];

    private static readonly IReadOnlyList<string> ServiceImplementationUsings =
    [
        "using System;",
        "using System.Collections.Generic;",
        "using System.Linq;",
        "using System.Threading;",
        "using System.Threading.Tasks;",
        "using CrestCreates.Aop.Interceptors;",
        "using CrestCreates.Authorization.Abstractions;",
        "using CrestCreates.Domain.Shared.Attributes;",
        "using CrestCreates.Application.Contracts.DTOs.Common;",
        "using CrestCreates.Application.Contracts.Query;",
        "using CrestCreates.Domain.Exceptions;",
        "using CrestCreates.Domain.Repositories;",
        "using CrestCreates.Domain.Shared.DataFilter;",
        "using CrestCreates.Domain.Shared.Entities.Auditing;",
        "using CrestCreates.Domain.Shared.Exceptions;",
    ];

    /// <summary>Materializes every artifact of one declared CRUD surface, in model order.</summary>
    public static IReadOnlyList<CrudArtifact> Emit(CrudGenerationModel model)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        var artifacts = new List<CrudArtifact>
        {
            EmitReadDto(model),
            EmitCreateDto(model),
            EmitUpdateDto(model),
            EmitListRequestDto(model),
            EmitPermissions(model),
            EmitObjectMappingSeeds(model),
            EmitServiceContract(model),
            EmitServiceImplementation(model),
        };

        foreach (var action in model.Actions.Where(a => a.UsesCapabilityInputEnvelope))
        {
            artifacts.Add(EmitCapabilityInputEnvelope(model, action));
        }

        return artifacts;
    }

    /// <summary>
    /// The single bundle text the build stage writes as one <c>CrestCreates.Crud.g.cs</c> Compile input.
    /// </summary>
    public static string EmitBundle(CrudGenerationModel model)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        var builder = StartDocument(model);
        foreach (var artifact in Emit(model))
        {
            Line(builder, ArtifactSeparator + artifact.HintName + " ----");
            builder.Append(artifact.Source);
        }

        return builder.ToString();
    }

    // ---- 1. contracts DTOs ---------------------------------------------------------------------

    private static CrudArtifact EmitReadDto(CrudGenerationModel model)
    {
        var builder = StartDocument(model);
        OpenNamespace(builder, model.ContractsNamespace, ReadDtoUsings);
        Line(builder, Indent + "public partial class " + model.ReadDtoName);
        Line(builder, Indent + "{");
        foreach (var field in model.ReadFields)
        {
            EmitField(builder, field, validationAttributes: false);
        }

        Line(builder, Indent + "}");
        CloseNamespace(builder);
        return Artifact(model.ReadDtoName + ".g.cs", builder);
    }

    private static CrudArtifact EmitCreateDto(CrudGenerationModel model)
    {
        var builder = StartDocument(model);
        OpenNamespace(builder, model.ContractsNamespace, DtoUsings(model.CreateFields));
        Line(builder, Indent + "public partial class " + model.CreateDtoName);
        Line(builder, Indent + "{");
        foreach (var field in model.CreateFields)
        {
            EmitField(builder, field, validationAttributes: true);
        }

        Line(builder, Indent + "}");
        CloseNamespace(builder);
        return Artifact(model.CreateDtoName + ".g.cs", builder);
    }

    private static CrudArtifact EmitUpdateDto(CrudGenerationModel model)
    {
        var builder = StartDocument(model);
        OpenNamespace(builder, model.ContractsNamespace, DtoUsings(model.UpdateFields));
        Line(builder, Indent + "public partial class " + model.UpdateDtoName);
        Line(builder, Indent + "{");
        foreach (var field in model.UpdateFields)
        {
            EmitField(builder, field, validationAttributes: true);
        }

        Line(builder, Indent + "}");
        CloseNamespace(builder);
        return Artifact(model.UpdateDtoName + ".g.cs", builder);
    }

    private static CrudArtifact EmitListRequestDto(CrudGenerationModel model)
    {
        var builder = StartDocument(model);
        OpenNamespace(builder, model.ContractsNamespace, ReadDtoUsings);
        Line(builder, Indent + "public partial class " + model.ListRequestDtoName
            + " : global::" + CrudResponseContract.PagedResultNamespace + ".PagedRequestDto");
        Line(builder, Indent + "{");
        Line(builder, Indent + "}");
        CloseNamespace(builder);
        return Artifact(model.ListRequestDtoName + ".g.cs", builder);
    }

    private static IEnumerable<string> DtoUsings(IReadOnlyList<CrudFieldModel> fields)
    {
        yield return "using System;";
        if (fields.Any(field => field.IsRequiredString))
        {
            yield return "using System.ComponentModel.DataAnnotations;";
        }
    }

    private static void EmitField(StringBuilder builder, CrudFieldModel field, bool validationAttributes)
    {
        if (validationAttributes && field.IsRequiredString)
        {
            Line(builder, NestedIndent + "[Required]");
            Line(builder, NestedIndent + "[StringLength(255)]");
        }

        Line(builder, NestedIndent + "public " + field.TypeDisplay + " " + field.Name + " { get; set; }");
        Line(builder, string.Empty);
    }

    // ---- 2. permissions ------------------------------------------------------------------------

    private static CrudArtifact EmitPermissions(CrudGenerationModel model)
    {
        var builder = StartDocument(model);
        OpenNamespace(builder, model.PermissionsNamespace);
        Line(builder, Indent + "public static partial class " + model.PermissionsClassName);
        Line(builder, Indent + "{");
        foreach (var action in OrderedActions(model))
        {
            Line(builder, NestedIndent
                + "private const string " + action.Name + "Value = \"" + action.PermissionName + "\";");
            if (model.SupportsPermissionNames)
            {
                Line(builder, NestedIndent
                    + "public static global::" + PermissionNameNamespace + ".PermissionName "
                    + action.Name + " { get; } = new(" + action.Name + "Value);");
            }
        }

        Line(builder, Indent + "}");
        CloseNamespace(builder);
        return Artifact(model.PermissionsClassName + ".g.cs", builder);
    }

    // ---- 3. object mapping seeds ---------------------------------------------------------------

    private static CrudArtifact EmitObjectMappingSeeds(CrudGenerationModel model)
    {
        var attribute = "global::" + ObjectMappingNamespace + ".GenerateObjectMapping";
        var direction = "global::" + ObjectMappingNamespace + ".MapDirection.";

        var builder = StartDocument(model);
        OpenNamespace(builder, model.MappingsNamespace);
        Line(builder, Indent + "[" + attribute + "(typeof(" + model.EntityQualifiedDisplay + "), "
            + "typeof(" + Qualified(model.ReadDtoFullName) + "))]");
        Line(builder, Indent + "[" + attribute + "(typeof(" + Qualified(model.CreateDtoFullName) + "), "
            + "typeof(" + model.EntityQualifiedDisplay + "), Direction = " + direction + "Create)]");
        Line(builder, Indent + "[" + attribute + "(typeof(" + Qualified(model.UpdateDtoFullName) + "), "
            + "typeof(" + model.EntityQualifiedDisplay + "), Direction = " + direction + "Apply)]");
        Line(builder, Indent + "public static partial class " + model.MappingClassName);
        Line(builder, Indent + "{");
        Line(builder, Indent + "}");
        CloseNamespace(builder);
        return Artifact(model.MappingClassName + ".g.cs", builder);
    }

    // ---- 4. service contract -------------------------------------------------------------------

    private static CrudArtifact EmitServiceContract(CrudGenerationModel model)
    {
        var builder = StartDocument(model);
        OpenNamespace(builder, model.InterfaceNamespace, ServiceContractUsings);
        Line(builder, Indent + "public partial interface " + model.ServiceInterfaceName
            + " : ICrudAppService<"
            + model.IdTypeDisplay + ", "
            + Qualified(model.ReadDtoFullName) + ", "
            + Qualified(model.CreateDtoFullName) + ", "
            + Qualified(model.UpdateDtoFullName) + ", "
            + Qualified(model.ListRequestFullName) + ">");
        Line(builder, Indent + "{");
        Line(builder, Indent + "}");
        CloseNamespace(builder);
        return Artifact(model.ServiceInterfaceName + ".g.cs", builder);
    }

    // ---- 5. service implementation -------------------------------------------------------------

    private static CrudArtifact EmitServiceImplementation(CrudGenerationModel model)
    {
        var entity = model.EntityQualifiedDisplay;
        var idType = model.IdTypeDisplay;
        var entityName = model.EntityClassName;
        var service = model.ServiceClassName;
        var contract = Qualified(model.InterfaceNamespace + "." + model.ServiceInterfaceName);
        var mappings = Qualified(model.MappingsNamespace + "." + model.MappingClassName);
        var repository = "ICrestRepositoryBase<" + entity + ", " + idType + ">";
        var readDto = Qualified(model.ReadDtoFullName);
        var createDto = Qualified(model.CreateDtoFullName);
        var updateDto = Qualified(model.UpdateDtoFullName);
        var listRequest = Qualified(model.ListRequestFullName);
        var pagedReadDto = CrudResponseContract.PagedPayload(model.ReadDtoFullName);

        var builder = StartDocument(model);
        OpenNamespace(builder, model.ImplementationNamespace, ServiceImplementationUsings);
        Line(builder, Indent + "[CrestService]");
        Line(builder, Indent + "public partial class " + service + " : " + contract);
        Line(builder, Indent + "{");

        Line(builder, NestedIndent + "protected readonly " + repository + " Repository;");
        Line(builder, NestedIndent + "protected readonly IPermissionChecker PermissionChecker;");
        Line(builder, NestedIndent + "protected readonly ICurrentUser CurrentUser;");
        Line(builder, NestedIndent + "protected readonly IDataPermissionFilter DataPermissionFilter;");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "public " + service + "(");
        Line(builder, NestedIndent + "    " + repository + " repository,");
        Line(builder, NestedIndent + "    IPermissionChecker permissionChecker,");
        Line(builder, NestedIndent + "    ICurrentUser currentUser,");
        Line(builder, NestedIndent + "    IDataPermissionFilter dataPermissionFilter)");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + "    Repository = repository ?? throw new ArgumentNullException(nameof(repository));");
        Line(builder, NestedIndent + "    PermissionChecker = permissionChecker ?? throw new ArgumentNullException(nameof(permissionChecker));");
        Line(builder, NestedIndent + "    CurrentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));");
        Line(builder, NestedIndent + "    DataPermissionFilter = dataPermissionFilter ?? throw new ArgumentNullException(nameof(dataPermissionFilter));");
        Line(builder, NestedIndent + "}");
        Line(builder, string.Empty);

        Line(builder, NestedIndent + "private static readonly HashSet<string> AllowedQueryFields = new(StringComparer.OrdinalIgnoreCase)");
        Line(builder, NestedIndent + "{");
        foreach (var field in model.QueryableFields)
        {
            Line(builder, NestedIndent + "    \"" + field + "\",");
        }

        Line(builder, NestedIndent + "};");

        EmitPermissionCheckHelper(builder);
        EmitDataPermissionHelper(builder, entity);
        EmitAuditHelpers(builder, model, entity);
        EmitOwnershipHelper(builder, model);
        EmitFieldGuards(builder, entityName);

        // Create
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "/// <summary>Creates one " + entityName + " and returns the read contract.</summary>");
        Line(builder, NestedIndent + "[UnitOfWorkMo]");
        Line(builder, NestedIndent + "public virtual async Task<" + readDto + "> CreateAsync("
            + createDto + " input, CancellationToken cancellationToken = default)");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + "    if (input == null)");
        Line(builder, NestedIndent + "        throw new ArgumentNullException(nameof(input));");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    await CheckPermissionAsync(" + Permission(model, "Create") + ", cancellationToken);");
        Line(builder, NestedIndent + "    await ValidateCreateAsync(input, cancellationToken);");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    var entity = " + mappings + ".ToTarget(input);");
        Line(builder, NestedIndent + "    await SetCreationAuditPropertiesAsync(entity);");
        Line(builder, NestedIndent + "    await OnCreatingAsync(entity, input, cancellationToken);");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    var created = await Repository.InsertAsync(entity, cancellationToken);");
        Line(builder, NestedIndent + "    await OnCreatedAsync(created, cancellationToken);");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    return " + mappings + ".ToTarget(created);");
        Line(builder, NestedIndent + "}");

        // GetById
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "/// <summary>Reads one " + entityName
            + " through the data-permission filtered query.</summary>");
        Line(builder, NestedIndent + "public virtual async Task<" + readDto + "?> GetByIdAsync("
            + idType + " id, CancellationToken cancellationToken = default)");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + "    await CheckPermissionAsync(" + Permission(model, "Get") + ", cancellationToken);");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    var query = Repository.GetQueryable();");
        Line(builder, NestedIndent + "    query = await ApplyDataPermissionFilterAsync(query);");
        Line(builder, NestedIndent + "    var entity = query.FirstOrDefault(x => x.Id.Equals(id));");
        Line(builder, NestedIndent + "    if (entity == null)");
        Line(builder, NestedIndent + "        throw new CrestEntityNotFoundException(\"" + entityName + "\", id);");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    await ValidateDataOwnershipAsync(entity);");
        Line(builder, NestedIndent + "    return " + mappings + ".ToTarget(entity);");
        Line(builder, NestedIndent + "}");

        // GetList
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "/// <summary>Reads a bounded page of " + entityName + " contracts.</summary>");
        Line(builder, NestedIndent + "public virtual async Task<" + pagedReadDto + "> GetListAsync("
            + listRequest + " input, CancellationToken cancellationToken = default)");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + "    if (input == null)");
        Line(builder, NestedIndent + "        throw new ArgumentNullException(nameof(input));");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    await CheckPermissionAsync(" + Permission(model, "Search") + ", cancellationToken);");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    EnsureAllowedFilterFields(input.Filters);");
        Line(builder, NestedIndent + "    EnsureAllowedSortFields(input.Sorts);");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    var query = Repository.GetQueryable();");
        Line(builder, NestedIndent + "    query = await ApplyDataPermissionFilterAsync(query);");
        Line(builder, NestedIndent + "    query = await ConfigureListQueryAsync(query, input, cancellationToken);");
        Line(builder, string.Empty);
        // S2 keeps the existing search execution point unchanged; the typed query adapter replaces these
        // two QueryExecutor calls in the next stage without altering the filter/sort/page contract above.
        Line(builder, NestedIndent + "    query = QueryExecutor<" + entity + ">.ApplyFilters("
            + "query, input.Filters ?? new List<FilterDescriptor>());");
        Line(builder, NestedIndent + "    var sorts = input.Sorts;");
        Line(builder, NestedIndent + "    if (sorts == null || sorts.Count == 0)");
        Line(builder, NestedIndent + "    {");
        Line(builder, NestedIndent + "        sorts = new List<SortDescriptor> { new SortDescriptor(\""
            + DefaultSortField(model) + "\", SortDirection.Descending) };");
        Line(builder, NestedIndent + "    }");
        Line(builder, NestedIndent + "    query = QueryExecutor<" + entity + ">.ApplySorts(query, sorts);");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    var totalCount = query.Count();");
        Line(builder, NestedIndent + "    query = QueryExecutor<" + entity + ">.ApplyPaging("
            + "query, input.GetSkipCount(), input.PageSize);");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    var entities = query.ToList();");
        Line(builder, NestedIndent + "    var dtos = entities.Select(" + mappings + ".ToTarget).ToList();");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    return new " + pagedReadDto + "(dtos, totalCount, input.PageIndex, input.PageSize);");
        Line(builder, NestedIndent + "}");

        // Update
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "/// <summary>Applies an update body onto the persisted " + entityName + ".</summary>");
        Line(builder, NestedIndent + "[UnitOfWorkMo]");
        Line(builder, NestedIndent + "public virtual async Task<" + readDto + "> UpdateAsync("
            + idType + " id, " + updateDto + " input, CancellationToken cancellationToken = default)");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + "    if (input == null)");
        Line(builder, NestedIndent + "        throw new ArgumentNullException(nameof(input));");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    await CheckPermissionAsync(" + Permission(model, "Update") + ", cancellationToken);");
        Line(builder, NestedIndent + "    await ValidateUpdateAsync(id, input, cancellationToken);");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    var entity = await Repository.GetAsync(id, cancellationToken);");
        Line(builder, NestedIndent + "    if (entity == null)");
        Line(builder, NestedIndent + "        throw new CrestEntityNotFoundException(\"" + entityName + "\", id);");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    await ValidateDataOwnershipAsync(entity);");
        Line(builder, NestedIndent + "    await OnUpdatingAsync(entity, input, cancellationToken);");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    " + mappings + ".Apply(input, entity);");
        Line(builder, NestedIndent + "    await SetModificationAuditPropertiesAsync(entity);");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    var updated = await Repository.UpdateAsync(entity, cancellationToken);");
        Line(builder, NestedIndent + "    await OnUpdatedAsync(updated, cancellationToken);");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    return " + mappings + ".ToTarget(updated);");
        Line(builder, NestedIndent + "}");

        // Delete
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "/// <summary>Deletes one " + entityName
            + "; an entity with a concurrency stamp requires the expected stamp.</summary>");
        Line(builder, NestedIndent + "[UnitOfWorkMo]");
        Line(builder, NestedIndent + "public virtual async Task DeleteAsync("
            + idType + " id, string? expectedStamp = null, CancellationToken cancellationToken = default)");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + "    await CheckPermissionAsync(" + Permission(model, "Delete") + ", cancellationToken);");
        Line(builder, string.Empty);
        if (model.EntityHasConcurrencyStamp)
        {
            Line(builder, NestedIndent + "    if (string.IsNullOrWhiteSpace(expectedStamp))");
            Line(builder, NestedIndent + "        throw new CrestPreconditionRequiredException(\"" + entityName + "\", id);");
            Line(builder, string.Empty);
            Line(builder, NestedIndent + "    // Load the entity so ownership validation and the pre-delete hook run before the stamp check.");
            Line(builder, NestedIndent + "    var entity = await Repository.GetAsync(id, cancellationToken);");
            Line(builder, NestedIndent + "    if (entity == null)");
            Line(builder, NestedIndent + "        throw new CrestEntityNotFoundException(\"" + entityName + "\", id);");
            Line(builder, string.Empty);
            Line(builder, NestedIndent + "    await ValidateDataOwnershipAsync(entity);");
            Line(builder, NestedIndent + "    await OnDeletingAsync(entity, cancellationToken);");
            Line(builder, string.Empty);
            Line(builder, NestedIndent + "    await Repository.DeleteAsync(id, expectedStamp!, cancellationToken);");
            Line(builder, NestedIndent + "    await OnDeletedAsync(id, cancellationToken);");
        }
        else
        {
            Line(builder, NestedIndent + "    var entity = await Repository.GetAsync(id, cancellationToken);");
            Line(builder, NestedIndent + "    if (entity == null)");
            Line(builder, NestedIndent + "        throw new CrestEntityNotFoundException(\"" + entityName + "\", id);");
            Line(builder, string.Empty);
            Line(builder, NestedIndent + "    await ValidateDataOwnershipAsync(entity);");
            Line(builder, NestedIndent + "    await OnDeletingAsync(entity, cancellationToken);");
            Line(builder, NestedIndent + "    await Repository.DeleteAsync(entity, cancellationToken);");
            Line(builder, NestedIndent + "    await OnDeletedAsync(id, cancellationToken);");
        }

        Line(builder, NestedIndent + "}");

        // Extension hooks
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "#region Extension Hooks");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "protected virtual Task ValidateCreateAsync("
            + createDto + " input, CancellationToken cancellationToken = default) => Task.CompletedTask;");
        Line(builder, NestedIndent + "protected virtual Task ValidateUpdateAsync("
            + idType + " id, " + updateDto + " input, CancellationToken cancellationToken = default) => Task.CompletedTask;");
        Line(builder, NestedIndent + "protected virtual Task OnCreatingAsync("
            + entity + " entity, " + createDto + " input, CancellationToken cancellationToken = default) => Task.CompletedTask;");
        Line(builder, NestedIndent + "protected virtual Task OnCreatedAsync("
            + entity + " entity, CancellationToken cancellationToken = default) => Task.CompletedTask;");
        Line(builder, NestedIndent + "protected virtual Task OnUpdatingAsync("
            + entity + " entity, " + updateDto + " input, CancellationToken cancellationToken = default) => Task.CompletedTask;");
        Line(builder, NestedIndent + "protected virtual Task OnUpdatedAsync("
            + entity + " entity, CancellationToken cancellationToken = default) => Task.CompletedTask;");
        Line(builder, NestedIndent + "protected virtual Task OnDeletingAsync("
            + entity + " entity, CancellationToken cancellationToken = default) => Task.CompletedTask;");
        Line(builder, NestedIndent + "protected virtual Task OnDeletedAsync("
            + idType + " id, CancellationToken cancellationToken = default) => Task.CompletedTask;");
        Line(builder, NestedIndent + "protected virtual Task<IQueryable<" + entity + ">> ConfigureListQueryAsync("
            + "IQueryable<" + entity + "> query, " + listRequest + " input, CancellationToken cancellationToken = default) "
            + "=> Task.FromResult(query);");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "#endregion");

        Line(builder, Indent + "}");
        CloseNamespace(builder);
        return Artifact(service + ".g.cs", builder);
    }

    private static void EmitPermissionCheckHelper(StringBuilder builder)
    {
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "protected virtual Task CheckPermissionAsync("
            + "string permissionName, CancellationToken cancellationToken = default)");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + "    return PermissionChecker.CheckAsync(permissionName);");
        Line(builder, NestedIndent + "}");
    }

    private static void EmitDataPermissionHelper(StringBuilder builder, string entity)
    {
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "protected virtual async Task<IQueryable<" + entity + ">> ApplyDataPermissionFilterAsync("
            + "IQueryable<" + entity + "> query)");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + "    return await DataPermissionFilter.ApplyFilterAsync(query);");
        Line(builder, NestedIndent + "}");
    }

    private static void EmitAuditHelpers(StringBuilder builder, CrudGenerationModel model, string entity)
    {
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "protected virtual Task SetCreationAuditPropertiesAsync(" + entity + " entity)");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + "    if (entity is IMustHaveTenant mustHaveTenant)");
        Line(builder, NestedIndent + "        mustHaveTenant.TenantId = CurrentUser.TenantId ?? throw new InvalidOperationException(\"当前用户没有关联租户\");");
        if (model.EntityHasTenantId)
        {
            Line(builder, NestedIndent + "    if (entity is " + MultiTenantInterface + " multiTenant)");
            Line(builder, NestedIndent + "        multiTenant.TenantId = CurrentUser.TenantId ?? throw new InvalidOperationException(\"当前用户没有关联租户\");");
        }

        Line(builder, NestedIndent + "    var creatorId = Guid.TryParse(CurrentUser.Id, out var userId) ? userId : (Guid?)null;");
        Line(builder, NestedIndent + "    if (entity is IHasCreator hasCreator)");
        Line(builder, NestedIndent + "        hasCreator.CreatorId = creatorId;");
        Line(builder, NestedIndent + "    if (entity is IAuditedEntity audited)");
        Line(builder, NestedIndent + "    {");
        Line(builder, NestedIndent + "        audited.CreationTime = DateTime.UtcNow;");
        Line(builder, NestedIndent + "        audited.CreatorId = creatorId;");
        Line(builder, NestedIndent + "    }");
        Line(builder, NestedIndent + "    return Task.CompletedTask;");
        Line(builder, NestedIndent + "}");

        Line(builder, string.Empty);
        Line(builder, NestedIndent + "protected virtual Task SetModificationAuditPropertiesAsync(" + entity + " entity)");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + "    if (entity is IAuditedEntity audited)");
        Line(builder, NestedIndent + "    {");
        Line(builder, NestedIndent + "        audited.LastModificationTime = DateTime.UtcNow;");
        Line(builder, NestedIndent + "        audited.LastModifierId = Guid.TryParse(CurrentUser.Id, out var userId) ? userId : (Guid?)null;");
        Line(builder, NestedIndent + "    }");
        Line(builder, NestedIndent + "    return Task.CompletedTask;");
        Line(builder, NestedIndent + "}");
    }

    private static void EmitOwnershipHelper(StringBuilder builder, CrudGenerationModel model)
    {
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "protected virtual Task ValidateDataOwnershipAsync("
            + model.EntityQualifiedDisplay + " entity)");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + "    if (entity is IMustHaveTenant mustHaveTenant && mustHaveTenant.TenantId != CurrentUser.TenantId)");
        Line(builder, NestedIndent + "        throw new UnauthorizedAccessException(\"您没有权限访问此数据：租户不匹配\");");
        if (model.EntityHasTenantId)
        {
            Line(builder, NestedIndent + "    if (entity is " + MultiTenantInterface
                + " multiTenant && multiTenant.TenantId != CurrentUser.TenantId)");
            Line(builder, NestedIndent + "        throw new UnauthorizedAccessException(\"您没有权限访问此数据：租户不匹配\");");
        }

        Line(builder, NestedIndent + "    return Task.CompletedTask;");
        Line(builder, NestedIndent + "}");
    }

    private static void EmitFieldGuards(StringBuilder builder, string entityName)
    {
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "private static void EnsureAllowedFilterFields(IEnumerable<FilterDescriptor>? filters)");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + "    if (filters == null)");
        Line(builder, NestedIndent + "        return;");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    foreach (var filter in filters)");
        Line(builder, NestedIndent + "    {");
        Line(builder, NestedIndent + "        if (!AllowedQueryFields.Contains(filter.Field))");
        Line(builder, NestedIndent + "            throw new CrestBusinessException(\"Crest.Crud.InvalidFilterField\", \""
            + entityName + "\", filter.Field);");
        Line(builder, NestedIndent + "    }");
        Line(builder, NestedIndent + "}");

        Line(builder, string.Empty);
        Line(builder, NestedIndent + "private static void EnsureAllowedSortFields(IEnumerable<SortDescriptor>? sorts)");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + "    if (sorts == null)");
        Line(builder, NestedIndent + "        return;");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "    foreach (var sort in sorts)");
        Line(builder, NestedIndent + "    {");
        Line(builder, NestedIndent + "        if (!AllowedQueryFields.Contains(sort.Field))");
        Line(builder, NestedIndent + "            throw new CrestBusinessException(\"Crest.Crud.InvalidSortField\", \""
            + entityName + "\", sort.Field);");
        Line(builder, NestedIndent + "    }");
        Line(builder, NestedIndent + "}");
    }

    // ---- 6. capability input envelopes ---------------------------------------------------------

    private static CrudArtifact EmitCapabilityInputEnvelope(CrudGenerationModel model, CrudActionModel action)
    {
        var name = model.EnvelopeName(action.Name);
        var builder = StartDocument(model);
        OpenNamespace(builder, model.EnvelopeNamespace);
        Line(builder, Indent + "internal sealed class " + name);
        Line(builder, Indent + "{");
        Line(builder, NestedIndent + "public " + model.IdTypeDisplay + " Id { get; init; }");
        if (string.Equals(action.Name, "Update", StringComparison.Ordinal))
        {
            Line(builder, NestedIndent + "public "
                + Qualified(action.BodyPayloadFullName ?? model.UpdateDtoFullName)
                + " Input { get; init; } = default!;");
        }
        else
        {
            Line(builder, NestedIndent + "public string? IfMatch { get; init; }");
        }

        Line(builder, Indent + "}");
        CloseNamespace(builder);
        return Artifact(name + ".g.cs", builder);
    }

    // ---- shared shape helpers ------------------------------------------------------------------

    /// <summary>
    /// The permission reference the service body uses. With Core.Abstractions it is the typed
    /// <c>PermissionName</c> the permissions artifact declares; without that assembly the typed property
    /// is not emitted, so the same model-owned name travels as its literal string value instead.
    /// </summary>
    private static string Permission(CrudGenerationModel model, string actionName)
    {
        var typed = Qualified(model.PermissionsNamespace + "." + model.PermissionsClassName) + "." + actionName;
        if (model.SupportsPermissionNames)
        {
            return typed;
        }

        var action = model.Actions.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, actionName, StringComparison.Ordinal));
        return action is null
            ? typed
            : "\"" + action.PermissionName + "\"";
    }

    private static string DefaultSortField(CrudGenerationModel model) =>
        model.QueryableFields.Contains("CreationTime", StringComparer.Ordinal) ? "CreationTime" : "Id";

    private static IEnumerable<CrudActionModel> OrderedActions(CrudGenerationModel model) =>
        PermissionActionOrder
            .Select(name => model.Actions.FirstOrDefault(action =>
                string.Equals(action.Name, name, StringComparison.Ordinal)))
            .Where(action => action is not null)!;

    private static string Qualified(string fullName) => "global::" + fullName;

    private static StringBuilder StartDocument(CrudGenerationModel model)
    {
        var builder = new StringBuilder();
        Line(builder, "// <auto-generated />");
        Line(builder, "// CrestCreates CRUD generated surface. Owner: " + model.OwnerAssemblyName
            + "|" + model.ServiceFullName + ". Model: " + CrudGenerationModel.ModelSchemaVersion + ". Do not edit.");
        Line(builder, "#nullable enable");
        Line(builder, string.Empty);
        return builder;
    }

    private static void OpenNamespace(StringBuilder builder, string namespaceName, IEnumerable<string>? usings = null)
    {
        Line(builder, "namespace " + namespaceName);
        Line(builder, "{");
        if (usings is not null)
        {
            foreach (var usingStatement in usings)
            {
                Line(builder, Indent + usingStatement);
            }

            Line(builder, string.Empty);
        }
    }

    private static void CloseNamespace(StringBuilder builder) => Line(builder, "}");

    private static void Line(StringBuilder builder, string text)
    {
        builder.Append(text);
        builder.Append('\n');
    }

    private static CrudArtifact Artifact(string hintName, StringBuilder builder)
    {
        var source = builder.ToString();
        return new CrudArtifact(hintName, hintName, source);
    }
}
