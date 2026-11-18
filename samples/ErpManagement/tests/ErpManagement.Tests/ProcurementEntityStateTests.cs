using ErpManagement.Domain.Entities;
using ErpManagement.Domain.Shared.Enums;
using FluentAssertions;
using Xunit;

namespace ErpManagement.Tests;

public class ProcurementEntityStateTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void Requisition_submit_requires_lines_and_moves_to_pending()
    {
        var r = new PurchaseRequisition(Guid.NewGuid(), "PR-1", "user-1", Tenant);
        var submit = () => r.Submit();
        submit.Should().Throw<InvalidOperationException>("a requisition without lines cannot be submitted");

        r.ReplaceLines(new[]
        {
            new PurchaseRequisitionLine(Guid.NewGuid(), r.Id, Guid.NewGuid(), 2m, 50m, 1, Tenant)
        });
        r.Submit();
        r.Status.Should().Be(RequisitionStatus.PendingApproval);
        r.TotalAmount.Should().Be(100m);
    }

    [Fact]
    public void Requisition_approve_then_convert_only_from_correct_states()
    {
        var r = new PurchaseRequisition(Guid.NewGuid(), "PR-2", "user-1", Tenant);
        r.ReplaceLines(new[] { new PurchaseRequisitionLine(Guid.NewGuid(), r.Id, Guid.NewGuid(), 1m, 10m, 1, Tenant) });
        r.Submit();

        r.Approve("mgr", "ok");
        r.Status.Should().Be(RequisitionStatus.Approved);

        var orderId = Guid.NewGuid();
        r.MarkConverted(orderId);
        r.Status.Should().Be(RequisitionStatus.Converted);
        r.ConvertedOrderId.Should().Be(orderId);

        var again = () => r.Approve("mgr", "ok");
        again.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Requisition_reject_from_pending()
    {
        var r = new PurchaseRequisition(Guid.NewGuid(), "PR-3", "user-1", Tenant);
        r.ReplaceLines(new[] { new PurchaseRequisitionLine(Guid.NewGuid(), r.Id, Guid.NewGuid(), 1m, 10m, 1, Tenant) });
        r.Submit();
        r.Reject("mgr", "no budget");
        r.Status.Should().Be(RequisitionStatus.Rejected);
    }

    [Fact]
    public void PurchaseOrder_confirm_then_record_receipt_progresses_status()
    {
        var po = new PurchaseOrder(Guid.NewGuid(), "PO-1", Guid.NewGuid(), Tenant);
        var line = new PurchaseOrderLine(Guid.NewGuid(), po.Id, Guid.NewGuid(), 10m, 5m, 1, Guid.NewGuid(), Guid.NewGuid(), Tenant);
        po.ReplaceLines(new[] { line });

        var confirmWithoutLines = () => new PurchaseOrder(Guid.NewGuid(), "PO-x", Guid.NewGuid(), Tenant).Confirm();
        confirmWithoutLines.Should().Throw<InvalidOperationException>();

        po.Confirm();
        po.Status.Should().Be(PurchaseOrderStatus.Confirmed);

        po.RecordReceipt(new Dictionary<Guid, decimal> { [line.Id] = 4m });
        po.Status.Should().Be(PurchaseOrderStatus.PartiallyReceived);

        po.RecordReceipt(new Dictionary<Guid, decimal> { [line.Id] = 6m });
        po.Status.Should().Be(PurchaseOrderStatus.Received);
    }

    [Fact]
    public void PurchaseOrder_line_rejects_over_receipt()
    {
        var po = new PurchaseOrder(Guid.NewGuid(), "PO-2", Guid.NewGuid(), Tenant);
        var line = new PurchaseOrderLine(Guid.NewGuid(), po.Id, Guid.NewGuid(), 5m, 1m, 1, Guid.NewGuid(), Guid.NewGuid(), Tenant);
        po.ReplaceLines(new[] { line });
        po.Confirm();

        var over = () => po.RecordReceipt(new Dictionary<Guid, decimal> { [line.Id] = 6m });
        over.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GoodsReceipt_post_requires_lines_and_is_terminal()
    {
        var receipt = new GoodsReceipt(Guid.NewGuid(), "GR-1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Tenant);
        var postEmpty = () => receipt.Post(DateTimeOffset.UtcNow);
        postEmpty.Should().Throw<InvalidOperationException>();

        receipt.ReplaceLines(new[]
        {
            new GoodsReceiptLine(Guid.NewGuid(), receipt.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3m, 2m, Tenant)
        });
        receipt.Post(DateTimeOffset.UtcNow);
        receipt.Status.Should().Be(GoodsReceiptStatus.Posted);
    }
}