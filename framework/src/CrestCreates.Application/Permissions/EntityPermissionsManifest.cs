using System;
using System.Collections.Generic;

namespace CrestCreates.Application.Permissions;

public class EntityPermissionsManifest
{
    public string Version { get; set; } = "1.0";
    public DateTime GeneratedAt { get; set; }
    public List<EntityPermissionInfo> Permissions { get; set; } = new();
}

public class EntityPermissionInfo
{
    public string ModuleName { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string EntityFullName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
}
