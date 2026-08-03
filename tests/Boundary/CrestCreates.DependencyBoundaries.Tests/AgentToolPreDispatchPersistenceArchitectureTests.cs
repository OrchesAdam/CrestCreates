using System.Reflection;
using CrestCreates.Agent.Tools;
using FluentAssertions;
using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

public class AgentToolPreDispatchPersistenceArchitectureTests
{
    private static readonly Assembly AbstractionsAssembly =
        typeof(AgentToolPreDispatchIdentity).Assembly;

    [Fact]
    public void RuntimeProjects_Should_Not_Reference_AgentToolPostgreSqlProvider()
    {
        var assemblyName = AbstractionsAssembly.GetName().Name;
        assemblyName.Should().Be("CrestCreates.Agent.Tools.Abstractions");

        var referencedAssemblies = AbstractionsAssembly.GetReferencedAssemblies();

        foreach (var refAssembly in referencedAssemblies)
        {
            refAssembly.Name.Should().NotContain("Npgsql",
                "Agent Tool Abstractions must not reference Npgsql.");
            refAssembly.Name.Should().NotContain("PostgreSql",
                "Agent Tool Abstractions must not reference PostgreSQL provider.");
        }
    }

    [Fact]
    public void AgentToolDurableContracts_Should_Not_Expose_NpgsqlTypes()
    {
        var publicTypes = AbstractionsAssembly.GetExportedTypes();

        foreach (var type in publicTypes)
        {
            type.Namespace.Should().NotStartWith("Npgsql",
                $"public type {type.FullName} must not expose Npgsql namespace.");

            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var field in fields)
            {
                field.FieldType.Namespace.Should().NotStartWith("Npgsql",
                    $"field {type.FullName}.{field.Name} must not expose Npgsql type.");
            }

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var property in properties)
            {
                property.PropertyType.Namespace.Should().NotStartWith("Npgsql",
                    $"property {type.FullName}.{property.Name} must not expose Npgsql type.");
            }

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (var method in methods)
            {
                if (method.ReturnType.Namespace is not null)
                {
                    method.ReturnType.Namespace.Should().NotStartWith("Npgsql",
                        $"return type of {type.FullName}.{method.Name} must not expose Npgsql type.");
                }

                foreach (var param in method.GetParameters())
                {
                    if (param.ParameterType.Namespace is not null)
                    {
                        param.ParameterType.Namespace.Should().NotStartWith("Npgsql",
                            $"parameter {param.Name} of {type.FullName}.{method.Name} must not expose Npgsql type.");
                    }
                }
            }
        }
    }

    [Fact]
    public void AgentToolPreDispatchIdentity_Should_Require_LogicalInvocationKey_And_AttemptId()
    {
        var type = typeof(AgentToolPreDispatchIdentity);

        var constructor = type.GetConstructors()[0];
        var parameters = constructor.GetParameters();

        parameters.Should().HaveCount(2);
        parameters[0].ParameterType.Should().Be(typeof(AgentToolLogicalInvocationKey));
        parameters[1].ParameterType.Should().Be(typeof(string));
    }

    [Fact]
    public void IAgentToolPreDispatchReconciliationStore_Should_Have_Read_And_Cas_Operations()
    {
        var storeType = typeof(IAgentToolPreDispatchReconciliationStore);

        storeType.GetMethod("ReadObservationAsync").Should().NotBeNull();
        storeType.GetMethod("TryUpsertObservationAsync").Should().NotBeNull();
        storeType.GetMethod("ReadReceiptAsync").Should().NotBeNull();
        storeType.GetMethod("TryInsertReceiptAsync").Should().NotBeNull();
    }

    [Fact]
    public void IAgentToolPreDispatchPersistenceCapabilities_Should_Declare_SupportLevel()
    {
        var capsType = typeof(IAgentToolPreDispatchPersistenceCapabilities);

        var supportProperty = capsType.GetProperty("Capability");
        supportProperty.Should().NotBeNull();
        supportProperty!.PropertyType.Should().Be(typeof(AgentToolPreDispatchPersistenceCapability));
    }
}
