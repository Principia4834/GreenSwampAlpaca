using System.Collections.Concurrent;

namespace GreenSwamp.Alpaca.Server.Services;

/// <summary>
/// Tracks active device view sessions across multiple browser tabs. Each tab has a unique session ID, and this registry maps those IDs to the device number being viewed and the last time the view was active. It allows for determining which devices are currently being viewed and cleaning up stale entries after a specified duration.
/// </summary>
public sealed class ActiveDeviceViewRegistry
{
    private sealed record ViewEntry(int DeviceNumber, DateTime LastSeenUtc);

    // key: per-browser-tab view session id
    private readonly ConcurrentDictionary<string, ViewEntry> _views = new();

    public void Touch(string viewSessionId, int deviceNumber)
        => _views[viewSessionId] = new ViewEntry(deviceNumber, DateTime.UtcNow);

    public void Remove(string viewSessionId)
        => _views.TryRemove(viewSessionId, out _);

    public IReadOnlyList<int> GetActiveDeviceNumbers(TimeSpan staleAfter)
    {
        var cutoff = DateTime.UtcNow - staleAfter;

        foreach (var kvp in _views)
        {
            if (kvp.Value.LastSeenUtc < cutoff)
                _views.TryRemove(kvp.Key, out _);
        }

        return _views.Values
            .Select(v => v.DeviceNumber)
            .Distinct()
            .ToList();
    }
}