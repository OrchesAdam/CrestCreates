namespace CrestCreates.Runtime.Persistence.Testing.Manifest;

public sealed record Phase9cAcceptanceCaseBinding(
    string CaseId,
    string AcceptanceName,
    IReadOnlyList<string> RequiredRunners,
    string EvidenceVector);

internal sealed record Phase9cAcceptanceCaseBindingGroup(
    IReadOnlyList<string> CaseIds,
    string AcceptanceName,
    IReadOnlyList<string> RequiredRunners,
    string EvidenceVector);

public sealed record Phase9cEvidenceTuple(
    string CaseId,
    string AcceptanceName,
    string Runner,
    string EvidenceVector);

/// <summary>
/// Explicit authority copied from the frozen Phase 9c Case/acceptance matrix.
/// No acceptance name is classified by a prefix or substring heuristic. The
/// group rows are expanded into one explicit CaseId binding below.
/// </summary>
public static class Phase9cEvidenceRunnerCatalog
{
    public static IReadOnlySet<string> RunnerNames { get; } = new HashSet<string>(StringComparer.Ordinal)
    { "SH", "IM", "PG", "WF", "HT", "DEL", "ACCT", "PROC", "ACT", "BND", "CW", "AOT" };

    private static IReadOnlyList<Phase9cAcceptanceCaseBindingGroup> BindingGroups { get; } =
    [
        new(["ARCH01"], "OutboxContracts_Should_Not_ExposeProviderTypes", ["DEL", "BND"], "composition"),
        new(["ARCH02"], "RuntimeDeliveryAbstractions_Should_Not_ReferenceDomainOrProviderImplementations", ["BND"], "composition"),
        new(["ARCH03"], "RuntimeDeliveryRuntime_Should_Not_ReferenceHumanTaskWorkflowOrAccountability", ["BND"], "composition"),
        new(["ARCH04"], "ProducerModules_Should_Not_ReferenceOutboxDispatchStore", ["BND"], "composition"),
        new(["A02", "ARCH05"], "TransactionalOutboxWriter_Should_FailWithoutAmbientRuntimeTransaction", ["SH", "IM", "PG"], "composition"),
        new(["ARCH06", "N06"], "OutboxMainline_Should_Not_UseRuntimeTypeNamesOrReflectionSerialization", ["DEL", "BND", "AOT"], "native"),
        new(["ARCH07"], "OutboxHandlerRegistry_Should_RejectDuplicateContractId", ["DEL"], "composition"),
        new(["ARCH08"], "OutboxHandlerRegistry_Should_CacheMetadata_NotScopedInstances", ["DEL"], "composition"),
        new(["ARCH09", "C07"], "ScopedOutboxHandler_Should_BeResolved_FromDeliveryScope", ["DEL"], "composition"),
        new(["ARCH10"], "RequiredConsumerRegistry_Should_CacheMetadata_NotScopedInstances", ["DEL"], "composition"),
        new(["ARCH11"], "OutboxPayload_Should_RequireGeneratedJsonTypeInfo", ["DEL", "AOT"], "native"),
        new(["ARCH12"], "ExistingEventBusAndDlq_Should_Not_BeOutboxAuthority", ["BND"], "composition"),
        new(["A10"], "ControlPlane_Save_Should_Not_Enlist_Runtime_Outbox", ["PG"], "semantic"),
        new(["C01"], "Missing_RequiredContractHandler_Should_Fail_Composition_Without_MessageMutation", ["DEL", "PG"], "composition"),
        new(["C08"], "ActiveMessage_WithUnsupportedContract_Should_Fail_Composition", ["SH", "IM", "PG"], "composition"),
        new(["C08"], "UnsupportedActiveContract_Should_Remain_Unmodified", ["SH", "IM", "PG"], "composition"),
        new(["C10"], "TerminalMessage_Should_Not_Require_CurrentHandlerRegistration", ["SH", "IM", "PG"], "composition"),
        new(["ARCH13"], "OutboxCompositionException_Should_Not_ExposeProviderDetails", ["DEL", "BND"], "composition"),
        new(["ARCH14"], "IAuditSink_Should_Not_GainDurabilityCapability", ["ACCT", "BND"], "composition"),
        new(["ARCH15"], "IAuditRecorder_Should_Not_Expose_PreparedEnvelopeBypass", ["ACCT", "BND"], "composition"),
        new(["ARCH16"], "PreparedAuditRecording_Should_Be_AccountabilityInternal", ["ACCT", "BND"], "composition"),
        new(["A01", "A05"], "State_Commit_Should_Atomically_Create_Outbox_Message", ["SH", "IM", "PG"], "semantic"),
        new(["A06"], "Rolled_Back_State_Should_Not_Create_Outbox_Message", ["SH", "IM", "PG"], "semantic"),
        new(["A07"], "CommitUnknown_Should_Never_Expose_Split_State_And_Outbox", ["PG", "CW"], "process-crash"),
        new(["A08"], "OutboxAppendFailure_Should_Rollback_RuntimeMutation", ["SH", "IM", "PG"], "semantic"),
        new(["A03"], "Append_Replay_With_SameIntegrity_Should_Be_Duplicate", ["SH", "IM", "PG"], "semantic"),
        new(["A04"], "OutboxConflict_Should_Abort_RuntimeTransaction", ["SH", "IM", "PG"], "semantic"),
        new(["A04"], "IgnoredConflict_Should_Not_Be_Possible_On_CanonicalProducerPath", ["DEL", "SH"], "semantic"),
        new(["A03"], "Duplicate_Should_Not_Abort_RuntimeTransaction", ["SH", "IM", "PG"], "semantic"),
        new(["A03"], "Append_Duplicate_Should_Not_Reset_DeliveryState", ["SH", "IM", "PG"], "semantic"),
        new(["A09"], "SameMessageId_InDifferentTenant_Should_Abort_RuntimeTransaction", ["SH", "IM", "PG"], "semantic"),
        new(["A11"], "AcceptedAppend_Should_Use_ProviderClock_ForInitialAvailability", ["SH", "IM", "PG"], "semantic"),
        new(["A12"], "RequiredConsumerIds_Should_Participate_In_OutboxIntegrity", ["DEL", "SH"], "semantic"),
        new(["A03"], "ImmutablePayload_Should_Not_Change_AfterCallerMutation", ["DEL", "SH"], "semantic"),
        new(["R03"], "Retry_Should_Not_Mutate_LogicalPayload", ["SH", "IM", "PG"], "semantic"),
        new(["L01"], "Pending_Message_Should_Be_Claimed_With_FirstFence", ["SH", "IM", "PG"], "semantic"),
        new(["L02", "R08"], "NotYetDue_Message_Should_Not_Be_Claimed", ["SH", "IM", "PG"], "semantic"),
        new(["L03"], "Concurrent_Dispatchers_Should_Respect_FencingToken", ["SH", "IM", "PG"], "semantic"),
        new(["L04", "R10"], "ExpiredLease_Should_Allow_NewerGeneration", ["SH", "IM", "PG"], "semantic"),
        new(["L05", "L08"], "Expired_Owner_Should_Not_Acknowledge_NewerLease", ["SH", "IM", "PG"], "semantic"),
        new(["L06"], "Stale_Owner_Should_Not_Schedule_Retry", ["SH", "IM", "PG"], "semantic"),
        new(["L07"], "Stale_Owner_Should_Not_DeadLetter", ["SH", "IM", "PG"], "semantic"),
        new(["L09"], "Valid_Owner_Should_Acknowledge_To_Delivered", ["SH", "IM", "PG"], "semantic"),
        new(["L10"], "Retry_Should_Use_ProviderClock_And_Preserve_MessageId", ["SH", "IM", "PG"], "semantic"),
        new(["L11", "R06", "R07"], "Poison_Message_Should_Move_To_DeadLetter", ["DEL", "SH", "IM", "PG"], "semantic"),
        new(["L12"], "Delivered_Message_Should_Not_Be_Claimed", ["SH", "IM", "PG"], "semantic"),
        new(["L12"], "DeadLettered_Message_Should_Not_Be_Claimed", ["SH", "IM", "PG"], "semantic"),
        new(["L11"], "DeadLetter_Should_Be_One_OutboxTerminalTransition", ["SH", "IM", "PG"], "semantic"),
        new(["L13"], "UnregisteredContract_Should_Not_BeClaimed_OrConsumeAttemptBudget", ["SH", "IM", "PG"], "semantic"),
        new(["R11", "CW04B"], "Repeated_ClaimCrash_Should_Consume_AttemptBudget", ["SH", "PG", "CW"], "process-crash"),
        new(["R12"], "AttemptBudgetExhausted_Should_DeadLetter_Without_HandlerInvocation", ["DEL", "SH", "PG", "CW"], "process-crash"),
        new(["L14"], "Ack_Replay_With_ExactTerminalFence_Should_Be_AlreadyApplied", ["SH", "IM", "PG"], "semantic"),
        new(["L15"], "DeadLetter_Replay_With_ExactTerminalFence_Should_Be_AlreadyApplied", ["SH", "IM", "PG"], "semantic"),
        new(["L16", "L17"], "TerminalReplay_With_DifferentFence_Should_Be_StaleOrConflict", ["SH", "IM", "PG"], "semantic"),
        new(["L14", "L15"], "AlreadyApplied_Should_Not_Reopen_TerminalState", ["SH", "IM", "PG"], "semantic"),
        new(["C13"], "UnsupportedActiveRequirement_Should_Throw_ProviderNeutralCompositionFailure", ["SH", "IM", "PG"], "composition"),
        new(["C13", "C14"], "CompositionFailure_Should_Not_Be_Classified_As_TransientStoreFailure", ["DEL", "PG"], "composition"),
        new(["R01", "CW03"], "Pending_Message_Should_Be_Recovered_After_Restart", ["PG", "CW"], "process-crash"),
        new(["R09", "CW06"], "RetryDue_Message_Should_Be_Recovered_After_Restart", ["PG", "CW"], "process-crash"),
        new(["R02"], "ExpiredLease_Should_Recover_After_Restart", ["PG", "CW"], "process-crash"),
        new(["R04"], "Publish_ResponseLoss_Should_Redeliver_SameMessageId", ["SH", "PG", "CW"], "process-crash"),
        new(["R05"], "Ack_ResponseLoss_AfterCommit_Should_Remain_Delivered", ["SH", "PG"], "semantic"),
        new(["CW01"], "Crash_BeforeProducerCommit_Should_ExposeNeitherStateNorOutbox", ["CW"], "process-crash"),
        new(["CW02"], "Crash_AfterProducerCommit_Should_RecoverPendingMessage", ["CW"], "process-crash"),
        new(["CW04"], "Crash_AfterClaim_Should_RecoverExpiredLease", ["CW"], "process-crash"),
        new(["CW05"], "Crash_AfterHandlerBeforeAck_Should_PermitSameMessageRedelivery", ["CW"], "process-crash"),
        new(["R13"], "Restart_Should_Not_Reset_AttemptBudget", ["PG", "CW"], "process-crash"),
        new(["C03"], "CompositionRecovery_Should_Allow_ExistingPendingMessage_To_Deliver", ["PG"], "composition"),
        new(["C11"], "RestoredContractRegistration_Should_Allow_PendingDelivery", ["SH", "PG"], "composition"),
        new(["H01"], "HumanTask_Completion_Should_Commit_Completed_And_Outbox", ["HT", "IM", "PG"], "semantic"),
        new(["H02"], "HumanTask_CompletionRollback_Should_ExposeNeitherPostStateNorOutbox", ["HT", "IM", "PG"], "semantic"),
        new(["H04"], "HumanTask_Delivery_Failure_Should_Not_Create_CompletionDispatchFailed", ["HT"], "semantic"),
        new(["H01"], "HumanTask_Completion_Should_Not_Publish_Synchronously", ["HT"], "semantic"),
        new(["H13", "H22"], "HumanTask_OutboxHandler_Should_Use_TypedLocalEventDispatch", ["HT"], "semantic"),
        new(["H05"], "Duplicate_HumanTask_Delivery_Should_Not_Duplicate_Continuation", ["HT", "WF"], "semantic"),
        new(["H06"], "HumanTask_CrashAfterCommit_Should_Eventually_Accept_WorkflowResume", ["PG", "CW"], "process-crash"),
        new(["H07"], "HumanTask_PoisonDelivery_Should_Preserve_CompletedBusinessState", ["HT", "PG"], "semantic"),
        new(["H08"], "Legacy_CompletionDispatchFailed_Should_Block_SilentCutover", ["PG"], "semantic"),
        new(["H08", "C06"], "Legacy_CompletionDispatchFailed_Preflight_Should_Be_V012ProviderOwned", ["PG", "BND"], "composition"),
        new(["C15"], "Legacy_ActiveHumanTask_RequiredConsumerGap_Should_BlockSilentCutover", ["HT", "PG"], "composition"),
        new(["H03", "H10"], "HumanTask_CommitUnknown_Should_Require_Observation_Before_CommandReplay", ["HT", "PG"], "semantic"),
        new(["H09"], "Completed_HumanTask_AfterCommitUnknown_Should_Preserve_OriginalCompletionEventId", ["HT", "PG"], "semantic"),
        new(["H11"], "CommitUnknown_Recovery_Should_Not_Create_SecondCompletionIdentity", ["HT", "PG"], "semantic"),
        new(["H12"], "Completed_HumanTask_WithoutCompletionEventId_Should_FailClosed", ["HT"], "semantic"),
        new(["H13"], "WorkflowCorrelated_HumanTask_Should_Require_ContinuationConsumer", ["HT", "WF"], "semantic"),
        new(["H14"], "Missing_WorkflowContinuationConsumer_Should_Not_Ack_Outbox", ["DEL", "HT"], "semantic"),
        new(["H14", "C09"], "Missing_WorkflowContinuationConsumer_Should_Fail_Composition", ["DEL", "HT"], "composition"),
        new(["H16"], "Standalone_HumanTask_Should_Not_Require_WorkflowContinuationConsumer", ["HT"], "semantic"),
        new(["H15"], "Zero_LocalEventHandlers_Should_Not_Prove_WorkflowContinuation", ["HT", "WF"], "semantic"),
        new(["H17"], "CommitUnknown_CompletedObservation_Should_Not_ProveCallerOwnership", ["HT"], "semantic"),
        new(["H17"], "CommitUnknown_DifferentCompletionWinner_Should_Not_Be_Reported_AsCallerSuccess", ["HT"], "semantic"),
        new(["H17", "H18"], "CommitUnknown_ConcurrentWinner_Should_Not_Create_SecondCompletion", ["HT", "PG"], "semantic"),
        new(["H19", "CW07"], "Crash_After_ResumeCommit_Before_ConsumerReturn_Should_Reconcile_SameCompletion", ["WF", "PG", "CW"], "process-crash"),
        new(["H19"], "Duplicate_Continuation_Should_Prove_AppliedCompletionIdentity", ["WF", "SH", "IM", "PG"], "semantic"),
        new(["H20"], "Different_CompletionId_Should_Not_Be_Treated_As_Duplicate", ["WF", "SH", "IM", "PG"], "semantic"),
        new(["H21"], "ReliableContinuationAck_Should_Not_Require_PostResume_WorkflowExecution", ["WF", "HT"], "semantic"),
        new(["H22"], "Optional_LocalEventHandler_Failure_Should_Not_Block_OutboxAck", ["HT", "DEL"], "semantic"),
        new(["H22"], "Optional_LocalEventHandler_Should_Not_Be_ImplicitReliableConsumer", ["HT", "DEL"], "semantic"),
        new(["H22"], "ReliableAck_Should_Depend_Only_On_PersistedConsumerObligations", ["HT", "DEL"], "semantic"),
        new(["H23"], "Required_BusinessConsumer_Should_Require_StableConsumerId", ["HT", "BND"], "semantic"),
        new(["H23"], "FirstParty_RequiredCompletionHandlers_Should_Use_StableConsumerIds", ["ACT", "PROC", "BND"], "semantic"),
        new(["H23"], "Procurement_Mainline_Should_Not_Register_CompletionFailurePolicy", ["PROC", "BND"], "semantic"),
        new(["W01"], "Workflow_Started_Should_Commit_Accountability_Fact", ["WF", "IM", "PG"], "semantic"),
        new(["W01"], "Workflow_Suspended_Should_Commit_Accountability_Fact", ["WF", "IM", "PG"], "semantic"),
        new(["W01"], "Workflow_Resumed_Should_Commit_Accountability_Fact", ["WF", "IM", "PG"], "semantic"),
        new(["W01"], "Workflow_Completed_Should_Commit_Accountability_Fact", ["WF", "IM", "PG"], "semantic"),
        new(["W01"], "Workflow_Failed_Should_Commit_Accountability_Fact", ["WF", "IM", "PG"], "semantic"),
        new(["W02"], "Workflow_StateFailure_Should_Not_Append_AccountabilityFact", ["WF", "IM", "PG"], "semantic"),
        new(["W03"], "Workflow_BestEffortObserverFailure_Should_Not_Change_Outbox", ["WF"], "semantic"),
        new(["W08"], "Workflow_Accountability_Should_Persist_Final_AuditEnvelope_NotLifecycleEvent", ["WF", "ACCT"], "semantic"),
        new(["W08"], "Workflow_Accountability_Should_Persist_PreparedEnvelope_WithIntegrity", ["WF", "ACCT"], "semantic"),
        new(["W03"], "Workflow_AccountabilityObserver_Should_Not_Remain_ReliableWritePath", ["WF", "BND"], "semantic"),
        new(["W07"], "Duplicate_Accountability_Delivery_Should_Preserve_AuditId", ["ACCT", "PG"], "semantic"),
        new(["W04", "W05"], "Partial_AccountabilitySinkFailure_Should_Retry_Until_AllAccepted", ["ACCT", "DEL"], "semantic"),
        new(["W09"], "Accountability_Retry_AfterSanitizerUpgrade_Should_Preserve_Integrity", ["ACCT"], "semantic"),
        new(["W10", "W11"], "Accountability_Preparation_Should_Be_SinglePath_ForImmediateAndOutboxRecording", ["ACCT"], "semantic"),
        new(["W12"], "Workflow_Should_Not_Reference_IAuditSink", ["BND"], "semantic"),
        new(["W11"], "Accountability_OutboxHandler_Should_Be_Owned_By_Accountability", ["ACCT", "BND"], "semantic"),
        new(["W11"], "OutboxPreparedAuditPath_Should_Not_Invoke_Sanitizer", ["ACCT"], "semantic"),
        new(["W10"], "OrdinaryAuditRecording_Should_Always_Invoke_Preparation", ["ACCT"], "semantic"),
        new(["W06"], "Accountability_Conflict_Should_DeadLetter", ["ACCT", "DEL"], "semantic"),
        new(["C02"], "Missing_RequiredAccountabilitySink_Should_Not_DeadLetter_Message", ["ACCT", "DEL"], "composition"),
        new(["W13"], "ReliableWorkflowAccountability_Should_Require_AtLeastOneConfiguredSink", ["ACCT"], "semantic"),
        new(["W14"], "FullDurableAccountability_Should_Use_PostgreSqlAuditSink", ["PG", "AOT"], "native"),
        new(["C04"], "Removed_AccountabilitySink_Should_End_FutureAttemptObligation", ["ACCT", "DEL"], "composition"),
        new(["C05"], "Added_AccountabilitySink_Should_Participate_In_SubsequentAttempt", ["ACCT", "DEL"], "composition"),
        new(["W03"], "BestEffort_WorkflowObservers_Should_Not_Participate_In_ReliableAck", ["WF", "ACCT"], "semantic"),
        new(["N01"], "V012_Should_Extend_Existing_RuntimeMigrationCatalog", ["PG"], "native"),
        new(["N01"], "V012_Should_Validate_ExactOutboxSchema", ["PG"], "native"),
        new(["N01"], "V012_Should_Persist_WorkflowContinuationAcceptanceDiscriminator", ["PG"], "native"),
        new(["N01"], "V012_Should_Reject_ChangedAppliedChecksum", ["PG"], "native"),
        new(["N01"], "V012_Should_Reject_OutboxSchemaDrift", ["PG"], "native"),
        new(["C08", "C09", "C10", "C11", "C12"], "ActiveRequirementsProbe_Should_Pass_SharedContractKit", ["SH", "IM", "PG"], "composition"),
        new(["H19", "H20"], "WorkflowContinuationAcceptance_Should_Pass_SharedContractKit", ["SH", "IM", "PG"], "semantic"),
        new(["C12", "C13"], "AtomicClaim_Should_Reject_UnsupportedActiveRequirement_WithoutMutation", ["SH", "IM", "PG"], "composition"),
        new(["N02"], "PostgreSqlOutbox_Should_Pass_SharedContractKit", ["SH", "PG"], "native"),
        new(["N02"], "InMemoryOutbox_Should_Pass_SharedContractKit", ["SH", "IM"], "native"),
        new(["N03"], "Persisted_HumanTaskPayload_Should_Dispatch_Under_NativeAot", ["AOT"], "native"),
        new(["N08"], "Required_WorkflowContinuationConsumer_Should_Execute_Under_NativeAot", ["AOT"], "native"),
        new(["N03", "N08"], "WorkflowContinuationAcceptance_Should_Reconcile_Under_NativeAot", ["AOT"], "native"),
        new(["N09"], "Optional_LocalEventFailure_Should_Not_Block_NativeOutboxAck", ["AOT"], "native"),
        new(["N04"], "Persisted_AuditEnvelope_Should_Dispatch_Under_NativeAot", ["AOT"], "native"),
        new(["N07"], "ActiveCompositionProbe_Should_Execute_Under_NativeAot", ["AOT"], "native"),
        new(["N01", "N02", "N03", "N04", "N05", "N06", "N07", "N08", "N09"], "PostgreSqlOutboxFixture_Should_PublishLinkAndRunNativeBinary", ["AOT"], "native"),
        new(["N01", "N02", "N03", "N04", "N05", "N06", "N07", "N08", "N09"], "NativeBinary_Should_Emit_ReliableDeliverySentinel", ["AOT"], "native"),
        new(["MRC01"], "Partial_RequiredConsumerFailure_Should_Retry_Message", ["DEL", "HT"], "semantic"),
        new(["MRC02"], "PreviouslyAcceptedRequiredConsumer_Should_Be_Duplicate_On_Retry", ["DEL", "HT", "PROC", "WF"], "semantic"),
        new(["MRC03"], "RequiredConsumer_Order_Should_Not_Be_BusinessContract", ["DEL", "HT"], "semantic"),
        new(["MRC04"], "RequiredConsumer_Conflict_Should_FailClosed", ["DEL", "HT"], "semantic"),
        new(["PROC01"], "Procurement_RequiredConsumer_Should_Not_Depend_On_RequestAmbientIdentity", ["PROC", "BND"], "semantic"),
        new(["PROC02"], "RequiredConsumer_Retry_AfterFreshServiceProvider_Should_Preserve_TenantSemantics", ["PROC"], "semantic"),
        new(["RCA01"], "WorkflowContinuationAcceptance_Integrity_Should_Use_FrozenV1Projection", ["WF", "SH", "IM", "PG", "BND"], "semantic"),
        new(["RCA02"], "Same_CompletionEventId_WithChangedOutcomeOrResult_Should_Conflict", ["WF", "SH", "IM", "PG"], "semantic"),
        new(["BOOT01"], "DB_CompositionPreflight_Should_Run_After_RuntimeSchemaCompatibility", ["DEL", "PG"], "composition"),
        new(["BOOT02"], "HumanTaskObligationPreflight_Should_Run_After_V012", ["HT", "PG"], "composition"),
        new(["BOOT03"], "DB_CompositionPreflight_Should_Not_Use_SyncOverAsync", ["DEL", "BND", "PG"], "composition"),
        new(["PROC03"], "Procurement_RequiredConsumer_Should_Dispatch_Through_CapabilityPipeline", ["PROC"], "semantic"),
        new(["PROC04"], "Procurement_BackgroundDispatch_Should_Use_DurableTenantAndActor", ["PROC"], "semantic"),
        new(["PROC05"], "Procurement_InternalApply_Should_Not_Reauthorize_RequestAmbientIdentity", ["PROC", "BND"], "semantic"),
        new(["SCHEMA01"], "WorkflowCorrelated_HumanTask_Row_Should_Require_ContinuationConsumerId", ["PG"], "semantic"),
        new(["SCHEMA02"], "HumanTask_ObligationColumn_Should_Not_Keep_FailOpenEmptyDefault", ["PG"], "semantic"),
        new(["OPT01"], "NonCooperative_OptionalHandler_Should_Not_Prevent_ReliableAckProgress", ["HT", "DEL"], "semantic"),
        new(["PROC06"], "Procurement_ExactDecisionReplay_Should_Be_Duplicate", ["PROC"], "semantic"),
        new(["PROC07"], "Procurement_ChangedDecisionIdentity_Should_Conflict", ["PROC"], "semantic"),
        new(["ACT01"], "ActivationReview_ExactDecisionReplay_Should_Be_Duplicate", ["ACT"], "semantic"),
        new(["ACT02"], "ActivationReview_ChangedDecisionIdentity_Should_Conflict", ["ACT"], "semantic"),
        new(["OUT01"], "HumanTask_Completion_Should_Persist_One_CanonicalOutcome", ["HT"], "semantic"),
        new(["OUT02"], "WorkflowContinuation_Should_Reuse_PersistedCanonicalOutcome", ["HT", "WF"], "semantic"),
        new(["OPT02"], "OptionalCompatibility_Should_Use_RemainingDeliveryAttemptBudget", ["HT", "DEL"], "semantic"),
        new(["HOC01"], "HumanTaskCreation_Should_Reject_UnregisteredRequiredConsumerId", ["HT", "DEL"], "composition"),
    ];

    public static IReadOnlyList<Phase9cAcceptanceCaseBinding> AcceptanceCaseBindings { get; } =
        BindingGroups
            .SelectMany(group => group.CaseIds.Select(caseId =>
                new Phase9cAcceptanceCaseBinding(
                    caseId,
                    group.AcceptanceName,
                    group.RequiredRunners,
                    Phase9cCaseManifest.Cases.Single(item => string.Equals(item.CaseId, caseId, StringComparison.Ordinal)).EvidenceVector)))
            .ToArray();

    public static IReadOnlyList<Phase9cEvidenceTuple> RequiredTuples { get; } =
        AcceptanceCaseBindings
            .SelectMany(binding => binding.RequiredRunners.Select(runner =>
                new Phase9cEvidenceTuple(binding.CaseId, binding.AcceptanceName, runner, binding.EvidenceVector)))
            .ToArray();

    public static IReadOnlyList<Phase9cEvidenceTuple> ForAcceptance(string acceptanceName)
        => RequiredTuples.Where(tuple => string.Equals(tuple.AcceptanceName, acceptanceName, StringComparison.Ordinal)).ToArray();

    public static void ValidateAuthority()
    {
        var names = Phase9cAcceptanceManifest.NormativeNames
            .Concat(Phase9cSupplementalAcceptanceManifest.Names)
            .ToHashSet(StringComparer.Ordinal);
        var boundNames = AcceptanceCaseBindings.Select(binding => binding.AcceptanceName).ToHashSet(StringComparer.Ordinal);
        if (!boundNames.SetEquals(names) || BindingGroups.Count != names.Count)
            throw new InvalidOperationException("Phase 9c acceptance/case authority does not cover exactly the frozen 145+25 names.");
        var knownCases = Phase9cCaseManifest.Cases.Select(item => item.CaseId).ToHashSet(StringComparer.Ordinal);
        var knownVectors = Phase9cCaseManifest.Cases.ToDictionary(item => item.CaseId, item => item.EvidenceVector, StringComparer.Ordinal);
        var boundCaseIds = AcceptanceCaseBindings.Select(binding => binding.CaseId).ToHashSet(StringComparer.Ordinal);
        if (!boundCaseIds.SetEquals(knownCases))
        {
            var missing = knownCases.Except(boundCaseIds, StringComparer.Ordinal).OrderBy(caseId => caseId, StringComparer.Ordinal);
            var unexpected = boundCaseIds.Except(knownCases, StringComparer.Ordinal).OrderBy(caseId => caseId, StringComparer.Ordinal);
            throw new InvalidOperationException($"Phase 9c authority CaseId coverage drifted. Missing: {string.Join(", ", missing)}. Unexpected: {string.Join(", ", unexpected)}.");
        }
        if (AcceptanceCaseBindings.GroupBy(binding => $"{binding.CaseId}/{binding.AcceptanceName}", StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw new InvalidOperationException("Phase 9c authority contains a duplicate CaseId/acceptance binding.");
        foreach (var binding in AcceptanceCaseBindings)
        {
            if (!knownCases.Contains(binding.CaseId))
                throw new InvalidOperationException($"Phase 9c binding '{binding.AcceptanceName}' references an unknown CaseId '{binding.CaseId}'.");
            if (!string.Equals(binding.EvidenceVector, knownVectors[binding.CaseId], StringComparison.Ordinal))
                throw new InvalidOperationException($"Phase 9c binding '{binding.AcceptanceName}' has a vector mismatch for CaseId '{binding.CaseId}'.");
            if (binding.RequiredRunners.Any(runner => !RunnerNames.Contains(runner)))
                throw new InvalidOperationException($"Phase 9c binding '{binding.AcceptanceName}' references an unknown runner.");
        }
    }
}
