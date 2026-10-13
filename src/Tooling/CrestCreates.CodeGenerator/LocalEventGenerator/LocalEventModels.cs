using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CrestCreates.CodeGenerator.LocalEventGenerator;

internal sealed record LegacyIdentityInfo(string Name, int Version);

internal sealed record HandlerBindingInfo(
    string HandlerGlobalName,
    string HandlerMetadataName,
    string EventGlobalName,
    string EventMetadataName,
    Location? Location);

internal sealed record EventDeclarationInfo(
    string EventMetadataName,
    bool Replayable,
    int Version,
    ImmutableArray<LegacyIdentityInfo> LegacyIdentities,
    Location? Location);

internal sealed record LocalEventDiagnostic(string Id, string Message, Location? Location);

internal sealed record LocalEventAnalysis(
    ImmutableArray<HandlerBindingInfo> Handlers,
    ImmutableArray<EventDeclarationInfo> EventDeclarations,
    ImmutableArray<LocalEventDiagnostic> Diagnostics);
