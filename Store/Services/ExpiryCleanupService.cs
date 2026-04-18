using InsanityGaming.Store.Interfaces;
using Microsoft.Extensions.Logging;

namespace InsanityGaming.Store.Services;

public class ExpiryCleanupService : IModule
{
    private readonly IInventoryRepository _inventoryRepo;
    private readonly ILogger<ExpiryCleanupService> _logger;
    private Timer? _timer;

    public ExpiryCleanupService(IInventoryRepository inventoryRepo, ILogger<ExpiryCleanupService> logger)
    {
        _inventoryRepo = inventoryRepo;
        _logger = logger;
    }

    public bool Init()
    {
        _timer = new Timer(OnTick, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
        _logger.LogInformation("ExpiryCleanupService started (60s interval)");
        return true;
    }

    public void Shutdown()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void OnTick(object? _) => _ = PurgeAsync();

    private async Task PurgeAsync()
    {
        try
        {
            await _inventoryRepo.PurgeExpiredAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to purge expired inventory items");
        }
    }
}
