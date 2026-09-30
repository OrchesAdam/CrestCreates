# Asset proposal retention and review replay

The live bounded HumanTask proposal passed parsing/review, but only its run summary
was retained. A summary cannot be approved or used to reconstruct exact content.
The next slice proves retention through existing production contracts before
adding any new authoring protocol or deployment owner.

Use the existing tenant-scoped IDescriptorDraftStore with the complete PostgreSQL
Runtime provider plus AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence.
Its official codec already owns explicit payload types, nullable HumanTask schema
refs and Outcomes. Do not expose the internal codec, invent a test-specific JSON
proposal format, or serialize abstract DescriptorDraftPayload through the incomplete
Authoring result context as if it round-tripped concrete data.

First add an offline Asset composition test: real provider-output parser creates
an Agent-owned HumanTask draft; real review succeeds; save the exact typed draft
to the PostgreSQL store; dispose the provider; construct a fresh provider over the
same isolated test schema; load the same tenant/draft identity; assert the complete
retained contract/definition hashes and author/version/status fields; re-run the
real review before the draft can be considered eligible for further governance.
A different tenant must not read it. Use production provider/migration/store APIs,
not a fake persistence adapter. No approval, activation, runtime registry mutation
or claim of durable approval/deployment is permitted.

A deterministic parsed fixture is not new live-model evidence. Preserve that
separation explicitly. First prove the store composition; only then decide whether
the opt-in live probe should retain accepted proposals in a deliberate persistent
local scope. Existing schema cleanup behavior must not accidentally erase a
proposal advertised as available for a later human review. Do not make an extra
model request during this offline slice.
