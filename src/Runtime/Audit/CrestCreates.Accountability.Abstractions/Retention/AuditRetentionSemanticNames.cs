using System.Security.Cryptography;
using System.Text;

namespace CrestCreates.Accountability.Abstractions.Retention;

public static class AuditRetentionSemanticNames
{
    public const string GovernanceTargetPrefix = "audit-retention-";
    public const string RunTargetKind = "audit-retention-run";
    public const string BatchTargetKind = "audit-retention-batch";
    public const string HoldTargetKind = "audit-retention-hold";
    public const string ActionKind = "audit.retention";
    public const string RunActionName = "retention.run";
    public const string BatchActionName = "retention.batch";
    public const string HoldPlaceActionName = "retention.hold.place";
    public const string HoldReleaseActionName = "retention.hold.release";
    public const string ProtectedClassification = "governance";

    public const string BatchIdSeparator = "/";

    public static bool IsGovernanceTarget(string? targetKind)
        => targetKind is not null
           && targetKind.StartsWith(GovernanceTargetPrefix, System.StringComparison.Ordinal);

    public static string BatchReceiptId(string runId, long batchOrdinal)
        => $"{runId}{BatchIdSeparator}{batchOrdinal}";

    public static string RunReceiptId(AuditRetentionRunKey key)
        => "auditret-run-" + Digest(key.StableSignature());

    public static string BatchFactAuditId(string runId, long batchOrdinal)
        => "auditret-batch-" + Digest($"{runId}\u001f{batchOrdinal}");

    public static string HoldFactAuditId(string holdId, long version)
        => "auditret-hold-" + Digest($"{holdId}\u001f{version}");

    private static string Digest(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..32];
}
