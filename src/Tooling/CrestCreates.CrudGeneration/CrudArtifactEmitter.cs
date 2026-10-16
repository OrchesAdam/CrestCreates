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
/// One artifact of an assembly bundle together with the owner identity the outputs manifest records for
/// it. Per-service artifacts carry the declaring service full name; the assembly-level activation
/// artifact is owned by the assembly (see <see cref="CrudArtifactEmitter.AssemblyActivationOwnerKey"/>).
/// </summary>
public sealed record CrudOwnedArtifact(string OwnerServiceFullName, CrudArtifact Artifact);

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

    private const string TripleIndent = NestedIndent + Indent;

    private const string QuadIndent = TripleIndent + Indent;

    private const string QuintIndent = QuadIndent + Indent;

    private const string SextIndent = QuintIndent + Indent;

    private const string ObjectMappingNamespace = "CrestCreates.Domain.Shared.ObjectMapping";

    private const string PermissionNameNamespace = "CrestCreates.Core.Abstractions.Identity";

    /// <summary>
    /// The shared model recognizes this exact tenant interface. Emit its fully-qualified identity so
    /// consumer namespace imports cannot change the generated tenant boundary.
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

        // §7.2: the ExplicitHost projection of this service — descriptors, typed bindings, invokers and
        // typed result envelopes. One concrete contribution type per service.
        artifacts.Add(EmitCapabilityContribution(model));

        return artifacts;
    }

    /// <summary>
    /// Every artifact of one assembly bundle: the per-model artifacts followed by the single
    /// assembly-level activation artifact. The precompile stage and the JSON reconciliation both consume
    /// this one list, so the outputs manifest and the JSON-side ownership check cannot disagree.
    /// </summary>
    public static IReadOnlyList<CrudOwnedArtifact> EmitBundleArtifacts(
        IReadOnlyList<CrudGenerationModel> models)
    {
        if (models is null)
        {
            throw new ArgumentNullException(nameof(models));
        }

        // Canonical order, so the bundle text, the outputs manifest and the JSON-side reconciliation
        // observe the same bytes regardless of the order the caller discovered the declarations in.
        var ordered = CanonicalOrder(models);
        var artifacts = new List<CrudOwnedArtifact>();
        foreach (var model in ordered)
        {
            foreach (var artifact in Emit(model))
            {
                artifacts.Add(new CrudOwnedArtifact(model.ServiceFullName, artifact));
            }
        }

        if (ordered.Count > 0)
        {
            artifacts.Add(new CrudOwnedArtifact(AssemblyActivationOwnerKey, EmitHostActivation(ordered)));
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

    /// <summary>
    /// The plain fully-qualified type names one artifact declares, read back from the emitted source so
    /// the outputs manifest records what the producer actually declared rather than a name the emitter
    /// merely intended. The owner identity (assembly + service full name) is a sibling field of the
    /// manifest entry, never part of these names: §5.1 keeps ownership out of the type identity so a
    /// consumer can ask one question — "is this exact type already owned?" — by fully-qualified name.
    /// This is the only place the rule exists; the precompile stage and the ownership tests read it here.
    /// </summary>
    public static IReadOnlyList<string> DeclaredTypeFullNames(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return Array.Empty<string>();
        }

        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source);
        return tree.GetRoot()
            .DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>()
            .Select(declaration =>
            {
                var namespaceName = declaration.Ancestors()
                    .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.NamespaceDeclarationSyntax>()
                    .LastOrDefault()?.Name?.ToString();
                return string.IsNullOrEmpty(namespaceName)
                    ? declaration.Identifier.ValueText
                    : namespaceName + "." + declaration.Identifier.ValueText;
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();
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
        Line(builder, NestedIndent + "public " + model.IdTypeDisplay + " "
            + CrudEnvelopeConvention.RouteIdentityMember + " { get; init; }");
        if (action.BodyPayloadFullName is not null)
        {
            Line(builder, NestedIndent + "public " + Qualified(action.BodyPayloadFullName) + " "
                + CrudEnvelopeConvention.BodyMember + " { get; init; } = default!;");
        }

        foreach (var header in action.HeaderInputs)
        {
            Line(builder, NestedIndent + "public string? " + header.MemberName + " { get; init; }");
        }

        Line(builder, Indent + "}");
        CloseNamespace(builder);
        return Artifact(name + ".g.cs", builder);
    }

    // ---- 7. ExplicitHost capability contribution (§5.2 / §7.2 / §7.4) --------------------------

    /// <summary>Owner key the outputs manifest records for the one assembly-level activation artifact.</summary>
    public const string AssemblyActivationOwnerKey = "<assembly>";

    /// <summary>
    /// The one assembly-level activation artifact: a public <c>AddGeneratedCrud(this IServiceCollection)</c>
    /// extension in the assembly-qualified generated namespace (§5.2). It registers every contributed
    /// service — the scoped AppService (interface and concrete resolving to the same instance) and the
    /// ExplicitHost contribution — through <c>AddCrestCapabilityEndpointContribution</c>. Emitted once per
    /// bundle, never once per model; repeated calls are idempotent by construction and nothing is written
    /// to process-global state.
    /// </summary>
    public static CrudArtifact EmitHostActivation(IReadOnlyList<CrudGenerationModel> models)
    {
        if (models is null)
        {
            throw new ArgumentNullException(nameof(models));
        }

        if (models.Count == 0)
        {
            throw new ArgumentException(
                "The assembly activation artifact requires at least one CRUD model.", nameof(models));
        }

        // Canonical order: the emitted text must be a pure function of the model set, not of the order
        // the caller happened to discover the declarations in (the JSON stage reconciles by content hash).
        models = CanonicalOrder(models);
        var owner = models[0].OwnerAssemblyName;
        foreach (var model in models)
        {
            if (!string.Equals(model.OwnerAssemblyName, owner, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "One activation artifact covers exactly one owner assembly; got '" + owner
                    + "' and '" + model.OwnerAssemblyName + "'.");
            }
        }

        var activationNamespace = models[0].ActivationNamespace;
        var builder = new StringBuilder();
        Line(builder, "// <auto-generated />");
        Line(builder, "// CrestCreates CRUD generated activation. Owner: " + owner
            + "|" + AssemblyActivationOwnerKey + ". Model: " + CrudGenerationModel.ModelSchemaVersion
            + ". Do not edit.");
        Line(builder, "#nullable enable");
        Line(builder, string.Empty);
        OpenNamespace(builder, activationNamespace,
        [
            "using System;",
            "using Microsoft.Extensions.DependencyInjection;",
            "using Microsoft.Extensions.DependencyInjection.Extensions;",
        ]);

        Line(builder, Indent + "/// <summary>");
        Line(builder, Indent + "/// #128 ExplicitHost activation for the generated CRUD surface of assembly '"
            + owner + "'.");
        Line(builder, Indent + "/// The host calls AddGeneratedCrud for exactly the assemblies it activates; the call is");
        Line(builder, Indent + "/// idempotent by construction (TryAddScoped + TryAddEnumerable) and never writes process-global");
        Line(builder, Indent + "/// registries. Generated code is immutable data and never captures an IServiceProvider.");
        Line(builder, Indent + "/// </summary>");
        Line(builder, Indent + "public static class " + CrudActivationConvention.ActivationTypeName);
        Line(builder, Indent + "{");
        Line(builder, NestedIndent + "/// <summary>Registers the CRUD AppServices and capability contributions of this assembly.</summary>");
        Line(builder, NestedIndent + "public static IServiceCollection "
            + CrudActivationConvention.ActivationMethodName + "(this IServiceCollection services)");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + Indent + "ArgumentNullException.ThrowIfNull(services);");

        foreach (var model in models)
        {
            var implementation = Qualified(model.ImplementationNamespace + "." + model.ServiceClassName);
            var contract = Qualified(model.InterfaceNamespace + "." + model.ServiceInterfaceName);
            Line(builder, string.Empty);
            Line(builder, NestedIndent + Indent + "// " + model.ServiceFullName
                + ": one scoped instance serves both the interface and the concrete type.");
            Line(builder, NestedIndent + Indent + "services.TryAddScoped<" + implementation + ">();");
            Line(builder, NestedIndent + Indent + "services.TryAddScoped<" + contract + ">(");
            Line(builder, NestedIndent + Indent + Indent + "serviceProvider => serviceProvider.GetRequiredService<"
                + implementation + ">());");
            Line(builder, NestedIndent + Indent
                + "global::CrestCreates.DynamicApi.CapabilityEndpointExtensions.AddCrestCapabilityEndpointContribution(");
            Line(builder, NestedIndent + Indent + Indent + "services,");
            Line(builder, NestedIndent + Indent + Indent + "new " + Qualified(model.ContributionFullName) + "());");
        }

        Line(builder, string.Empty);
        Line(builder, NestedIndent + Indent + "return services;");
        Line(builder, NestedIndent + "}");
        Line(builder, Indent + "}");
        CloseNamespace(builder);
        return Artifact(CrudActivationConvention.ActivationTypeName + ".g.cs", builder);
    }

    /// <summary>
    /// The per-service ExplicitHost contribution: descriptors for the five actions, their typed input
    /// bindings, handler invokers and typed result envelopes, the body types the host must serialize and
    /// the required registry identities. Everything the runtime needs is baked into this immutable type;
    /// no module initializer, no static registry write and no IServiceProvider capture.
    /// </summary>
    private static CrudArtifact EmitCapabilityContribution(CrudGenerationModel model)
    {
        var contribution = model.ContributionTypeName;
        var builder = StartDocument(model);
        OpenNamespace(builder, model.ActivationNamespace);

        Line(builder, Indent + "/// <summary>");
        Line(builder, Indent + "/// ExplicitHost contribution of " + model.ServiceFullName + " (owner "
            + model.OwnerAssemblyName + "): the five CRUD actions of " + model.EntityClassName
            + " with their capability/endpoint descriptors, typed input bindings, invokers and typed");
        Line(builder, Indent + "/// result envelopes. Activated per host by AddGeneratedCrud.");
        Line(builder, Indent + "/// </summary>");
        Line(builder, Indent + "internal sealed class " + contribution
            + " : global::CrestCreates.DynamicApi.CapabilityEndpointContribution");
        Line(builder, Indent + "{");
        Line(builder, NestedIndent + "public " + contribution + "()");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + Indent + "OwnerAssemblyName = \"" + Escape(model.OwnerAssemblyName) + "\";");
        EmitContributionCapabilities(builder, model);
        EmitContributionEndpoints(builder, model);
        EmitContributionBindingContracts(builder, model);
        EmitContributionHandlerBindings(builder, model);
        EmitContributionResultBindings(builder, model);
        EmitContributionBodyTypes(builder, model);
        EmitContributionRequiredIdentities(builder, model);
        Line(builder, NestedIndent + "}");
        Line(builder, string.Empty);

        foreach (var action in model.Actions)
        {
            EmitContributionBindingMethod(builder, model, action);
            EmitContributionResultMapper(builder, model, action);
        }

        Line(builder, Indent + "}");
        Line(builder, string.Empty);

        foreach (var action in model.Actions)
        {
            EmitContributionInvoker(builder, model, action);
        }

        CloseNamespace(builder);
        return Artifact(contribution + ".g.cs", builder);
    }

    private static void EmitContributionCapabilities(StringBuilder builder, CrudGenerationModel model)
    {
        var kind = "global::CrestCreates.Metadata.Abstractions.DescriptorCapability.";
        Line(builder, NestedIndent + Indent + "CapabilityDescriptors = new global::CrestCreates.Metadata.CapabilityDescriptor[]");
        Line(builder, NestedIndent + Indent + "{");
        foreach (var action in model.Actions)
        {
            Line(builder, QuadIndent + "new global::CrestCreates.Metadata.CapabilityDescriptor");
            Line(builder, QuadIndent + "{");
            Line(builder, QuintIndent + "Namespace = \"capability\",");
            Line(builder, QuintIndent + "Id = \"" + Escape(action.CapabilityId) + "\",");
            Line(builder, QuintIndent + "Name = \"" + Escape(action.Name) + "\",");
            Line(builder, QuintIndent + "State = global::CrestCreates.Metadata.Abstractions.DescriptorState.Active,");
            Line(builder, QuintIndent + "Version = 1,");
            Line(builder, QuintIndent + "CapabilityKind = " + kind
                + (action.OperationKind == CrudOperationKind.Query ? "CapabilityKind.Query," : "CapabilityKind.Command,"));
            Line(builder, QuintIndent + "Permissions = new[] { \"" + Escape(action.PermissionName) + "\" },");
            Line(builder, QuintIndent + "RiskLevel = " + kind + "CapabilityRiskLevel.Medium,");
            Line(builder, QuintIndent + "InputSchema = null,");
            Line(builder, QuintIndent + "OutputSchema = null,");
            Line(builder, QuintIndent + "ProjectionKind = " + kind + "CapabilityProjectionKind.AppServiceCompatibility,");
            Line(builder, QuadIndent + "},");
        }

        Line(builder, NestedIndent + Indent + "};");
    }

    private static void EmitContributionEndpoints(StringBuilder builder, CrudGenerationModel model)
    {
        Line(builder, NestedIndent + Indent + "EndpointDescriptors = new global::CrestCreates.DynamicApi.CapabilityEndpointDescriptor[]");
        Line(builder, NestedIndent + Indent + "{");
        foreach (var action in model.Actions)
        {
            Line(builder, QuadIndent + "new global::CrestCreates.DynamicApi.CapabilityEndpointDescriptor");
            Line(builder, QuadIndent + "{");
            Line(builder, QuintIndent + "Id = \"" + Escape(action.EndpointId) + "\",");
            Line(builder, QuintIndent + "Name = \"" + Escape(action.Name) + "\",");
            Line(builder, QuintIndent + "Version = 1,");
            Line(builder, QuintIndent + "State = global::CrestCreates.Metadata.Abstractions.DescriptorState.Active,");
            Line(builder, QuintIndent + "Capability = new global::CrestCreates.Metadata.Abstractions.VersionedDescriptorRef<global::CrestCreates.Metadata.CapabilityDescriptor>(\""
                + Escape(action.CapabilityId) + "\", 1),");
            Line(builder, QuintIndent + "HttpMethod = global::CrestCreates.DynamicApi.CapabilityEndpointHttpMethod."
                + HttpMethodEnum(action.HttpMethod) + ",");
            Line(builder, QuintIndent + "RoutePattern = \"" + Escape(action.RoutePattern) + "\",");
            Line(builder, QuintIndent + "AuthorizationMode = global::CrestCreates.DynamicApi.CapabilityEndpointAuthorizationMode.InheritCapability,");
            Line(builder, QuintIndent + "InputBindings = new global::CrestCreates.DynamicApi.CapabilityEndpointInputBinding[]");
            Line(builder, QuintIndent + "{");
            foreach (var input in InputBindingsOf(action, model))
            {
                Line(builder, SextIndent + "new global::CrestCreates.DynamicApi.CapabilityEndpointInputBinding");
                Line(builder, SextIndent + "{");
                Line(builder, SextIndent + Indent + "Name = \"" + Escape(input.Name) + "\",");
                Line(builder, SextIndent + Indent + "Source = global::CrestCreates.DynamicApi.CapabilityEndpointParameterSource."
                    + input.Source + ",");
                Line(builder, SextIndent + Indent + "Required = " + (input.Required ? "true" : "false") + ",");
                Line(builder, SextIndent + "},");
            }

            Line(builder, QuintIndent + "},");
            Line(builder, QuintIndent + "OutputMapping = new global::CrestCreates.DynamicApi.CapabilityEndpointOutputMapping { SuccessStatusCode = 200 },");
            Line(builder, QuintIndent + "Projection = new global::CrestCreates.DynamicApi.CapabilityEndpointProjectionMetadata");
            Line(builder, QuintIndent + "{");
            Line(builder, QuintIndent + Indent + "OperationId = \"" + Escape(action.CapabilityId.Replace('.', '_')) + "\",");
            Line(builder, QuintIndent + Indent + "Tags = new[] { \"" + Escape(model.ServiceClassName) + "\" },");
            Line(builder, QuintIndent + "},");
            Line(builder, QuadIndent + "},");
        }

        Line(builder, NestedIndent + Indent + "};");
    }

    private static void EmitContributionBindingContracts(StringBuilder builder, CrudGenerationModel model)
    {
        Line(builder, NestedIndent + Indent + "Bindings = new global::CrestCreates.DynamicApi.CapabilityEndpointBindingContract[]");
        Line(builder, NestedIndent + Indent + "{");
        foreach (var action in model.Actions)
        {
            Line(builder, QuadIndent + "new global::CrestCreates.DynamicApi.CapabilityEndpointBindingContract(");
            Line(builder, QuintIndent + "\"" + Escape(action.EndpointId) + "\",");
            Line(builder, QuintIndent + "1,");
            Line(builder, QuintIndent + BindingMethodName(action) + "),");
        }

        Line(builder, NestedIndent + Indent + "};");
    }

    private static void EmitContributionHandlerBindings(StringBuilder builder, CrudGenerationModel model)
    {
        Line(builder, NestedIndent + Indent + "Handlers = new global::CrestCreates.DynamicApi.CapabilityEndpointHandlerBinding[]");
        Line(builder, NestedIndent + Indent + "{");
        foreach (var action in model.Actions)
        {
            Line(builder, QuadIndent + "new global::CrestCreates.DynamicApi.CapabilityEndpointHandlerBinding(");
            Line(builder, QuintIndent + "\"" + Escape(action.CapabilityId) + "\",");
            Line(builder, QuintIndent + "new " + Qualified(InvokerTypeName(model, action)) + "()),");
        }

        Line(builder, NestedIndent + Indent + "};");
    }

    private static void EmitContributionResultBindings(StringBuilder builder, CrudGenerationModel model)
    {
        Line(builder, NestedIndent + Indent + "ResultMappers = new global::CrestCreates.DynamicApi.CapabilityEndpointResultBinding[]");
        Line(builder, NestedIndent + Indent + "{");
        foreach (var action in model.Actions)
        {
            Line(builder, QuadIndent + "new global::CrestCreates.DynamicApi.CapabilityEndpointResultBinding(");
            Line(builder, QuintIndent + "\"" + Escape(action.EndpointId) + "\",");
            Line(builder, QuintIndent + "1,");
            Line(builder, QuintIndent + ResultMapperName(action) + "),");
        }

        Line(builder, NestedIndent + Indent + "};");
    }

    private static void EmitContributionBodyTypes(StringBuilder builder, CrudGenerationModel model)
    {
        var bodyTypes = new List<string>();
        foreach (var action in model.Actions)
        {
            if (action.BodyPayloadFullName is not null
                && !bodyTypes.Contains(action.BodyPayloadFullName, StringComparer.Ordinal))
            {
                bodyTypes.Add(action.BodyPayloadFullName);
            }

            if (action.UsesCapabilityInputEnvelope
                && !bodyTypes.Contains(model.EnvelopeFullName(action.Name), StringComparer.Ordinal))
            {
                bodyTypes.Add(model.EnvelopeFullName(action.Name));
            }
        }

        Line(builder, NestedIndent + Indent + "BodyTypes = new global::System.Type[]");
        Line(builder, NestedIndent + Indent + "{");
        foreach (var bodyType in bodyTypes)
        {
            Line(builder, QuadIndent + "typeof(" + Qualified(bodyType) + "),");
        }
        Line(builder, NestedIndent + Indent + "};");
    }

    private static void EmitContributionRequiredIdentities(StringBuilder builder, CrudGenerationModel model)
    {
        Line(builder, NestedIndent + Indent + "RequiredIdentityIds = new global::CrestCreates.DynamicApi.CapabilityEndpointRequiredIdentity[]");
        Line(builder, NestedIndent + Indent + "{");
        foreach (var action in model.Actions)
        {
            Line(builder, QuadIndent + "new global::CrestCreates.DynamicApi.CapabilityEndpointRequiredIdentity(");
            Line(builder, QuintIndent + "\"" + Escape(action.EndpointId) + "\",");
            Line(builder, QuintIndent + "\"" + Escape(model.OwnerAssemblyName) + "\",");
            Line(builder, QuintIndent + "\"" + Escape(action.Name) + "\",");
            Line(builder, QuintIndent + "\"" + Escape(model.EntityClassName) + "\",");
            Line(builder, QuintIndent + "1),");
        }

        Line(builder, NestedIndent + Indent + "};");
    }

    /// <summary>
    /// §7.3/§7.4 input binding per action, all through the typed, no-fallback body reader: required
    /// Create/Search bodies throw 400 on empty/whitespace/null/bad JSON before any business call, the
    /// route identity parses statically, and the composed envelopes carry the route id and the If-Match
    /// header.
    /// </summary>
    private static void EmitContributionBindingMethod(
        StringBuilder builder,
        CrudGenerationModel model,
        CrudActionModel action)
    {
        var readsBody = action.BodyPayloadFullName is not null;
        Line(builder, NestedIndent + "private static "
            + (readsBody ? "async " : string.Empty)
            + "global::System.Threading.Tasks.ValueTask<object?> " + BindingMethodName(action) + "(");
        Line(builder, NestedIndent + Indent + "global::Microsoft.AspNetCore.Http.HttpContext context,");
        Line(builder, NestedIndent + Indent + "global::System.Threading.CancellationToken ct)");
        Line(builder, NestedIndent + "{");

        if (action.RouteIdentityParameter is not null)
        {
            EmitRouteIdentityParse(builder, model, action);
        }

        if (readsBody && !action.UsesCapabilityInputEnvelope)
        {
            // Create / Search: the required body is the capability input itself.
            Line(builder, NestedIndent + Indent + "var jsonTypeInfo = global::CrestCreates.DynamicApi.CapabilityEndpointJsonTypeInfoResolver.Require<"
                + Qualified(action.BodyPayloadFullName!) + ">(context.RequestServices);");
            Line(builder, NestedIndent + Indent + "return await global::CrestCreates.DynamicApi.CapabilityEndpointBodyReader.ReadNativeBodyAsync(");
            Line(builder, NestedIndent + Indent + Indent + "context, jsonTypeInfo, optional: false, ct);");
        }
        else if (readsBody)
        {
            // Update: route id + required body composed into the capability input envelope. The method
            // already awaits the body read, so the envelope is returned directly.
            Line(builder, NestedIndent + Indent + "var jsonTypeInfo = global::CrestCreates.DynamicApi.CapabilityEndpointJsonTypeInfoResolver.Require<"
                + Qualified(action.BodyPayloadFullName!) + ">(context.RequestServices);");
            Line(builder, NestedIndent + Indent + "var body = await global::CrestCreates.DynamicApi.CapabilityEndpointBodyReader.ReadNativeBodyAsync(");
            Line(builder, NestedIndent + Indent + Indent + "context, jsonTypeInfo, optional: false, ct);");
            Line(builder, string.Empty);
            EmitEnvelopeConstruction(builder, model, action, "body!", returnDirectly: true);
        }
        else if (action.HeaderInputs.Count == 0)
        {
            // GetById: the route identity is the whole input. The method has nothing to await, so the
            // parse result is wrapped explicitly instead of relying on an async state machine.
            Line(builder, NestedIndent + Indent
                + "return global::System.Threading.Tasks.ValueTask.FromResult<object?>(id);");
        }
        else
        {
            // Delete: route id + the optional If-Match concurrency header. Nothing is awaited, so the
            // envelope is built first and then wrapped in a completed ValueTask.
            EmitEnvelopeConstruction(builder, model, action, bodyExpression: null, returnDirectly: false);
        }

        Line(builder, NestedIndent + "}");
        Line(builder, string.Empty);
    }

    private static void EmitRouteIdentityParse(
        StringBuilder builder,
        CrudGenerationModel model,
        CrudActionModel action)
    {
        var routeParameter = action.RouteIdentityParameter!;
        Line(builder, NestedIndent + Indent + "var routeId = context.Request.RouteValues[\""
            + Escape(routeParameter) + "\"]?.ToString();");
        switch (model.IdWireKind)
        {
            case CrudIdWireKind.String:
                Line(builder, NestedIndent + Indent + "if (routeId is null)");
                Line(builder, NestedIndent + Indent + "{");
                Line(builder, NestedIndent + Indent + Indent + "throw new global::Microsoft.AspNetCore.Http.BadHttpRequestException(");
                Line(builder, NestedIndent + Indent + Indent + Indent + "\"The route value '" + Escape(routeParameter) + "' is required.\");");
                Line(builder, NestedIndent + Indent + "}");
                Line(builder, string.Empty);
                Line(builder, NestedIndent + Indent + "var id = routeId;");
                break;
            default:
                Line(builder, NestedIndent + Indent + "if (routeId is null || !"
                    + IdTryParseExpression(model) + ")");
                Line(builder, NestedIndent + Indent + "{");
                Line(builder, NestedIndent + Indent + Indent + "throw new global::Microsoft.AspNetCore.Http.BadHttpRequestException(");
                Line(builder, NestedIndent + Indent + Indent + Indent + "\"The route value '" + Escape(routeParameter)
                    + "' must be a valid " + Escape(MessageTypeName(model.IdTypeDisplay)) + ".\");");
                Line(builder, NestedIndent + Indent + "}");
                Line(builder, string.Empty);
                break;
        }
    }

    /// <summary>The readable type name for a diagnostic message (drops the emitted <c>global::</c> prefix).</summary>
    private static string MessageTypeName(string typeDisplay) =>
        typeDisplay.StartsWith("global::", StringComparison.Ordinal)
            ? typeDisplay.Substring("global::".Length)
            : typeDisplay;

    private static void EmitEnvelopeConstruction(
        StringBuilder builder,
        CrudGenerationModel model,
        CrudActionModel action,
        string? bodyExpression,
        bool returnDirectly)
    {
        // `returnDirectly` is the awaited (async) binding path; otherwise the caller wraps the built
        // envelope into a completed ValueTask itself.
        Line(builder, NestedIndent + Indent + (returnDirectly ? "return new " : "var envelope = new ")
            + Qualified(model.EnvelopeFullName(action.Name)));
        Line(builder, NestedIndent + Indent + "{");
        Line(builder, NestedIndent + Indent + Indent + CrudEnvelopeConvention.RouteIdentityMember + " = id,");
        if (bodyExpression is not null)
        {
            Line(builder, NestedIndent + Indent + Indent + CrudEnvelopeConvention.BodyMember + " = " + bodyExpression + ",");
        }

        foreach (var header in action.HeaderInputs)
        {
            Line(builder, NestedIndent + Indent + Indent + header.MemberName + " = "
                + HeaderValueExpression(header) + ",");
        }

        Line(builder, NestedIndent + Indent + "};");
        if (!returnDirectly)
        {
            Line(builder, NestedIndent + Indent
                + "return global::System.Threading.Tasks.ValueTask.FromResult<object?>(envelope);");
        }
    }

    private static string HeaderValueExpression(CrudHeaderInputModel header)
    {
        // The generated artifact carries no file-level usings, so the LINQ accessor is fully qualified
        // instead of depending on the consumer's implicit usings.
        var read = "global::System.Linq.Enumerable.FirstOrDefault(context.Request.Headers[\""
            + Escape(header.HeaderName) + "\"])";
        return header.TrimQuotes ? read + "?.Trim('\"')" : read;
    }

    /// <summary>
    /// §7.3 result envelope per action: a checked conversion from the declared static payload type into
    /// the typed, no-fallback mapper overload whose metadata comes from the host's source-generated
    /// resolver. The void/404 envelope and the GET branch are explicit; nothing is inferred from the
    /// static type of <c>ctx.Output</c>.
    /// </summary>
    private static void EmitContributionResultMapper(
        StringBuilder builder,
        CrudGenerationModel model,
        CrudActionModel action)
    {
        var payload = action.ResultPayloadFullName is null
            ? null
            : action.UsesPagedResult
                ? CrudResponseContract.PagedPayload(action.ResultPayloadFullName)
                : Qualified(action.ResultPayloadFullName);
        var require = "global::CrestCreates.DynamicApi.CapabilityEndpointJsonTypeInfoResolver.Require";
        var mapper = "global::CrestCreates.DynamicApi.CompatibilityHttpResultMapper";

        string expression;
        if (payload is null)
        {
            expression = mapper + ".WrapVoidResult(" + require
                + "<" + CrudResponseContract.VoidEnvelope + ">(serviceProvider))";
        }
        else if (string.Equals(action.HttpMethod, "GET", StringComparison.Ordinal))
        {
            // §7.3/§7.4: the typed GET overload takes the envelope metadata of the declared payload —
            // for a paged payload that is DynamicApiResponse<PagedResultDto<Dto>>, never the DTO envelope.
            expression = mapper + ".WrapGetResult<" + payload + ">("
                + "ctx.Output as " + payload + ", "
                + require + "<" + CrudResponseContract.EnvelopeOfPayload(payload) + ">(serviceProvider), "
                + require + "<" + CrudResponseContract.VoidEnvelope + ">(serviceProvider))";
        }
        else
        {
            expression = mapper + ".WrapResult<" + payload + ">("
                + "(" + payload + ")ctx.Output!, "
                + require + "<" + CrudResponseContract.EnvelopeOfPayload(payload) + ">(serviceProvider))";
        }

        Line(builder, NestedIndent + "private static object " + ResultMapperName(action) + "(");
        Line(builder, NestedIndent + Indent + "global::CrestCreates.DynamicApi.EndpointExecutionContext ctx,");
        Line(builder, NestedIndent + Indent + "global::System.IServiceProvider serviceProvider)");
        Line(builder, NestedIndent + Indent + "=> " + expression + ";");
        Line(builder, string.Empty);
    }

    /// <summary>
    /// §7.4 execution: the invoker resolves the scoped AppService from the capability execution context
    /// and awaits it with the managed request token, so permission/validation/UoW all run on the unique
    /// Capability/AppService core. The plain invoker overload is not an entry point.
    /// </summary>
    private static void EmitContributionInvoker(
        StringBuilder builder,
        CrudGenerationModel model,
        CrudActionModel action)
    {
        var contract = Qualified(model.InterfaceNamespace + "." + model.ServiceInterfaceName);
        var invocation = "service." + action.ServiceMethodName + "(";
        var arguments = new List<string>();
        string? typedInputDeclaration = null;

        if (action.RouteIdentityParameter is null)
        {
            typedInputDeclaration = "var typedInput = (" + Qualified(action.BodyPayloadFullName!) + ")context.Input!;";
            arguments.Add("typedInput");
        }
        else if (action.UsesCapabilityInputEnvelope)
        {
            typedInputDeclaration = "var envelope = (" + Qualified(model.EnvelopeFullName(action.Name)) + ")context.Input!;";
            arguments.Add("envelope." + CrudEnvelopeConvention.RouteIdentityMember);
            if (action.BodyPayloadFullName is not null)
            {
                arguments.Add("envelope." + CrudEnvelopeConvention.BodyMember);
            }

            foreach (var header in action.HeaderInputs)
            {
                arguments.Add("envelope." + header.MemberName);
            }
        }
        else
        {
            typedInputDeclaration = "var id = (" + model.IdTypeDisplay + ")context.Input!;";
            arguments.Add("id");
        }

        arguments.Add("ct");

        Line(builder, Indent + "internal sealed class " + InvokerDeclarationName(model, action)
            + " : global::CrestCreates.Capability.Abstractions.ICapabilityContextAwareHandlerInvoker");
        Line(builder, Indent + "{");
        Line(builder, NestedIndent + "public async global::System.Threading.Tasks.Task<object?> InvokeAsync(");
        Line(builder, NestedIndent + Indent + "global::CrestCreates.Capability.Abstractions.CapabilityExecutionContext context,");
        Line(builder, NestedIndent + Indent + "global::System.Threading.CancellationToken ct)");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + Indent + "var service = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<"
            + contract + ">(context.ServiceProvider);");
        Line(builder, NestedIndent + Indent + typedInputDeclaration);
        Line(builder, NestedIndent + Indent
            + (action.ResultPayloadFullName is null ? "await " : "return await ")
            + invocation + string.Join(", ", arguments) + ").ConfigureAwait(false);");
        if (action.ResultPayloadFullName is null)
        {
            Line(builder, NestedIndent + Indent + "return null;");
        }

        Line(builder, NestedIndent + "}");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "public global::System.Threading.Tasks.Task<object?> InvokeAsync(");
        Line(builder, NestedIndent + Indent + "object? input,");
        Line(builder, NestedIndent + Indent + "global::System.Threading.CancellationToken ct)");
        Line(builder, NestedIndent + Indent + "=> throw new global::System.InvalidOperationException(");
        Line(builder, NestedIndent + Indent + Indent
            + "\"The generated CRUD invoker requires a CapabilityExecutionContext; the plain ICapabilityHandlerInvoker overload is not an entry point.\");");
        Line(builder, Indent + "}");
        Line(builder, string.Empty);
    }

    private static string BindingMethodName(CrudActionModel action) => "Bind_" + action.Name + "_Async";

    private static string ResultMapperName(CrudActionModel action) => "MapResult_" + action.Name;

    private static string InvokerDeclarationName(CrudGenerationModel model, CrudActionModel action) =>
        model.ContributionTypeName + "_" + action.Name + "_Invoker";

    private static string InvokerTypeName(CrudGenerationModel model, CrudActionModel action) =>
        model.ActivationNamespace + "." + InvokerDeclarationName(model, action);

    /// <summary>The static route-id parse call for the frozen id wire kind (§7.4).</summary>
    private static string IdTryParseExpression(CrudGenerationModel model)
    {
        var core = model.IdTypeDisplay;
        var invariant = "global::System.Globalization.CultureInfo.InvariantCulture";
        var numeric = "global::System.Globalization.NumberStyles.Integer, " + invariant;
        return model.IdWireKind switch
        {
            CrudIdWireKind.Guid => "global::System.Guid.TryParse(routeId, out var id)",
            CrudIdWireKind.Int16 => "short.TryParse(routeId, " + numeric + ", out var id)",
            CrudIdWireKind.Int32 => "int.TryParse(routeId, " + numeric + ", out var id)",
            CrudIdWireKind.Int64 => "long.TryParse(routeId, " + numeric + ", out var id)",
            CrudIdWireKind.UInt16 => "ushort.TryParse(routeId, " + numeric + ", out var id)",
            CrudIdWireKind.UInt32 => "uint.TryParse(routeId, " + numeric + ", out var id)",
            CrudIdWireKind.UInt64 => "ulong.TryParse(routeId, " + numeric + ", out var id)",
            CrudIdWireKind.Byte => "byte.TryParse(routeId, " + numeric + ", out var id)",
            CrudIdWireKind.SByte => "sbyte.TryParse(routeId, " + numeric + ", out var id)",
            _ => "((global::System.Func<bool>)(() => throw new global::System.InvalidOperationException(\"Unsupported id kind " + core + "\")))()",
        };
    }

    private static string HttpMethodEnum(string httpMethod) => httpMethod switch
    {
        "GET" => "Get",
        "POST" => "Post",
        "PUT" => "Put",
        "PATCH" => "Patch",
        "DELETE" => "Delete",
        _ => "None",
    };

    private sealed record InputBindingModel(string Name, string Source, bool Required);

    private static IEnumerable<InputBindingModel> InputBindingsOf(
        CrudActionModel action,
        CrudGenerationModel model)
    {
        if (action.RouteIdentityParameter is not null)
        {
            yield return new InputBindingModel(action.RouteIdentityParameter, "Route", true);
        }

        if (action.BodyPayloadFullName is not null)
        {
            yield return new InputBindingModel("input", "Body", true);
        }

        foreach (var header in action.HeaderInputs)
        {
            yield return new InputBindingModel(header.HeaderName, "Header", header.Required);
        }
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

    private static IReadOnlyList<CrudGenerationModel> CanonicalOrder(
        IReadOnlyList<CrudGenerationModel> models) =>
        models.Count > 1
            ? models.OrderBy(model => model.ServiceFullName, StringComparer.Ordinal).ToArray()
            : models;

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

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
