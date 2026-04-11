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
    
}