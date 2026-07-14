using System.Reflection;
using CrestCreates.Agent.ControlPlane.Abstractions;
using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

public class ControlPlaneAbstractionsPublicApiGuardTests
{
    /// <summary>
    /// The concrete <see cref="CrestCreates.DescriptorDraft"/> assembly name (without .Abstractions suffix).
    /// Types from this assembly must not leak into the public API of ControlPlane.Abstractions.
    /// </summary>
    private const string ForbiddenDescriptorDraftAssembly = "CrestCreates.DescriptorDraft";

    /// <summary>
    /// The abstractions assembly name — allowed in the public API.
    /// </summary>
    private const string AllowedDescriptorDraftAbstractionsAssembly = "CrestCreates.DescriptorDraft.Abstractions";

    [Fact]
    public void ControlPlaneAbstractions_PublicApi_DoesNotExposeConcreteDescriptorDraftTypes()
    {
        var assembly = typeof(AgentToolResultStatus).Assembly;
        var publicTypes = assembly.GetExportedTypes();

        var visited = new HashSet<Type>();
        var violations = new List<string>();

        foreach (var type in publicTypes.OrderBy(t => t.FullName))
        {
            CollectTypeReferencesInPublicApi(type, type.Name, visited, violations);
        }

        Assert.True(
            violations.Count == 0,
            $"ControlPlane.Abstractions public API must not expose types from the concrete {ForbiddenDescriptorDraftAssembly} assembly." +
            Environment.NewLine +
            "These types leak the concrete DescriptorDraft dependency into the public abstractions contract:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static void CollectTypeReferencesInPublicApi(Type rootType, string context, HashSet<Type> visited, List<string> violations)
    {
        if (!visited.Add(rootType))
            return;

        // Base types and interfaces
        CheckType(rootType.BaseType, rootType, "base type", violations);

        foreach (var iface in rootType.GetInterfaces())
        {
            CheckType(iface, rootType, $"implements {iface.Name}", violations);
        }

        // Fields
        foreach (var field in rootType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            CheckType(field.FieldType, rootType, $"field {field.Name}", violations);
            CheckGenericArguments(field.FieldType, rootType, $"field {field.Name}", violations, visited);
        }

        // Properties
        foreach (var prop in rootType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            CheckType(prop.PropertyType, rootType, $"property {prop.Name}", violations);
            CheckGenericArguments(prop.PropertyType, rootType, $"property {prop.Name}", violations, visited);
        }

        // Methods (including interface methods declared on this type)
        foreach (var method in rootType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            // Skip accessor methods (get_/set_) — they are covered by properties
            if (method.IsSpecialName)
                continue;

            CheckType(method.ReturnType, rootType, $"method {method.Name} return type", violations);
            CheckGenericArguments(method.ReturnType, rootType, $"method {method.Name} return type", violations, visited);

            foreach (var param in method.GetParameters())
            {
                CheckType(param.ParameterType, rootType, $"method {method.Name} parameter '{param.Name}'", violations);
                CheckGenericArguments(param.ParameterType, rootType, $"method {method.Name} parameter '{param.Name}'", violations, visited);
            }
        }

        // Events
        foreach (var evt in rootType.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            CheckType(evt.EventHandlerType!, rootType, $"event {evt.Name}", violations);
        }

        // Generic type arguments on the type itself
        if (rootType.IsGenericType)
        {
            foreach (var ga in rootType.GetGenericArguments())
            {
                CheckType(ga, rootType, "generic argument", violations);
            }
        }
    }

    private static void CheckGenericArguments(Type type, Type owner, string context, List<string> violations, HashSet<Type> visited)
    {
        if (!type.IsGenericType && !type.IsArray)
            return;

        // For arrays, check element type
        if (type.IsArray)
        {
            var elementType = type.GetElementType()!;
            CheckType(elementType, owner, $"{context} (array element)", violations);
            CheckGenericArguments(elementType, owner, $"{context} (array element)", violations, visited);
            return;
        }

        // For generic types, check each generic argument
        foreach (var ga in type.GetGenericArguments())
        {
            CheckType(ga, owner, $"{context} (generic argument)", violations);
            // Recurse into the generic argument's own surface
            if (IsDescriptorDraftConcreteType(ga) && ga != owner)
            {
                // Don't recurse into forbidden types — just report the violation
                continue;
            }
            CheckGenericArguments(ga, owner, $"{context} (generic argument)", violations, visited);
        }
    }

    private static void CheckType(Type? type, Type owner, string context, List<string> violations)
    {
        if (type is null)
            return;

        if (IsDescriptorDraftConcreteType(type))
        {
            violations.Add(
                $"  {owner.FullName}: {context} uses {type.FullName} " +
                $"(assembly: {type.Assembly.GetName().Name})");
            return;
        }

        // If it's an allowed DescriptorDraft.Abstractions type, no need to recurse
        if (IsDescriptorDraftAbstractionsType(type))
            return;

        // Don't recurse into system/BCL types
        if (type.Assembly == typeof(object).Assembly || type.Assembly == typeof(Task).Assembly)
            return;
    }

    private static bool IsDescriptorDraftConcreteType(Type type)
    {
        var assemblyName = type.Assembly.GetName().Name;
        return string.Equals(assemblyName, ForbiddenDescriptorDraftAssembly, StringComparison.Ordinal);
    }

    private static bool IsDescriptorDraftAbstractionsType(Type type)
    {
        var assemblyName = type.Assembly.GetName().Name;
        return string.Equals(assemblyName, AllowedDescriptorDraftAbstractionsAssembly, StringComparison.Ordinal);
    }
}
