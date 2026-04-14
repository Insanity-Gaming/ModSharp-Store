using Microsoft.Extensions.Configuration;

namespace InsanityGaming.Store.Interfaces;

public interface IModule
{
    bool Init();

    void Shutdown()
    {
    }

    void ConfigReload(IConfig config)
    {
    }

    void OnLibraryConnected(string name)
    {
    }

    void OnLibraryDisconnect(string name)
    {
    }

    void OnAllModulesLoaded()
    {
    }
}