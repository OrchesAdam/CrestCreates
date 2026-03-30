namespace CrestCreates.Infrastructure.Caching.MultiTenancy
{
    public interface ICacheKeyGenerator
    {
        string GenerateKey(string baseKey);
    }
}
