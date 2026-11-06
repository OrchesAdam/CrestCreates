#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static CrestCreates.Tooling.CrudGeneration.CrudArtifactWriter;

namespace CrestCreates.Tooling.CrudGeneration;

/// <summary>
/// Permission definition artifacts and the shared permission-name projection used by the service
/// implementation.
/// </summary>
internal static class CrudPermissionEmitter
{
    private const string PermissionNameNamespace = "CrestCreates.Core.Abstractions.Identity";

    private static readonly string[] PermissionActionOrder = ["Create", "Get", "Search", "Update", "Delete"];

    internal static CrudArtifact EmitPermissions(CrudGenerationModel model)
    {
        var builder = StartDocument(model);
        OpenNamespace(builder, model.PermissionsNamespace);
        Line(builder, Indent + "public static partial class " + model.PermissionsClassName);
        Line(builder, Indent + "{");
        foreach (var action in OrderedActions(model))
        {
            Line(builder, NestedIndent
                + "private const string " + action.Name + "Value = \"" + action.PermissionName + "\";");
            if (model.SupportsPermissionNames)
            {
                Line(builder, NestedIndent
                    + "public static global::" + PermissionNameNamespace + ".PermissionName "
                    + action.Name + " { get; } = new(" + action.Name + "Value);");
            }
        }

        Line(builder, Indent + "}");
        CloseNamespace(builder);
        return Artifact(model.PermissionsClassName + ".g.cs", builder);
    }

    internal static string Permission(CrudGenerationModel model, string actionName)
    {
        var typed = Qualified(model.PermissionsNamespace + "." + model.PermissionsClassName) + "." + actionName;
        if (model.SupportsPermissionNames)
        {
            return typed;
        }

        var action = model.Actions.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, actionName, StringComparison.Ordinal));
        return action is null
            ? typed
            : "\"" + action.PermissionName + "\"";
    }

    private static IEnumerable<CrudActionModel> OrderedActions(CrudGenerationModel model) =>
        PermissionActionOrder
            .Select(name => model.Actions.FirstOrDefault(action =>
                string.Equals(action.Name, name, StringComparison.Ordinal)))
            .Where(action => action is not null)!;
}
