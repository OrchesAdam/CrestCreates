namespace CrestCreates.Aop.Abstractions.Options;

public class PermissionOptions
{
    public Dictionary<string, string> PermissionMappings { get; set; } = new();
    public bool EnableCaching { get; set; } = true;
    public bool ThrowExceptionOnDenied { get; set; } = true;

    public string GetPermissionName(string key)
    {
        return PermissionMappings.TryGetValue(key, out var name) ? name : key;
    }
}
