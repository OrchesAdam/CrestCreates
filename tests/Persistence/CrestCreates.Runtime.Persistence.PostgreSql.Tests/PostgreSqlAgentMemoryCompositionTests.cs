using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Agent.Memory;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Runtime.Persistence.PostgreSql;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

/// <summary>
/// DI composition for the durable Agent Memory provider. These tests do not
/// require a database: ValidateOnBuild proves the service graph resolves.
/// </summary>
public sealed class PostgreSqlAgentMemoryCompositionTests
{
    private static PostgreSqlRuntimePersistenceOptions Options()
        => new() { ConnectionString = "Host=localhost", Schema = $"itest_{Guid.NewGuid():N}" };

    /// <summary>Deterministic canonical hash computer required by Agent Memory
    /// runtime registration (ICanonicalHashComputer prerequisite).</summary>
    private static ServiceCollection AddHashComputer(ServiceCollection services)
    {
        services.AddSingleton<ICanonicalHashComputer>(new DeterministicHashComputer());
        return services;
    }

    private sealed class DeterministicHashComputer : ICanonicalHashComputer
    {
        public CanonicalHash ComputeContractHash(IDescriptor descriptor, CanonicalHashScope scope)
            => Deterministic(descriptor, scope, "contract");

        public CanonicalHash ComputeDefinitionHash(IDescriptor descriptor, CanonicalHashScope scope)
            => Deterministic(descriptor, scope, "definition");

        public CanonicalHash ComputeFromProjection(CanonicalHashProjectionResult projection)
            => new()
            {
                Value = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(projection.Metadata.ArtifactKind + "-" + Guid.NewGuid().ToString("N")))).ToLowerInvariant(),
                Algorithm = "SHA-256",
                AlgorithmVersion = projection.Metadata.AlgorithmVersion,
                ArtifactKind = projection.Metadata.ArtifactKind,
                Scope = projection.Metadata.Scope,
                Purpose = projection.Metadata.Purpose,
                ContractVersion = projection.Metadata.ContractVersion,
                CanonicalShapeVersion = projection.Metadata.CanonicalShapeVersion
            };

        private static CanonicalHash Deterministic(IDescriptor descriptor, CanonicalHashScope scope, string kind)
            => new()
            {
                Value = $"{kind}-{descriptor.GetType().Name}-{Guid.NewGuid():N}",
                Algorithm = "SHA-256",
                AlgorithmVersion = "sha256-canonical-json-v1",
                ArtifactKind = kind,
                Scope = scope.ToString(),
                Purpose = kind,
                ContractVersion = "canonical-hash-v1",
                CanonicalShapeVersion = "test-v1"
            };
    }

    [Fact]
    public void PostgreSqlProvider_WithoutAgentMemoryRuntime_Should_ValidateAndBuild()
    {
        using var provider = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(Options())
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

        provider.GetRequiredService<IRuntimeTransactionCoordinator>();
    }

    [Fact]
    public void PostgreSqlAgentMemoryPersistence_Should_ReplaceStores_InEitherOrder()
    {
        // Runtime first, provider second.
        using (var provider = AddHashComputer(new ServiceCollection())
            .AddAgentMemoryRuntime()
            .AddCrestCreatesPostgreSqlRuntimePersistence(Options())
            .AddCrestCreatesPostgreSqlAgentMemoryPersistence()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }))
        {
            AssertPostgreSqlStoresSelected(provider);
        }

        // Provider first, runtime second — the explicit provider extension owns
        // the final Store selections and TryAdd must not restore InMemory.
        using (var provider = AddHashComputer(new ServiceCollection())
            .AddCrestCreatesPostgreSqlRuntimePersistence(Options())
            .AddCrestCreatesPostgreSqlAgentMemoryPersistence()
            .AddAgentMemoryRuntime()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }))
        {
            AssertPostgreSqlStoresSelected(provider);
        }
    }

    [Fact]
    public void ExplicitAgentMemoryProviderRegistration_Should_ReplaceFourStores_InEitherOrder()
    {
        using var provider = AddHashComputer(new ServiceCollection())
            .AddCrestCreatesPostgreSqlRuntimePersistence(Options())
            .AddCrestCreatesPostgreSqlAgentMemoryPersistence()
            .AddAgentMemoryRuntime()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var conversation = provider.GetRequiredService<IAgentConversationStore>();
        var task = provider.GetRequiredService<IAgentTaskHistoryStore>();
        var context = provider.GetRequiredService<IAgentCompressedContextStore>();
        var memory = provider.GetRequiredService<IAgentMemoryStore>();

        conversation.GetType().FullName.Should().Contain("PostgreSqlAgentConversationStore");
        task.GetType().FullName.Should().Contain("PostgreSqlAgentTaskHistoryStore");
        context.GetType().FullName.Should().Contain("PostgreSqlAgentCompressedContextStore");
        memory.GetType().FullName.Should().Contain("PostgreSqlAgentMemoryStore");

        // Capability is truthful by implementation phase: Unknown until Slice 8.
        ((IAgentMemoryStoreCapabilities)memory).CurationOutcomeGuarantee
            .Should().Be(AgentMemoryCurationOutcomeGuarantee.Unknown);
    }

    [Fact]
    public void SelectedMemoryStore_Should_ImplementConditionalAndCapabilitiesWithoutSeparateDescriptors()
    {
        using var provider = AddHashComputer(new ServiceCollection())
            .AddAgentMemoryRuntime()
            .AddCrestCreatesPostgreSqlRuntimePersistence(Options())
            .AddCrestCreatesPostgreSqlAgentMemoryPersistence()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var memory = provider.GetRequiredService<IAgentMemoryStore>();
        memory.Should().BeAssignableTo<IAgentMemoryConditionalCurationStore>();
        memory.Should().BeAssignableTo<IAgentMemoryStoreCapabilities>();

        // No independent conditional/capability descriptors exist.
        var conditionalDescriptors = provider.GetServices<IAgentMemoryConditionalCurationStore>();
        conditionalDescriptors.Should().HaveCount(0, "conditional curation is discovered by casting the selected IAgentMemoryStore, not by DI.");
    }

    [Fact]
    public void BaseProvider_WithoutMemoryExtension_Should_Not_RegisterAgentMemoryStores()
    {
        using var provider = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(Options())
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var services = provider.GetService<IAgentConversationStore>();
        services.Should().BeNull("base PostgreSQL provider must remain feature-neutral.");
    }

    [Fact]
    public void MemoryProvider_WithoutBaseProvider_Should_FailResolution()
    {
        var act = () => AddHashComputer(new ServiceCollection())
            .AddCrestCreatesPostgreSqlAgentMemoryPersistence()
            .AddAgentMemoryRuntime()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        act.Should().Throw<AggregateException>()
            .WithMessage("*PostgreSqlRuntimePersistenceOptions*");
    }

    [Fact]
    public void RepeatedMemoryProviderExtension_Should_Not_CreateDuplicateStores()
    {
        var services = AddHashComputer(new ServiceCollection())
            .AddAgentMemoryRuntime()
            .AddCrestCreatesPostgreSqlRuntimePersistence(Options());
        services.AddCrestCreatesPostgreSqlAgentMemoryPersistence();
        services.AddCrestCreatesPostgreSqlAgentMemoryPersistence();
        services.AddCrestCreatesPostgreSqlAgentMemoryPersistence();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        provider.GetServices<IAgentMemoryStore>().Should().HaveCount(1, "repeated extension calls must not duplicate Stores.");
        provider.GetServices<IAgentTaskHistoryStore>().Should().HaveCount(1);
    }


    private static void AssertPostgreSqlStoresSelected(ServiceProvider provider)
    {
        provider.GetRequiredService<IAgentConversationStore>().GetType().FullName.Should().Contain("PostgreSqlAgentConversationStore");
        provider.GetRequiredService<IAgentTaskHistoryStore>().GetType().FullName.Should().Contain("PostgreSqlAgentTaskHistoryStore");
        provider.GetRequiredService<IAgentCompressedContextStore>().GetType().FullName.Should().Contain("PostgreSqlAgentCompressedContextStore");
        provider.GetRequiredService<IAgentMemoryStore>().GetType().FullName.Should().Contain("PostgreSqlAgentMemoryStore");
    }
}
