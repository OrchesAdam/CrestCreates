# Asset two-stage business review prerequisite

Design/review owner: primary agent. Coding owner: GPT-5.6 Luna, high.
Issue #87; depends on the Workflow condition repair in PR #91.

## Scope of this implementation slice

Prove B01–B05 of the accepted Phase 10c design using the real Asset host, SQLite
business state, PostgreSQL Workflow/HumanTask state and required Outbox consumer.
This slice does not activate v2 in the default application and does not complete
#87. Approved package handoff, live authoring and restart evidence remain later
acceptance work. Tests may compose candidate descriptors explicitly; that is
candidate behavior verification, not deployment approval.

## Business meaning belongs to Asset

The existing Asset HumanTask is a terminal maintenance decision. Preserve its
descriptor identity, version and hash for v1. Add one compiled, bounded initial
review HumanTask contract whose approval is nonterminal and whose rejection is
terminal. A candidate v2 workflow places that initial review before the existing
terminal review, guarded by `PreviousHumanTaskApproved`.

Distinguish these business roles using the exact known HumanTask contract identity
and version, with existing pin validation. Do not use authored workflow step names,
the current latest workflow version, remaining-step counts, caller-supplied role
flags, or a copy of Workflow condition evaluation. An unknown or changed task
contract must not silently acquire terminal-decision authority.

The required Asset consumer may acknowledge a valid initial approval without
applying a terminal business decision. Initial rejection and existing terminal
review decisions continue through the existing capability/authorization/domain
path. This bounded domain distinction does not require a generic Workflow
"business completion disposition": Workflow cannot determine whether an arbitrary
business application considers one of its steps terminal.

Retain the existing responsibility boundaries: authorization and tenant context
keep their current owners, Workflow evaluates conditions, HumanTask owns durable
completion, Outbox owns delivery/receipts, and Asset owns maintenance state changes.
Do not poll and abort a final task to emulate rejected condition routing.

## Host and API behavior

Default production composition still registers/selects maintenance workflow v1.
Expose only the narrow composition seams needed for a later evidence-validated
inventory; do not add a public enable-v2 boolean or caller-authorized package hash.
Candidate v2 descriptors belong to the test composition for this slice.

`CompleteAsync` must stop waiting exclusively for terminal Asset state. A successful
initial approval can return after reliable continuation is observable while the
Asset remains pending. Preserve terminal behavior for v1, initial rejection and
final review. Observe persisted runtime state/receipts rather than adding an
independent continuation protocol. A replay of initial approval must never become
a terminal decision merely because the workflow has since completed.

## Independent acceptance observations

Use an integration entry point at the real Asset HTTP/capability host boundary.
Do not substitute direct domain-method tests or Markdown assertions.

| Case | Observation |
| --- | --- |
| B01 | Default v1 still has one review; approval restores prior Available/Assigned state. |
| B02 | Candidate v2 initial approval leaves the Asset pending and creates exactly one final HumanTask. |
| B03 | Final approval restores prior state and produces one terminal maintenance record. |
| B04 | Initial rejection restores prior state as rejected; no final task is created. |
| B05 | Final rejection creates a rejected decision and no approved terminal record. |
| Replay | Redelivery/repeated initial approval cannot apply a terminal decision or duplicate final tasks/records. |
| Boundary | Wrong-tenant and unauthorized completion cannot mutate task or Asset state. |

Keep v1 and v2 identities observable in assertions. Candidate behavior tests do not
prove B06/B08 activation authority, B10 retained-pin deployment, B11 process restart,
or model quality. Preserve existing native fixture composition; maintain a native
candidate case if the changed host/consumer mainline requires it.

## Handoff to subsequent work

Before authoring/activation implementation, exercise the existing control-plane
request, HumanTask approval, package and evidence recheck APIs. A live, authoritative
control-plane process may authorize a fresh runtime host; runtime process restart
does not by itself require control-plane process restart. Do not infer a need for
new durable activation infrastructure merely from in-memory implementation names.
If the bounded handoff cannot be expressed, record a concrete failing acceptance
case before proposing a platform capability through #88. Never represent an
inventory object or successful sample report as approval authority.

Commit implementation and executable tests in reviewable chunks. Report exact
commands and results. Keep this PR separate from #91; initially it may be stacked
on #91's branch, with that dependency explicit in its description.
