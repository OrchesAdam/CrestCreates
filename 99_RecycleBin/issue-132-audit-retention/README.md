# Issue #132 — legacy audit cleanup exit

Archived on 2026-10-27 as part of the Audit retention/governance design (issue #132, §10).

The legacy AuditLog cleanup chain was a naked bulk delete over the compatibility AuditLog model:

- `src/Framework/Ddd/CrestCreates.Application/AuditLog/AuditLogCleanupAppService.cs`
- `src/Framework/Ddd/CrestCreates.Application.Contracts/DTOs/AuditLog/CleanupAuditLogsDto.cs`
- `src/Framework/Ddd/CrestCreates.Application.Contracts/Interfaces/IAuditLogCleanupAppService.cs`
- `tests/Framework/Web/CrestCreates.IntegrationTests/AuditLogIntegrationTests.cs` (original copy; the
  five `AuditLog_Cleanup_*` tests and the two cleanup end-to-end flows were removed from the live file,
  while the legacy AuditLog **query** tests remain as compatibility coverage)

Also removed in place (not archived as files): `IAuditLogRepository.DeleteOlderThanAsync` and its EF
implementation, plus the DI registrations in `AddAuditLogging` and the test hosts.

Reason: the old entry accepted arbitrary `BeforeTime`/negative `RetentionDays`, had no preview, hold,
batch budget, receipt, or tenant-scope authority, and returned a pre-delete `Count` as the deleted
count. The formal retention mainline is the AuditEnvelope PostgreSQL ledger/receipt protocol. If legacy
AuditLog data cleanup is still needed operationally, it must be a one-off migration tool, not a
framework default service.

Exit condition documented in `docs/superpowers/specs/2026-10-27-issue-132-audit-retention-governance-design.md` §10.
