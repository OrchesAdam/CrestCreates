using System;

namespace CrestCreates.Accountability.Abstractions.Retention;

public sealed class AuditRetentionException : Exception
{
    public AuditRetentionException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
        => Code = code;

    public string Code { get; }
}

public static class AuditRetentionErrorCodes
{
    public const string StoreUnknown = "RETENTION_STORE_UNKNOWN";
    public const string StoreMismatch = "RETENTION_STORE_MISMATCH";
    public const string StoreDuplicate = "RETENTION_STORE_DUPLICATE";
    public const string ScopeInvalid = "RETENTION_SCOPE_INVALID";
    public const string PolicyInvalid = "RETENTION_POLICY_INVALID";
    public const string RequestConflict = "RETENTION_REQUEST_CONFLICT";
    public const string PolicyHashMismatch = "RETENTION_POLICY_HASH_MISMATCH";
    public const string RunNotFound = "RETENTION_RUN_NOT_FOUND";
    public const string RunNotExecutable = "RETENTION_RUN_NOT_EXECUTABLE";
    public const string BatchNotFound = "RETENTION_BATCH_NOT_FOUND";
    public const string BatchOrdinalConflict = "RETENTION_BATCH_ORDINAL_CONFLICT";
    public const string BatchBindingConflict = "RETENTION_BATCH_BINDING_CONFLICT";
    public const string LedgerInvariantViolation = "RETENTION_LEDGER_INVARIANT_VIOLATION";
    public const string HoldNotFound = "RETENTION_HOLD_NOT_FOUND";
    public const string HoldVersionConflict = "RETENTION_HOLD_VERSION_CONFLICT";
    public const string HoldAlreadyPurged = "RETENTION_HOLD_ALREADY_PURGED";
    public const string HoldScopeMismatch = "RETENTION_HOLD_SCOPE_MISMATCH";
    public const string HoldQuarantined = "RETENTION_HOLD_QUARANTINED";
    public const string CapabilityMissing = "RETENTION_CAPABILITY_MISSING";
    public const string InvalidRequest = "RETENTION_INVALID_REQUEST";
    public const string IdentityRequired = "RETENTION_IDENTITY_REQUIRED";
    public const string Disabled = "RETENTION_DISABLED";
}
