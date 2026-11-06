namespace CrestCreates.Domain.Settings;

public static class AuditRetentionSettingNames
{
    public const string RetentionDays = "AuditLogging.RetentionDays";
    public const string Enabled = "AuditLogging.Retention.Enabled";
    public const string BatchSize = "AuditLogging.Retention.BatchSize";
    public const string MaxBatchesPerRun = "AuditLogging.Retention.MaxBatchesPerRun";
    public const string MaxRunSeconds = "AuditLogging.Retention.MaxRunSeconds";
}
