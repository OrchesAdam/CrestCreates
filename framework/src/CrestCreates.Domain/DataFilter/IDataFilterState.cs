using System;
using System.Collections.Generic;

namespace CrestCreates.Domain.DataFilter
{
    public interface IDisableDataFilter
    {
        bool IsEnabled<TFilter>() where TFilter : class;

        IDisposable Disable<TFilter>() where TFilter : class;

        IDisposable Enable<TFilter>() where TFilter : class;
    }

    public class DataFilterState
    {
        public Dictionary<Type, bool> FilterStates { get; } = new Dictionary<Type, bool>();

        public bool IsEnabled<TFilter>() where TFilter : class
        {
            var filterType = typeof(TFilter);
            return FilterStates.TryGetValue(filterType, out var enabled) ? enabled : true;
        }

        public void SetFilterState<TFilter>(bool enabled) where TFilter : class
        {
            var filterType = typeof(TFilter);
            FilterStates[filterType] = enabled;
        }
    }
}
