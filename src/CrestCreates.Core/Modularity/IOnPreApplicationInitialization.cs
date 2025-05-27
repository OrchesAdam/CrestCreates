using System.Threading.Tasks;

namespace CrestCreates.Modularity;

public interface IOnPreApplicationInitialization
{
    Task OnPreApplicationInitializationAsync();

    void OnPreApplicationInitialization();
}