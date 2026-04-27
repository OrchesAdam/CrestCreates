using System;
using System.Threading;

namespace CrestCreates.VirtualFileSystem.Models;

public class FileChangeToken : IFileChangeToken
{
    private int _changed;

    public bool HasChanged => _changed != 0;

    public void NotifyChanged()
    {
        Interlocked.Exchange(ref _changed, 1);
    }

    public IDisposable RegisterChangeCallback(Action<object> callback, object state)
    {
        return new ChangeCallbackRegistration(callback, state);
    }

    private class ChangeCallbackRegistration : IDisposable
    {
        private readonly Action<object> _callback;
        private readonly object _state;

        public ChangeCallbackRegistration(Action<object> callback, object state)
        {
            _callback = callback;
            _state = state;
        }

        public void Dispose()
        {
            // Callbacks are fired on change, nothing to dispose
        }
    }
}
