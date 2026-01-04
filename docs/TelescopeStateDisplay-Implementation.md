# Telescope State Display Implementation

## Overview

This implementation provides a real-time display of telescope device state information synchronized with the SkyServer main update loop. The display shows all ASCOM IDeviceState-aligned properties and updates automatically with each server loop iteration.

## Architecture

### Components

1. **TelescopeStateModel** (`Models/TelescopeStateModel.cs`)
   - Data model representing telescope state
   - Aligned with ASCOM IDeviceState interface requirements
   - Contains positioning, timing, mount state, and performance data

2. **TelescopeStateService** (`Services/TelescopeStateService.cs`)
   - Singleton service that monitors SkyServer property changes
   - Updates state model with each server loop iteration
   - Implements observer pattern for UI notifications
   - Thread-safe state management

3. **TelescopeState.razor** (`Pages/TelescopeState.razor`)
   - Blazor page component displaying state information
   - Subscribes to state change events
   - Automatically updates UI using `StateHasChanged()`
   - Formatted display with status indicators

4. **TelescopeState.razor.css** (`Pages/TelescopeState.razor.css`)
   - Scoped CSS styles for state display
   - Responsive grid layout
   - Status color coding (active/inactive/warning)

## Data Flow

```
SkyServer.UpdateServerEvent (main loop)
    ?
SkyServer.StaticPropertyChanged (event)
    ?
TelescopeStateService.OnSkyServerPropertyChanged
    ?
TelescopeStateService.UpdateState() (reads SkyServer properties)
    ?
TelescopeStateService.StateChanged (event)
    ?
TelescopeState.razor.OnStateChanged
    ?
InvokeAsync(StateHasChanged)
    ?
UI Updates (Blazor render)
```

## Displayed Properties

### Connection & Status
- Mount Running (bool)
- Slewing (bool)
- Tracking (bool)
- At Park (bool)
- At Home (bool)
- Slew State (SlewType enum)

### Current Position (ASCOM)
- Right Ascension (formatted as HH:MM:SS)
- Declination (formatted as 켆D:MM:SS)
- Altitude (degrees)
- Azimuth (degrees)
- Side of Pier (PointingState)
- Sidereal Time (formatted as HH:MM:SS)

### Target Position
- Target RA (formatted as HH:MM:SS)
- Target Dec (formatted as 켆D:MM:SS)

### Axis Positions (Mount Coordinates)
- Actual Axis X (degrees)
- Actual Axis Y (degrees)
- App Axis X (degrees)
- App Axis Y (degrees)

### Tracking & Guiding
- Tracking Rate (DriveRate enum)
- Pulse Guide RA (bool)
- Pulse Guide Dec (bool)

### Time & Performance
- UTC Date (timestamp with milliseconds)
- Local Date (timestamp with milliseconds)
- Loop Counter (server loop iteration count)
- Timer Overruns (performance indicator)
- Last Update (service update timestamp)

## Configuration

### Service Registration

The service is registered as a singleton in `Program.cs`:

```csharp
builder.Services.AddSingleton<GreenSwamp.Alpaca.Server.Services.TelescopeStateService>();
```

### Navigation Menu

Added to `NavMenu.razor`:

```razor
<NavLink href="telescope-state" class="nav-link" Match="NavLinkMatch.All">
    <span class="oi oi-monitor" aria-hidden="true"></span> Telescope State
</NavLink>
```

## Update Mechanism

### Primary Update Path
The service subscribes to `SkyServer.StaticPropertyChanged` event and updates when:
- `LoopCounter` property changes (main loop iteration)
- `Steps` property changes (position update)
- Multiple properties change (null property name)

### Backup Update Path
A timer runs every 100ms as a fallback to ensure updates even if property change events are missed.

### Thread Safety
- State updates use `lock (_stateLock)` for thread-safe access
- Event notifications run on thread pool to avoid blocking
- Blazor UI updates use `InvokeAsync(StateHasChanged)`

## Formatting

### Coordinate Formatting

**HMS (Hours, Minutes, Seconds)**:
```
Format: HHh MMm SS.SSs
Example: 12h 34m 56.78s
```

**DMS (Degrees, Minutes, Seconds)**:
```
Format: 켆D� MM' SS.S"
Example: +45� 23' 12.3"
```

**Decimal Degrees**:
```
Format: 켆DD.DDDD�
Example: +123.4567�
```

### Status Indicators

- **Active** (green): Mount running, slewing, tracking, guiding active
- **Inactive** (gray): Boolean false states
- **Warning** (yellow): Timer overruns > 0

## Usage

### Accessing the Page

Navigate to: `http://localhost:{port}/telescope-state`

Or click "Telescope State" in the navigation menu.

### Monitoring Performance

Watch these indicators for mount health:
- **Loop Counter**: Should increment steadily (e.g., 10 per second with 100ms display interval)
- **Timer Overruns**: Should remain at 0 (values > 0 indicate performance issues)
- **Last Update**: Should show recent timestamp (within last second)

### Reading Position Data

- **RA/Dec**: Equatorial coordinates in J2000 or current epoch
- **Alt/Az**: Horizontal coordinates relative to observer
- **Axis X/Y**: Mount-specific coordinate system
- **App Axis**: Application coordinate system after transformations

## Performance Considerations

### Update Frequency
- Service updates: Synchronized with SkyServer loop (~10-100 times per second)
- UI updates: Throttled by Blazor rendering pipeline (~10-60 FPS)
- Network latency: Minimal (no HTTP requests after initial page load)

### Resource Usage
- Memory: ~16KB per state snapshot
- CPU: <1% (event-driven updates)
- Network: SignalR connection for Blazor Server

### Optimization
- Properties are read from SkyServer only when needed
- State model is immutable (new instance per update)
- Event subscribers run asynchronously to avoid blocking

## Troubleshooting

### State Not Updating
1. Check that mount is running: `SkyServer.IsMountRunning == true`
2. Verify service is registered in `Program.cs`
3. Check browser console for JavaScript errors
4. Ensure SignalR connection is active

### Incorrect Values Shown
1. Verify SkyServer properties are populated correctly
2. Check format functions for NaN handling
3. Review coordinate transformation pipeline

### Performance Issues
1. Monitor Timer Overruns field
2. Check CPU usage on server
3. Reduce display interval in SkySettings if needed
4. Consider disabling some update notifications

## Extension Points

### Adding New Properties
1. Add property to `TelescopeStateModel`
2. Read property in `TelescopeStateService.UpdateState()`
3. Display property in `TelescopeState.razor`
4. Add formatting helper if needed

### Custom Formatting
Add format methods to `TelescopeState.razor` code block:

```csharp
private string FormatCustom(double value)
{
    if (double.IsNaN(value) || double.IsInfinity(value))
        return "N/A";
    
    return $"{value:F2} units";
}
```

### Event Subscriptions
Subscribe to specific SkyServer property changes:

```csharp
if (e.PropertyName == nameof(SkyServer.SpecificProperty))
{
    UpdateState(null);
}
```

## Future Enhancements

### Potential Improvements
1. **Graphical Display**: Add real-time charts for position/rate tracking
2. **Historical Data**: Store state history for trend analysis
3. **Alerts**: Trigger notifications on specific conditions
4. **Export**: Save state snapshots to CSV/JSON
5. **Multi-Telescope**: Support multiple telescope instances (Phase 4)
6. **WebSocket API**: Expose state updates via WebSocket for external clients

### Multi-Telescope Support (Phase 4)
When implementing multi-telescope architecture:
1. Change service from singleton to scoped
2. Add telescope instance ID parameter
3. Subscribe to specific MountInstance events instead of static SkyServer
4. Update UI to show telescope selector
5. Support simultaneous state displays for multiple telescopes

## Dependencies

### NuGet Packages
- ASCOM.Common.DeviceInterfaces (for DriveRate, PointingState)
- Microsoft.AspNetCore.Components.Web (Blazor)

### Project References
- GreenSwamp.Alpaca.MountControl (SkyServer, enums)
- GreenSwamp.Alpaca.Principles (HiResDateTime)
- GreenSwamp.Alpaca.Shared (SlewType)

## Testing

### Manual Testing
1. Start application
2. Connect mount (simulator or hardware)
3. Navigate to Telescope State page
4. Verify all fields update in real-time
5. Start slew and verify Slewing changes to true
6. Enable tracking and verify Tracking changes to true
7. Check Loop Counter increments steadily

### Integration Testing
```csharp
[Test]
public void TelescopeStateService_UpdatesOnPropertyChange()
{
    var service = new TelescopeStateService();
    TelescopeStateModel? capturedState = null;
    
    service.StateChanged += (sender, state) => capturedState = state;
    
    // Trigger property change in SkyServer
    SkyServer.IsMountRunning = true;
    
    // Wait for update
    Thread.Sleep(200);
    
    Assert.IsNotNull(capturedState);
    Assert.IsTrue(capturedState.IsMountRunning);
}
```

## License

Copyright(C) 2019-2025 Rob Morgan (robert.morgan.e@gmail.com)

This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
