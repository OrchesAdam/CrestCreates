using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using CrestCreates.Authorization.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Preparation;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.TestServices;
using ErpManagement.Application.Contracts.Interfaces;
using CrestCreates.Data.Abstractions;
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Features;
using Microsoft.Extensions.Configuration;
using ErpManagement.Application.Contracts.DTOs;
using ErpManagement.Domain.Entities;
using ErpManagement.Domain.Inventory;
using ErpManagement.Domain.Shared.Enums;
using ErpManagement.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using CrestCreates.Application.Contracts.DTOs.AuditLog;
using CrestCreates.Domain.Permission;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Xunit;

namespace CrestCreates.IntegrationTests;

public class AuditLogIntegrationTests : IClassFixture<ErpManagementWebApplicationFactory>
{
    private const string HostTenantId = "host";
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Admin123!";

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ErpManagementWebApplicationFactory _factory;

    public AuditLogIntegrationTests(ErpManagementWebApplicationFactory factory)
    {
        _factory = factory;
        // Trigger host initialization before any tests run
        _ = _factory.CreateClient();
    }

    [Fact]
    public async Task Http_audit_projects_to_the_recorded_tenant_database_and_persists_non_http_facts()
    {
        var isolatedDatabase = await ExternalTestServices.CreateIsolatedDatabaseAsync(_factory.ConnectionString, "erp_audit");
        var tenantConnection = new NpgsqlConnectionStringBuilder(isolatedDatabase) { SearchPath = "public" }.ConnectionString;
        var tenantId = Guid.NewGuid().ToString();
        try
        {
            await using var tenantDb = new ErpManagementDbContext(
                new DbContextOptionsBuilder<ErpManagementDbContext>().UseNpgsql(tenantConnection).Options);
            await tenantDb.Database.EnsureCreatedAsync();

            var directory = new Mock<ITenantProvider>();
            directory.Setup(x => x.GetTenantAsync(tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TenantInfo(tenantId, "audit-projection", tenantConnection));
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Default"] = _factory.ConnectionString }).Build());
            services.AddSingleton<ICurrentTenant, CurrentTenant>();
            services.AddSingleton(directory.Object);
            new ErpManagement.EntityFrameworkCore.Modules.EntityFrameworkCoreModule().OnConfigureServices(services);
            await using var scopeProvider = services.BuildServiceProvider();
            var tenant = scopeProvider.GetRequiredService<ICurrentTenant>();
            var canonical = _factory.Services.GetRequiredService<PostgreSqlAuditSink>();
            var sink = new ErpHttpAuditSink(canonical, scopeProvider.GetRequiredService<IServiceScopeFactory>(), tenant,
                directory.Object);
            var preparer = _factory.Services.GetRequiredService<IAuditEnvelopePreparer>();
            var envelope = new AuditEnvelope
            {
                AuditId = Guid.NewGuid().ToString("N"),
                OccurredAt = DateTimeOffset.UtcNow,
                TenantId = tenantId,
                CorrelationId = Guid.NewGuid().ToString("N"),
                Actor = new AuditActor { Kind = "user", Id = "projection-test" },
                Action = new AuditAction { Kind = "http.request", Name = "GET /api/material" },
                Target = new AuditTarget { Kind = "http.endpoint", Id = "GET /api/material" },
                Outcome = new AuditOutcome { Status = "succeeded" },
                Runtime = new AuditRuntimeContext { InvocationSource = "http" }
            };
            var http = await preparer.PrepareAsync(envelope);
            http.IsAccepted.Should().BeTrue(string.Join(", ", http.Issues));
            (await sink.WriteAsync(http.Envelope!)).Status.Should().Be(AuditSinkWriteStatus.Accepted);
            (await sink.WriteAsync(http.Envelope!)).Status.Should().Be(AuditSinkWriteStatus.Duplicate);
            tenant.Id.Should().BeNull();
            var httpId = Guid.Parse(envelope.AuditId);
            (await tenantDb.AuditLogs.SingleAsync(row => row.Id == httpId)).TenantId.Should().Be(tenantId);
            await using var hostScope = _factory.Services.CreateAsyncScope();
            var hostDb = hostScope.ServiceProvider.GetRequiredService<ErpManagementDbContext>();
            (await hostDb.AuditLogs.AnyAsync(row => row.Id == httpId)).Should().BeFalse();

            var method = await preparer.PrepareAsync(envelope with
            {
                AuditId = Guid.NewGuid().ToString("N"),
                Action = new AuditAction { Kind = "method.invoke", Name = "Probe" },
                Target = new AuditTarget { Kind = "application.method", Id = "Probe" },
                Runtime = new AuditRuntimeContext { InvocationSource = "system" }
            });
            method.IsAccepted.Should().BeTrue(string.Join(", ", method.Issues));
            (await sink.WriteAsync(method.Envelope!)).Status.Should().Be(AuditSinkWriteStatus.Accepted);
            (await sink.WriteAsync(method.Envelope!)).Status.Should().Be(AuditSinkWriteStatus.Duplicate);
            var methodId = Guid.Parse(method.Envelope!.AuditId);
            (await tenantDb.AuditLogs.AnyAsync(row => row.Id == methodId)).Should().BeFalse();
        }
        finally
        {
            await ExternalTestServices.DropDatabaseAsync(_factory.ConnectionString, ExternalTestServices.DatabaseName(isolatedDatabase));
        }
    }

    [Fact]
    public async Task Readiness_uses_business_database_and_approval_store_probes()
    {
        var health = _factory.Services.GetRequiredService<HealthCheckService>();
        var report = await health.CheckHealthAsync(registration => registration.Name is "erp-postgresql" or "erp-approval-runtime");
        report.Entries.Keys.Should().BeEquivalentTo("erp-postgresql", "erp-approval-runtime");
        report.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task AuditLog_GetList_ShouldReturnPagedResults()
    {
        await _factory.EnsureSeedCompleteAsync();

        // Verify seed worked - query should return seeded data
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        // When
        var response = await client.GetAsync("/api/audit-logs?pageIndex=0&pageSize=10");

        // Then
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var envelope = await ReadJsonAsync<DynamicApiResponse<PagedResultResponse<AuditLogResponse>>>(response);
        envelope.Data.Should().NotBeNull();
        envelope.Data!.TotalCount.Should().BeGreaterThanOrEqualTo(0);
        envelope.Data.Items.Should().NotBeNull();
        envelope.Data.PageIndex.Should().Be(0);
        envelope.Data.PageSize.Should().Be(10);
    }

    [Fact]
    public async Task AuditLog_GetList_ShouldReturnEmpty_WhenNoAuditLogs()
    {
        await _factory.EnsureSeedCompleteAsync();

        // Given
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        // When - query with a keyword filter that matches no records
        var response = await client.GetAsync("/api/audit-logs?keyword=__nonexistent_keyword_for_test__&pageIndex=0&pageSize=10");

        // Then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await ReadJsonAsync<DynamicApiResponse<PagedResultResponse<AuditLogResponse>>>(response);
        envelope.Data!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task AuditLog_GetList_ShouldFilterByStatus()
    {
        await _factory.EnsureSeedCompleteAsync();

        // Given
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        // When - filter by status=0 (Success)
        var response = await client.GetAsync("/api/audit-logs?status=0&pageIndex=0&pageSize=10");

        // Then
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var envelope = await ReadJsonAsync<DynamicApiResponse<PagedResultResponse<AuditLogResponse>>>(response);
        envelope.Data.Should().NotBeNull();
        // All returned items should have Status=0
        foreach (var item in envelope.Data!.Items)
        {
            item.Status.Should().Be(0);
        }
    }

    [Fact]
    public async Task AuditLog_GetList_ShouldFilterByHttpMethod()
    {
        await _factory.EnsureSeedCompleteAsync();

        // Given
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        // When - filter by HttpMethod=GET
        var response = await client.GetAsync("/api/audit-logs?httpMethod=GET&pageIndex=0&pageSize=10");

        // Then
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var envelope = await ReadJsonAsync<DynamicApiResponse<PagedResultResponse<AuditLogResponse>>>(response);
        envelope.Data.Should().NotBeNull();
        foreach (var item in envelope.Data!.Items)
        {
            item.HttpMethod.Should().Be("GET");
        }
    }

    [Fact]
    public async Task AuditLog_GetList_ShouldContainExpectedFields()
    {
        await _factory.EnsureSeedCompleteAsync();

        // Given
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        // When
        var response = await client.GetAsync("/api/audit-logs?pageIndex=0&pageSize=1");

        // Then
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var envelope = await ReadJsonAsync<DynamicApiResponse<PagedResultResponse<AuditLogResponse>>>(response);
        envelope.Data.Should().NotBeNull();
        envelope.Data!.Items.Should().NotBeEmpty("seed data should provide at least one audit log record");

        var first = envelope.Data.Items.First();
        first.Id.Should().NotBeEmpty();
        first.ExecutionTime.Should().NotBe(default(DateTime));
        first.ExecutionTime.Should().BeBefore(DateTime.UtcNow.AddMinutes(5));
        first.ExecutionTime.Should().BeAfter(DateTime.UtcNow.AddYears(-1));
        first.Duration.Should().BeGreaterThanOrEqualTo(0L);
        first.Status.Should().BeInRange(0, 1);
    }

    [Fact]
    public async Task AuditLog_GetList_TenantContext_ShouldOnlyReturnOwnTenantLogs()
    {
        await _factory.EnsureSeedCompleteAsync();

        // Given - tenant-a context
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, ErpManagementWebApplicationFactory.AuditTenantName);

        // When - query all logs without tenantId filter
        var response = await client.GetAsync("/api/audit-logs?pageIndex=0&pageSize=100");

        // Then - should only see tenant-a logs, not host logs
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var envelope = await ReadJsonAsync<DynamicApiResponse<PagedResultResponse<AuditLogResponse>>>(response);
        envelope.Data.Should().NotBeNull();

        envelope.Data!.Items.Should().NotBeEmpty();
        foreach (var item in envelope.Data.Items)
        {
            item.TenantId.Should().Be(_factory.AuditTenantId.ToString(), "tenant context should only see own tenant's logs");
        }
    }

    [Fact]
    public async Task AuditLog_GetList_TenantContext_ShouldNotBeAbleToQueryOtherTenantById()
    {
        await _factory.EnsureSeedCompleteAsync();

        // Given - tenant-a context trying to query seeded host logs
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, ErpManagementWebApplicationFactory.AuditTenantName);

        // When - try to query host tenant logs by passing tenantId parameter
        var response = await client.GetAsync($"/api/audit-logs?tenantId={_factory.HostTenantId}&pageIndex=0&pageSize=100");

        // The tenant restriction and the requested filter must exclude every host row.
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var envelope = await ReadJsonAsync<DynamicApiResponse<PagedResultResponse<AuditLogResponse>>>(response);
        envelope.Data.Should().NotBeNull();

        envelope.Data!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task AuditLog_GetList_HostContext_ShouldBeAbleToQuerySpecificTenant()
    {
        await _factory.EnsureSeedCompleteAsync();

        // Given - host context querying tenant-a logs
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        // When - query tenant-a logs specifically
        var response = await client.GetAsync($"/api/audit-logs?tenantId={_factory.AuditTenantId}&pageIndex=0&pageSize=100");

        // Then - should see only tenant-a logs (or at least some results when filtering by tenant-a)
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var envelope = await ReadJsonAsync<DynamicApiResponse<PagedResultResponse<AuditLogResponse>>>(response);
        envelope.Data.Should().NotBeNull();

        // All returned items should be for tenant-a
        envelope.Data!.Items.Should().NotBeEmpty();
        foreach (var item in envelope.Data.Items)
        {
            item.TenantId.Should().Be(_factory.AuditTenantId.ToString(), "host context with tenantId filter should return only that tenant's logs");
        }
    }

    [Fact]
    public async Task AuditLog_GetList_HostContext_ShouldBeAbleToQueryAllTenants()
    {
        await _factory.EnsureSeedCompleteAsync();

        // Given - host context without tenant filter
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, HostTenantId);

        // When - query all logs with large page size
        var response = await client.GetAsync("/api/audit-logs?pageIndex=0&pageSize=1000");
        var rawContent = await response.Content.ReadAsStringAsync();

        // Then - query should succeed and return properly structured response
        response.StatusCode.Should().Be(HttpStatusCode.OK, rawContent);
        var envelope = await ReadJsonAsync<DynamicApiResponse<PagedResultResponse<AuditLogResponse>>>(response);
        envelope.Data.Should().NotBeNull();
        // The host context can see all audit logs (no tenant filtering)
        // Results may include login audit logs (null TenantId) and/or seeded logs (host/tenant-a)
        envelope.Data.Items.Should().NotBeEmpty($"host query should return audit logs. Response: {rawContent}");
    }

    [Fact]
    public async Task Settlement_and_report_replay_the_same_operation_without_repeating_business_effects()
    {
        await _factory.EnsureSeedCompleteAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        using var tenantScope = tenantContext.Change(await scope.ServiceProvider.GetRequiredService<ITenantProvider>()
            .GetTenantAsync(_factory.HostTenantId.ToString()));
        var db = scope.ServiceProvider.GetRequiredService<ErpManagementDbContext>();
        var tenant = _factory.HostTenantId;
        // 专属物料/仓库/库位：共享种子库存会被其他测试的出入库消耗，
        // 导致本测试的领料在串行顺序变化时出现 Insufficient stock。
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var unit = new UnitOfMeasure(Guid.NewGuid(), $"U-{suffix}", "Replay unit", tenant);
        var warehouse = new Warehouse(Guid.NewGuid(), $"W-{suffix}", "Replay warehouse", tenant);
        var location = new StorageLocation(Guid.NewGuid(), warehouse.Id, $"L-{suffix}", "Replay location", tenant);
        var material = new Material(Guid.NewGuid(), $"M-{suffix}", "Replay material", unit.Id, tenant);
        material.SetDefaultWarehouse(warehouse.Id);
        db.UnitsOfMeasure.Add(unit);
        db.Warehouses.Add(warehouse);
        db.StorageLocations.Add(location);
        db.Materials.Add(material);
        await db.SaveChangesAsync();
        await PostStockAsync(tenant, Guid.NewGuid(), new StockMovementInput(material.Id, warehouse.Id, location.Id, 10m, StockMovementType.Receipt));
        var order = new WorkOrder(Guid.NewGuid(), "RP-" + Guid.NewGuid().ToString("N"), tenant);
        order.Plan(material.Id, 2m, null, null);
        db.WorkOrders.Add(order);
        var ledger = new ArApLedger(Guid.NewGuid(), ArApDirection.Payable, "Test", Guid.NewGuid(), Guid.NewGuid(), 100m, tenant);
        db.ArApLedgers.Add(ledger);
        await db.SaveChangesAsync();
        using var identityScope = AuthenticateScope(scope.ServiceProvider, tenant);
        var bom = new Bom(Guid.NewGuid(), material.Id, "test", tenant);
        bom.ReplaceLines([new BomLine(Guid.NewGuid(), bom.Id, material.Id, 1m, 1, tenant)]);
        bom.Activate();
        db.Boms.Add(bom);
        await db.SaveChangesAsync();
        var features = scope.ServiceProvider.GetRequiredService<IFeatureManager>();
        await features.SetTenantAsync("Erp.MesIntegration", tenant.ToString(), "true");
        await features.SetTenantAsync("Erp.CrmIntegration", tenant.ToString(), "true");
        var workOrders = scope.ServiceProvider.GetRequiredService<IWorkOrderAppService>();
        await workOrders.ReleaseAsync(order.Id);
        var finance = scope.ServiceProvider.GetRequiredService<IArApLedgerAppService>();
        var reportId = Guid.NewGuid();
        var settleId = Guid.NewGuid();
        var report = await workOrders.ReportAsync(order.Id, new WorkOrderReportInput { ReportedQuantity = 1m, OperationId = reportId });
        var replay = await workOrders.ReportAsync(order.Id, new WorkOrderReportInput { ReportedQuantity = 1m, OperationId = reportId });
        replay.Id.Should().Be(report.Id);
        var settled = await finance.SettleAsync(ledger.Id, new SettlementInput { Amount = 30m, OperationId = settleId });
        (await finance.SettleAsync(ledger.Id, new SettlementInput { Amount = 30m, OperationId = settleId }))
            .SettledAmount.Should().Be(settled.SettledAmount);
        await Assert.ThrowsAnyAsync<Exception>(() => workOrders.ReportAsync(order.Id,
            new WorkOrderReportInput { ReportedQuantity = 2m, OperationId = reportId }));
        await Assert.ThrowsAnyAsync<Exception>(() => finance.SettleAsync(ledger.Id,
            new SettlementInput { Amount = 40m, OperationId = settleId }));
        await using var check = _factory.Services.CreateAsyncScope();
        var persisted = check.ServiceProvider.GetRequiredService<ErpManagementDbContext>();
        (await persisted.WorkOrderReports.CountAsync(x => x.WorkOrderId == order.Id)).Should().Be(1);
        (await persisted.SettlementRecords.CountAsync(x => x.LedgerId == ledger.Id)).Should().Be(1);
        (await persisted.StockTransactions.CountAsync(x => x.SourceDocId == order.Id)).Should().Be(2);
        (await persisted.ArApLedgers.SingleAsync(x => x.Id == ledger.Id)).SettledAmount.Should().Be(30m);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var settlements = await Task.WhenAll(SettleInNewScope(ledger.Id, 40m, first), SettleInNewScope(ledger.Id, 40m, second));
        settlements.Count(x => x).Should().Be(1);
        await using (var verification = _factory.Services.CreateAsyncScope())
        {
            var state = verification.ServiceProvider.GetRequiredService<ErpManagementDbContext>();
            (await state.ArApLedgers.SingleAsync(x => x.Id == ledger.Id)).SettledAmount.Should().Be(70m);
        }
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var mesOperationId = Guid.NewGuid();
        var crmCode = "CRM-" + Guid.NewGuid().ToString("N");
        // Feature values propagate asynchronously; retry with the same idempotency key until accepted.
        await AssertAsync(async () =>
        {
            using var mes = new HttpRequestMessage(HttpMethod.Post, $"/api/integration/mes/work-orders/{order.Id}/report")
            { Content = JsonContent.Create(new WorkOrderReportInput { ReportedQuantity = 1m, OperationId = mesOperationId }) };
            mes.Headers.Add("X-Integration-Api-Key", config["Integration:Clients:Mes:ApiKey"]);
            using var mesResponse = await _factory.CreateClient().SendAsync(mes);
            mesResponse.IsSuccessStatusCode.Should().BeTrue(await mesResponse.Content.ReadAsStringAsync());
        });
        await AssertAsync(async () =>
        {
            using var crm = new HttpRequestMessage(HttpMethod.Post, "/api/integration/crm/customers")
            { Content = JsonContent.Create(new { code = crmCode, name = "Broker customer" }) };
            crm.Headers.Add("X-Integration-Api-Key", config["Integration:Clients:Crm:ApiKey"]);
            using var crmResponse = await _factory.CreateClient().SendAsync(crm);
            crmResponse.IsSuccessStatusCode.Should().BeTrue(await crmResponse.Content.ReadAsStringAsync());
        });
        await AssertAsync(async () =>
        {
            await using var verification = _factory.Services.CreateAsyncScope();
            var state = verification.ServiceProvider.GetRequiredService<ErpManagementDbContext>();
            (await state.IntegrationEventInboxes.CountAsync(x => x.ContractId == ErpManagement.Application.Contracts.Events.ErpIntegrationWire.MesWorkOrderReleasedId)).Should().Be(1);
            (await state.IntegrationEventInboxes.CountAsync(x => x.ContractId == ErpManagement.Application.Contracts.Events.ErpIntegrationWire.MesWorkOrderCompletedId)).Should().Be(1);
            (await state.IntegrationEventInboxes.CountAsync(x => x.ContractId == ErpManagement.Application.Contracts.Events.ErpIntegrationWire.CrmCustomerChangedId)).Should().Be(1);
        });
    }

    private async Task<bool> SettleInNewScope(Guid ledgerId, decimal amount, Guid operationId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var tenant = await scope.ServiceProvider.GetRequiredService<ITenantProvider>().GetTenantAsync(_factory.HostTenantId.ToString());
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(tenant);
        using var identityScope = AuthenticateScope(scope.ServiceProvider, _factory.HostTenantId);
        try
        {
            await scope.ServiceProvider.GetRequiredService<IArApLedgerAppService>().SettleAsync(ledgerId,
                new SettlementInput { Amount = amount, OperationId = operationId });
            return true;
        }
        catch (CrestCreates.Domain.Shared.Exceptions.CrestValidationException) { return false; }
        catch (CrestConcurrencyException) { return false; }
    }

    private static async Task AssertAsync(Func<Task> assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            try { await assertion(); return; }
            catch (Exception) when (!timeout.IsCancellationRequested) { await Task.Delay(100, timeout.Token); }
        }
    }

    [Fact]
    public async Task Receipt_and_its_outbox_roll_back_together_when_event_append_fails()
    {
        await _factory.EnsureSeedCompleteAsync();
        var tenantId = _factory.HostTenantId.ToString();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ErpManagementDbContext>();
        var tenant = Guid.Parse(tenantId);
        var material = await db.Materials.FirstAsync();
        var warehouse = await db.Warehouses.FirstAsync();
        var location = await db.StorageLocations.FirstAsync(x => x.WarehouseId == warehouse.Id);
        var supplier = await db.Suppliers.FirstAsync();
        var order = new PurchaseOrder(Guid.NewGuid(), "AT-" + Guid.NewGuid().ToString("N"), supplier.Id, tenant);
        var orderLine = new PurchaseOrderLine(Guid.NewGuid(), order.Id, material.Id, 3m, 5m, 1,
            warehouse.Id, location.Id, tenant);
        order.ReplaceLines([orderLine]);
        order.Confirm();
        db.PurchaseOrders.Add(order);
        var receipt = new GoodsReceipt(Guid.NewGuid(), "AT-" + Guid.NewGuid().ToString("N"), order.Id, supplier.Id, warehouse.Id, tenant);
        receipt.ReplaceLines([new GoodsReceiptLine(Guid.NewGuid(), receipt.Id, orderLine.Id, material.Id,
            location.Id, 3m, 5m, tenant)]);
        db.GoodsReceipts.Add(receipt);
        await db.SaveChangesAsync();
        using var identityScope = AuthenticateScope(scope.ServiceProvider, tenant);
        var service = scope.ServiceProvider.GetRequiredService<IGoodsReceiptAppService>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        using var tenantScope = tenantContext.Change(await scope.ServiceProvider.GetRequiredService<ITenantProvider>().GetTenantAsync(tenantId));
        _factory.FailNextErpSaveChanges = new InvalidOperationException("Simulated business flush failure after event append.");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PostAsync(receipt.Id));

        await using var check = _factory.Services.CreateAsyncScope();
        var persisted = check.ServiceProvider.GetRequiredService<ErpManagementDbContext>();
        var saved = await persisted.GoodsReceipts.Include(x => x.Lines).SingleAsync(x => x.Id == receipt.Id);
        saved.Status.Should().Be(GoodsReceiptStatus.Draft);
        saved.Lines.Single().Quantity.Should().Be(3m);
        (await persisted.PurchaseOrders.Include(x => x.Lines).SingleAsync(x => x.Id == order.Id))
            .Lines.Single().ReceivedQuantity.Should().Be(0m);
        (await persisted.StockTransactions.AnyAsync(x => x.SourceDocId == receipt.Id)).Should().BeFalse();
        await using var connection = new NpgsqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""
            SELECT count(*) FROM "{_factory.RuntimeSchema}".runtime_outbox_messages WHERE message_id=@id
            """, connection);
        command.Parameters.AddWithValue("id", $"erp.stock-posted:{receipt.Id}");
        ((long)(await command.ExecuteScalarAsync())!).Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_and_sequential_receipt_posts_apply_business_facts_once()
    {
        var (receiptId, orderId) = await SeedReceiptForDeliveryAsync();
        var (authenticatedClient, _) = await CreateAuthenticatedClientAsync(
            AdminUserName, AdminPassword, _factory.HostTenantId.ToString());
        using var client = authenticatedClient;
        var route = $"/api/goods-receipts/{receiptId}/post";

        await using var connection = new NpgsqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        await using var blocker = await connection.BeginTransactionAsync();
        await using (var rowLock = new NpgsqlCommand("""
            SELECT 1 FROM "GoodsReceipts" WHERE "Id" = @id FOR UPDATE
            """, connection, blocker))
        {
            rowLock.Parameters.AddWithValue("id", receiptId);
            (await rowLock.ExecuteScalarAsync()).Should().Be(1);
        }

        await using var observer = new NpgsqlConnection(_factory.ConnectionString);
        await observer.OpenAsync();
        var first = client.PostAsync(route, content: null);
        var second = client.PostAsync(route, content: null);
        try
        {
            await AssertAsync(async () =>
            {
                await using var waiters = new NpgsqlCommand("""
                    SELECT count(*) FROM pg_stat_activity
                     WHERE datname = current_database() AND wait_event_type = 'Lock'
                       AND query LIKE 'SELECT "Status" FROM "GoodsReceipts"%FOR UPDATE%'
                    """, observer);
                (await waiters.ExecuteScalarAsync()).Should().Be(2L);
            });
        }
        finally
        {
            await blocker.CommitAsync();
        }

        using var firstResponse = await first;
        using var secondResponse = await second;
        using var replay = await client.PostAsync(route, content: null);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK, await firstResponse.Content.ReadAsStringAsync());
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK, await secondResponse.Content.ReadAsStringAsync());
        replay.StatusCode.Should().Be(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        var firstPosted = (await ReadJsonAsync<DynamicApiResponse<GoodsReceiptDto>>(firstResponse)).Data!;
        firstPosted.Status.Should().Be(GoodsReceiptStatus.Posted);
        var secondPosted = (await ReadJsonAsync<DynamicApiResponse<GoodsReceiptDto>>(secondResponse)).Data!;
        var replayed = (await ReadJsonAsync<DynamicApiResponse<GoodsReceiptDto>>(replay)).Data!;
        secondPosted.Should().BeEquivalentTo(firstPosted, options => options.Excluding(x => x.PostedAt));
        replayed.Should().BeEquivalentTo(firstPosted, options => options.Excluding(x => x.PostedAt));
        secondPosted.PostedAt.Should().BeCloseTo(firstPosted.PostedAt!.Value, TimeSpan.FromMicroseconds(1));
        replayed.PostedAt.Should().BeCloseTo(firstPosted.PostedAt.Value, TimeSpan.FromMicroseconds(1));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ErpManagementDbContext>();
        (await db.StockTransactions.CountAsync(x => x.SourceDocId == receiptId)).Should().Be(1);
        (await db.PurchaseOrders.Include(x => x.Lines).SingleAsync(x => x.Id == orderId))
            .Lines.Single().ReceivedQuantity.Should().Be(3m);
        await using var outbox = new NpgsqlCommand($"""
            SELECT count(*) FROM "{_factory.RuntimeSchema}".runtime_outbox_messages WHERE message_id = @id
            """, connection);
        outbox.Parameters.AddWithValue("id", $"erp.stock-posted:{receiptId}");
        (await outbox.ExecuteScalarAsync()).Should().Be(1L);
    }

    [Fact]
    public async Task Receipt_delivery_is_tenant_scoped_and_retries_only_exhausted_dead_letters()
    {
        var (receiptId, _) = await SeedReceiptForDeliveryAsync();
        var (ownerClient, _) = await CreateAuthenticatedClientAsync(
            AdminUserName, AdminPassword, _factory.HostTenantId.ToString());
        var (otherTenantClient, _) = await CreateAuthenticatedClientAsync(
            AdminUserName, AdminPassword, ErpManagementWebApplicationFactory.AuditTenantName);
        using var owner = ownerClient;
        using var otherTenant = otherTenantClient;
        var route = $"/api/goods-receipts/{receiptId}";
        using var posted = await owner.PostAsync($"{route}/post", content: null);
        posted.StatusCode.Should().Be(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());

        async Task<GoodsReceiptDeliveryStatusDto> ReadStatusAsync()
        {
            using var response = await owner.GetAsync($"{route}/delivery-status");
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await ReadJsonAsync<DynamicApiResponse<GoodsReceiptDeliveryStatusDto>>(response)).Data!;
        }

        await AssertAsync(async () =>
        {
            var status = await ReadStatusAsync();
            status.Status.Should().Be("Delivered");
            status.Consumed.Should().BeTrue();
        });

        using var foreignStatus = await otherTenant.GetAsync($"{route}/delivery-status");
        using var foreignRetry = await otherTenant.PostAsync($"{route}/retry-delivery", content: null);
        using var foreignPost = await otherTenant.PostAsync($"{route}/post", content: null);
        foreignStatus.StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreignRetry.StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreignPost.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var deliveredRetry = await owner.PostAsync($"{route}/retry-delivery", content: null);
        deliveredRetry.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var connection = new NpgsqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        var outboxTable = $"\"{_factory.RuntimeSchema}\".runtime_outbox_messages";
        var messageId = $"erp.stock-posted:{receiptId}";
        async Task MoveDeliveryTenantAsync(Guid tenantId)
        {
            await using var command = new NpgsqlCommand($"UPDATE {outboxTable} SET tenant_id = @tenant WHERE message_id = @id",
                connection);
            command.Parameters.AddWithValue("id", messageId);
            command.Parameters.AddWithValue("tenant", tenantId.ToString());
            (await command.ExecuteNonQueryAsync()).Should().Be(1);
        }

        async Task MoveInboxTenantAsync(Guid tenantId)
        {
            await using var command = new NpgsqlCommand("""
                UPDATE "IntegrationEventInboxes" SET "TenantId" = @tenant WHERE "MessageId" = @id
                """, connection);
            command.Parameters.AddWithValue("id", messageId);
            command.Parameters.AddWithValue("tenant", tenantId);
            (await command.ExecuteNonQueryAsync()).Should().Be(1);
        }

        await MoveInboxTenantAsync(_factory.AuditTenantId);
        (await ReadStatusAsync()).Consumed.Should().BeFalse();
        await MoveInboxTenantAsync(_factory.HostTenantId);

        await using var immutableFact = new NpgsqlCommand($"""
            SELECT payload_utf8, integrity_json::text, fencing_token FROM {outboxTable} WHERE message_id = @id
            """, connection);
        immutableFact.Parameters.AddWithValue("id", messageId);
        async Task<(byte[] Payload, string Integrity, long Fence)> ReadFactAsync()
        {
            await using var reader = await immutableFact.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            return (reader.GetFieldValue<byte[]>(0), reader.GetString(1), reader.GetInt64(2));
        }

        var original = await ReadFactAsync();
        await using (var active = new NpgsqlCommand($"""
            UPDATE {outboxTable} SET status = 1, delivered_at = NULL, lease_owner_id = 'test-owner',
                lease_expires_at = clock_timestamp() + interval '1 hour', terminal_lease_owner_id = NULL,
                terminal_fencing_token = NULL WHERE message_id = @id
            """, connection))
        {
            active.Parameters.AddWithValue("id", messageId);
            (await active.ExecuteNonQueryAsync()).Should().Be(1);
        }
        using var activeRetry = await owner.PostAsync($"{route}/retry-delivery", content: null);
        activeRetry.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadStatusAsync()).Status.Should().Be("InFlight");

        async Task SetDeadLetterAsync(string failureCode)
        {
            await using var command = new NpgsqlCommand($"""
                UPDATE {outboxTable} SET status = 3, attempt_count = 100, lease_owner_id = NULL, lease_expires_at = NULL,
                    dead_lettered_at = clock_timestamp(), terminal_lease_owner_id = 'test-owner',
                    terminal_fencing_token = fencing_token, terminal_failure_code = @failure, last_failure_code = @failure
                WHERE message_id = @id
                """, connection);
            command.Parameters.AddWithValue("id", messageId);
            command.Parameters.AddWithValue("failure", failureCode);
            (await command.ExecuteNonQueryAsync()).Should().Be(1);
        }

        await SetDeadLetterAsync("INTEGRITY_MISMATCH");
        using var permanentRetry = await owner.PostAsync($"{route}/retry-delivery", content: null);
        permanentRetry.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await SetDeadLetterAsync("ATTEMPT_BUDGET_EXHAUSTED");
        await MoveDeliveryTenantAsync(_factory.AuditTenantId);
        (await ReadStatusAsync()).Status.Should().Be("NotAppended");
        using var mismatchedRetry = await owner.PostAsync($"{route}/retry-delivery", content: null);
        mismatchedRetry.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await MoveDeliveryTenantAsync(_factory.HostTenantId);
        (await ReadStatusAsync()).Status.Should().Be("DeadLettered");

        var attempts = await Task.WhenAll(owner.PostAsync($"{route}/retry-delivery", content: null),
            owner.PostAsync($"{route}/retry-delivery", content: null));
        using var accepted = attempts.Single(x => x.StatusCode == HttpStatusCode.OK);
        using var rejected = attempts.Single(x => x.StatusCode == HttpStatusCode.Conflict);
        var requeued = (await ReadJsonAsync<DynamicApiResponse<GoodsReceiptDeliveryStatusDto>>(accepted)).Data!;
        requeued.MessageId.Should().Be(messageId);
        requeued.Status.Should().Be("Pending");
        requeued.AttemptCount.Should().Be(0);
        requeued.FailureCode.Should().BeNull();

        await AssertAsync(async () =>
        {
            var status = await ReadStatusAsync();
            status.Status.Should().Be("Delivered");
            status.Consumed.Should().BeTrue();
        });
        var redelivered = await ReadFactAsync();
        redelivered.Payload.Should().Equal(original.Payload);
        redelivered.Integrity.Should().Be(original.Integrity);
        redelivered.Fence.Should().Be(original.Fence + 1);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ErpManagementDbContext>();
        (await db.StockTransactions.CountAsync(x => x.SourceDocId == receiptId)).Should().Be(1);
        (await db.IntegrationEventInboxes.CountAsync(x => x.MessageId == messageId)).Should().Be(1);
    }

    private async Task<(Guid ReceiptId, Guid OrderId)> SeedReceiptForDeliveryAsync()
    {
        await _factory.EnsureSeedCompleteAsync();
        var tenantId = _factory.HostTenantId;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ErpManagementDbContext>();
        var material = await db.Materials.FirstAsync(x => x.TenantId == tenantId);
        var warehouse = await db.Warehouses.FirstAsync(x => x.TenantId == tenantId);
        var location = await db.StorageLocations.FirstAsync(x => x.WarehouseId == warehouse.Id);
        var supplier = await db.Suppliers.FirstAsync(x => x.TenantId == tenantId);
        var order = new PurchaseOrder(Guid.NewGuid(), $"AT-{Guid.NewGuid():N}", supplier.Id, tenantId);
        var line = new PurchaseOrderLine(Guid.NewGuid(), order.Id, material.Id, 3m, 5m, 1,
            warehouse.Id, location.Id, tenantId);
        order.ReplaceLines([line]);
        order.Confirm();
        db.PurchaseOrders.Add(order);
        var receipt = new GoodsReceipt(Guid.NewGuid(), $"AT-{Guid.NewGuid():N}", order.Id, supplier.Id, warehouse.Id, tenantId);
        receipt.ReplaceLines([new GoodsReceiptLine(Guid.NewGuid(), receipt.Id, line.Id, material.Id,
            location.Id, 3m, 5m, tenantId)]);
        db.GoodsReceipts.Add(receipt);
        await db.SaveChangesAsync();
        return (receipt.Id, order.Id);
    }

    [Fact]
    public async Task Stock_count_notes_only_update_succeeds()
    {
        var tenant = _factory.AuditTenantId;
        var movement = new StockMovementInput(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10m, StockMovementType.Receipt);
        await PostStockAsync(tenant, Guid.NewGuid(), movement);
        var (client, _) = await CreateAuthenticatedClientAsync(AdminUserName, AdminPassword, ErpManagementWebApplicationFactory.AuditTenantName);
        using var _ = client;
        using var createdResponse = await client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountDto
        {
            WarehouseId = movement.WarehouseId,
            Lines = [new CountLineInput { MaterialId = movement.MaterialId, StorageLocationId = movement.StorageLocationId, CountedQuantity = 12m }]
        });
        var created = (await ReadJsonAsync<DynamicApiResponse<StockCountDto>>(createdResponse)).Data!;
        using var req = new HttpRequestMessage(HttpMethod.Put, $"/api/stock-counts/{created.Id}")
        { Content = JsonContent.Create(new UpdateStockCountDto { WarehouseId = movement.WarehouseId, Notes = "notes only", ConcurrencyStamp = created.ConcurrencyStamp,
            Lines = [new CountLineInput { MaterialId = movement.MaterialId, StorageLocationId = movement.StorageLocationId, CountedQuantity = 12m }] }) };
        req.Headers.TryAddWithoutValidation("If-Match", created.ConcurrencyStamp);
        using var resp = await client.SendAsync(req);
        resp.IsSuccessStatusCode.Should().BeTrue(await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Stock_count_uses_tenant_snapshot_and_persists_ledger_with_http_audit()
    {
        var tenant = _factory.AuditTenantId;
        var movement = new StockMovementInput(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10m, StockMovementType.Receipt);
        var secondLocation = Guid.NewGuid();
        await PostStockAsync(tenant, Guid.NewGuid(), movement);
        await PostStockAsync(_factory.HostTenantId, Guid.NewGuid(), movement with { SignedQuantity = 90m });
        await PostStockAsync(tenant, Guid.NewGuid(), movement with { WarehouseId = Guid.NewGuid(), SignedQuantity = 80m });
        var (authenticatedClient, _) = await CreateAuthenticatedClientAsync(
            AdminUserName, AdminPassword, ErpManagementWebApplicationFactory.AuditTenantName);
        using var client = authenticatedClient;
        var input = new CountLineInput
        {
            MaterialId = movement.MaterialId,
            StorageLocationId = movement.StorageLocationId,
            BookQuantity = 999m,
            CountedQuantity = 12m
        };
        using var createdResponse = await client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountDto
        {
            WarehouseId = movement.WarehouseId,
            Notes = "Initial count",
            Lines = [input]
        });
        createdResponse.IsSuccessStatusCode.Should().BeTrue(await createdResponse.Content.ReadAsStringAsync());
        var created = (await ReadJsonAsync<DynamicApiResponse<StockCountDto>>(createdResponse)).Data!;
        created.Lines.Should().ContainSingle().Which.BookQuantity.Should().Be(10m);

        await PostStockAsync(tenant, Guid.NewGuid(), movement with { SignedQuantity = 4m });
        await using (var probe = _factory.Services.CreateAsyncScope())
        {
            var pdb = probe.ServiceProvider.GetRequiredService<ErpManagementDbContext>();
            var dbStamp = (await pdb.StockCounts.AsNoTracking().SingleAsync(x => x.Id == created.Id)).ConcurrencyStamp;
            dbStamp.Should().Be(created.ConcurrencyStamp, "create DTO stamp must equal the persisted stamp");
            (await pdb.StockCountLines.AsNoTracking().CountAsync(l => l.CountId == created.Id))
                .Should().Be(1, "create must persist its line rows");
        }
        input.CountedQuantity = 13m;
        using var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/stock-counts/{created.Id}")
        { Content = JsonContent.Create(new UpdateStockCountDto
        {
            WarehouseId = movement.WarehouseId,
            ConcurrencyStamp = created.ConcurrencyStamp,
            Notes = "Recount of physical quantity",
            Lines = [input, new CountLineInput
            {
                MaterialId = movement.MaterialId,
                StorageLocationId = secondLocation,
                BookQuantity = 999m,
                CountedQuantity = 0m
            }]
        }) };
        updateRequest.Headers.TryAddWithoutValidation("If-Match", created.ConcurrencyStamp);
        using var updatedResponse = await client.SendAsync(updateRequest);
        updatedResponse.IsSuccessStatusCode.Should().BeTrue(await updatedResponse.Content.ReadAsStringAsync());
        var updated = (await ReadJsonAsync<DynamicApiResponse<StockCountDto>>(updatedResponse)).Data!;
        updated.Lines.Single(l => l.StorageLocationId == movement.StorageLocationId).BookQuantity.Should().Be(10m);
        updated.Lines.Single(l => l.StorageLocationId == secondLocation).BookQuantity.Should().Be(0m);

        var beforePosting = DateTime.UtcNow;
        using var postedResponse = await client.PostAsync($"/api/stock-counts/{created.Id}/post", content: null);
        postedResponse.IsSuccessStatusCode.Should().BeTrue(await postedResponse.Content.ReadAsStringAsync());
        var posted = (await ReadJsonAsync<DynamicApiResponse<StockCountDto>>(postedResponse)).Data!;
        posted.Status.Should().Be(StockCountStatus.Posted);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ErpManagementDbContext>();
        var ledger = await db.StockTransactions.SingleAsync(t => t.SourceDocId == created.Id);
        ledger.SourceDocType.Should().Be(nameof(StockCount));
        ledger.MovementType.Should().Be(StockMovementType.CountGain);
        ledger.SignedQuantity.Should().Be(3m);
        ledger.BalanceAfter.Should().Be(17m);
        ledger.TenantId.Should().Be(tenant);
        ledger.CreationTime.Kind.Should().Be(DateTimeKind.Utc);
        ledger.CreationTime.Should().BeOnOrAfter(beforePosting.AddMilliseconds(-1)).And.BeOnOrBefore(DateTime.UtcNow);
        var tenantId = tenant.ToString();
        var actorId = await db.Users.Where(u => u.TenantId == tenantId && u.UserName == AdminUserName)
            .Select(u => u.Id).SingleAsync();
        ledger.CreatorId.Should().Be(actorId);
        var persistedBalance = await db.StockBalances.SingleAsync(b => b.TenantId == tenant
            && b.MaterialId == movement.MaterialId && b.WarehouseId == movement.WarehouseId
            && b.StorageLocationId == movement.StorageLocationId);
        persistedBalance.Quantity.Should().Be(17m);
        persistedBalance.LastModifierId.Should().Be(actorId);

        using var ledgerResponse = await client.GetAsync($"/api/stock-transactions/{ledger.Id}");
        ledgerResponse.IsSuccessStatusCode.Should().BeTrue(await ledgerResponse.Content.ReadAsStringAsync());
        var ledgerDto = (await ReadJsonAsync<DynamicApiResponse<StockTransactionDto>>(ledgerResponse)).Data!;
        ledgerDto.SourceDocType.Should().Be(nameof(StockCount));
        ledgerDto.SourceDocId.Should().Be(created.Id);
        ledgerDto.CreationTime.Should().Be(ledger.CreationTime);
        _factory.AuditSink.GetRecords().Should().ContainSingle(record =>
            record.Action.Name == "POST /api/stock-counts/{id}/post" &&
            record.TenantId == tenant.ToString() && record.Actor.Id == actorId.ToString() &&
            record.Outcome.Status == "succeeded");
    }

    [Fact]
    public async Task Concurrent_first_stock_balance_creation_maps_only_its_natural_key_violation()
    {
        var tenant = _factory.HostTenantId;
        var movement = new StockMovementInput(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5m, StockMovementType.Receipt);
        var winningSource = Guid.NewGuid();
        var winningOperation = Guid.NewGuid();
        var losingSource = Guid.NewGuid();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var loserStaged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLoser = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loserTask = PostStockAsync(tenant, losingSource, movement with { SignedQuantity = 8m }, async () =>
        {
            loserStaged.SetResult();
            await releaseLoser.Task.WaitAsync(timeout.Token);
        }, timeout.Token);
        try
        {
            var stagedOrFailed = await Task.WhenAny(loserStaged.Task, loserTask).WaitAsync(timeout.Token);
            await stagedOrFailed;
            await PostStockAsync(tenant, winningSource, movement, cancellationToken: timeout.Token, sourceOperationId: winningOperation);
        }
        finally
        {
            releaseLoser.TrySetResult();
        }

        var conflict = await Assert.ThrowsAsync<CrestConcurrencyException>(() => loserTask);
        conflict.EntityType.Should().Be(nameof(StockBalance));
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ErpManagementDbContext>();
        var balance = await db.StockBalances.AsNoTracking().SingleAsync(b => b.TenantId == tenant && b.MaterialId == movement.MaterialId);
        balance.Quantity.Should().Be(5m);
        var ledgers = await db.StockTransactions.Where(t => t.SourceDocId == winningSource || t.SourceDocId == losingSource).ToListAsync();
        var winningLedger = ledgers.Should().ContainSingle().Which;
        winningLedger.SourceDocId.Should().Be(winningSource);
        winningLedger.SourceOperationId.Should().Be(winningOperation);

        db.StockBalances.Add(new StockBalance(balance.Id, Guid.NewGuid(), movement.WarehouseId, movement.StorageLocationId, tenant)
        {
            CreationTime = DateTime.UtcNow
        });
        var otherViolation = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(otherViolation.InnerException);
        postgres.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.Should().Be("PK_StockBalances");
    }

    private async Task PostStockAsync(Guid tenant, Guid sourceId, StockMovementInput movement,
        Func<Task>? beforeCommit = null, CancellationToken cancellationToken = default, Guid? sourceOperationId = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var unitOfWorkManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var stock = scope.ServiceProvider.GetRequiredService<StockPostingManager>();
        await using var uow = unitOfWorkManager.BeginScope();
        await uow.StartAsync(cancellationToken);
        await stock.PostAsync(tenant, [movement], false, nameof(GoodsReceipt), sourceId, cancellationToken, sourceOperationId);
        if (beforeCommit is not null)
            await beforeCommit();
        await uow.CompleteAsync(cancellationToken);
    }

    private static IDisposable AuthenticateScope(IServiceProvider services, Guid tenantId)
        => services.GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim("tenantid", tenantId.ToString()), new Claim("is_super_admin", "true")], "erp-test")));

    private HttpClient CreateTenantClient(string tenantId)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);
        return client;
    }

    private async Task<(HttpClient Client, TokenResponse LoginResult)> CreateAuthenticatedClientAsync(
        string userName,
        string password,
        string tenantId)
    {
        var client = CreateTenantClient(tenantId);
        var loginResult = await LoginAsync(client, userName, password);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginResult.AccessToken);

        if (tenantId == HostTenantId)
        {
            client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        }

        return (client, loginResult);
    }

    private async Task<TokenResponse> LoginAsync(
        HttpClient client,
        string userName,
        string password)
    {
        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = userName,
            ["password"] = password,
            ["client_id"] = "test-client",
            ["scope"] = "openid profile email offline_access"
        });
        var response = await client.PostAsync("/connect/token", formContent);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await ReadJsonAsync<TokenResponse>(response);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        var result = await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions);
        result.Should().NotBeNull();
        return result!;
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;
    }

    private sealed class DynamicApiResponse<T>
    {
        public int Code { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
    }

    private sealed class PagedResultResponse<T>
    {
        public T[] Items { get; set; } = Array.Empty<T>();
        public int TotalCount { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; }
    }

    private sealed class AuditLogResponse
    {
        public Guid Id { get; set; }
        public DateTime ExecutionTime { get; set; }
        public long Duration { get; set; }
        public string? UserId { get; set; }
        public string? UserName { get; set; }
        public string? TenantId { get; set; }
        public string? HttpMethod { get; set; }
        public string? Url { get; set; }
        public string? ServiceName { get; set; }
        public string? MethodName { get; set; }
        public int Status { get; set; }
        public string? ClientIpAddress { get; set; }
    }

}
