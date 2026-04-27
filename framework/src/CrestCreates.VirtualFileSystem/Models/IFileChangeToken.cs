using System;

namespace CrestCreates.VirtualFileSystem.Models;

public interface IFileChangeToken
{
    bool HasChanged { get; }
    IDisposable RegisterChangeCallback(Action<object> callback, object state);
}
