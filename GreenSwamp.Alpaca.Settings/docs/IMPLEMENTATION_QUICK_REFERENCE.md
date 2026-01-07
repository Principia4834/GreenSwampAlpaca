# Quick Reference: Profile Loading Implementation

## ?? Goal
Load settings from active profile (if available) or fallback to JSON, using a single explicit mapping method.

---

## ?? Changes Summary

| File | Changes | Lines |
|------|---------|-------|
| `SkySettingsInstance.cs` | Add field, update constructor, rename method, add new method | ~50 |
| `Program.cs` | Update DI registration | 3 |

---

## ?? Code Changes

### 1. Add Field (SkySettingsInstance.cs)
```csharp
private readonly IProfileLoaderService? _profileLoaderService;
```

### 2. Update Constructor (SkySettingsInstance.cs)
```csharp
public SkySettingsInstance(
    IVersionedSettingsService settingsService,
    IProfileLoaderService? profileLoaderService = null)
{
    _settingsService = settingsService;
    _profileLoaderService = profileLoaderService;
    
    var settings = LoadSettingsFromSource();
    ApplySettings(settings);
    
    LogSettings("Initialized", $"Mount:{_mount}|Port:{_port}");
}
```

### 3. Add LoadSettingsFromSource (SkySettingsInstance.cs)
```csharp
private Settings.Models.SkySettings LoadSettingsFromSource()
{
    if (_profileLoaderService != null)
    {
        try
        {
            var profileSettings = _profileLoaderService
                .LoadActiveProfileAsync()
                .GetAwaiter()
                .GetResult();
            
            LogSettings("LoadedFromProfile", "Active profile loaded");
            return profileSettings;
        }
        catch (Exception ex)
        {
            LogSettings("ProfileLoadFailed", $"Fallback to JSON: {ex.Message}");
        }
    }
    
    return _settingsService.GetSettings();
}
```

### 4. Rename LoadFromJson ? ApplySettings
```csharp
// OLD: private void LoadFromJson()
// NEW:
private void ApplySettings(Settings.Models.SkySettings settings)
{
    try
    {
        // Remove: var settings = _settingsService.GetSettings();
        // settings is now a parameter
        
        // Keep all existing mapping code (100+ lines)
        if (Enum.TryParse<MountType>(settings.Mount, true, out var mountType))
            _mount = mountType;
        // ... rest unchanged ...
        
        LogSettings("AppliedSettings", $"Mount:{_mount}|Port:{_port}");
    }
    catch (Exception ex)
    {
        LogSettings("ApplySettingsFailed", ex.Message);
        throw;
    }
}
```

### 5. Update DI (Program.cs)
```csharp
builder.Services.AddSingleton(sp =>
{
    var settingsService = sp.GetRequiredService<IVersionedSettingsService>();
    var profileLoader = sp.GetService<IProfileLoaderService>();
    return new SkySettingsInstance(settingsService, profileLoader);
});
```

---

## ? Testing Quick Check

```bash
# 1. Build
dotnet build

# 2. Start app
# Check logs for "LoadedFromProfile" or "LoadingFromJSON"

# 3. No profile? Should say "LoadingFromJSON"
# Active profile? Should say "LoadedFromProfile"

# 4. Delete active-profile.txt
# Restart ? Should fallback to JSON gracefully
```

---

## ?? Key Points

### What to Keep
- ? All existing mapping code in `ApplySettings`
- ? Direct field assignment (`_field = value`)
- ? All enum parsing with fallbacks

### What Changes
- ? Method name: `LoadFromJson` ? `ApplySettings`
- ? Method signature: Add `Settings.Models.SkySettings` parameter
- ? Remove first line that gets settings from service

### What NOT to Touch
- ? Property setters (no changes)
- ? SaveAsync method (no changes)
- ? Other methods in class

---

## ?? Troubleshooting

| Problem | Solution |
|---------|----------|
| Build error: `IProfileLoaderService` not found | Add: `using GreenSwamp.Alpaca.Settings.Services;` |
| Always loads from JSON | Check ProfileLoaderService registered in Program.cs |
| Crash on startup | Check logs for "ApplySettingsFailed" message |
| Settings not applied | Ensure `ApplySettings` uses field assignment not properties |

---

## ?? Expected Behavior

### With Profile Service
```
Startup ? LoadSettingsFromSource()
          ?
      Profile found?
      ?YES          ?NO
Load Profile    Load JSON
      ?             ?
   ApplySettings(settings)
      ?
   Initialize mount
```

### Without Profile Service (Backward Compatible)
```
Startup ? LoadSettingsFromSource()
          ?
      Load JSON
      ?
   ApplySettings(settings)
      ?
   Initialize mount
```

---

## ?? Success Criteria

- [ ] Application builds without errors
- [ ] Application starts with profiles
- [ ] Application starts without profiles
- [ ] Settings load from active profile
- [ ] Fallback to JSON works
- [ ] Existing JSON-only installations continue to work
- [ ] No side effects during initialization
- [ ] Logs show source of settings (profile vs JSON)

---

## ?? One-Liner Summary

**Replace `LoadFromJson()` with `ApplySettings(settings)` and add `LoadSettingsFromSource()` to support profile loading with JSON fallback.**

---

## ?? Need Help?

See full details in `DETAILED_IMPLEMENTATION_GUIDE.md`

**Total implementation time**: ~15 minutes ??
