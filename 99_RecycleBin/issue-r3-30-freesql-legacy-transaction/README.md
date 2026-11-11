# R3-30 — FreeSql legacy transaction track archive

Archived 2026-11-11 from `src/Persistence/CrestCreates.Data.FreeSql/Attributes/TransactionalAttribute.cs`.

## Reason

R3-30 required the old FreeSql `TransactionalAttribute` / `TransactionHelper` ambient track to be settled against
actual consumers. Repository-wide search found no `[Transactional]` usage, no `TransactionHelper.Execute*` call and no
`SetServiceProvider` caller. The type was an inert AOP declaration plus a helper that opened a FreeSql SDK
`UnitOfWorkManager` transaction outside the unified kernel.

The only execution chain is `AddUnitOfWork` + typed provider binding + `UnitOfWorkManager`; the provider UoW is reached
through `IUnitOfWork` and is registered by `AddFreeSqlUnitOfWorkCore`.

## Exit condition

`CrestCreates.OrmProviders.Tests` asserts the FreeSql assembly no longer exposes `TransactionalAttribute` or
`TransactionHelper`; reintroducing an ambient provider-owned transaction helper fails the guard. The design notes in
`docs/superpowers/specs/2026-10-10-issue-137-uow-transaction-propagation-design.md` were updated accordingly.
