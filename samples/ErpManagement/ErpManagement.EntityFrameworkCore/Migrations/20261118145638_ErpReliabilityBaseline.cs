using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpManagement.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class ErpReliabilityBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "StockEventEmittedAt", table: "GoodsReceipts");

            migrationBuilder.AddColumn<Guid>(
                name: "SourceOperationId",
                table: "StockTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OperationId",
                table: "WorkOrderReports",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationResult",
                table: "WorkOrderReports",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OperationId",
                table: "SettlementRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationResult",
                table: "SettlementRecords",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayloadHash",
                table: "IntegrationEventInboxes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderReports_TenantId_WorkOrderId_OperationId",
                table: "WorkOrderReports",
                columns: new[] { "TenantId", "WorkOrderId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SettlementRecords_TenantId_LedgerId_OperationId",
                table: "SettlementRecords",
                columns: new[] { "TenantId", "LedgerId", "OperationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkOrderReports_TenantId_WorkOrderId_OperationId",
                table: "WorkOrderReports");

            migrationBuilder.DropIndex(
                name: "IX_SettlementRecords_TenantId_LedgerId_OperationId",
                table: "SettlementRecords");

            migrationBuilder.DropColumn(
                name: "OperationId",
                table: "WorkOrderReports");

            migrationBuilder.DropColumn(
                name: "OperationResult",
                table: "WorkOrderReports");

            migrationBuilder.DropColumn(
                name: "OperationId",
                table: "SettlementRecords");

            migrationBuilder.DropColumn(
                name: "OperationResult",
                table: "SettlementRecords");

            migrationBuilder.AddColumn<DateTimeOffset>(name: "StockEventEmittedAt", table: "GoodsReceipts",
                type: "timestamp with time zone", nullable: true);

            migrationBuilder.DropColumn(
                name: "SourceOperationId",
                table: "StockTransactions");

            migrationBuilder.DropColumn(
                name: "PayloadHash",
                table: "IntegrationEventInboxes");
        }
    }
}
