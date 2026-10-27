using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Retention;
using CrestCreates.Application.AuditLog;
using CrestCreates.Application.Contracts.AuditRetention;
using CrestCreates.Authorization.Abstractions;
using CrestPermissionException = CrestCreates.Domain.Exceptions.CrestPermissionException;
using CrestCreates.Domain.Settings;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.AuditLog;

public sealed class AuditRetentionAppServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly AuditRetentionStoreIdentity Identity = new()
    {
        StoreId = "postgresql-audit-retention",
        SinkId = "postgresql-runtime-audit",
        Capabilities = AuditRetentionStoreCapabilities.Full
    };

    [Fact]
    public async Task Anonymous_call_is_rejected_before_any_retention_call()
    {
        var harness = new Harness(authenticated: false);
        var act = () => harness.Service.PreviewAsync(new AuditRetentionPreviewInput { StoreId = Identity.StoreId });

        await act.Should().ThrowAsync<AuditRetentionException>()
            .Where(exception => exception.Code == AuditRetentionErrorCodes.IdentityRequired);
        harness.Retention.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Missing_permission_is_rejected_before_any_retention_call()
    {
        var harness = new Harness(permissionDenied: true);
        var act = () => harness.Service.ExecuteAsync(new AuditRetentionExecuteInput
        {
            StoreId = Identity.StoreId,
            RequestId = "req-1",
            ReasonCode = "retention.manual"
        });

        await act.Should().ThrowAsync<CrestPermissionException>();
        harness.Retention.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Tenant_caller_cannot_target_another_tenant()
    {
        var harness = new Harness(tenantId: "tenant-a");
        var act = () => harness.Service.PreviewAsync(new AuditRetentionPreviewInput
        {
            StoreId = Identity.StoreId,
            TenantId = "tenant-b"
        });

        await act.Should().ThrowAsync<AuditRetentionException>()
            .Where(exception => exception.Code == AuditRetentionErrorCodes.ScopeInvalid);
        harness.Retention.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Tenant_caller_uses_the_current_authoritative_id()
    {
        var harness = new Harness(tenantId: "tenant-a");
        AuditRetentionPreviewRequest? captured = null;
        harness.Retention
            .Setup(service => service.PreviewAsync(It.IsAny<AuditRetentionPreviewRequest>(), It.IsAny<CancellationToken>()))
            .Callback((AuditRetentionPreviewRequest request, CancellationToken _) => captured = request)
            .ReturnsAsync((AuditRetentionPreviewRequest request, CancellationToken _) => Preview(request));

        await harness.Service.PreviewAsync(new AuditRetentionPreviewInput { StoreId = Identity.StoreId });

        captured.Should().NotBeNull();
        captured!.Scope.Kind.Should().Be(AuditRetentionScope.TenantKind);
        captured.Scope.TenantId.Should().Be("tenant-a");
    }

    [Fact]
    public async Task Host_caller_resolves_a_tenant_name_to_the_authoritative_id()
    {
        var resolvedId = Guid.NewGuid();
        var harness = new Harness(tenantId: null);
        harness.Tenants
            .Setup(provider => provider.GetTenantAsync("contoso", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantInfo(resolvedId.ToString(), "contoso"));
        AuditRetentionPreviewRequest? captured = null;
        harness.Retention
            .Setup(service => service.PreviewAsync(It.IsAny<AuditRetentionPreviewRequest>(), It.IsAny<CancellationToken>()))
            .Callback((AuditRetentionPreviewRequest request, CancellationToken _) => captured = request)
            .ReturnsAsync((AuditRetentionPreviewRequest request, CancellationToken _) => Preview(request));

        await harness.Service.PreviewAsync(new AuditRetentionPreviewInput
        {
            StoreId = Identity.StoreId,
            TenantId = "contoso"
        });

        captured!.Scope.TenantId.Should().Be(resolvedId.ToString());
    }

    [Fact]
    public async Task Host_caller_rejects_an_unknown_tenant()
    {
        var harness = new Harness(tenantId: null);
        harness.Tenants
            .Setup(provider => provider.GetTenantAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ITenantInfo?)null);
        var act = () => harness.Service.PreviewAsync(new AuditRetentionPreviewInput
        {
            StoreId = Identity.StoreId,
            TenantId = "missing"
        });

        await act.Should().ThrowAsync<AuditRetentionException>()
            .Where(exception => exception.Code == AuditRetentionErrorCodes.ScopeInvalid);
        harness.Retention.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Disabled_setting_rejects_execute_but_allows_preview()
    {
        var harness = new Harness(tenantId: "tenant-a");
        harness.SettingValues[AuditRetentionSettingNames.Enabled] = "false";
        harness.Retention
            .Setup(service => service.PreviewAsync(It.IsAny<AuditRetentionPreviewRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuditRetentionPreviewRequest request, CancellationToken _) => Preview(request));

        await harness.Service.PreviewAsync(new AuditRetentionPreviewInput { StoreId = Identity.StoreId });

        var act = () => harness.Service.ExecuteAsync(new AuditRetentionExecuteInput
        {
            StoreId = Identity.StoreId,
            RequestId = "req-disabled",
            ReasonCode = "retention.manual"
        });
        await act.Should().ThrowAsync<AuditRetentionException>()
            .Where(exception => exception.Code == AuditRetentionErrorCodes.Disabled);
        harness.Retention.Verify(service => service.ExecuteAsync(It.IsAny<AuditRetentionRunRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(AuditRetentionSettingNames.RetentionDays, "abc")]
    [InlineData(AuditRetentionSettingNames.RetentionDays, "0")]
    [InlineData(AuditRetentionSettingNames.BatchSize, "1001")]
    [InlineData(AuditRetentionSettingNames.MaxBatchesPerRun, "-1")]
    [InlineData(AuditRetentionSettingNames.MaxRunSeconds, "301")]
    public async Task Invalid_policy_setting_rejects_execute_without_any_store_call(string key, string value)
    {
        var harness = new Harness(tenantId: "tenant-a");
        harness.SettingValues[key] = value;
        var act = () => harness.Service.ExecuteAsync(new AuditRetentionExecuteInput
        {
            StoreId = Identity.StoreId,
            RequestId = "req-invalid",
            ReasonCode = "retention.manual"
        });

        await act.Should().ThrowAsync<AuditRetentionException>()
            .Where(exception => exception.Code == AuditRetentionErrorCodes.PolicyInvalid);
        harness.Retention.Verify(service => service.ExecuteAsync(It.IsAny<AuditRetentionRunRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_admits_policy_snapshot_cutoff_actor_and_scope()
    {
        var harness = new Harness(tenantId: "tenant-a");
        AuditRetentionRunRequest? captured = null;
        harness.Retention
            .Setup(service => service.ExecuteAsync(It.IsAny<AuditRetentionRunRequest>(), It.IsAny<CancellationToken>()))
            .Callback((AuditRetentionRunRequest request, CancellationToken _) => captured = request)
            .ReturnsAsync((AuditRetentionRunRequest request, CancellationToken _) => RunResult(request));

        var result = await harness.Service.ExecuteAsync(new AuditRetentionExecuteInput
        {
            StoreId = Identity.StoreId,
            RequestId = "req-admitted",
            ReasonCode = "retention.manual",
            ExpectedPolicyHash = "expected-hash"
        });

        captured.Should().NotBeNull();
        captured!.Policy.RetentionDays.Should().Be(90);
        captured.Policy.BatchSize.Should().Be(50);
        captured.Policy.MaxBatchesPerRun.Should().Be(5);
        captured.Policy.MaxRunSeconds.Should().Be(30);
        captured.SnapshotAt.Should().Be(Now);
        captured.Cutoff.Should().Be(Now.AddDays(-90));
        captured.Scope.TenantId.Should().Be("tenant-a");
        captured.Actor.Kind.Should().Be("user");
        captured.Actor.Id.Should().Be("user-1");
        captured.ExpectedPolicyHash.Should().Be("expected-hash");
        captured.CorrelationId.Should().NotBeNullOrWhiteSpace();
        result.State.Should().Be("completed");
        result.CommitKnowledge.Should().Be("committed");
    }

    [Fact]
    public async Task Hold_and_release_use_the_same_scope_and_actor_admission()
    {
        var harness = new Harness(tenantId: "tenant-a");
        AuditHoldPlacement? placement = null;
        AuditHoldRelease? release = null;
        harness.Retention
            .Setup(service => service.PlaceHoldAsync(It.IsAny<AuditHoldPlacement>(), It.IsAny<CancellationToken>()))
            .Callback((AuditHoldPlacement value, CancellationToken _) => placement = value)
            .ReturnsAsync((AuditHoldPlacement value, CancellationToken _) => HoldResult(value));
        harness.Retention
            .Setup(service => service.ReleaseHoldAsync(It.IsAny<AuditHoldRelease>(), It.IsAny<CancellationToken>()))
            .Callback((AuditHoldRelease value, CancellationToken _) => release = value)
            .ReturnsAsync((AuditHoldRelease value, CancellationToken _) => HoldResult(value));

        await harness.Service.PlaceHoldAsync(new AuditRetentionHoldInput
        {
            StoreId = Identity.StoreId,
            AuditId = "audit-1",
            ReasonCode = "retention.legal"
        });
        await harness.Service.ReleaseHoldAsync(new AuditRetentionReleaseInput
        {
            StoreId = Identity.StoreId,
            HoldId = "hold-1",
            ExpectedVersion = 3,
            ReasonCode = "retention.legal-cleared"
        });

        placement!.Scope.TenantId.Should().Be("tenant-a");
        placement.Actor.Id.Should().Be("user-1");
        release!.Scope.TenantId.Should().Be("tenant-a");
        release.ExpectedVersion.Should().Be(3);
        release.Actor.Id.Should().Be("user-1");
    }

    [Fact]
    public async Task GetRun_maps_null_and_record_results()
    {
        var harness = new Harness(tenantId: "tenant-a");
        harness.Retention
            .Setup(service => service.GetRunAsync(It.IsAny<AuditRetentionRunKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuditRetentionRunRecord?)null);

        (await harness.Service.GetRunAsync(Identity.StoreId, "missing")).Should().BeNull();

        var record = new AuditRetentionRunRecord
        {
            Key = new AuditRetentionRunKey
            {
                StoreId = Identity.StoreId,
                SinkId = Identity.SinkId,
                Scope = AuditRetentionScope.ForTenant("tenant-a"),
                RequestId = "req-record"
            },
            Policy = new AuditRetentionPolicy
            {
                RetentionDays = 90,
                BatchSize = 50,
                MaxBatchesPerRun = 5,
                MaxRunSeconds = 30
            },
            PolicyHash = "hash",
            SnapshotAt = Now,
            Cutoff = Now.AddDays(-90),
            ReasonCode = "retention.manual",
            Actor = new AuditActor { Kind = "user", Id = "user-1" },
            CorrelationId = "corr",
            State = AuditRetentionRunState.BudgetReached,
            CommittedDeletedCount = 12,
            CommittedBatchCount = 2,
            StopReason = AuditRetentionStopReasons.BudgetReached,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        harness.Retention
            .Setup(service => service.GetRunAsync(It.IsAny<AuditRetentionRunKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var mapped = await harness.Service.GetRunAsync(Identity.StoreId, "req-record");
        mapped.Should().NotBeNull();
        mapped!.State.Should().Be("budget-reached");
        mapped.CommittedDeletedCount.Should().Be(12);
        mapped.RemainingMayExist.Should().BeTrue();
    }

    private static AuditRetentionPreview Preview(AuditRetentionPreviewRequest request)
        => new()
        {
            StoreId = request.StoreId,
            SinkId = request.SinkId,
            Scope = request.Scope,
            SnapshotAt = request.SnapshotAt,
            Cutoff = request.Cutoff,
            PolicyHash = request.Policy.ComputeHash(),
            BatchSize = request.Policy.BatchSize,
            MaxBatchesPerRun = request.Policy.MaxBatchesPerRun,
            HasMore = false,
            Sample = [],
            EligibleInSample = 0,
            HeldInSample = 0,
            ProtectedInSample = 0,
            QuarantinedInSample = 0
        };

    private static AuditRetentionRunResult RunResult(AuditRetentionRunRequest request)
        => new()
        {
            Key = request.Key,
            State = AuditRetentionRunState.Completed,
            CommitKnowledge = AuditRetentionCommitKnowledge.Committed,
            CommittedDeletedCount = 3,
            CommittedBatchCount = 1,
            RemainingMayExist = false,
            StopReason = AuditRetentionStopReasons.NoCandidates
        };

    private static AuditHoldResult HoldResult(AuditHoldPlacement placement)
        => new()
        {
            Hold = new AuditHold
            {
                HoldId = "hold-1",
                StoreId = placement.StoreId,
                SinkId = placement.SinkId,
                AuditId = placement.AuditId,
                Scope = placement.Scope,
                Version = 1,
                ReasonCode = placement.ReasonCode,
                Actor = placement.Actor,
                CorrelationId = placement.CorrelationId,
                CreatedAt = Now
            },
            MirrorOutcome = AuditRetentionMirrorOutcome.Succeeded
        };

    private static AuditHoldResult HoldResult(AuditHoldRelease release)
        => new()
        {
            Hold = new AuditHold
            {
                HoldId = release.HoldId,
                StoreId = release.StoreId,
                SinkId = release.SinkId,
                AuditId = "audit-1",
                Scope = release.Scope,
                Version = release.ExpectedVersion + 1,
                ReasonCode = "retention.legal",
                Actor = release.Actor,
                CorrelationId = release.CorrelationId,
                CreatedAt = Now,
                ReleasedAt = Now,
                ReleaseActor = release.Actor,
                ReleaseCorrelationId = release.CorrelationId,
                ReleaseReasonCode = release.ReasonCode
            },
            MirrorOutcome = AuditRetentionMirrorOutcome.Succeeded
        };

    private sealed class Harness
    {
        private readonly bool _authenticated;
        private readonly bool _permissionDenied;
        private readonly string? _tenantId;

        public Dictionary<string, string?> SettingValues { get; } = new()
        {
            [AuditRetentionSettingNames.RetentionDays] = "90",
            [AuditRetentionSettingNames.Enabled] = "true",
            [AuditRetentionSettingNames.BatchSize] = "50",
            [AuditRetentionSettingNames.MaxBatchesPerRun] = "5",
            [AuditRetentionSettingNames.MaxRunSeconds] = "30"
        };

        public Mock<IAuditRetentionService> Retention { get; } = new();
        public Mock<ITenantProvider> Tenants { get; } = new();

        public AuditRetentionAppService Service { get; }

        public Harness(bool authenticated = true, bool permissionDenied = false, string? tenantId = "tenant-a")
        {
            _authenticated = authenticated;
            _permissionDenied = permissionDenied;
            _tenantId = tenantId;
            Retention.Setup(service => service.GetStoreIdentity(It.IsAny<string>())).Returns(Identity);

            var settings = new Mock<ISettingProvider>();
            settings
                .Setup(provider => provider.GetOrNullAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string name, CancellationToken _) => SettingValues.TryGetValue(name, out var value) ? value : null);

            var permissions = new Mock<IPermissionChecker>();
            if (_permissionDenied)
            {
                permissions
                    .Setup(checker => checker.CheckAsync(It.IsAny<string>()))
                    .ThrowsAsync(new CrestPermissionException("denied"));
            }
            else
            {
                permissions.Setup(checker => checker.CheckAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
            }

            var tenant = new Mock<ICurrentTenant>();
            tenant.SetupGet(current => current.Id).Returns(_tenantId);

            var user = new Mock<ICurrentUser>();
            user.SetupGet(current => current.IsAuthenticated).Returns(_authenticated);
            user.SetupGet(current => current.Id).Returns("user-1");
            user.SetupGet(current => current.UserName).Returns("Alice");

            Service = new AuditRetentionAppService(
                Retention.Object,
                settings.Object,
                permissions.Object,
                tenant.Object,
                user.Object,
                Tenants.Object,
                new FixedTimeProvider(Now));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
