using System.Linq.Expressions;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Repositories;
using ErpManagement.Domain.Entities;
using ErpManagement.Domain.Inventory;
using ErpManagement.Domain.Shared.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace ErpManagement.Tests;

public class StockPostingTests
{
    [Fact]
    public async Task Two_movements_to_a_new_balance_create_one_balance_and_ordered_ledger_entries()
    {
        var balances = new Mock<ICrestRepositoryBase<StockBalance, Guid>>();
        var transactions = new Mock<ICrestRepositoryBase<StockTransaction, Guid>>();
        var entries = new List<StockTransaction>();
        balances.Setup(x => x.GetAsync(It.IsAny<Expression<Func<StockBalance, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockBalance?)null);
        transactions.Setup(x => x.InsertAsync(It.IsAny<StockTransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockTransaction transaction, CancellationToken _) =>
            {
                entries.Add(transaction);
                return transaction;
            });
        var actorId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.Id).Returns(actorId.ToString());
        var manager = new StockPostingManager(balances.Object, transactions.Object, currentUser.Object);
        var movement = new StockMovementInput(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2m, StockMovementType.Receipt);
        var sourceId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var beforePosting = DateTime.UtcNow;

        var results = await manager.PostAsync(Guid.NewGuid(), [movement, movement with { SignedQuantity = 3m }],
            false, nameof(GoodsReceipt), sourceId, sourceOperationId: operationId);

        balances.Verify(x => x.InsertAsync(It.Is<StockBalance>(b => b.Quantity == 5m), It.IsAny<CancellationToken>()), Times.Once);
        entries.Select(x => x.BalanceAfter).Should().Equal(2m, 5m);
        results.Select(x => x.NewQuantity).Should().Equal(2m, 5m);
        var afterPosting = DateTime.UtcNow;
        foreach (var entry in entries)
        {
            entry.CreationTime.Should().BeOnOrAfter(beforePosting).And.BeOnOrBefore(afterPosting);
            entry.CreationTime.Kind.Should().Be(DateTimeKind.Utc);
            entry.CreatorId.Should().Be(actorId);
            entry.SourceDocType.Should().Be(nameof(GoodsReceipt));
            entry.SourceDocId.Should().Be(sourceId);
            entry.SourceOperationId.Should().Be(operationId);
        }
    }
}
