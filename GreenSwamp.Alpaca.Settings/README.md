# GreenSwamp.Alpaca.Settings

Versioned settings infrastructure for GreenSwamp Alpaca mount control.

## Overview

This library provides a modern .NET 8 configuration system with:
- **Versioned user settings** - Each app version gets its own settings folder
- **Automatic migration** - Settings migrate from previous versions
- **JSON-based** - No hard-coded defaults, all from `appsettings.json`
- **Type-safe** - Strongly-typed settings classes with validation
- **Non-invasive** - Can be adopted gradually without changing existing code

## Architecture

```
%AppData%\GreenSwampAlpaca\
??? 1.0.0\
?   ??? appsettings.user.json    (Version 1.0 settings)
??? 1.1.0\
?   ??? appsettings.user.json    (Version 1.1 settings)
??? current.version               (Tracks current version)
```

## Usage

### 1. Add to Program.cs (ASP.NET Core/Blazor)

```csharp
using GreenSwamp.Alpaca.Settings.Extensions;
using GreenSwamp.Alpaca.Settings.Services;

var builder = WebApplication.CreateBuilder(args);

// Add versioned user settings to configuration
builder.Configuration.AddVersionedUserSettings();

// Register settings services
builder.Services.AddVersionedSettings(builder.Configuration);

// Configure strongly-typed settings
builder.Services.Configure<SkySettings>(
    builder.Configuration.GetSection("SkySettings"));

var app = builder.Build();

// Optional: Trigger migration on startup
var settingsService = app.Services.GetRequiredService<IVersionedSettingsService>();
await settingsService.MigrateFromPreviousVersionAsync();

app.Run();
```

### 2. Use in Services (Dependency Injection)

```csharp
using GreenSwamp.Alpaca.Settings.Models;
using GreenSwamp.Alpaca.Settings.Services;
using Microsoft.Extensions.Options;

public class MountService
{
    private readonly SkySettings _settings;
    private readonly IVersionedSettingsService _settingsService;
    
    public MountService(
        IOptions<SkySettings> options,
        IVersionedSettingsService settingsService)
    {
        _settings = options.Value;
        _settingsService = settingsService;
    }
    
    public void Connect()
    {
        var port = _settings.Port;
        var latitude = _settings.Latitude;
        // Use settings...
    }
    
    public async Task UpdateSettings(SkySettings newSettings)
    {
        await _settingsService.SaveSettingsAsync(newSettings);
        // Settings are automatically reloaded
    }
}
```

### 3. Use in Blazor Components

```razor
@page "/settings"
@using GreenSwamp.Alpaca.Settings.Models
@using GreenSwamp.Alpaca.Settings.Services
@inject IVersionedSettingsService SettingsService

<h3>Settings (Version @SettingsService.CurrentVersion)</h3>

<EditForm Model="_settings" OnValidSubmit="SaveSettings">
    <DataAnnotationsValidator />
    
    <div class="mb-3">
        <label>Mount Type</label>
        <InputSelect @bind-Value="_settings.Mount" class="form-select">
            <option value="Simulator">Simulator</option>
            <option value="SkyWatcher">SkyWatcher</option>
        </InputSelect>
    </div>
    
    <!-- More fields... -->
    
    <button type="submit" class="btn btn-primary">Save</button>
    <button type="button" @onclick="ResetToDefaults" class="btn btn-secondary">
        Reset to Defaults
    </button>
</EditForm>

@code {
    private SkySettings _settings = new();
    
    protected override void OnInitialized()
    {
        _settings = SettingsService.GetSettings();
        SettingsService.SettingsChanged += OnSettingsChanged;
    }
    
    private async Task SaveSettings()
    {
        await SettingsService.SaveSettingsAsync(_settings);
    }
    
    private async Task ResetToDefaults()
    {
        await SettingsService.ResetToDefaultsAsync();
        _settings = SettingsService.GetSettings();
    }
    
    private void OnSettingsChanged(object? sender, SkySettings newSettings)
    {
        _settings = newSettings;
        InvokeAsync(StateHasChanged);
    }
    
    public void Dispose()
    {
        SettingsService.SettingsChanged -= OnSettingsChanged;
    }
}
```

## Default Settings

Default settings are defined in `appsettings.json` (no hard-coded values):

```json
{
  "SkySettings": {
    "Mount": "Simulator",
    "Port": "COM3",
    "BaudRate": 115200,
    "Latitude": 28.5,
    "Longitude": -81.5,
    "Elevation": 30.0,
    "AutoTrack": false,
    "AlignmentMode": "GermanPolar"
    // ... more settings
  }
}
```

## JSON Schema Support

The project includes a JSON schema (`appsettings.schema.json`) for:
- IntelliSense in Visual Studio
- Validation
- Auto-completion

To use in your `appsettings.json`:
```json
{
  "$schema": "./appsettings.schema.json",
  "SkySettings": { ... }
}
```

## Migration

The service automatically handles version migrations. To add custom migration logic:

```csharp
// In VersionedSettingsService.ApplyMigrations()
if (from < new Version("2.0.0") && to >= new Version("2.0.0"))
{
    _logger?.LogInformation("Applying 1.x ? 2.0 migration");
    
    // Example: Property renamed
    // settings.NewPropertyName = settings.OldPropertyName;
    
    // Example: New required property
    // if (settings.NewProperty == default)
    //     settings.NewProperty = defaultValue;
}
```

## Features

- ? **No hard-coded defaults** - All in JSON
- ? **Versioned folders** - Settings preserved per version
- ? **Automatic migration** - Settings upgrade with app
- ? **Rollback support** - Old versions preserved
- ? **Hot reload** - Changes apply without restart
- ? **Validation** - Data annotations + JSON schema
- ? **Events** - Notified when settings change
- ? **Thread-safe** - Concurrent access protected

## API Reference

### IVersionedSettingsService

| Method | Description |
|--------|-------------|
| `GetSettings()` | Gets current settings |
| `SaveSettingsAsync(settings)` | Saves settings to current version |
| `MigrateFromPreviousVersionAsync()` | Migrates from last version |
| `ResetToDefaultsAsync()` | Resets to `appsettings.json` defaults |
| `CurrentVersion` | Gets current app version |
| `AvailableVersions` | Gets all version folders |
| `UserSettingsPath` | Gets path to current user settings file |
| `SettingsChanged` | Event raised when settings change |

## Gradual Migration Strategy

You don't need to change existing code immediately:

1. **Add the library** to your project
2. **Register services** in `Program.cs`
3. **New code** uses `IVersionedSettingsService`
4. **Old code** continues using existing settings
5. **Gradually refactor** old code to use new system

## License

GNU General Public License v3.0 - Copyright (C) 2019-2025 Rob Morgan
