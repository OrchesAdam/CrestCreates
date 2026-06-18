using System;
using System.Collections.Generic;

namespace CrestCreates.Web;

public sealed class CrestGeneratedApiWebOptions
{
    private readonly List<Type> _serviceMarkerTypes = new();

    public IReadOnlyList<Type> ServiceMarkerTypes => _serviceMarkerTypes;

    public CrestGeneratedApiWebOptions AddApplicationServiceAssembly<TMarker>()
    {
        var markerType = typeof(TMarker);
        if (!_serviceMarkerTypes.Contains(markerType))
        {
            _serviceMarkerTypes.Add(markerType);
        }

        return this;
    }
}
