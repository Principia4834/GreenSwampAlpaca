# Profile Loading Implementation Guide

## Overview

This guide provides manual instructions for implementing profile loading when the driver is created, and copying profiles to the user documents folder.

---

## Part 1: Services Created

### 1.1 ProfileLoaderService ? DONE

**File Created**: `GreenSwamp.Alpaca.Settings/Services/ProfileLoaderService.cs`

**Interface**: `IProfileLoaderService`

**Methods**:
- `LoadActiveProfileAsync()` - Loads active profile settings
- `CopyActiveProfileToDocumentsAsync()` - Copies active profile to Documents
- `CopyAllProfilesToDocumentsAsync()` - Backs up all profiles to Documents

**Registration**: ? Added to `SettingsServiceCollectionExtensions.cs`

---

## Part 2: Manual Code Changes Required

### 2.1 Update SkySettingsInstance Constructor

**File**: `GreenSwamp.Alpaca.MountControl/SkySettingsInstance.cs`

**Current Code** (around line 197):
```csharp
public SkySettingsInstance(IVersionedSettingsService settingsService)
{
    _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

    // Initialize with defaults (already done via field initializers)

    // Load from JSON (overwrites defaults)
    LoadFromJson();

    LogSettings("Initialized", $"Mount:{_mount}|Port:{_port}");
}
```

**Change To**:
```csharp
private readonly IProfileLoaderService? _profileLoaderService;

public SkySettingsInstance(
    IVersionedSettingsService settingsService,
    IProfileLoaderService? profileLoaderService = null)
{
    _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    _profileLoaderService = profileLoaderService;

    // Initialize with defaults (already done via field initializers)

    // Try to load from active profile first, fall back to JSON if not available
    if (_profileLoaderService != null)
    {
        try
        {
            var profileSettings = _profileLoaderService.LoadActiveProfileAsync().GetAwaiter().GetResult();
            LoadFromSkySettings(profileSettings);
            LogSettings("Initialized from active profile", $"Mount:{_mount}|Port:{_port}");
        }
        catch (Exception ex)
        {
            // Fall back to loading from JSON
            LogSettings("Profile load failed, falling back to JSON", ex.Message);
            LoadFromJson();
        }
    }
    else
    {
        // No profile service available, load from JSON (backward compatibility)
        LoadFromJson();
    }

    LogSettings("Initialized", $"Mount:{_mount}|Port:{_port}");
}
```

**Add New Method** (add after `LoadFromJson()` method):
```csharp
/// <summary>
/// Load settings from a SkySettings object (from profile)
/// </summary>
private void LoadFromSkySettings(SkySettings.Models.SkySettings settings)
{
    // Connection & Mount Settings
    _mount = Enum.TryParse<MountType>(settings.Mount, out var mountType) ? mountType : MountType.Simulator;
    _port = settings.Port ?? "COM1";
    _baudRate = (SerialSpeed)settings.BaudRate;
    _dataBits = settings.DataBits;
    _handShake = Enum.TryParse<Handshake>(settings.Handshake, out var handshake) ? handshake : Handshake.None;
    _readTimeout = settings.ReadTimeout;
    _dtrEnable = settings.DTREnable;
    _rtsEnable = settings.RTSEnable;
    
    // Alignment & Tracking
    _alignmentMode = Enum.TryParse<AlignmentMode>(settings.AlignmentMode, out var alignMode) ? alignMode : AlignmentMode.GermanPolar;
    _equatorialCoordinateType = Enum.TryParse<EquatorialCoordinateType>(settings.EquatorialCoordinateType, out var coordType) ? coordType : EquatorialCoordinateType.Other;
    _atPark = settings.AtPark;
    _trackingRate = Enum.TryParse<DriveRate>(settings.TrackingRate, out var trackRate) ? trackRate : DriveRate.Sidereal;
    _autoTrack = settings.AutoTrack;
    
    // Location
    _latitude = settings.Latitude;
    _longitude = settings.Longitude;
    _elevation = settings.Elevation;
    
    // Home & Park Positions
    _homeAxisX = settings.HomeAxisX;
    _homeAxisY = settings.HomeAxisY;
    _autoHomeAxisX = settings.AutoHomeAxisX;
    _autoHomeAxisY = settings.AutoHomeAxisY;
    _parkName = settings.ParkName ?? "Default";
    _parkAxes = settings.ParkAxes?.ToList() ?? new List<double> { 0, 90 };
    
    // Park positions - convert from Settings model to internal format
    if (settings.ParkPositions != null && settings.ParkPositions.Any())
    {
        // Note: You'll need to convert ParkPosition objects to your internal format
        // This depends on your internal ParkPosition class structure
    }
    
    // Tracking Rates
    _siderealRate = settings.SiderealRate;
    _lunarRate = settings.LunarRate;
    _solarRate = settings.SolarRate;
    _kingRate = settings.KingRate;
    _raTrackingOffset = settings.RATrackingOffset;
    
    // Limits
    _hourAngleLimit = settings.HourAngleLimit;
    _axisUpperLimitY = settings.AxisUpperLimitY;
    _axisLowerLimitY = settings.AxisLowerLimitY;
    _axisLimitX = settings.AxisLimitX;
    _axisHzTrackingLimit = settings.AxisHzTrackingLimit;
    _limitTracking = settings.LimitTracking;
    _limitPark = settings.LimitPark;
    _parkLimitName = settings.ParkLimitName ?? "Default";
    _hzLimitTracking = settings.HzLimitTracking;
    _hzLimitPark = settings.HzLimitPark;
    _parkHzLimitName = settings.ParkHzLimitName ?? "Default";
    _noSyncPastMeridian = settings.NoSyncPastMeridian;
    _syncLimit = settings.SyncLimit;
    _syncLimitOn = settings.SyncLimitOn;
    
    // Polar Mode
    _polarMode = Enum.TryParse<PolarMode>(settings.PolarMode, out var polarMode) ? polarMode : PolarMode.Left;
    
    // Guiding
    _raBacklash = settings.RaBacklash;
    _decBacklash = settings.DecBacklash;
    _minPulseRa = settings.MinPulseRa;
    _minPulseDec = settings.MinPulseDec;
    _decPulseToGoTo = settings.DecPulseToGoTo;
    _st4Guiderate = settings.St4Guiderate;
    _guideRateOffsetX = settings.GuideRateOffsetX;
    _guideRateOffsetY = settings.GuideRateOffsetY;
    
    // Optics
    _apertureDiameter = settings.ApertureDiameter;
    _apertureArea = settings.ApertureArea;
    _focalLength = settings.FocalLength;
    _refraction = settings.Refraction;
    _temperature = settings.Temperature;
    
    // Custom Gearing
    _customGearing = settings.CustomGearing;
    _customRa360Steps = settings.CustomRa360Steps;
    _customRaWormTeeth = settings.CustomRaWormTeeth;
    _customDec360Steps = settings.CustomDec360Steps;
    _customDecWormTeeth = settings.CustomDecWormTeeth;
    _customRaTrackingOffset = settings.CustomRaTrackingOffset;
    _customDecTrackingOffset = settings.CustomDecTrackingOffset;
    
    // PEC
    _pecOn = settings.PecOn;
    _ppecOn = settings.PpecOn;
    _alternatingPPEC = settings.AlternatingPPEC;
    _pecOffSet = settings.PecOffSet;
    _pecWormFile = settings.PecWormFile ?? string.Empty;
    _pec360File = settings.Pec360File ?? string.Empty;
    _pecMode = Enum.TryParse<PecMode>(settings.PecMode, out var pecMode) ? pecMode : PecMode.PecWorm;
    
    // Hand Controller
    _hcSpeed = Enum.TryParse<SlewSpeed>(settings.HcSpeed, out var hcSpeed) ? hcSpeed : SlewSpeed.Eight;
    _hcMode = Enum.TryParse<HcMode>(settings.HcMode, out var hcMode) ? hcMode : HcMode.Guiding;
    _hcAntiRa = settings.HcAntiRa;
    _hcAntiDec = settings.HcAntiDec;
    _hcFlipEW = settings.HcFlipEW;
    _hcFlipNS = settings.HcFlipNS;
    _disableKeysOnGoTo = settings.DisableKeysOnGoTo;
    
    // Camera
    _cameraWidth = settings.CameraWidth;
    _cameraHeight = settings.CameraHeight;
    _eyepieceFS = settings.EyepieceFS;
    
    // UTC Offset
    if (!string.IsNullOrEmpty(settings.UTCOffset) && TimeSpan.TryParse(settings.UTCOffset, out var utcOffset))
    {
        _utcOffset = utcOffset;
    }
    
    // Encoders
    _encodersOn = settings.EncodersOn;
}
```

---

### 2.2 Update Program.cs Registration

**File**: `GreenSwamp.Alpaca.Server/Program.cs`

**Find** (around line 150-156):
```csharp
// Configure Server Settings from configuration
builder.Services.AddSingleton(sp =>
{
    // Phase 4.2: Create instance with default (static) settings
    var settingsService = sp.GetRequiredService<IVersionedSettingsService>();
    return new GreenSwamp.Alpaca.MountControl.SkySettingsInstance(settingsService);
});
```

**Replace With**:
```csharp
// Configure Server Settings from configuration
builder.Services.AddSingleton(sp =>
{
    // Phase 4.2: Create instance with profile loading support
    var settingsService = sp.GetRequiredService<IVersionedSettingsService>();
    var profileLoader = sp.GetService<IProfileLoaderService>(); // Optional for backward compatibility
    return new GreenSwamp.Alpaca.MountControl.SkySettingsInstance(settingsService, profileLoader);
});
```

---

### 2.3 Add Profile Copying to SkyServer.Core.cs MountConnect

**File**: `GreenSwamp.Alpaca.MountControl/SkyServer.Core.cs`

**Location**: In the `MountConnect()` method, find the section that copies `appsettings.user.json` to logs (around line 424-470)

**After the existing appsettings.user.json copy block**, add:

```csharp
// Copy active profile to user documents folder
try
{
    // Get profile loader service from DI container (if available)
    // Note: You'll need to inject this via Program.cs or pass it through
    var profileLoader = Program.ServiceProvider?.GetService<IProfileLoaderService>();
    
    if (profileLoader != null)
    {
        var documentsPath = profileLoader.CopyActiveProfileToDocumentsAsync().GetAwaiter().GetResult();
        
        monitorItem = new MonitorEntry
        {
            Datetime = HiResDateTime.UtcNow,
            Device = MonitorDevice.Server,
            Category = MonitorCategory.Mount,
            Type = MonitorType.Information,
            Method = MethodBase.GetCurrentMethod()?.Name,
            Thread = Thread.CurrentThread.ManagedThreadId,
            Message = $"Copied active profile to documents: {documentsPath}"
        };
        MonitorLog.LogToMonitor(monitorItem);
    }
}
catch (Exception e)
{
    monitorItem = new MonitorEntry
    {
        Datetime = HiResDateTime.UtcNow,
        Device = MonitorDevice.Server,
        Category = MonitorCategory.Mount,
        Type = MonitorType.Warning,
        Method = MethodBase.GetCurrentMethod()?.Name,
        Thread = Thread.CurrentThread.ManagedThreadId,
        Message = $"Could not copy profile to documents. {e.Message}"
    };
    MonitorLog.LogToMonitor(monitorItem);
}
```

**IMPORTANT**: The challenge with SkyServer.Core.cs is accessing the DI service provider. You have two options:

#### Option A: Store ServiceProvider in Program.cs

**Add to Program.cs** (after `var app = builder.Build();`):
```csharp
// Store service provider for SkyServer to access services
Program.ServiceProvider = app.Services;
```

**Add to Program class**:
```csharp
public static IServiceProvider? ServiceProvider { get; set; }
```

#### Option B: Pass ProfileLoaderService to SkyServer.Initialize

**Change SkyServer.Initialize signature**:
```csharp
public static void Initialize(
    SkySettingsInstance settings,
    IProfileLoaderService? profileLoader = null)
{
    _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    _profileLoader = profileLoader;
    
    // ... rest of initialization
}
```

**Store in static field**:
```csharp
private static IProfileLoaderService? _profileLoader;
```

**Use in MountConnect**:
```csharp
if (_profileLoader != null)
{
    var documentsPath = _profileLoader.CopyActiveProfileToDocumentsAsync().GetAwaiter().GetResult();
    // ... logging
}
```

---

## Part 3: Testing Checklist

### 3.1 Profile Loading on Startup
- [ ] Start application
- [ ] Check logs for "Initialized from active profile"
- [ ] Verify mount settings match active profile
- [ ] Change active profile
- [ ] Restart application
- [ ] Verify new profile loaded

### 3.2 Profile Copying to Documents
- [ ] Start application
- [ ] Connect to mount (triggers MountConnect)
- [ ] Check `%USERPROFILE%\Documents\GreenSwampAlpaca\Profiles\`
- [ ] Verify active profile copied with timestamp
- [ ] Check logs for "Copied active profile to documents"

### 3.3 Backward Compatibility
- [ ] Remove profile service from DI (comment out registration)
- [ ] Application should still start
- [ ] Settings load from appsettings.user.json
- [ ] No errors in logs

### 3.4 Error Handling
- [ ] Delete active-profile.txt
- [ ] Application should fall back to default or JSON
- [ ] No crash, error logged
- [ ] Corrupt profile file
- [ ] Application should fall back to JSON
- [ ] Error logged but app continues

---

## Part 4: File System Layout

### After Implementation

```
%AppData%/GreenSwampAlpaca/{version}/
??? profiles/
?   ??? default-germanpolar.json
?   ??? default-polar.json
?   ??? default-altaz.json
?   ??? my-eq6r.json
??? active-profile.txt              ? Points to active profile
??? appsettings.user.json           ? Fallback if profiles not available

%USERPROFILE%/Documents/GreenSwampAlpaca/Profiles/
??? my-eq6r-20250120-143052.json    ? Timestamped backup on connect
??? my-eq6r-20250121-091523.json    ? Another backup
??? Backup-20250120-150000/         ? Full backup folder (if using CopyAllProfilesToDocumentsAsync)
    ??? default-germanpolar.json
    ??? default-polar.json
    ??? default-altaz.json
    ??? my-eq6r.json
```

---

## Part 5: Migration Path

### For Existing Users

1. **First Run with Profiles**:
   - Old `appsettings.user.json` exists
   - No profiles exist yet
   - Default profiles auto-created
   - Settings continue to load from `appsettings.user.json`

2. **User Creates Profile**:
   - User creates profile via UI
   - Profile includes settings from current `appsettings.user.json`
   - User activates profile
   - On next restart, settings load from profile

3. **Ongoing Operation**:
   - Settings always load from active profile
   - `appsettings.user.json` kept for backward compatibility
   - Each mount connect backs up profile to Documents

---

## Part 6: Alternative: Simpler Implementation

If the above seems too complex, here's a minimal implementation:

### Minimal Changes Only

1. **In Program.cs** - Keep current registration, just pass profile loader:
```csharp
var profileLoader = sp.GetService<IProfileLoaderService>();
return new SkySettingsInstance(settingsService, profileLoader);
```

2. **In SkySettingsInstance Constructor** - Just log that profiles are available:
```csharp
if (_profileLoaderService != null)
{
    LogSettings("Profile loader available", "Will use profile system");
}
LoadFromJson(); // Still load from JSON for now
```

3. **In MountConnect** - Just copy profile to documents, don't change loading:
```csharp
// At end of MountConnect, add profile backup
if (_profileLoader != null)
{
    try
    {
        _profileLoader.CopyActiveProfileToDocumentsAsync().Wait();
    }
    catch { /* Ignore errors */ }
}
```

This way:
- ? Profiles get backed up to Documents
- ? No change to loading logic (still uses JSON)
- ? Easy to test
- ? Can enhance later

---

## Part 7: Recommended Implementation Order

1. ? **Phase 1**: Create ProfileLoaderService (DONE)
2. ? **Phase 2**: Register in DI (DONE)
3. ?? **Phase 3**: Minimal integration
   - Pass profileLoader to SkySettingsInstance
   - Don't use it yet, just store it
   - Test that app still works
4. ?? **Phase 4**: Add profile backup to MountConnect
   - Copy active profile to Documents on connect
   - Test backup creation
5. ?? **Phase 5**: Full integration
   - Load settings from active profile in constructor
   - Add LoadFromSkySettings method
   - Test profile loading
6. ?? **Phase 6**: Polish
   - Error handling
   - Logging
   - Testing

---

## Summary

**Files Created**:
- ? `ProfileLoaderService.cs` - Service for loading profiles and copying to documents

**Manual Edits Required**:
- ?? `SkySettingsInstance.cs` - Update constructor, add LoadFromSkySettings method
- ?? `Program.cs` - Pass profileLoader to SkySettingsInstance
- ?? `SkyServer.Core.cs` - Add profile copying in MountConnect (optional)

**Recommendation**: Start with minimal implementation (Phase 3-4), test thoroughly, then enhance to full integration (Phase 5-6).

