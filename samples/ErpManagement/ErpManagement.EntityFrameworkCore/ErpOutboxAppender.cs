using CrestCreates.Data.EFCore.UnitOfWork;
using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using CrestCreates.Runtime.Persistence.PostgreSql;
using ErpManagement.Application.Events;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace ErpManagement.EntityFrameworkCore;

public sealed class ErpOutboxAppender(IEfCoreTransactionBorrower borrower, IPostgreSqlOutboxAppender appender) : IErpOutboxAppender
{
    public async ValueTask AppendAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (!borrower.TryBorrowCurrent(out var context, out var transaction) ||
            context is not ErpManagementDbContext || transaction?.GetDbTransaction() is not NpgsqlTransaction postgresTransaction)
        {
            throw new InvalidOperationException("ERP event append requires an active managed ERP PostgreSQL transaction.");
        }

        await appender.AppendAsync(postgresTransaction, message, cancellationToken);
    }
}
