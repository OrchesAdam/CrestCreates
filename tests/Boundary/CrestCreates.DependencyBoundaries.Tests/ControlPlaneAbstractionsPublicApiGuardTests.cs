using System.Reflection;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

public class ControlPlaneAbstractionsPublicApiGuardTests
{
    private const string ForbiddenDescriptorDraftAssembly = "CrestCreates.DescriptorDraft";
    private const string ActivationAbstractionsAssembly = "CrestCreates.Agent.ControlPlane.Activation.Abstractions";

    [Fact]
    public void ControlPlaneAbstractions_PublicApi_DoesNotExposeConcreteDescriptorDraftTypes()
    {
        AssertNoPublicApiReferences(
            typeof(AgentToolResultStatus).Assembly,
            ForbiddenDescriptorDraftAssembly,
            "ControlPlane.Abstractions public API must not expose concrete DescriptorDraft types.");
    }

    [Fact]
    public void ActivationAbstractions_PublicApi_DoesNotExposeConcreteDescriptorDraftTypes()
    {
        AssertNoPublicApiReferences(
            typeof(ActivationRequest).Assembly,
            ForbiddenDescriptorDraftAssembly,
            "ControlPlane.Activation.Abstractions public API must not expose concrete DescriptorDraft types.");
    }

    [Fact]
    public void ControlPlaneAbstractions_PublicApi_DoesNotExposeActivationContracts()
    {
        AssertNoPublicApiReferences(
            typeof(AgentToolResultStatus).Assembly,
            ActivationAbstractionsAssembly,
            "ControlPlane.Abstractions public API must not expose Activation contracts.");
    }

    private static void AssertNoPublicApiReferences(Assembly assembly, string forbiddenAssemblyName, string message)
    {
        var visited = new HashSet<Type>();
        var violations = new List<string>();

        foreach (var type in assembly.GetExportedTypes().OrderBy(t => t.FullName))
        {
            CollectTypeReferencesInPublicApi(type, type.Name, visited, violations, forbiddenAssemblyName);
        }

        Assert.True(
            violations.Count == 0,
            message + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static void CollectTypeReferencesInPublicApi(
        Type rootType,
        string context,
        HashSet<Type> visited,
        List<string> violations,
        string forbiddenAssemblyName)
    {
        if (!visited.Add(rootType))
            return;

        CheckType(rootType.BaseType, rootType, "base type", violations, forbiddenAssemblyName);
        foreach (var iface in rootType.GetInterfaces())
        {
            CheckType(iface, rootType, $"implements {iface.Name}", violations, forbiddenAssemblyName);
        }

        foreach (var field in rootType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            CheckType(field.FieldType, rootType, $"field {field.Name}", violations, forbiddenAssemblyName);
            CheckGenericArguments(field.FieldType, rootType, $"field {field.Name}", violations, forbiddenAssemblyName);
        }

        foreach (var prop in rootType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            CheckType(prop.PropertyType, rootType, $"property {prop.Name}", violations, forbiddenAssemblyName);
            CheckGenericArguments(prop.PropertyType, rootType, $"property {prop.Name}", violations, forbiddenAssemblyName);
        }

        foreach (var method in rootType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (method.IsSpecialName)
                continue;

            CheckType(method.ReturnType, rootType, $"method {method.Name} return type", violations, forbiddenAssemblyName);
            CheckGenericArguments(method.ReturnType, rootType, $"method {method.Name} return type", violations, forbiddenAssemblyName);
            foreach (var parameter in method.GetParameters())
            {
                CheckType(parameter.ParameterType, rootType, $"method {method.Name} parameter '{parameter.Name}'", violations, forbiddenAssemblyName);
                CheckGenericArguments(parameter.ParameterType, rootType, $"method {method.Name} parameter '{parameter.Name}'", violations, forbiddenAssemblyName);
            }
        }

        foreach (var evt in rootType.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            CheckType(evt.EventHandlerType, rootType, $"event {evt.Name}", violations, forbiddenAssemblyName);
        }

        if (rootType.IsGenericType)
        {
            foreach (var argument in rootType.GetGenericArguments())
            {
                CheckType(argument, rootType, "generic argument", violations, forbiddenAssemblyName);
            }
        }
    }

    private static void CheckGenericArguments(
        Type type,
        Type owner,
        string context,
        List<string> violations,
        string forbiddenAssemblyName)
    {
        if (type.IsArray)
        {
            var elementType = type.GetElementType()!;
            CheckType(elementType, owner, $"{context} (array element)", violations, forbiddenAssemblyName);
            CheckGenericArguments(elementType, owner, $"{context} (array element)", violations, forbiddenAssemblyName);
            return;
        }

        if (!type.IsGenericType)
            return;

        foreach (var argument in type.GetGenericArguments())
        {
            CheckType(argument, owner, $"{context} (generic argument)", violations, forbiddenAssemblyName);
            CheckGenericArguments(argument, owner, $"{context} (generic argument)", violations, forbiddenAssemblyName);
        }
    }

    private static void CheckType(
        Type? type,
        Type owner,
        string context,
        List<string> violations,
        string forbiddenAssemblyName)
    {
        if (type is null)
            return;

        if (string.Equals(type.Assembly.GetName().Name, forbiddenAssemblyName, StringComparison.Ordinal))
        {
            violations.Add(
                $"  {owner.FullName}: {context} uses {type.FullName} " +
                $"(assembly: {type.Assembly.GetName().Name})");
        }
    }
}
