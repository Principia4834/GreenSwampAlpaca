# Integration Guide

This guide shows how to integrate the versioned settings system into the GreenSwamp.Alpaca.Server project **without modifying existing code**.

## Step 1: Add Project Reference

In `GreenSwamp.Alpaca.Server.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\GreenSwamp.Alpaca.Settings\GreenSwamp.Alpaca.Settings.csproj" />
</ItemGroup>
```

## Step 2: Copy appsettings.json Template

Copy `Templates/appsettings.json` to the Server project root and customize defaults.

## Step 3: Update Program.cs (Minimal Changes)

```csharp
using GreenSwamp.Alpaca.Settings.Extensions;
using GreenSwamp.Alpaca.Settings.Services;

var builder = WebApplication.CreateBuilder(args);

// ADD THIS: Load versioned user settings
builder.Configuration.AddVersionedUserSettings();

// ADD THIS: Register settings services
builder.Services.AddVersionedSettings(builder.Configuration);

// ADD THIS: Configure strongly-typed settings (optional for gradual migration)
builder.Services.Configure<GreenSwamp.Alpaca.Settings.Models.SkySettings>(
    builder.Configuration.GetSection("SkySettings"));

// ... existing service registrations ...

var app = builder.Build();

// ADD THIS: Optional migration on startup
try
{
    var settingsService = app.Services.GetRequiredService<IVersionedSettingsService>();
    await settingsService.MigrateFromPreviousVersionAsync();
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Could not migrate settings");
}

// ... existing middleware ...

app.Run();
```

## Step 4: Create Settings Page (Optional)

Create a new Blazor page for managing settings:

```razor
@page "/settings"
@using GreenSwamp.Alpaca.Settings.Models
@using GreenSwamp.Alpaca.Settings.Services
@inject IVersionedSettingsService SettingsService
@inject ILogger<SettingsPage> Logger

<PageTitle>Mount Settings</PageTitle>

<h3>Mount Settings - Version @SettingsService.CurrentVersion</h3>

@if (_showMigration)
{
    <div class="alert alert-info">
        <h5>New Version Detected</h5>
        <p>Settings from version @_previousVersion are available.</p>
        <button class="btn btn-primary" @onclick="MigrateSettings">
            Migrate Settings
        </button>
        <button class="btn btn-secondary" @onclick="@(() => _showMigration = false)">
            Use Defaults
        </button>
    </div>
}

@if (_message != null)
{
    <div class="alert alert-@(_isError ? "danger" : "success")">
        @_message
    </div>
}

<EditForm Model="_settings" OnValidSubmit="SaveSettings">
    <DataAnnotationsValidator />
    <ValidationSummary />

    <div class="row">
        <div class="col-md-6">
            <h4>Connection</h4>
            
            <div class="mb-3">
                <label class="form-label">Mount Type</label>
                <InputSelect @bind-Value="_settings.Mount" class="form-select">
                    <option value="Simulator">Simulator</option>
                    <option value="SkyWatcher">SkyWatcher</option>
                </InputSelect>
            </div>

            <div class="mb-3">
                <label class="form-label">Serial Port</label>
                <InputText @bind-Value="_settings.Port" class="form-control" />
            </div>

            <div class="mb-3">
                <label class="form-label">Baud Rate</label>
                <InputNumber @bind-Value="_settings.BaudRate" class="form-control" />
            </div>
        </div>

        <div class="col-md-6">
            <h4>Location</h4>
            
            <div class="mb-3">
                <label class="form-label">Latitude (°)</label>
                <InputNumber @bind-Value="_settings.Latitude" class="form-control" />
            </div>

            <div class="mb-3">
                <label class="form-label">Longitude (°)</label>
                <InputNumber @bind-Value="_settings.Longitude" class="form-control" />
            </div>

            <div class="mb-3">
                <label class="form-label">Elevation (m)</label>
                <InputNumber @bind-Value="_settings.Elevation" class="form-control" />
            </div>
        </div>
    </div>

    <div class="mt-3">
        <button type="submit" class="btn btn-primary" disabled="@_saving">
            @if (_saving)
            {
                <span class="spinner-border spinner-border-sm me-2"></span>
            }
            Save Settings
        </button>
        
        <button type="button" class="btn btn-secondary" @onclick="ResetToDefaults" disabled="@_saving">
            Reset to Defaults
        </button>
        
        <button type="button" class="btn btn-info" @onclick="ReloadSettings">
            Reload
        </button>
    </div>
</EditForm>

<div class="mt-4">
    <h5>Available Versions</h5>
    <ul class="list-group">
        @foreach (var version in SettingsService.AvailableVersions)
        {
            <li class="list-group-item">
                Version @version 
                @if (version == SettingsService.CurrentVersion)
                {
                    <span class="badge bg-primary">Current</span>
                }
            </li>
        }
    </ul>
</div>

<div class="mt-3">
    <small class="text-muted">
        Settings file: @SettingsService.UserSettingsPath
    </small>
</div>

@code {
    private SkySettings _settings = new();
    private bool _saving;
    private bool _showMigration;
    private string? _previousVersion;
    private string? _message;
    private bool _isError;

    protected override void OnInitialized()
    {
        LoadSettings();
        CheckForMigration();
        SettingsService.SettingsChanged += OnSettingsChanged;
    }

    private void LoadSettings()
    {
        _settings = SettingsService.GetSettings();
    }

    private void CheckForMigration()
    {
        var versions = SettingsService.AvailableVersions
            .Where(v => v != SettingsService.CurrentVersion)
            .OrderByDescending(v => new Version(v))
            .ToList();

        if (versions.Any())
        {
            _previousVersion = versions.First();
            _showMigration = true;
        }
    }

    private async Task SaveSettings()
    {
        _saving = true;
        _message = null;
        
        try
        {
            await SettingsService.SaveSettingsAsync(_settings);
            _message = "Settings saved successfully!";
            _isError = false;
            Logger.LogInformation("Settings saved by user");
        }
        catch (Exception ex)
        {
            _message = $"Error saving settings: {ex.Message}";
            _isError = true;
            Logger.LogError(ex, "Failed to save settings");
        }
        finally
        {
            _saving = false;
        }
    }

    private async Task ResetToDefaults()
    {
        try
        {
            await SettingsService.ResetToDefaultsAsync();
            LoadSettings();
            _message = "Settings reset to defaults";
            _isError = false;
        }
        catch (Exception ex)
        {
            _message = $"Error resetting: {ex.Message}";
            _isError = true;
        }
    }

    private async Task MigrateSettings()
    {
        try
        {
            var success = await SettingsService.MigrateFromPreviousVersionAsync();
            if (success)
            {
                LoadSettings();
                _showMigration = false;
                _message = $"Settings migrated from version {_previousVersion}";
                _isError = false;
            }
            else
            {
                _message = "No settings to migrate";
                _isError = true;
            }
        }
        catch (Exception ex)
        {
            _message = $"Migration failed: {ex.Message}";
            _isError = true;
        }
    }

    private void ReloadSettings()
    {
        LoadSettings();
        _message = "Settings reloaded";
        _isError = false;
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

## Step 5: Gradual Migration of Existing Code

You can migrate existing code gradually. Here's an example:

### Before (Old Code - No Changes Required)
```csharp
public class SkyServer
{
    private static string _port = "COM3";
    
    public static void Connect()
    {
        // Use _port...
    }
}
```

### After (New Code - When Ready to Migrate)
```csharp
using GreenSwamp.Alpaca.Settings.Services;
using Microsoft.Extensions.Options;

public class SkyServer
{
    private readonly SkySettings _settings;
    
    public SkyServer(IOptions<SkySettings> options)
    {
        _settings = options.Value;
    }
    
    public void Connect()
    {
        var port = _settings.Port; // Now from settings service
        // Use port...
    }
}
```

## Step 6: Coexistence Strategy

During migration, both systems can coexist:

```csharp
public class SkyServer
{
    // Old static fields (keep for now)
    private static string _port = "COM3";
    
    // New DI-based settings (add when migrating)
    private readonly SkySettings? _settings;
    
    public SkyServer(IOptions<SkySettings>? options = null)
    {
        _settings = options?.Value;
    }
    
    public void Connect()
    {
        // Use new settings if available, fall back to old
        var port = _settings?.Port ?? _port;
    }
}
```

## Testing the Integration

1. **Run the application**
2. **Check settings location**:
   - Windows: `%AppData%\GreenSwampAlpaca\1.0.0\appsettings.user.json`
3. **Navigate to** `/settings` page
4. **Modify settings** and save
5. **Verify changes** persist across restarts
6. **Change app version** in `AssemblyInfo.cs`
7. **Run again** and verify migration prompt appears

## Notes

- **No existing code is modified** - only additions to `Program.cs`
- **Old settings continue working** - gradual migration is supported
- **Settings are versioned** - each version has its own folder
- **Migration is automatic** - prompts user when new version detected
- **Rollback is easy** - just copy old version's settings file
