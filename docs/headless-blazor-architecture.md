# Headless Blazor Server Architecture
## GreenSwamp Alpaca — Remote Browser Support

**Date:** 2026-03-31  
**Status:** Design / Implementation Guide  
**Branch:** master

---

## 1. Goal

Run the Blazor Server application headlessly — Kestrel starts, mount control initialises, and the ASCOM Alpaca API is available, all **without a browser window on the host machine**. A remote browser can connect at any time and get a live UI. Disconnecting the browser leaves the mount running undisturbed.

---

## 2. Architecture Summary

```
┌─────────────────────────────────────────────────────────────────┐
│                    .NET Generic Host (IHost)                     │
│                                                                  │
│  ┌───────────────────────────┐   ┌──────────────────────────┐   │
│  │  MountHostedService       │   │    Kestrel Web Server     │   │
│  │  (IHostedService)         │   │    0.0.0.0:31426          │   │
│  │                           │   │    Always listening       │   │
│  │  StartAsync → MountStart()│   │                           │   │
│  │  StopAsync  → MountStop() │   │  ┌────────────────────┐   │   │
│  │                           │   │  │  Blazor Server     │   │   │
│  │  Runs regardless of       │   │  │  SignalR Hub       │   │   │
│  │  any browser connection   │   │  │  (circuit / conn)  │   │   │
│  └──────────────┬────────────┘   │  └────────┬───────────┘   │   │
│                 │  Singleton DI  └───────────┼───────────────┘   │
│                 └────────────────────────────┘  Optional         │
│                                                 browser          │
└─────────────────────────────────────────────────────────────────┘
          ▲                                  ▲
    Runs headless                   Remote browser connects
    at startup                      anytime via http://host:31426
```

### Key Principle

The **mount control layer** (`MountInstance`, `SkyServer`) lives in singleton-scoped `IHostedService` instances. The **Blazor circuit** (one per browser tab) is created on connection and torn down on disconnect. These two lifetimes are fully independent.

---

## 3. What Already Works

| Feature | Status | Location |
|---|---|---|
| `AllowRemoteAccess` binds Kestrel to `0.0.0.0` | ✅ Exists | `Program.cs` lines 123–135 |
| `AutoStartBrowser` gates local browser launch | ✅ Exists | `ServerSettings.cs`, `Program.cs` lines 427–436 |
| Mount runs independently of UI circuits | ✅ Exists | `MountInstance.OnUpdateServerEvent`, `MediaTimer` |
| `SetConnected()` starts mount on first ASCOM connect | ✅ Exists | `MountInstance.cs` |
| Port-in-use detection re-opens browser | ✅ Exists | `Program.cs` lines 55–77 |

### Current Gap

The mount is **not started at host startup**. It starts only when the first ASCOM client calls `SetConnected(id, true)`. For headless use the mount should start immediately at host startup so that:
- ASCOM clients can connect at any time with no cold-start delay
- Blazor UI shows live state even before any ASCOM client has connected

---

## 4. Required Code Changes

### 4.1 New File — `MountHostedService.cs`

**Location:** `GreenSwamp.Alpaca.Server\Services\MountHostedService.cs`

This `IHostedService` starts all registered `MountInstance` objects at application startup and stops them on shutdown. It is the **only** change that fundamentally enables headless operation.

```csharp
using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Server.Services;

namespace GreenSwamp.Alpaca.Server.Services;

/// <summary>
/// Starts all registered mount instances at application startup and stops
/// them on application shutdown. This decouples mount lifetime from browser
/// or ASCOM client connections — the mount runs as a true background service.
/// </summary>
internal sealed class MountHostedService : IHostedService
{
    private readonly ILogger<MountHostedService> _logger;

    public MountHostedService(ILogger<MountHostedService> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("MountHostedService: starting registered mount instances");

        foreach (var entry in UnifiedDeviceRegistry.GetAll())
        {
            try
            {
                if (!entry.Instance.IsRunning)
                {
                    entry.Instance.Start();
                    _logger.LogInformation("MountHostedService: started device {DeviceNumber} ({DeviceName})",
                        entry.DeviceNumber, entry.DeviceName);
                }
            }
            catch (Exception ex)
            {
                // Log but do not crash the host — a bad serial port should not prevent
                // other devices or the web UI from starting.
                _logger.LogError(ex,
                    "MountHostedService: failed to start device {DeviceNumber} ({DeviceName})",
                    entry.DeviceNumber, entry.DeviceName);
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("MountHostedService: stopping all mount instances");

        foreach (var entry in UnifiedDeviceRegistry.GetAll())
        {
            try
            {
                entry.Instance.Stop();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "MountHostedService: error stopping device {DeviceNumber}", entry.DeviceNumber);
            }
        }

        return Task.CompletedTask;
    }
}
```

> **Note:** `UnifiedDeviceRegistry.GetAll()` does not currently exist — see §4.4.

---

### 4.2 `Program.cs` — Register `MountHostedService`

Add one line in the DI registration block, **after** `UnifiedDeviceRegistry` is populated (currently around line 356):

```csharp
// Register mount background service — starts all MountInstance objects at host startup.
// This enables headless operation: mount runs even with no browser or ASCOM client connected.
builder.Services.AddHostedService<GreenSwamp.Alpaca.Server.Services.MountHostedService>();
```

---

### 4.3 `Program.cs` — Windows Service / Systemd Support (Optional)

For production deployment without a console window, add before `WebApplication.CreateBuilder`:

```csharp
// Support running as a Windows Service or systemd unit (headless, no console window).
builder.Host.UseWindowsService();   // no-op when running interactively
// builder.Host.UseSystemd();       // Linux alternative
```

---

### 4.4 `UnifiedDeviceRegistry.cs` — Add `GetAll()` Method

`MountHostedService` needs to enumerate all registered devices. Add this method to `UnifiedDeviceRegistry`:

```csharp
/// <summary>
/// Returns all registered device entries for enumeration by hosted services.
/// </summary>
public static IReadOnlyList<DeviceRegistryEntry> GetAll()
    => _devices.Values.OrderBy(e => e.DeviceNumber).ToList();
```

Where `DeviceRegistryEntry` is the existing record / class used internally by the registry. Check the current internal type name before applying.

---

### 4.5 `ServerSettings.cs` — Add `HeadlessMode` Setting

This allows the setting to be persisted and toggled from the setup UI without recompiling:

```csharp
/// <summary>
/// When true the application starts without launching a local browser window.
/// Kestrel remains accessible to remote browsers via AllowRemoteAccess.
/// </summary>
internal static bool HeadlessMode
{
    get
    {
        if (bool.TryParse(Profile.GetValue("HeadlessMode", false.ToString()), out bool result))
            return result;
        return false;
    }
    set => Profile.WriteValue("HeadlessMode", value.ToString());
}
```

Then in `Program.cs`, replace the current `AutoStartBrowser` check:

```csharp
// Before (current):
if (ServerSettings.AutoStartBrowser)
{
    StartBrowser(ServerSettings.ServerPort);
}

// After:
if (ServerSettings.AutoStartBrowser && !ServerSettings.HeadlessMode)
{
    try { StartBrowser(ServerSettings.ServerPort); }
    catch (Exception ex) { Logger.LogWarning(ex.Message); }
}
```

---

### 4.6 `appsettings.json` — Kestrel Explicit Binding (Production)

For production / service deployment add an explicit Kestrel configuration so the bind address is not dependent on the runtime argument constructed in `Program.cs`:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:31426"
      }
    }
  }
}
```

This is a fallback; the `--urls` argument injected by `Program.cs` takes precedence when present.

---

### 4.7 `Program.cs` — Fix "Already Running" Branch

The current already-running detection (lines 55–77) calls `StartBrowser()` and then `return`s from `Main`, preventing a second instance from trying to bind the port. This is correct **for interactive use**. For headless mode the behaviour is unchanged — a second headless instance should still exit, so no change is required here.

---

## 5. Deployment Patterns

### 5.1 Interactive Headless (Observatory PC, no monitor)

Set `HeadlessMode = true` and `AllowRemoteAccess = true` in the setup UI, then start the application normally. The process runs in the system tray or as a console background process. A browser on any machine on the same network can reach `http://<host-ip>:31426`.

### 5.2 Windows Service

```powershell
# Install as a service (run once as Administrator)
sc.exe create GreenSwampAlpaca `
    binPath= "T:\source\repos\GreenSwampAlpaca\publish\GreenSwamp.Alpaca.Server.exe" `
    start= auto `
    DisplayName= "Green Swamp Alpaca Server"

sc.exe start GreenSwampAlpaca
```

Requires `UseWindowsService()` in `Program.cs` (§4.3). The service starts on Windows boot before any user logs in.

### 5.3 Task Scheduler (No Code Changes Needed)

Create a scheduled task that runs at system startup under a service account. This is the lightest-weight option and requires no code changes — just set `AutoStartBrowser = false` in settings.

---

## 6. Blazor Circuit Lifecycle vs. Service Lifetime

```
Host lifetime:   [=== App Start ================================================ App Stop ===]
Kestrel:         [=== Listening on 0.0.0.0:31426 ==================================== Stop ===]
MountInstance:   [=== Running (IHostedService) ====================================== Stop ===]
Browser 1:                   [=== Connected ===]
Browser 2:                                           [=== Connected ===]
ASCOM Client:                          [=== Connected ===========================]
```

**Rules that follow from this:**
- Blazor components **must not** hold the only reference to mount state — state lives in singletons
- `StateHasChanged()` in components must be guarded: the circuit may be disposed when the event fires
- `SetConnected(id, true)` in `MountInstance` should become a no-op if `MountHostedService` has already started the mount — the `if (!IsMountRunning)` guard already handles this correctly

---

## 7. State Notification to Blazor UI

When no browser is connected, `SkyServer.NotifyStepsChanged()` (called from `MountInstance.ReceiveSteps`) fires events with zero subscribers — this is safe and incurs negligible cost. When a browser connects and a Blazor component subscribes, it immediately starts receiving live updates.

The existing `TelescopeStateService` is already registered as a singleton and receives updates via events. Blazor components inject it and call `StateHasChanged()` via `InvokeAsync`. No changes are needed here for headless support.

The one risk is **event handler leak**: if a Blazor component subscribes in `OnInitializedAsync` but does not unsubscribe in `IAsyncDisposable.DisposeAsync`, the circuit holds a reference and prevents GC. All components that subscribe to `TelescopeStateService` or `MonitorLog` events **must** implement `IAsyncDisposable` and unsubscribe.

---

## 8. Summary of Files to Change

| File | Change | Priority |
|---|---|---|
| `Services\MountHostedService.cs` | **New file** — starts/stops all `MountInstance` objects | High |
| `Program.cs` | Register `MountHostedService`; add `UseWindowsService()` | High |
| `Services\UnifiedDeviceRegistry.cs` | Add `GetAll()` method | High |
| `ServerSettings.cs` | Add `HeadlessMode` setting | Medium |
| `Program.cs` | Guard `StartBrowser` with `HeadlessMode` | Medium |
| `appsettings.json` | Add explicit Kestrel binding | Low |
| All Blazor components that subscribe to events | Add `IAsyncDisposable` unsubscribe | Medium |

---

## 9. Testing Headless Operation

```powershell
# Start the app without a browser window
$env:ASPNETCORE_ENVIRONMENT = "Production"
Start-Process -FilePath ".\GreenSwamp.Alpaca.Server.exe" -WindowStyle Hidden

# Confirm port is listening
Test-NetConnection -ComputerName localhost -Port 31426

# Confirm Alpaca API is reachable
Invoke-RestMethod -Uri "http://localhost:31426/api/v1/telescope/0/connected" -Method Get

# Open UI from a remote machine
Start-Process "http://<host-ip>:31426"
```
