using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using CrestCreates.EventBus.Local;

namespace CrestCreates.EventBus.Tests
{
    public class InMemoryEventIdempotencyStoreTests
    {
        [Fact]
        public async Task IsProcessedAsync_WhenNotProcessed_ReturnsFalse()
        {
            // Arrange
            var store = new InMemoryEventIdempotencyStore();
            var eventId = "event-001";

            // Act
            var result = await store.IsProcessedAsync(eventId);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task IsProcessedAsync_AfterMarkAsProcessed_ReturnsTrue()
        {
            // Arrange
            var store = new InMemoryEventIdempotencyStore();
            var eventId = "event-002";

            // Act
            await store.MarkAsProcessedAsync(eventId);
            var result = await store.IsProcessedAsync(eventId);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public async Task MarkAsProcessedAsync_MultipleCalls_DoesNotThrow()
        {
            // Arrange
            var store = new InMemoryEventIdempotencyStore();
            var eventId = "event-003";

            // Act
            var act = async () =>
            {
                await store.MarkAsProcessedAsync(eventId);
                await store.MarkAsProcessedAsync(eventId);
                await store.MarkAsProcessedAsync(eventId);
            };

            // Assert
            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task IsProcessedAsync_ConcurrentAccess_IsThreadSafe()
        {
            // Arrange
            var store = new InMemoryEventIdempotencyStore();
            var eventId = "event-004";
            var tasks = new List<Task>();

            // Act - Mark as processed concurrently
            for (int i = 0; i < 100; i++)
            {
                tasks.Add(store.MarkAsProcessedAsync(eventId));
            }

            await Task.WhenAll(tasks);

            // Assert
            var result = await store.IsProcessedAsync(eventId);
            result.Should().BeTrue();
        }
    }
}
