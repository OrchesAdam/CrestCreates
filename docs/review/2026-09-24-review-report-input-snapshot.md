# Captured report facts

Durable review artifacts need to preserve what was reviewed without serializing
runtime descriptor implementations or private topology indexes. The report builder
also reads more detail than AgentReviewResultDto retains. A finite report-input
snapshot captures those facts and the review-time owner identity instead.

Existing Build(request) calls capture an input snapshot and use the same core as
Build(snapshot). Capturing requires the existing visibility-applied precondition;
the data itself grants no authorization. A future artifact store must enforce
tenant/owner/scope access before building or returning a report.

The snapshot preserves diagnostic groups, materialized descriptor facts, topology
counts, impact/compatibility/governance facts, package identities, full hashes and
owner status/version. Mutable source collections are copied. It derives the report's
projected canonical hash input from those same facts. Original activation-binding
hashes are a separate outer-artifact concern, not interchangeable report identities.

Reports still use the template/catalog version, contract version and clock at build
time. Kind summaries remain sorted at render time. Failed reviews and absent optional
analysis remain representable; required nullable members are serialized explicitly
so they can be deserialized. Unsupported versions and malformed structures fail
before rendering. This is an input contract, not a persistent review/request store.

The public IDescriptorReviewReportBuilder interface gains a snapshot overload;
external custom implementations need to implement it. No second rendering engine,
fallback interface or draft-payload protocol was introduced.

Initial focused report tests passed30/30; final full Control Plane regression
passed566/566 after sorting/null-preservation review fixes. Actual NativeAOT
publish/link/run passed1/1 in1m32s. It builds full reports from round-tripped rich
and failed-review inputs with a fixed clock and compares complete generated JSON
output, including the CONTROL_PLANE_REPORT_INPUT_NATIVEAOT_OK marker.
Full CI remains pending. No model request, retained DB mutation, approval,
activation or merge.
