using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.VirtualFileSystem.Models;

public interface IVirtualDirectory
{
    VirtualPath Path { get; }
    string Name { get; }
    DateTimeOffset LastModified { get; }
    bool Exists { get; }

    Task<IEnumerable<IVirtualFile>> GetFilesAsync(CancellationToken ct = default);
    Task<IEnumerable<IVirtualDirectory>> GetDirectoriesAsync(CancellationToken ct = default);
}
