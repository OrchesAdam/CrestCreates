using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CrestCreates.Accountability.Abstractions.Retention;

/// <summary>
/// Immutable, admitted policy snapshot. A run keeps this value for its whole life; later
/// setting changes only affect new runs.
/// </summary>
public sealed record AuditRetentionPolicy
{
    public const int MinRetentionDays = 1;
    public const int MaxRetentionDays = 36500;
    public const int MinBatchSize = 1;
    public const int MaxBatchSize = 1000;
    public const int MinBatchesPerRun = 1;
    public const int MaxBatchesLimit = 100;
    public const int MinRunSeconds = 1;
    public const int MaxRunSecondsLimit = 300;
    public const int DefaultRetentionDays = 90;
    public const int DefaultBatchSize = 500;
    public const int DefaultBatchesPerRun = 20;
    public const int DefaultRunSeconds = 60;

    public required int RetentionDays { get; init; }

    public required int BatchSize { get; init; }

    public required int MaxBatchesPerRun { get; init; }

    public required int MaxRunSeconds { get; init; }

    public void EnsureValid()
    {
        if (RetentionDays is < MinRetentionDays or > MaxRetentionDays)
            throw new ArgumentException($"RetentionDays must be within [{MinRetentionDays}, {MaxRetentionDays}].");
        if (BatchSize is < MinBatchSize or > MaxBatchSize)
            throw new ArgumentException($"BatchSize must be within [{MinBatchSize}, {MaxBatchSize}].");
        if (MaxBatchesPerRun is < MinBatchesPerRun or > MaxBatchesLimit)
            throw new ArgumentException($"MaxBatchesPerRun must be within [{MinBatchesPerRun}, {MaxBatchesLimit}].");
        if (MaxRunSeconds is < MinRunSeconds or > MaxRunSecondsLimit)
            throw new ArgumentException($"MaxRunSeconds must be within [{MinRunSeconds}, {MaxRunSecondsLimit}].");
    }

    public TimeSpan RetentionWindow => TimeSpan.FromDays(RetentionDays);

    public TimeSpan MaxRunDuration => TimeSpan.FromSeconds(MaxRunSeconds);

    public long MaxRunDeletions => (long)BatchSize * MaxBatchesPerRun;

    // Persistence stores timestamptz at microsecond precision. Truncating here keeps every
    // admitted cutoff byte-identical after the run row round-trips, so receipt facts built from
    // the in-memory run and from a re-read run share one canonical hash.
    public DateTimeOffset ComputeCutoff(DateTimeOffset snapshotAt)
    {
        var cutoff = snapshotAt - RetentionWindow;
        return new DateTimeOffset(cutoff.Ticks - (cutoff.Ticks % TimeSpan.TicksPerMicrosecond), cutoff.Offset);
    }

    public string ComputeHash()
    {
        EnsureValid();
        var canonical = string.Create(
            CultureInfo.InvariantCulture,
            $"crest.audit-retention.policy/v1|days={RetentionDays}|batch={BatchSize}|batches={MaxBatchesPerRun}|seconds={MaxRunSeconds}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
