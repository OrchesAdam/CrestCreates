using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.VirtualFileSystem.Models;

namespace CrestCreates.VirtualFileSystem.Providers;

public interface IEmbeddedResourceProvider
{
    Assembly Assembly { get; }
    string BaseNamespace { get; }

    Task<IEnumerable<string>> GetResourceNamesAsync(CancellationToken ct = default);

    Task<Stream?> GetResourceStreamAsync(string resourceName, CancellationToken ct = default);
}
