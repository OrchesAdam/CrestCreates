using System;
using System.Collections.Generic;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Shared;

namespace CrestCreates.Domain.Permission;

public class TenantInitializationRecord : Entity<Guid>
{
    protected TenantInitializationRecord()
    {
        StepResultsJson = "[]";
        CorrelationId = string.Empty;
    }

    public TenantInitializationRecord(
        Guid id,
        Guid tenantId,
        int attemptNo,
        string correlationId)
    {
        Id = id;
        TenantId = tenantId;
        AttemptNo = attemptNo;
        Status = TenantInitializationStatus.Initializing;
        CorrelationId = correlationId;
        StartedAt = DateTime.UtcNow;
        StepResultsJson = "[]";
    }

    public Guid TenantId { get; private set; }
    public TenantInitializationStatus Status { get; private set; }
    public string? CurrentStep { get; private set; }
    public string StepResultsJson { get; private set; }
    public string? Error { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public int AttemptNo { get; private set; }
    public string CorrelationId { get; private set; }

    public void SetCurrentStep(string stepName)
    {
        CurrentStep = stepName;
    }

    public void AppendStepResult(string name, TenantInitializationStepStatus status,
        DateTime startedAt, DateTime? completedAt, string? error)
    {
        var results = DeserializeResults();
        results.Add(new StepResult
        {
            Name = name,
            Status = status,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            Error = error
        });
#pragma warning disable IL2026, IL3050
        StepResultsJson = System.Text.Json.JsonSerializer.Serialize(results);
#pragma warning restore IL2026, IL3050
    }

    public void MarkSucceeded()
    {
        Status = TenantInitializationStatus.Initialized;
        CompletedAt = DateTime.UtcNow;
        CurrentStep = null;
    }

    public void MarkFailed(string? detailedError)
    {
        Status = TenantInitializationStatus.Failed;
        Error = detailedError;
        CompletedAt = DateTime.UtcNow;
        CurrentStep = null;
    }

    public IReadOnlyList<StepResult> GetSteps()
    {
        return DeserializeResults();
    }

    private List<StepResult> DeserializeResults()
    {
        if (string.IsNullOrEmpty(StepResultsJson))
            return new List<StepResult>();
#pragma warning disable IL2026, IL3050
        return System.Text.Json.JsonSerializer.Deserialize<List<StepResult>>(StepResultsJson)
               ?? new List<StepResult>();
#pragma warning restore IL2026, IL3050
    }

    public class StepResult
    {
        public string Name { get; set; } = string.Empty;
        public TenantInitializationStepStatus Status { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? Error { get; set; }
    }
}
