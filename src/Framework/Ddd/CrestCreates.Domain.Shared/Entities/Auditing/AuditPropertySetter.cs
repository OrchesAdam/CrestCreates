using System;

namespace CrestCreates.Domain.Shared.Entities.Auditing;

/// <summary>
/// User-context-free audit stamping for seeders and other startup writers that bypass the
/// AppService audit pipeline (G-10). Mirrors the Added branch of the EF Core AuditInterceptor
/// so both share one definition of creation-audit semantics. CreatorId is left null when no
/// actor is available (the seed case).
/// </summary>
public static class AuditPropertySetter
{
    public static void SetCreationAudit(IAuditedEntity entity, DateTime now, Guid? userId = null)
    {
        entity.CreationTime = now;
        entity.CreatorId = userId;
        entity.LastModificationTime = null;
        entity.LastModifierId = null;
    }
}