using Sharp.Shared;
using Microsoft.Extensions.Configuration;

namespace Store;

public class Store : IModSharpModule
{
    public Store(
        ISharedSystem sharedSystem,
        string dllPath,
        string sharpPath,
        Version version,
        IConfiguration configuration,
        bool hotReload)
    {

    }

    public bool Init()
    {
        return true;
    }

    public void Shutdown()
    {
    }

    string IModSharpModule.DisplayName => "Store";
    string IModSharpModule.DisplayAuthor => "Retro";
}
