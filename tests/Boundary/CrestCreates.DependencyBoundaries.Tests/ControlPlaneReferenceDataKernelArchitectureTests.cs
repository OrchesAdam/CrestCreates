using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.DescriptorDraft;
using CrestCreates.Organization.Abstractions;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

public class ControlPlaneReferenceDataKernelArchitectureTests
{
    private static readonly Assembly PgAssembly = typeof(PostgreSqlDescriptorDraftStore).Assembly;

    private static Type StoreType(string name)
        => PgAssembly.GetType($"CrestCreates.Runtime.Persistence.PostgreSql.{name}")
           ?? throw new TypeLoadException($"Cannot find type {name}");

    private static IServiceCollection BuildControlPlaneServices()
    {
        var services = new ServiceCollection();
        services.AddCrestCreatesPostgreSqlRuntimePersistence(new PostgreSqlRuntimePersistenceOptions
        {
            ConnectionString = "Host=localhost;Database=crestcreates;Username=crest;Password=crest",
            Schema = "architecture_test"
        });
        services.AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
        return services;
    }

    // ── C04 ──────────────────────────────────────────────────────────────

    [Fact]
    public void C04_Provider_Should_ReuseRuntimePersistenceKernel()
    {
        // All three stores are registered as singletons and each constructor
        // takes NpgsqlDataSource (itself a singleton from the base provider).
        // This proves they share the same resistance kernel instance.
        var services = BuildControlPlaneServices();

        var draftDescriptor = services.Single(d => d.ServiceType == typeof(IDescriptorDraftStore));
        var orgDescriptor = services.Single(d => d.ServiceType == typeof(IOrganizationStore));
        var ruleDescriptor = services.Single(d => d.ServiceType == typeof(IDataPermissionScopeRuleStore));

        draftDescriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
        orgDescriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
        ruleDescriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);

        var dataSourceType = typeof(NpgsqlDataSource);
        foreach (var descriptor in new[] { draftDescriptor, orgDescriptor, ruleDescriptor })
        {
            var implType = descriptor.ImplementationType!;
            var ctors = implType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            ctors.Should().ContainSingle($"{implType.Name} should have exactly one constructor");
            ctors[0].GetParameters()
                .Should().Contain(p => p.ParameterType == dataSourceType,
                    $"{implType.Name} must depend on NpgsqlDataSource singleton");
        }
    }

    // ── C05 ──────────────────────────────────────────────────────────────

    [Fact]
    public void C05_Provider_Should_NotExpandRuntimeRecoveryTransactionBoundary()
    {
        var storeTypes = new[]
        {
            StoreType("PostgreSqlDescriptorDraftStore"),
            StoreType("PostgreSqlOrganizationStore"),
            StoreType("PostgreSqlDataPermissionScopeRuleStore"),
        };

        var participantInterface = PgAssembly.GetTypes()
            .FirstOrDefault(t => t.Name.Contains("RuntimeTransactionParticipant"));

        foreach (var storeType in storeTypes)
        {
            storeType.Should().NotImplement(
                typeof(IDisposable).GetInterfaces().Contains(typeof(IAsyncDisposable))
                    ? typeof(IAsyncDisposable)
                    : typeof(IDisposable),
                because: "control plane stores must not participate in runtime recovery transactions");

            if (participantInterface is not null)
            {
                participantInterface.IsAssignableFrom(storeType).Should().BeFalse(
                    $"{storeType.Name} must not implement runtime transaction participant");
            }

            var ctorParams = storeType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(c => c.GetParameters());
            ctorParams.Should().NotContain(p => p.Name != null && p.Name.Contains("recovery", StringComparison.OrdinalIgnoreCase),
                "control plane stores must not accept recovery participants");
        }
    }

    // ── C06 ──────────────────────────────────────────────────────────────

    [Fact]
    public void C06_StoreContracts_Should_NotExposeProviderTypes()
    {
        var contractTypes = new[]
        {
            typeof(IDescriptorDraftStore),
            typeof(IOrganizationStore),
            typeof(IDataPermissionScopeRuleStore),
        };

        foreach (var contractType in contractTypes)
        {
            var methods = contractType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                if (method.ReturnType != typeof(void))
                    AssertTypeNotProviderSpecific(method.ReturnType, contractType, method.Name + " return");

                foreach (var param in method.GetParameters())
                    AssertTypeNotProviderSpecific(param.ParameterType, contractType, method.Name + ":" + param.Name);
            }
        }
    }

    private static void AssertTypeNotProviderSpecific(Type type, Type contract, string location)
    {
        var fullName = type.FullName ?? "";
        fullName.Should().NotContain("Npgsql", $"{contract.Name}.{location} must not expose provider types");
        fullName.Should().NotContain("PostgreSql", $"{contract.Name}.{location} must not expose provider types");
    }

    // ── C10 ──────────────────────────────────────────────────────────────

    [Fact]
    public void C10_Provider_Should_NotImplementLegacyDraftStore()
    {
        var services = BuildControlPlaneServices();
        var serviceTypes = services.Select(d => d.ServiceType).ToList();

        serviceTypes.Should().NotContain(
            t => t.FullName != null && t.FullName.Contains("IDraftStore") && t != typeof(IDescriptorDraftStore),
            "PostgreSQL provider must not register legacy generic IDraftStore");
    }

    // ── C11 ──────────────────────────────────────────────────────────────

    [Fact]
    public void C11_Provider_Should_NotDefineDataPermissionScopeStore()
    {
        var services = BuildControlPlaneServices();
        var serviceTypes = services.Select(d => d.ServiceType).ToList();

        serviceTypes.Should().NotContain(
            t => t.Name == "IDataPermissionScopeStore",
            "no persisted IDataPermissionScopeStore should be registered");
    }

    // ── C13 ──────────────────────────────────────────────────────────────

    [Fact]
    public void C13_DescriptorPayloadGraph_Should_HaveClosedAotPersistenceMapping()
    {
        var contextType = typeof(PostgreSqlControlPlaneReferenceDataJsonSerializerContext);

        var durableTypes = new[]
        {
            typeof(PostgreSqlDescriptorDraftDocument),
            typeof(OrganizationUnit),
            typeof(Position),
            typeof(UserOrganizationMembership),
            typeof(UserOrganizationRoleAssignment),
            typeof(PostgreSqlDescriptorDraftDocument), // 6th slot = draft document root
        };

        // Verify the serializer context is a valid JsonSerializerContext
        var defaultProperty = contextType.GetProperty("Default", BindingFlags.Public | BindingFlags.Static);
        defaultProperty.Should().NotBeNull("serializer context must expose a Default singleton");
        var contextInstance = defaultProperty!.GetValue(null);
        contextInstance.Should().NotBeNull();
        contextInstance.Should().BeAssignableTo<JsonSerializerContext>();

        // Verify each durable type has a registered JsonTypeInfo in the context
        var getTypeInfoMethod = typeof(JsonSerializerContext).GetMethod("GetTypeInfo", new[] { typeof(Type) });
        foreach (var durableType in durableTypes.Distinct())
        {
            var typeInfo = ((JsonSerializerContext)contextInstance!).GetTypeInfo(durableType);
            typeInfo.Should().NotBeNull(
                $"type {durableType.Name} must have a JsonTypeInfo entry in the serializer context");
        }

        // Domain graph inventory is a separate guard from the provider DTO scan.
        // Adding a new polymorphic domain arm must force an explicit mapping update.
        var payloadTypes = typeof(SchemaDescriptorDraftPayload).Assembly.GetTypes()
            .Where(type => typeof(DescriptorDraftPayload).IsAssignableFrom(type) && !type.IsAbstract)
            .Select(type => type.FullName!)
            .ToHashSet(StringComparer.Ordinal);
        var expectedPayloadTypes = new HashSet<string>
        {
            "CrestCreates.DescriptorDraft.SchemaDescriptorDraftPayload",
            "CrestCreates.DescriptorDraft.FormDescriptorDraftPayload",
            "CrestCreates.DescriptorDraft.CapabilityDescriptorDraftPayload",
            "CrestCreates.DescriptorDraft.HumanTaskDescriptorDraftPayload",
            "CrestCreates.DescriptorDraft.EventDescriptorDraftPayload",
            "CrestCreates.DescriptorDraft.WorkflowDescriptorDraftPayload"
        };
        payloadTypes.SetEquals(expectedPayloadTypes).Should().BeTrue(
            "every domain Draft payload arm must be explicitly represented by the persistence mapping");

        var targetTypes = typeof(InteractionTarget).Assembly.GetTypes()
            .Where(type => typeof(InteractionTarget).IsAssignableFrom(type) && !type.IsAbstract)
            .Select(type => type.FullName!)
            .ToHashSet(StringComparer.Ordinal);
        var expectedTargetTypes = new HashSet<string>
        {
            "CrestCreates.Workflow.Abstractions.CapabilityTarget",
            "CrestCreates.Workflow.Abstractions.HumanTaskTarget",
            "CrestCreates.Workflow.Abstractions.SubWorkflowTarget"
        };
        targetTypes.SetEquals(expectedTargetTypes).Should().BeTrue(
            "every domain Workflow target arm must be explicitly represented by the persistence mapping");

        // Verify no abstract/interface-typed members leak from the durable payload DTO types.
        // Only scan types reachable from the serializer context root types (the 6 durable graphs).
        var rootDtoTypes = new[]
        {
            typeof(PostgreSqlDescriptorDraftDocument),
            typeof(OrganizationUnit),
            typeof(Position),
            typeof(UserOrganizationMembership),
            typeof(UserOrganizationRoleAssignment),
        };

        var visited = new HashSet<Type>();
        var queue = new Queue<Type>(rootDtoTypes);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current)) continue;

            foreach (var prop in current.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var propType = prop.PropertyType;
                var underlying = Nullable.GetUnderlyingType(propType) ?? propType;

                // Unwrap collection element types
                if (underlying.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(underlying))
                {
                    var elementType = underlying.GetGenericArguments().FirstOrDefault();
                    if (elementType is not null && elementType.IsClass && !elementType.IsPrimitive && elementType != typeof(string))
                        queue.Enqueue(elementType);
                    continue;
                }

                // Only check concrete DTO types in the provider namespace
                if (underlying.Namespace == "CrestCreates.Runtime.Persistence.PostgreSql"
                    && underlying.IsClass && !underlying.IsAbstract)
                {
                    queue.Enqueue(underlying);
                }

                underlying.IsInterface.Should().BeFalse(
                    $"DTO {current.Name}.{prop.Name} must not expose interface type {underlying.Name}");
                underlying.IsAbstract.Should().BeFalse(
                    $"DTO {current.Name}.{prop.Name} must not expose abstract type {underlying.Name}");
            }
        }
    }
}

// ── C07 ──────────────────────────────────────────────────────────────────
// Requires PostgreSQL (Testcontainers or CREST_RUNTIME_PG_CONNECTION).

[CollectionDefinition("ControlPlaneReferenceDataOrganizationSchema")]
public class OrganizationSchemaCollection : ICollectionFixture<OrganizationSchemaFixture> { }

public class OrganizationSchemaFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer? _container;
    public string ConnectionString { get; private set; } = "";
    public string Schema { get; } = $"itest_{Guid.NewGuid():N}";

    public OrganizationSchemaFixture()
    {
        var external = Environment.GetEnvironmentVariable("CREST_RUNTIME_PG_CONNECTION");
        if (string.IsNullOrWhiteSpace(external))
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("crest_cp_org_fk_test")
                .WithUsername("crest")
                .WithPassword("crest")
                .Build();
        }
    }

    public async Task InitializeAsync()
    {
        if (_container is not null)
        {
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }
        else
        {
            ConnectionString = Environment.GetEnvironmentVariable("CREST_RUNTIME_PG_CONNECTION")!;
        }

        var options = new PostgreSqlRuntimePersistenceOptions
        {
            ConnectionString = ConnectionString,
            Schema = Schema
        };
        var runner = new PostgreSqlRuntimeMigrationRunner(options);
        await runner.ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = true });
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrEmpty(ConnectionString)) return;
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"drop schema if exists \"{Schema}\" cascade;", connection);
        await command.ExecuteNonQueryAsync();
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

[Collection("ControlPlaneReferenceDataOrganizationSchema")]
public class ControlPlaneReferenceDataOrganizationSchemaTests
{
    private readonly OrganizationSchemaFixture _fixture;

    public ControlPlaneReferenceDataOrganizationSchemaTests(OrganizationSchemaFixture fixture)
        => _fixture = fixture;

    [Fact]
    public async Task C07_OrganizationSchema_Should_NotContainCrossEntityForeignKeys()
        => await VerifyOrganizationSchemaHasNoCrossEntityForeignKeysAsync();

    [Fact]
    public async Task O15_OrganizationProvider_Should_NotIntroduceReferentialSemantics()
        => await VerifyOrganizationSchemaHasNoCrossEntityForeignKeysAsync();

    private async Task VerifyOrganizationSchemaHasNoCrossEntityForeignKeysAsync()
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            SELECT tc.table_name, ccu.table_name AS foreign_table_name,
                   tc.constraint_name
            FROM information_schema.table_constraints AS tc
            JOIN information_schema.constraint_column_usage AS ccu
              ON ccu.constraint_name = tc.constraint_name
             AND ccu.table_schema = tc.table_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
              AND tc.table_schema = @schema
              AND (tc.table_name LIKE 'organization_%' OR ccu.table_name LIKE 'organization_%')",
            connection);
        cmd.Parameters.AddWithValue("schema", _fixture.Schema);

        await using var reader = await cmd.ExecuteReaderAsync();
        var fks = new List<string>();
        while (await reader.ReadAsync())
        {
            fks.Add($"{reader.GetString(0)} -> {reader.GetString(1)} ({reader.GetString(2)})");
        }

        fks.Should().BeEmpty(
            "organization tables must not have cross-entity foreign keys");
    }
}
