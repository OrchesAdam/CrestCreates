# Generated draft identity binding

Primary owns design/review; GPT-5.6 Luna high owns implementation. This is a
prerequisite repair for Issue #87's approved-content handoff, not a new authoring
or activation authority.

## Evidence and scope

The real CreateDescriptorDraftAsync -> PreviewDescriptorPackageAsync -> default
materializer/package builder path saves the requested ID on the draft envelope,
but generated projection leaves the actual descriptor ID empty. The primary
independently reproduced the failing retained-content test. The original fixture
also omitted editable payload Version; correct that input before interpreting
version behavior. Version is already an AgentDraftField and is checked against
ProposedVersion by the existing draft validator.

ContractModelBuilder intentionally excludes immutable Id from editable fields.
Both generated Create and Merge construct descriptors without that property;
Merge also risks erasing a valid existing ID. Keep Id out of editable DTOs. The
phase-7a design and current validator require envelope identity == payload identity.
Do not repair descriptors in test helpers, validators, materializers or handlers.

## Single generated implementation

Require descriptorId explicitly in generated AgentDraftPayloadProjection.Create.
The one production caller passes request.DescriptorId. Generate Id assignment
alongside the existing typed field initializers for every supported kind. Update
all in-repository callers; do not retain an identityless Create overload/fallback.
Reject absent/blank identity through the established typed projection-error result;
preserve exact nonblank identity without normalization.

Generated Merge copies Id from the existing descriptor, independently of editable
ChangedFields. It must not expose Id as an editable field or reconstruct identity
from patch content. Invalid existing identity must fail closed, not be fabricated.
Use the generator's existing kind model; no handwritten runtime six-kind switch,
reflection, service locator, new wire DTO identity field or second materializer.

Version remains the existing editable field. Valid fixtures explicitly provide a
matching payload Version and ProposedVersion. Keep validator mismatch rejection;
do not silently overwrite versions or introduce another version authority.

## Validation

Preserve the real failing baseline with valid Version input and a Merge baseline
starting from a nonempty ID. Test generated Create/Merge identity for all supported
kinds, invalid identity, and the real control-plane create -> update -> preview
path. Verify actual definitions and manifest refs, not only the draft envelope.
Retain draft validation for conflicting versions and supported patch semantics.

Run relevant DraftContracts/generator and ControlPlane suites and affected boundary
gates. Extend the existing ControlPlane.JsonContracts.AotFixture to execute bound
Create and identity-preserving Merge; real publish, native link and run are required.
Record public C# projection signature change; wire DTOs remain unchanged. Complete
final-head CI before marking the successor PR ready. Actual HumanTask-approved
content selection and fresh Asset host remain subsequent acceptance work.
