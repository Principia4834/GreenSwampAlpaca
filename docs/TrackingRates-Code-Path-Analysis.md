# TrackingRates Code Path Analysis Report

**Generated:** 2025-01-XX  
**Purpose:** Comprehensive documentation of all code paths that set and get `_mTrackingRates` in `Telescope.cs`  
**Scope:** Initialization, JSON settings properties, calculated values, and data flows

---

## Executive Summary

The `_mTrackingRates` backing variable in `Telescope.cs` is a private field that controls which tracking rates are reported as available to ASCOM clients. This report documents:

1. How `_mTrackingRates` is initialized
2. All places where it's set or accessed
3. JSON settings properties that influence its behavior
4. Calculated values that contribute to tracking rate determination

---

## 1. Variable Declaration and Type

### Location
**File:** `GreenSwamp.Alpaca.Server\TelescopeDriver\Telescope.cs`

### Declaration
```csharp
// Line ~22-24 (approximate)
private AxisRates[] _mAxisRates;
private TrackingRates _mTrackingRates;
private TrackingRatesSimple _mTrackingRatesSimple;
```

### Type Definition
**File:** `GreenSwamp.Alpaca.Server\TelescopeDriver\Rates.cs`

**Class:** `TrackingRates` (lines 199-302)
- Implements: `ITrackingRates`, `IEnumerable`, `IEnumerator`, `IDisposable`
- Purpose: Strongly-typed collection of `DriveRate` values
- **Constructor initializes with fixed array:**
  ```csharp
  public TrackingRates()
  {
      // Lines 212-216
      _mTrackingRates = new[] { 
          DriveRate.Sidereal, 
          DriveRate.King, 
          DriveRate.Lunar, 
          DriveRate.Solar 
      };
  }
  ```

**Companion Class:** `TrackingRatesSimple` (lines 306-398)
- Same interfaces, but only contains: `DriveRate.Sidereal`
- Used when `CanTrackingRates` capability is `false`

---

## 2. Initialization Code Paths

### 2.1 Constructor (Parameterless)
**File:** `Telescope.cs`  
**Lines:** ~38-70

```csharp
public Telescope()
{
    _deviceNumber = 0; // Default device
    
    // Initialization occurs here (lines 48-50):
    _mAxisRates = new AxisRates[3];
    _mAxisRates[0] = new AxisRates(TelescopeAxis.Primary);
    _mAxisRates[1] = new AxisRates(TelescopeAxis.Secondary);
    _mAxisRates[2] = new AxisRates(TelescopeAxis.Tertiary);
    _mTrackingRates = new TrackingRates();              // ← INITIALIZATION #1
    _mTrackingRatesSimple = new TrackingRatesSimple();  // ← INITIALIZATION #2
}
```

**Result:**
- `_mTrackingRates` contains: `[Sidereal, King, Lunar, Solar]`
- `_mTrackingRatesSimple` contains: `[Sidereal]`

### 2.2 Constructor (With Device Number)
**File:** `Telescope.cs`  
**Lines:** ~76-106

```csharp
public Telescope(int deviceNumber)
{
    _deviceNumber = deviceNumber;
    
    // Identical initialization (lines 90-92):
    _mAxisRates = new AxisRates[3];
    _mAxisRates[0] = new AxisRates(TelescopeAxis.Primary);
    _mAxisRates[1] = new AxisRates(TelescopeAxis.Secondary);
    _mAxisRates[2] = new AxisRates(TelescopeAxis.Tertiary);
    _mTrackingRates = new TrackingRates();              // ← INITIALIZATION #1
    _mTrackingRatesSimple = new TrackingRatesSimple();  // ← INITIALIZATION #2
}
```

**Result:** Same as parameterless constructor

---

## 3. Access Code Paths (Getters)

### 3.1 TrackingRates Property Getter
**File:** `Telescope.cs`  
**Lines:** ~1174-1190 (approximate)

```csharp
public ITrackingRates TrackingRates
{
    get
    {
        var inst = GetInstance();
        MonitorEntry monitorItem;
        
        if (inst.Settings.CanTrackingRates)
        {
            monitorItem = new MonitorEntry { ... };
            MonitorLog.LogToMonitor(monitorItem);
            return _mTrackingRates;  // ← RETURNS FULL LIST (4 rates)
        }
        
        monitorItem = new MonitorEntry { ... };
        MonitorLog.LogToMonitor(monitorItem);
        return _mTrackingRatesSimple;  // ← RETURNS SIMPLE LIST (1 rate)
    }
}
```

**Decision Logic:**
- **IF** `inst.Settings.CanTrackingRates == true`:  
  → Returns `_mTrackingRates` (4 tracking rates)
  
- **ELSE**:  
  → Returns `_mTrackingRatesSimple` (1 tracking rate: Sidereal only)

---

## 4. JSON Settings Properties

### 4.1 CanTrackingRates Capability
**Primary Control:** This single boolean determines which collection is returned.

#### Settings Model Definition
**File:** `GreenSwamp.Alpaca.Settings\Models\SkySettings.cs`  
**Line:** ~196

```csharp
public bool CanTrackingRates { get; set; }
```

#### Settings Instance Property
**File:** `GreenSwamp.Alpaca.MountControl\SkySettingsInstance.cs`  
**Lines:** ~1750 (read-only property)

```csharp
// Backing field (line ~169)
private bool _canTrackingRates = true;

// Property (line ~1750)
public bool CanTrackingRates => _canTrackingRates;
```

#### Settings Load Path
**File:** `SkySettingsInstance.cs`  
**Method:** `ApplySettings(Settings.Models.SkySettings settings)`  
**Line:** 1976

```csharp
_canTrackingRates = settings.CanTrackingRates;
```

#### Settings Save Path
**File:** `SkySettingsInstance.cs`  
**Method:** `SaveAsync()`  
**Line:** 2173

```csharp
settings.CanTrackingRates = _canTrackingRates;
```

---

## 5. Tracking Rate Values

### 5.1 Individual Rate Properties
These properties define the **numeric values** (degrees per hour) for each tracking rate type.

#### Settings Model (JSON Source)
**File:** `GreenSwamp.Alpaca.Settings\Models\SkySettings.cs`  
**Lines:** ~103-106

```csharp
public double SiderealRate { get; set; }
public double LunarRate { get; set; }
public double SolarRate { get; set; }
public double KingRate { get; set; }
```

#### Settings Instance (Runtime Storage)
**File:** `SkySettingsInstance.cs`  
**Backing Fields (Lines ~91-94):**

```csharp
private double _siderealRate = 15.0410671786691;
private double _lunarRate = 14.511415534643;
private double _solarRate = 15.0;
private double _kingRate = 15.0369;
```

**Properties (Lines ~612-663):**

```csharp
public double SiderealRate
{
    get => _siderealRate;
    set
    {
        if (Math.Abs(_siderealRate - value) > 0.0001)
        {
            _siderealRate = value;
            OnPropertyChanged();
        }
    }
}

// Similar for LunarRate, SolarRate, KingRate
```

#### Load from JSON
**File:** `SkySettingsInstance.cs`  
**Method:** `ApplySettings`  
**Lines:** 1852-1855

```csharp
_siderealRate = settings.SiderealRate;
_lunarRate = settings.LunarRate;
_solarRate = settings.SolarRate;
_kingRate = settings.KingRate;
```

#### Save to JSON
**File:** `SkySettingsInstance.cs`  
**Method:** `SaveAsync`  
**Lines:** 2067-2070

```csharp
settings.SiderealRate = _siderealRate;
settings.LunarRate = _lunarRate;
settings.SolarRate = _solarRate;
settings.KingRate = _kingRate;
```

---

## 6. Current Tracking Rate Selection

### 6.1 TrackingRate Property (Current Selection)
**File:** `Telescope.cs`  
**Lines:** ~1136-1156 (getter), ~1851-1861 (setter - approximate)

#### Getter
```csharp
public DriveRate TrackingRate
{
    get
    {
        var inst = GetInstance();
        var r = inst.Settings.TrackingRate;  // ← Reads from Settings
        
        var monitorItem = new MonitorEntry { ... };
        MonitorLog.LogToMonitor(monitorItem);
        
        return r;
    }
}
```

#### Setter
```csharp
set
{
    var monitorItem = new MonitorEntry { ... };
    MonitorLog.LogToMonitor(monitorItem);
    
    var inst = GetInstance();
    CheckTrackingRate("TrackingRate", value);
    inst.Settings.TrackingRate = value;  // ← Writes to Settings
}
```

### 6.2 Settings Instance Storage
**File:** `SkySettingsInstance.cs`  
**Backing Field (Line ~62):**

```csharp
private DriveRate _trackingRate = DriveRate.Sidereal;
```

**Property (Lines ~358-371):**

```csharp
public DriveRate TrackingRate
{
    get => _trackingRate;
    set
    {
        if (_trackingRate != value)
        {
            _trackingRate = value;
            OnPropertyChanged();
        }
    }
}
```

**Load from JSON (Line 1827):**

```csharp
_trackingRate = Enum.Parse<DriveRate>(settings.TrackingRate);
```

**Save to JSON (Line 2046):**

```csharp
settings.TrackingRate = _trackingRate.ToString();
```

---

## 7. MountInstance Context

### 7.1 GetInstance() Method
**File:** `Telescope.cs`  
**Lines:** ~108-118 (approximate)

```csharp
private MountInstance GetInstance()
{
    var instance = MountInstanceRegistry.GetInstance(_deviceNumber);
    if (instance == null)
    {
        throw new InvalidOperationException($"Device number {_deviceNumber} not found in registry");
    }
    return instance;
}
```

### 7.2 Settings Access Pattern
Every property access in `Telescope.cs` follows this pattern:

```csharp
var inst = GetInstance();
// Access: inst.Settings.CanTrackingRates
// Access: inst.Settings.TrackingRate
```

**Result:** Settings are retrieved from the `MountInstance` associated with this telescope's device number.

---

## 8. Disposal Code Path

### 8.1 Dispose Method
**File:** `Telescope.cs`  
**Lines:** ~1270-1285 (approximate)

```csharp
public void Dispose()
{
    Dispose(true);
}

private void Dispose(bool disposing)
{
    if (!disposing) return;
    
    Connected = false;
    _mAxisRates[0].Dispose();
    _mAxisRates[1].Dispose();
    _mAxisRates[2].Dispose();
    _mAxisRates = null;
    _mTrackingRates.Dispose();        // ← DISPOSAL #1
    _mTrackingRates = null;
    _mTrackingRatesSimple.Dispose();  // ← DISPOSAL #2
    _mTrackingRatesSimple = null;
}
```

---

## 9. Calculated Values and Dependencies

### 9.1 No Direct Calculations
**Key Finding:** The `_mTrackingRates` variable itself is **NOT** calculated or modified after initialization.

- ✅ Initialized once in constructor
- ✅ Never reassigned
- ✅ Contains static array: `[Sidereal, King, Lunar, Solar]`

### 9.2 Decision-Based Selection
The **selection** between `_mTrackingRates` and `_mTrackingRatesSimple` is dynamic:

```
┌─────────────────────────────────────┐
│  TrackingRates Property Getter      │
└─────────────────────────────────────┘
              ↓
    GetInstance().Settings.CanTrackingRates
              ↓
         ┌────┴────┐
         │         │
      TRUE      FALSE
         │         │
         ↓         ↓
  _mTrackingRates  _mTrackingRatesSimple
  [4 rates]        [1 rate: Sidereal]
```

### 9.3 Tracking Rate Values (Numeric)
The **numeric values** (degrees/hour) for each rate are stored separately:

- **Source:** JSON settings → `SkySettingsInstance`
- **Access:** Via `MountInstance.Settings.SiderealRate` (etc.)
- **Usage:** These values are used by tracking logic, **NOT** by the `TrackingRates` property

---

## 10. JSON Settings File Structure

### 10.1 Relevant JSON Properties

**File:** `appsettings.user.json` (or `appsettings.json`)

```json
{
  "CanTrackingRates": true,
  "TrackingRate": "Sidereal",
  "SiderealRate": 15.0410671786691,
  "LunarRate": 14.511415534643,
  "SolarRate": 15.0,
  "KingRate": 15.0369
}
```

### 10.2 Settings Load Sequence

```
Application Startup
  ↓
Program.cs → ConfigureServices
  ↓
IVersionedSettingsService.GetSettings()
  ↓
SkySettings model loaded from JSON
  ↓
MountInstance constructor
  ↓
SkySettingsInstance(deviceSettings, settingsService)
  ↓
ApplySettings(settings)
  ↓
Backing fields populated:
  - _canTrackingRates = settings.CanTrackingRates
  - _trackingRate = Enum.Parse<DriveRate>(settings.TrackingRate)
  - _siderealRate = settings.SiderealRate
  - _lunarRate = settings.LunarRate
  - _solarRate = settings.SolarRate
  - _kingRate = settings.KingRate
```

---

## 11. Complete Data Flow Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                          JSON SETTINGS FILE                         │
│  appsettings.user.json (or appsettings.json)                        │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│               IVersionedSettingsService.GetSettings()               │
│  Returns: SkySettings model (Settings\Models\SkySettings.cs)        │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│               MountInstance constructor                             │
│  Creates: SkySettingsInstance with device-specific config           │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│          SkySettingsInstance.ApplySettings(settings)                │
│  Loads:                                                              │
│    _canTrackingRates ← settings.CanTrackingRates                    │
│    _trackingRate ← Enum.Parse(settings.TrackingRate)                │
│    _siderealRate ← settings.SiderealRate                            │
│    _lunarRate ← settings.LunarRate                                  │
│    _solarRate ← settings.SolarRate                                  │
│    _kingRate ← settings.KingRate                                    │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│         MountInstance.Settings (SkySettingsInstance)                │
│  Exposes properties:                                                 │
│    .CanTrackingRates → returns _canTrackingRates                    │
│    .TrackingRate → returns _trackingRate                            │
│    .SiderealRate → returns _siderealRate                            │
│    .LunarRate → returns _lunarRate                                  │
│    .SolarRate → returns _solarRate                                  │
│    .KingRate → returns _kingRate                                    │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│              Telescope.TrackingRates property GET                   │
│                                                                      │
│  1. var inst = GetInstance()                                        │
│  2. Check: inst.Settings.CanTrackingRates                           │
│  3. IF TRUE: return _mTrackingRates (4 rates)                       │
│     ELSE: return _mTrackingRatesSimple (1 rate)                     │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│                    ASCOM Client Receives                            │
│  Collection of DriveRate enum values:                               │
│    - IF CanTrackingRates=true: [Sidereal, King, Lunar, Solar]      │
│    - IF CanTrackingRates=false: [Sidereal]                          │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 12. Key Findings Summary

### 12.1 Initialization
- `_mTrackingRates` is initialized **once** in constructor
- Contains hardcoded array: `[Sidereal, King, Lunar, Solar]`
- Never modified after construction

### 12.2 Runtime Behavior
- Property `TrackingRates` returns **different objects** based on `CanTrackingRates`
- Decision is made **every time** the property is accessed (no caching)
- Selection logic is in `Telescope.cs` lines ~1174-1190

### 12.3 Settings Dependencies
- **Primary Control:** `CanTrackingRates` (boolean)
  - JSON property: `"CanTrackingRates"`
  - Default value: `true` (initialized in backing field)
  
- **Secondary (Numeric Values):**
  - `SiderealRate`, `LunarRate`, `SolarRate`, `KingRate`
  - Used elsewhere (not by TrackingRates property)

### 12.4 No Calculated Values
- The returned collection is **not calculated**
- It is **selected** from two pre-initialized objects
- No runtime computation of tracking rates occurs in this code path

---

## 13. Potential Issue Areas (Observation Only)

### 13.1 Static Position Counter
**File:** `Rates.cs` (TrackingRates class)  
**Line:** ~208

```csharp
private static int _pos = -1;
```

**Observation:** Position counter is `static` but class implements `IEnumerator`. Multiple instances or concurrent access could cause enumeration issues.

### 13.2 IndexOutOfRange Risk
**File:** `Rates.cs` (TrackingRatesSimple class)  
**Lines:** ~386-391

```csharp
public DriveRate this[int index]
{
    get
    {
        if (index <= 1 || index > Count)  // ← Condition uses <=
            throw new InvalidValueException(...);
        return _mTrackingRates[index - 1];
    }
}
```

**Observation:** Condition is `index <= 1` (should be `index < 1`?), combined with 1-based indexing. For a single-element array, valid index should be `1`, but condition rejects it.

**Contrast with TrackingRates (full version):**
```csharp
if (index < 1 || index > Count)  // ← Uses < (correct)
```

### 13.3 No Dynamic Updates
**Observation:** Once `Telescope` object is constructed, `_mTrackingRates` and `_mTrackingRatesSimple` are fixed. Changes to `CanTrackingRates` in settings will affect **which** object is returned, but not the **contents** of those objects.

---

## 14. Code Locations Reference Table

| Item | File | Lines | Notes |
|------|------|-------|-------|
| `_mTrackingRates` declaration | `Telescope.cs` | ~22 | Private field |
| Constructor initialization | `Telescope.cs` | ~48-50, ~90-92 | Two constructors |
| `TrackingRates` property getter | `Telescope.cs` | ~1174-1190 | Returns one of two objects |
| `TrackingRates` class definition | `Rates.cs` | 199-302 | Contains 4 rates |
| `TrackingRatesSimple` class | `Rates.cs` | 306-398 | Contains 1 rate |
| `CanTrackingRates` JSON property | `Models\SkySettings.cs` | ~196 | JSON model |
| `CanTrackingRates` runtime property | `SkySettingsInstance.cs` | ~1750 | Read-only |
| Settings load (ApplySettings) | `SkySettingsInstance.cs` | 1976 | `_canTrackingRates = settings.CanTrackingRates` |
| Settings save (SaveAsync) | `SkySettingsInstance.cs` | 2173 | `settings.CanTrackingRates = _canTrackingRates` |
| Rate values (JSON model) | `Models\SkySettings.cs` | ~103-106 | Numeric degrees/hour |
| Rate values (runtime) | `SkySettingsInstance.cs` | ~612-663 | Properties with backing fields |
| Rate values load | `SkySettingsInstance.cs` | 1852-1855 | From JSON to instance |
| Rate values save | `SkySettingsInstance.cs` | 2067-2070 | From instance to JSON |
| `GetInstance()` method | `Telescope.cs` | ~108-118 | Device registry lookup |
| Dispose | `Telescope.cs` | ~1270-1285 | Cleanup |

---

## 15. Conclusion

The `_mTrackingRates` variable is:

1. **Statically Initialized:** Contains fixed array `[Sidereal, King, Lunar, Solar]`
2. **Never Modified:** No code path changes the array after construction
3. **Selection-Based:** Returned conditionally based on `CanTrackingRates` setting
4. **Settings-Driven:** JSON property `CanTrackingRates` controls behavior
5. **Instance-Scoped:** Each `Telescope` instance has its own copies
6. **Device-Aware:** Settings retrieved via `MountInstance` for multi-device support

**Rate Values (Numeric):**
- Stored separately in `SkySettingsInstance`
- Loaded from JSON properties: `SiderealRate`, `LunarRate`, `SolarRate`, `KingRate`
- Not used by `TrackingRates` property (used by tracking logic elsewhere)

**Key Control Flow:**
```
JSON → VersionedSettingsService → SkySettings model → 
  SkySettingsInstance (ApplySettings) → MountInstance.Settings → 
    Telescope.GetInstance().Settings.CanTrackingRates → 
      Return _mTrackingRates OR _mTrackingRatesSimple
```

---

## Appendix A: Related Classes

### DriveRate Enum (ASCOM Standard)
```csharp
public enum DriveRate
{
    Sidereal = 0,
    Lunar = 1,
    Solar = 2,
    King = 3
}
```

### ITrackingRates Interface (ASCOM Standard)
```csharp
public interface ITrackingRates : IEnumerable, IEnumerator, IDisposable
{
    int Count { get; }
    DriveRate this[int index] { get; }
}
```

---

## Appendix B: Settings Persistence Architecture

```
User Edits Settings in UI
  ↓
SkySettingsInstance property setter
  ↓
OnPropertyChanged()
  ↓
QueueSave() (debounced 2 seconds)
  ↓
SaveAsync()
  ↓
Map instance fields → SkySettings model
  ↓
IVersionedSettingsService.SaveSettingsAsync(settings)
  ↓
Write to appsettings.user.json
```

---

**End of Report**
