using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Infrastructure.Caching.Advanced
{
    public interface ICacheWarmer
    {
        string Name { get; }
        int Priority { get; }
        Task WarmUpAsync(CancellationToken cancellationToken = default);
    }
}
