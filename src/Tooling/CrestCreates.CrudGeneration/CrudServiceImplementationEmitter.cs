#nullable enable
using System.Collections.Generic;
using System.Text;
using static CrestCreates.Tooling.CrudGeneration.CrudArtifactWriter;
using static CrestCreates.Tooling.CrudGeneration.CrudPermissionEmitter;

namespace CrestCreates.Tooling.CrudGeneration;

/// <summary>
/// Service implementation artifact: the generated CRUD AppService with the permission, data-permission,
/// audit, ownership and extension-hook helpers baked into ordinary C#.
/// </summary>
internal static class CrudServiceImplementationEmitter
{
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
        "using CrestCreates.Domain.Exceptions;",
        "using CrestCreates.Domain.Repositories;",
        "using CrestCreates.Domain.Shared.DataFilter;",
        "using CrestCreates.Domain.Shared.Entities.Auditing;",
        "using CrestCreates.Domain.Shared.Exceptions;",
    ];

    /// <summary>
    /// The shared model recognizes this exact tenant interface. Emit its fully-qualified identity so
    /// consumer namespace imports cannot change the generated tenant boundary.
    /// </summary>
    private const string MultiTenantInterface = "global::CrestCreates.DataFilter.Entities.IMultiTenant";

    internal static CrudArtifact EmitServiceImplementation(CrudGenerationModel model)
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
        var queryAdapter = Qualified(model.ImplementationNamespace + "." + model.TypedQueryAdapterName);

        var builder = StartDocument(model);
        OpenNamespace(builder, model.ImplementationNamespace, ServiceImplementationUsings);
        Line(builder, Indent + "[CrestService]");
        Line(builder, Indent + "public partial class " + service + " : " + contract
            + ", global::CrestCreates.Aop.Abstractions.IServiceProviderAccessor");
        Line(builder, Indent + "{");

        Line(builder, NestedIndent + "protected readonly " + repository + " Repository;");
        Line(builder, NestedIndent + "protected readonly IPermissionChecker PermissionChecker;");
        Line(builder, NestedIndent + "protected readonly ICurrentUser CurrentUser;");
        Line(builder, NestedIndent + "protected readonly IDataPermissionFilter DataPermissionFilter;");
        Line(builder, string.Empty);
        // IServiceProviderAccessor is the intercept contract: without it the woven [UnitOfWorkMo] frame
        // cannot bind the kernel and fails closed instead of running the write unmanaged.
        Line(builder, NestedIndent + "public global::System.IServiceProvider ServiceProvider { get; }");
        Line(builder, string.Empty);
        Line(builder, NestedIndent + "public " + service + "(");
        Line(builder, NestedIndent + "    " + repository + " repository,");
        Line(builder, NestedIndent + "    IPermissionChecker permissionChecker,");
        Line(builder, NestedIndent + "    ICurrentUser currentUser,");
        Line(builder, NestedIndent + "    IDataPermissionFilter dataPermissionFilter,");
        Line(builder, NestedIndent + "    global::System.IServiceProvider serviceProvider)");
        Line(builder, NestedIndent + "{");
        Line(builder, NestedIndent + "    Repository = repository ?? throw new ArgumentNullException(nameof(repository));");
        Line(builder, NestedIndent + "    PermissionChecker = permissionChecker ?? throw new ArgumentNullException(nameof(permissionChecker));");
        Line(builder, NestedIndent + "    CurrentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));");
        Line(builder, NestedIndent + "    DataPermissionFilter = dataPermissionFilter ?? throw new ArgumentNullException(nameof(dataPermissionFilter));");
        Line(builder, NestedIndent + "    ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));");
        Line(builder, NestedIndent + "}");
        Line(builder, string.Empty);

        EmitPermissionCheckHelper(builder);
        EmitDataPermissionHelper(builder, entity);
        EmitAuditHelpers(builder, model, entity);
        EmitOwnershipHelper(builder, model);

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
        Line(builder, NestedIndent + "    var query = Repository.GetQueryable();");
        Line(builder, NestedIndent + "    query = await ApplyDataPermissionFilterAsync(query);");
        Line(builder, NestedIndent + "    query = await ConfigureListQueryAsync(query, input, cancellationToken);");
        Line(builder, string.Empty);
        // §8: the generated typed query adapter owns filter/sort planning, the stable default order and
        // the paging bounds; the reflective QueryExecutor<T> is never part of the formal CRUD search.
        Line(builder, NestedIndent + "    return " + queryAdapter + ".Execute(query, input);");
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
}
