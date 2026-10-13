using System.Linq;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.DeadLetter.EFCore;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace CrestCreates.EventBus.Local.Tests.Integration;

/// <summary>
/// Compatibility verification for the shared EF dead-letter model: the local replay key travels
/// as a nullable additive column without changing existing column semantics. The EF provider's
/// own NativeAOT status remains separately declared.
/// </summary>
public class LocalDeadLetterEfModelTests
{
    private static DbContextOptions<DeadLetterDbContext> InMemoryOptions()
        => new DbContextOptionsBuilder<DeadLetterDbContext>()
            .UseInMemoryDatabase("dead-letter-upsert-" + System.Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static DeadLetterMessage CreateMessage(
        string messageId,
        DeadLetterStatus status = DeadLetterStatus.Pending,
        int retryCount = 0,
        int maxRetries = 3)
        => new(
            messageId,
            "My.Event",
            1,
            null,
            null,
            null,
            CrestCreates.Event.Abstractions.EventScope.Local,
            typeof(LocalDeadLetterEfModelTests).AssemblyQualifiedName!,
            "{}"u8.ToArray(),
            "failure",
            null,
            System.DateTime.UtcNow,
            System.DateTime.UtcNow,
            retryCount,
            maxRetries,
            status,
            LocalReplayKey: "SomeAssembly:My.Event");

    [Fact]
    public void DeadLetterModel_HasNullableLocalReplayKey_WithoutChangingExistingColumns()
    {
        var options = new DbContextOptionsBuilder<DeadLetterDbContext>()
            .UseInMemoryDatabase("dead-letter-model")
            .Options;
        using var context = new DeadLetterDbContext(options);

        var entity = context.Model.FindEntityType(typeof(DeadLetterEntity))!;

        var localReplayKey = entity.FindProperty(nameof(DeadLetterEntity.LocalReplayKey));
        localReplayKey.Should().NotBeNull();
        localReplayKey!.IsNullable.Should().BeTrue();
        localReplayKey.ClrType.Should().Be(typeof(string));

        entity.FindProperty(nameof(DeadLetterEntity.PayloadTypeFullName))!.IsNullable.Should().BeFalse();
        entity.FindProperty(nameof(DeadLetterEntity.Status))!.IsNullable.Should().BeFalse();
        entity.GetKeys().Should().ContainSingle(key => key.Properties.Count == 1 &&
            key.Properties[0].Name == nameof(DeadLetterEntity.MessageId));
    }

    [Fact]
    public async Task EfCoreStore_RoundTrips_LocalReplayKey()
    {
        var options = new DbContextOptionsBuilder<DeadLetterDbContext>()
            .UseInMemoryDatabase("dead-letter-roundtrip-" + System.Guid.NewGuid())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var context = new DeadLetterDbContext(options);
        var store = new EfCoreDeadLetterStore(context);

        var message = new DeadLetterMessage(
            "id-1",
            "My.Event",
            1,
            null,
            null,
            null,
            CrestCreates.Event.Abstractions.EventScope.Local,
            typeof(LocalDeadLetterEfModelTests).AssemblyQualifiedName!,
            "{}"u8.ToArray(),
            "failure",
            null,
            System.DateTime.UtcNow,
            System.DateTime.UtcNow,
            0,
            3,
            DeadLetterStatus.Pending,
            LocalReplayKey: "SomeAssembly:Some.Event");

        await store.EnqueueAsync(message, default);
        var stored = await store.GetByIdAsync("id-1", default);

        stored.Should().NotBeNull();
        stored!.LocalReplayKey.Should().Be("SomeAssembly:Some.Event");
        stored.Status.Should().Be(DeadLetterStatus.Pending);
    }

    [Fact]
    public async Task EnqueueAsync_UpsertsStateTransition_OnSameContext()
    {
        // R1-LE-03：同一 context 内对同一 MessageId 再次 Enqueue 必须是 upsert。
        // 修复前的 Add(new DeadLetterEntity) 会在第二次写入时命中 identity-tracking 冲突。
        await using var context = new DeadLetterDbContext(InMemoryOptions());
        var store = new EfCoreDeadLetterStore(context);

        await store.EnqueueAsync(CreateMessage("id-upsert"), default);
        await store.EnqueueAsync(CreateMessage("id-upsert", DeadLetterStatus.Retried, retryCount: 1), default);
        await store.EnqueueAsync(CreateMessage("id-upsert", DeadLetterStatus.Archived, retryCount: 3), default);

        var stored = await store.GetByIdAsync("id-upsert", default);
        stored!.Status.Should().Be(DeadLetterStatus.Archived);
        stored.RetryCount.Should().Be(3);
        (await store.CountAsync(null, default)).Should().Be(1);
    }

    [Fact]
    public async Task EnqueueAsync_UpsertsStateTransition_OnFreshContext()
    {
        // R1-LE-03：跨 context（重放执行器每次写状态都会新建 scope）写同一 MessageId 也必须是 upsert。
        // 修复前用新 context 执行 Add 会撞数据库主键。
        var options = InMemoryOptions();

        await using (var seedContext = new DeadLetterDbContext(options))
        {
            await new EfCoreDeadLetterStore(seedContext).EnqueueAsync(CreateMessage("id-fresh"), default);
        }

        await using (var transitionContext = new DeadLetterDbContext(options))
        {
            await new EfCoreDeadLetterStore(transitionContext)
                .EnqueueAsync(CreateMessage("id-fresh", DeadLetterStatus.Retried, retryCount: 1), default);
        }

        await using var verifyContext = new DeadLetterDbContext(options);
        var store = new EfCoreDeadLetterStore(verifyContext);
        var stored = await store.GetByIdAsync("id-fresh", default);
        stored!.Status.Should().Be(DeadLetterStatus.Retried);
        stored.RetryCount.Should().Be(1);
        (await store.CountAsync(null, default)).Should().Be(1);
    }

    [Fact]
    public async Task EnqueueAsync_UpsertPreservesBudgetAndCancelRecoveryTransitions()
    {
        var options = InMemoryOptions();
        await using var context = new DeadLetterDbContext(options);
        var store = new EfCoreDeadLetterStore(context);

        await store.EnqueueAsync(CreateMessage("id-budget", maxRetries: 2), default);
        // 失败一次：RetryCount++ 且仍 Pending
        await store.EnqueueAsync(CreateMessage("id-budget", DeadLetterStatus.Pending, retryCount: 1, maxRetries: 2), default);
        // 取消恢复：Retrying 回到 Pending，RetryCount 不增
        await store.EnqueueAsync(CreateMessage("id-budget", DeadLetterStatus.Pending, retryCount: 1, maxRetries: 2), default);
        // 预算耗尽：归档终态
        await store.EnqueueAsync(CreateMessage("id-budget", DeadLetterStatus.Archived, retryCount: 2, maxRetries: 2), default);

        var stored = await store.GetByIdAsync("id-budget", default);
        stored!.Status.Should().Be(DeadLetterStatus.Archived);
        stored.RetryCount.Should().Be(2);
        stored.MaxRetries.Should().Be(2);
        (await store.CountAsync(null, default)).Should().Be(1);
    }
}
