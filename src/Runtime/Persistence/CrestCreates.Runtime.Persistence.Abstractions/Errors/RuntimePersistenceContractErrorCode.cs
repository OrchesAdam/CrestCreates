namespace CrestCreates.Runtime.Persistence.Abstractions.Errors;

public enum RuntimePersistenceContractErrorCode
{
    Unknown = 0,
    ActiveStepCorrelationConflict = 1,
    WaitingTaskCorrelationConflict = 2,
    PersistedInvariantViolation = 3,
    ConcurrentAmbientUse = 4
}
