using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.CodeGenerator.LocalEventGenerator;

/// <summary>
/// Generates the per-assembly local event registration contributor: typed invokers, handler DI
/// bindings, expected-handler diagnostics metadata and a public AddLocalEvents() extension that
/// the host (or the owning framework package) must call explicitly. No runtime scanning, no
/// reflection dispatch and no fallback when the contributor is not activated.
/// </summary>
[Generator]
public sealed class LocalEventSourceGenerator : IIncrementalGenerator
{
    internal const string HandlerInterfaceMetadataName = "CrestCreates.EventBus.Abstractions.ILocalEventHandler`1";
    internal const string LocalEventInterfaceMetadataName = "CrestCreates.EventBus.Abstractions.ILocalEvent";
    internal const string LocalEventAttributeMetadataName = "CrestCreates.EventBus.Abstractions.LocalEventAttribute";
    internal const string LegacyIdentityAttributeMetadataName = "CrestCreates.EventBus.Abstractions.LocalEventLegacyIdentityAttribute";
    internal const string ReplayAliasAttributeMetadataName = "CrestCreates.EventBus.Abstractions.LocalEventReplayAliasAttribute";
    internal const string ToolchainPropertyName = "build_property.CrestCreatesLocalEventJsonContracts";

    public static class DiagnosticDescriptors
    {
        public const string OpenGenericId = "CCCLE001";
        public const string InvalidEventTypeId = "CCCLE002";
        public const string UnsupportedAccessibilityId = "CCCLE003";
        public const string ReplayableMissingCodecToolchainId = "CCCLE004";
        public const string UnsealedPayloadId = "CCCLE005";

        public static readonly DiagnosticDescriptor OpenGeneric = new(
            id: OpenGenericId,
            title: "Open generic local event handler or event",
            messageFormat: "{0}",
            category: "CrestCreates.EventBus.LocalEvent",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InvalidEventType = new(
            id: InvalidEventTypeId,
            title: "Invalid local event declaration",
            messageFormat: "{0}",
            category: "CrestCreates.EventBus.LocalEvent",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnsupportedAccessibility = new(
            id: UnsupportedAccessibilityId,
            title: "Local event type is not accessible from generated code",
            messageFormat: "{0}",
            category: "CrestCreates.EventBus.LocalEvent",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ReplayableMissingCodecToolchain = new(
            id: ReplayableMissingCodecToolchainId,
            title: "Replayable local event requires the JSON contract toolchain",
            messageFormat: "{0}",
            category: "CrestCreates.EventBus.LocalEvent",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnsealedPayload = new(
            id: UnsealedPayloadId,
            title: "Replayable local event payload must be a closed type",
            messageFormat: "{0}",
            category: "CrestCreates.EventBus.LocalEvent",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is TypeDeclarationSyntax { BaseList.Types.Count: > 0 },
                transform: static (generatorContext, _) => LocalEventSymbolAnalyzer.Analyze(generatorContext))
            .Where(static analysis => analysis is not null)
            .Select(static (analysis, _) => analysis!)
            .Collect();

        var compilationAndOptions = context.CompilationProvider.Combine(context.AnalyzerConfigOptionsProvider);

        context.RegisterSourceOutput(
            candidates.Combine(compilationAndOptions),
            static (productionContext, source) =>
                Emit(productionContext, source.Left, source.Right.Left, source.Right.Right));
    }

    private static void Emit(
        SourceProductionContext context,
        ImmutableArray<LocalEventAnalysis> analyses,
        Compilation compilation,
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        foreach (var analysis in analyses)
        {
            foreach (var diagnostic in analysis.Diagnostics)
            {
                context.ReportDiagnostic(ToDiagnostic(diagnostic));
            }
        }

        if (compilation.AssemblyName is not { Length: > 0 } assemblyName)
        {
            return;
        }

        if (compilation.GetTypeByMetadataName(HandlerInterfaceMetadataName) is null ||
            compilation.GetTypeByMetadataName(LocalEventInterfaceMetadataName) is null)
        {
            return;
        }

        var handlersByEvent = new Dictionary<string, List<HandlerBindingInfo>>(StringComparer.Ordinal);
        var declarations = new Dictionary<string, EventDeclarationInfo>(StringComparer.Ordinal);

        foreach (var analysis in analyses)
        {
            foreach (var handler in analysis.Handlers)
            {
                if (!handlersByEvent.TryGetValue(handler.EventMetadataName, out var list))
                {
                    list = new List<HandlerBindingInfo>();
                    handlersByEvent[handler.EventMetadataName] = list;
                }

                if (!list.Any(existing => existing.HandlerMetadataName == handler.HandlerMetadataName))
                {
                    list.Add(handler);
                }
            }

            foreach (var declaration in analysis.EventDeclarations)
            {
                if (declarations.TryGetValue(declaration.EventMetadataName, out var existing))
                {
                    // The same event can be seen both as a local declaration and as a handler's
                    // referenced type. Ownership is owned if any producer observed it as owned.
                    declarations[declaration.EventMetadataName] = declaration with { Owned = existing.Owned || declaration.Owned };
                }
                else
                {
                    declarations[declaration.EventMetadataName] = declaration;
                }
            }
        }

        var eventNames = handlersByEvent.Keys
            .Union(declarations.Keys, StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        if (eventNames.Count == 0)
        {
            return;
        }

        var hasToolchain = optionsProvider.GlobalOptions.TryGetValue(ToolchainPropertyName, out var toolchainValue) &&
                           string.Equals(toolchainValue, "true", StringComparison.OrdinalIgnoreCase);

        // Only the assembly that owns a replayable event is responsible for its JSON codec. A
        // handler-only assembly that merely references another assembly's replayable event must not
        // require the toolchain nor emit a codec contribution for it (the owner registers the codec,
        // and the handler-only binding resolves it at freeze time).
        var ownsReplayableEvent = false;
        foreach (var eventName in eventNames)
        {
            if (declarations.TryGetValue(eventName, out var declaration) && declaration.Replayable && declaration.Owned)
            {
                ownsReplayableEvent = true;
                if (!hasToolchain)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.ReplayableMissingCodecToolchain,
                        declaration.Location,
                        $"Replayable event '{eventName}' requires a source-generated JSON codec. " +
                        "Import the CrestCreates.JsonContracts.Build toolchain in this project so its generated " +
                        "roots and codec contribution are available."));
                }
            }
        }

        var source = LocalEventCodeWriter.Write(
            assemblyName,
            eventNames,
            handlersByEvent,
            declarations,
            emitCodecContribution: hasToolchain && ownsReplayableEvent);

        context.AddSource("LocalEventRegistration.g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static Diagnostic ToDiagnostic(LocalEventDiagnostic diagnostic)
    {
        var descriptor = diagnostic.Id switch
        {
            DiagnosticDescriptors.OpenGenericId => DiagnosticDescriptors.OpenGeneric,
            DiagnosticDescriptors.InvalidEventTypeId => DiagnosticDescriptors.InvalidEventType,
            DiagnosticDescriptors.UnsupportedAccessibilityId => DiagnosticDescriptors.UnsupportedAccessibility,
            DiagnosticDescriptors.UnsealedPayloadId => DiagnosticDescriptors.UnsealedPayload,
            _ => DiagnosticDescriptors.InvalidEventType,
        };

        return Diagnostic.Create(descriptor, diagnostic.Location, diagnostic.Message);
    }
}
