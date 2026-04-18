namespace InsanityGaming.Store.Services;

public class SessionItemStore
{
    private readonly Dictionary<ulong, HashSet<string>> _items = new();
    private readonly Lock _lock = new();

    public void Add(ulong steamId, string unique)
    {
        lock (_lock)
        {
            if (!_items.TryGetValue(steamId, out var set))
                _items[steamId] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(unique);
        }
    }

    public bool Remove(ulong steamId, string unique)
    {
        lock (_lock)
        {
            return _items.TryGetValue(steamId, out var set) && set.Remove(unique);
        }
    }

    public void Clear(ulong steamId)
    {
        lock (_lock)
        {
            _items.Remove(steamId);
        }
    }

    public bool Has(ulong steamId, string unique)
    {
        lock (_lock)
        {
            return _items.TryGetValue(steamId, out var set) && set.Contains(unique);
        }
    }

    public IReadOnlyList<string> GetAll(ulong steamId)
    {
        lock (_lock)
        {
            return _items.TryGetValue(steamId, out var set)
                ? set.ToList()
                : [];
        }
    }
}
