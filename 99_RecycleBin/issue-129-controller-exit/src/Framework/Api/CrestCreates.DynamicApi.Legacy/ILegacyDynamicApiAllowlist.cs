namespace CrestCreates.DynamicApi;

/// <summary>
/// The exact activation policy every legacy mapping entry requires. It is internal so only the
/// explicit Web.Legacy activation package can supply one; the raw registry/map entry points cannot
/// be used without it.
/// </summary>
internal interface ILegacyDynamicApiAllowlist
{
    IReadOnlyCollection<Type> ServiceTypes { get; }

    bool TryGetAllowedActions(Type serviceType, out IReadOnlyCollection<string> actions);
}
