using System;

namespace CrestCreates.Domain.DataFilter;

public interface IDisableDataFilter
{
    bool IsEnabled<TFilter>() where TFilter : class;

    IDisposable Disable<TFilter>() where TFilter : class;

    IDisposable Enable<TFilter>() where TFilter : class;
}