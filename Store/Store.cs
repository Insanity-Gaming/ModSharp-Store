using System;
using System.IO;
using System.Text.Json;
using InsanityGaming.Store.Configs;
using InsanityGaming.Store.Interfaces;
using InsanityGaming.Store.Items;
using InsanityGaming.Store.Modules;
using InsanityGaming.Store.Repository;
using InsanityGaming.Store.Services;
using InsanityGaming.Store.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sharp.Shared;
using Sharp.Shared.Listeners;
using SqlSugar;

namespace InsanityGaming.Store;

public class Store : IModSharpModule, IGameListener
{
    private readonly IServiceProvider _services;
    private readonly InterfaceBridge _bridge;
    private readonly ILogger<Store> _logger;

    private IConfigurationRoot _configRoot = null!;
    private volatile StoreConfig _config = null!;

    public Store(
        ISharedSystem sharedSystem,
        string dllPath,
        string sharpPath,
        Version version,
        IConfiguration configuration,
        bool hotReload)
    {
        _bridge = new InterfaceBridge(dllPath, sharpPath, version, sharedSystem);

        var configPath = Path.Combine(_bridge.ConfigPath, "store.json");

        // --- Ensure config exists ---
        if (!File.Exists(configPath))
        {
            var defaultConfig = new StoreConfig();

            var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(configPath, json);
        }

        // --- Load config ---
        _configRoot = new ConfigurationBuilder()
            .SetBasePath(_bridge.ConfigPath)
            .AddJsonFile("store.json", optional: false, reloadOnChange: true)
            .Build();

        // Bind initial config
        _config = new StoreConfig();
        _configRoot.Bind(_config);

        // Hook reload
        RegisterReload();

        // --- DI ---
        var services = new ServiceCollection();

        services.AddSingleton(_bridge);
        services.AddSingleton(sharedSystem);

        services.AddSingleton(_bridge.LoggerFactory);
        services.AddLogging();

        services.AddSingleton(this); // optional if you want access elsewhere

        services.AddSingleton<ISqlSugarClient>(_ => new SqlSugarClient(new ConnectionConfig
        {
            DbType = DbType.MySql,
            ConnectionString = $"Server={_config.DbHost};Port={_config.DbPort};Database={_config.DbName};Uid={_config.DbUser};Pwd={_config.DbPassword};",
            IsAutoCloseConnection = true
        }));

        services.AddSingleton<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<IInventoryRepository, InventoryRepository>();
        services.AddSingleton<IEquipmentRepository, EquipmentRepository>();

        // Register the live config instance so modules can inject StoreConfig directly
        services.AddSingleton(_config);

        // Item types — add one line per item type as new types are created
        services.AddSingleton<IItemType, ItemPlayerModels>();

        // Item and equipment services
        services.AddSingleton<SessionItemStore>();
        services.AddSingleton<IItemService, ItemService>();
        services.AddSingleton<IEquipmentManager, EquipmentManager>();
        services.AddSingleton<IPurchaseService, PurchaseService>();

        // CreditsModule — registered as concrete type so IModule + ICredits can both resolve the same instance
        services.AddSingleton<CreditsModule>();
        services.AddSingleton<ICredits>(sp => sp.GetRequiredService<CreditsModule>());

        // ItemsModule — same triple-registration pattern as CreditsModule
        services.AddSingleton<ItemsModule>();
        services.AddSingleton<IItems>(sp => sp.GetRequiredService<ItemsModule>());

        // EquipmentModule — registered as concrete type so IModule + IEquipment can both resolve the same instance
        services.AddSingleton<EquipmentModule>();
        services.AddSingleton<IEquipment>(sp => sp.GetRequiredService<EquipmentModule>());

        // Modules — DatabaseModule first (creates tables), then EquipmentModule, CreditsModule, StoreMenuModule
        services.AddSingleton<IModule, DatabaseModule>();
        services.AddSingleton<IModule, ExpiryCleanupService>();
        services.AddSingleton<IModule>(sp => sp.GetRequiredService<CreditsModule>());
        services.AddSingleton<IModule>(sp => sp.GetRequiredService<EquipmentModule>());
        services.AddSingleton<IModule>(sp => sp.GetRequiredService<ItemsModule>());
        services.AddSingleton<IModule, StoreMenuModule>();
        services.AddSingleton<IModule, AdminCommandModule>();

        _services = services.BuildServiceProvider();

        _logger = _services.GetRequiredService<ILogger<Store>>();

        _logger.LogInformation("Store module constructed (HotReload: {HotReload})", hotReload);
    }

    public bool Init()
    {
        foreach (var service in _services.GetServices<IModule>())
        {
            if(!service.Init())
                _logger.LogError($"Store Module {service.GetType().Name} could not be initialized.");
        }

        _logger.LogInformation("Store Init");
        return true;
    }

    public void PostInit()
    {
        var credits = _services.GetRequiredService<CreditsModule>();
        _bridge.ModuleManager.RegisterSharpModuleInterface<ICreditsApi>(
            this,
            ICreditsApi.Identity,
            credits
        );
        
        var equipment = _services.GetRequiredService<EquipmentModule>();
        _bridge.ModuleManager.RegisterSharpModuleInterface<IEquipmentApi>(
            this,
            IEquipmentApi.Identity,
            equipment
        );
        
        var items =  _services.GetRequiredService<ItemsModule>();
        _bridge.ModuleManager.RegisterSharpModuleInterface<IItemsApi>(
            this,
            IItemsApi.Identity,
            items
        );
    }

    public void OnLibraryConnected(string name)
    {
        foreach (var service in _services.GetServices<IModule>())
            service.OnLibraryConnected(name);
    }

    public void OnLibraryDisconnect(string name)
    {
        foreach (var service in _services.GetServices<IModule>())
            service.OnLibraryDisconnect(name);
    }

    public void OnAllModulesLoaded()
    {
        foreach (var service in _services.GetServices<IModule>())
            service.OnAllModulesLoaded();
    }

    public void Shutdown()
    {
        _logger.LogInformation("Store Shutdown");

        foreach (var service in _services.GetServices<IModule>())
        {
            service.Shutdown();
        }

        if (_services is IDisposable disposable)
            disposable.Dispose();
    }

    private void OnConfigReload()
    {
        foreach (var service in _services.GetServices<IModule>())
        {
            service.ConfigReload(_config);
        }
    }

    private void RegisterReload()
    {
        _configRoot.GetReloadToken().RegisterChangeCallback(_ =>
        {
            try
            {
                _logger.LogInformation("Config reload detected");

                var newConfig = new StoreConfig();
                _configRoot.Bind(newConfig);

                _config = newConfig;
                OnConfigReload();

                _logger.LogInformation("Config successfully reloaded");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reload config");
            }

            RegisterReload();

        }, null);
    }
    
    public void OnResourcePrecache()
    {
        var assetPath = Path.Combine(_bridge.SharpPath, "assets");

        if (!Directory.Exists(assetPath))
        {
            return;
        }

        var files = Directory.EnumerateFiles(assetPath, "*", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            var relative = file[(assetPath.Length + 1)..].Replace("\\", "/");

            var asset = relative.EndsWith("_c", StringComparison.OrdinalIgnoreCase)
                ? relative[..^2]
                : relative;

            _bridge.ModSharp.PrecacheResource(asset);

        }
    }

    int IGameListener.ListenerVersion => IGameListener.ApiVersion;
    int IGameListener.ListenerPriority => 0;


    // Easy access anywhere inside this class
    private StoreConfig Config => _config;

    string IModSharpModule.DisplayName => "Store";
    string IModSharpModule.DisplayAuthor => "Retro";
}