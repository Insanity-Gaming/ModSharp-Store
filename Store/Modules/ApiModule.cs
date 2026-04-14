using InsanityGaming.Store.Api;
using InsanityGaming.Store.Interfaces;
using InsanityGaming.Store.Shared;
using Microsoft.Extensions.Logging;

namespace InsanityGaming.Store.Modules;

public class ApiModule : IModule
{
    private readonly InterfaceBridge _bridge;
    private readonly Store _plugin;
    private readonly IItemsApi _itemsApi;
    private readonly IEquipmentApi _equipmentApi;
    private readonly ILogger<ApiModule> _logger;

    public ApiModule(
        InterfaceBridge bridge,
        Store plugin,
        IItemsApi itemsApi,
        IEquipmentApi equipmentApi,
        ILogger<ApiModule> logger)
    {
        _bridge = bridge;
        _plugin = plugin;
        _itemsApi = itemsApi;
        _equipmentApi = equipmentApi;
        _logger = logger;
    }

    public bool Init()
    {
        _bridge.ModuleManager.RegisterSharpModuleInterface<IItemsApi>(
            _plugin,
            IItemsApi.Identity,
            _itemsApi
        );

        _bridge.ModuleManager.RegisterSharpModuleInterface<IEquipmentApi>(
            _plugin,
            IEquipmentApi.Identity,
            _equipmentApi
        );

        _logger.LogInformation("Store API interfaces registered");
        return true;
    }
}
