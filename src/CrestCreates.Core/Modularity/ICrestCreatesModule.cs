namespace CrestCreates.Modularity
{
    /// <summary>
    /// 
    /// </summary>
    public interface ICrestCreatesModule : IOnPostApplicationInitialization, IOnPostApplicationShutdown, IOnPreApplicationInitialization, IOnPreApplicationShutdown
    {
        
    }
}