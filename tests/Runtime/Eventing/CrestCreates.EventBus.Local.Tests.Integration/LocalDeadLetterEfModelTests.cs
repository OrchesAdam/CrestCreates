using System.Linq;
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
}
