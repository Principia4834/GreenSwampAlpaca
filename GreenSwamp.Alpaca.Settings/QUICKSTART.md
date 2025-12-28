# Quick Start Guide

Get up and running with versioned settings in 5 minutes.

## Step 1: Add Project Reference (30 seconds)

In `GreenSwamp.Alpaca.Server.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\GreenSwamp.Alpaca.Settings\GreenSwamp.Alpaca.Settings.csproj" />
</ItemGroup>
```

## Step 2: Copy Default Settings (1 minute)

1. Copy `Templates/appsettings.json` to `GreenSwamp.Alpaca.Server/appsettings.json`
2. Customize the default values for your environment
3. Copy `Templates/appsettings.schema.json` to same location (optional, for IntelliSense)

## Step 3: Update Program.cs (2 minutes)

Add these lines to `GreenSwamp.Alpaca.Server/Program.cs`:

```csharp
using GreenSwamp.Alpaca.Settings.Extensions;   // ADD THIS
using GreenSwamp.Alpaca.Settings.Services;     // ADD THIS

var builder = WebApplication.CreateBuilder(args);

// ADD THESE TWO LINES:
builder.Configuration.AddVersionedUserSettings();
builder.Services.AddVersionedSettings(builder.Configuration);

// ... rest of your existing code ...

var app = builder.Build();

// ADD THIS (optional - auto-migrate on startup):
var settingsService = app.Services.GetRequiredService<IVersionedSettingsService>();
await settingsService.MigrateFromPreviousVersionAsync();

app.Run();
```

## Step 4: Test It (1 minute)

Run your application:

```bash
dotnet run --project GreenSwamp.Alpaca.Server
```

Check settings location:
- **Windows**: `%AppData%\GreenSwampAlpaca\1.0.0\appsettings.user.json`
- **Linux**: `~/.config/GreenSwampAlpaca/1.0.0/appsettings.user.json`
- **macOS**: `~/Library/Application Support/GreenSwampAlpaca/1.0.0/appsettings.user.json`

## Step 5: Use in Code (30 seconds)

Inject the service anywhere:

```csharp
public class MyService
{
    private readonly IVersionedSettingsService _settings;
    
    public MyService(IVersionedSettingsService settings)
    {
        _settings = settings;
    }
    
    public void DoWork()
    {
        var current = _settings.GetSettings();
        Console.WriteLine($"Mount: {current.Mount}");
        Console.WriteLine($"Port: {current.Port}");
    }
}
```

## That's It! ??

You now have:
- ? Versioned settings (each version in its own folder)
- ? No hard-coded defaults (all in JSON)
- ? Automatic migration between versions
- ? Type-safe configuration
- ? Hot reload support

## Next Steps

### Create a Settings Page

See `INTEGRATION.md` for a complete Blazor settings page example.

### Save Settings Programmatically

```csharp
var settings = _settingsService.GetSettings();
settings.Port = "COM5";
await _settingsService.SaveSettingsAsync(settings);
```

### Listen for Changes

```csharp
protected override void OnInitialized()
{
    _settingsService.SettingsChanged += OnSettingsChanged;
}

private void OnSettingsChanged(object? sender, SkySettings newSettings)
{
    Console.WriteLine("Settings changed!");
    StateHasChanged(); // Blazor
}
```

### Migrate from Old Settings

```csharp
// Your old code
var port = Properties.Settings.Default.Port;

// New code (when ready to migrate)
var port = _settingsService.GetSettings().Port;
```

## Common Tasks

### Reset to Defaults
```csharp
await _settingsService.ResetToDefaultsAsync();
```

### Check Current Version
```csharp
Console.WriteLine(_settingsService.CurrentVersion);
```

### List All Versions
```csharp
foreach (var version in _settingsService.AvailableVersions)
{
    Console.WriteLine($"Found version: {version}");
}
```

### Get Settings File Path
```csharp
Console.WriteLine(_settingsService.UserSettingsPath);
```

## Troubleshooting

### Settings not saving?
- Check write permissions to `%AppData%\GreenSwampAlpaca`
- Look for exceptions in logs

### Settings not loading?
- Verify `appsettings.json` exists and is valid JSON
- Check configuration binding in `Program.cs`

### Old settings still being used?
- Old code doesn't use new service automatically
- Migrate gradually using DI

### Migration not working?
- Check that previous version folder exists
- Verify `appsettings.user.json` in old version folder
- Look at migration logs

## Support

- ?? Full docs: `README.md`
- ?? Integration guide: `INTEGRATION.md`
- ?? Feature summary: `SUMMARY.md`
- ?? Issues: Use GitHub Issues

---

**Time to integrate: ~5 minutes**  
**Breaking changes: None**  
**Risk: Minimal** (old code continues working)
