#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CrestCreates.Tooling.HttpProjection;

/// <summary>
/// #129 S2.2: the HTTP emission rules both front-ends share. The CRUD emitter and the hand-written
/// projection emitter must not carry two copies of these rules (declared-type provenance and the host
/// JSON resolver insertion policy).
/// </summary>
public static class HttpProjectionShared
{
    /// <summary>
    /// Types a generated source file declares, using the #128 provenance rule: every type declaration,
    /// qualified by its nearest block namespace. Nested declarations are reported by their own name.
    /// </summary>
    public static IReadOnlyList<string> DeclaredTypeFullNames(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return Array.Empty<string>();
        }

        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source);
        return tree.GetRoot()
            .DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>()
            .Select(declaration =>
            {
                var namespaceName = declaration.Ancestors()
                    .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.NamespaceDeclarationSyntax>()
                    .LastOrDefault()?.Name?.ToString();
                return string.IsNullOrEmpty(namespaceName)
                    ? declaration.Identifier.ValueText
                    : namespaceName + "." + declaration.Identifier.ValueText;
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// #128 §7.1/§7.2 resolver insertion policy, shared verbatim: an already-present generated context
    /// is not re-inserted; the context goes immediately before the uncustomized framework-default
    /// reflection resolver when present, otherwise it is appended.
    /// </summary>
    public static void EmitJsonConfigurator(StringBuilder builder, string jsonContextFullName, string methodIndent)
    {
        Line(builder, methodIndent + "var generatedContext = " + jsonContextFullName + ".Default;");
        Line(builder, methodIndent + "var chain = options.SerializerOptions.TypeInfoResolverChain;");
        Line(builder, methodIndent + "for (var index = 0; index < chain.Count; index++)");
        Line(builder, methodIndent + "{");
        Line(builder, methodIndent + "    if (global::System.Object.ReferenceEquals(chain[index], generatedContext))");
        Line(builder, methodIndent + "    {");
        Line(builder, methodIndent + "        return;");
        Line(builder, methodIndent + "    }");
        Line(builder, methodIndent + "}");
        Line(builder, string.Empty);
        Line(builder, methodIndent + "var insertionIndex = chain.Count;");
        Line(builder, methodIndent + "for (var index = 0; index < chain.Count; index++)");
        Line(builder, methodIndent + "{");
        Line(builder, methodIndent
            + "    if (chain[index] is global::System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver defaultResolver");
        Line(builder, methodIndent + "        && defaultResolver.GetType() == typeof(global::System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver)");
        Line(builder, methodIndent + "        && defaultResolver.Modifiers.Count == 0)");
        Line(builder, methodIndent + "    {");
        Line(builder, methodIndent + "        insertionIndex = index;");
        Line(builder, methodIndent + "        break;");
        Line(builder, methodIndent + "    }");
        Line(builder, methodIndent + "}");
        Line(builder, string.Empty);
        Line(builder, methodIndent + "chain.Insert(insertionIndex, generatedContext);");
    }

    private static void Line(StringBuilder builder, string text) => builder.AppendLine(text);
}
