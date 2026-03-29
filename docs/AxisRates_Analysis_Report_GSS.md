# ASCOM AxisRates Command - Comprehensive Analysis Report

**Generated:** 2024-01-XX  
**Purpose:** Detailed analysis of AxisRates calculation and initialization paths  
**Scope:** ASCOM Telescope.AxisRates property through to SlewSpeedEight derivation

---

## Executive Summary

The ASCOM `AxisRates` property returns rate ranges for axis movement via `MoveAxis()`. For all three telescope axes (Primary, Secondary, Tertiary), the system returns a single rate range from `0.0` to `SkyServer.SlewSpeedEight` degrees per second.

**Key Finding:** `SlewSpeedEight` is calculated as `MaxSlewRate * 1.0` where `MaxSlewRate` comes from user configuration settings.

---

## 1. ASCOM Driver Entry Point

### File: `GS.Ascom.SkyTelescope\Telescope.cs`

#### 1.1 AxisRates Property (Lines 1220-1242)

```csharp
public IAxisRates AxisRates(TelescopeAxes axis)
{
    var monitorItem = new MonitorEntry
    { 
        Datetime = HiResDateTime.UtcNow, 
        Device = MonitorDevice.Telescope, 
        Category = MonitorCategory.Driver, 
        Type = MonitorType.Data, 
        Method = MethodBase.GetCurrentMethod()?.Name, 
        Thread = Thread.CurrentThread.ManagedThreadId, 
        Message = $"   {axis}" 
    };
    MonitorLog.LogToMonitor(monitorItem);

    switch (axis)
    {
        case TelescopeAxes.axisPrimary:
            return new AxisRates(TelescopeAxes.axisPrimary);
        case TelescopeAxes.axisSecondary:
            return new AxisRates(TelescopeAxes.axisSecondary);
        case TelescopeAxes.axisTertiary:
            return new AxisRates(TelescopeAxes.axisTertiary);
        default:
            return null;
    }
}
```

**Behavior:** Creates a new `AxisRates` instance for the requested axis each time the property is accessed.

#### 1.2 Telescope Constructor (Lines 63-88)

```csharp
public Telescope()
{
    // ... initialization code ...
    _mAxisRates = new AxisRates[3];
    _mAxisRates[0] = new AxisRates(TelescopeAxes.axisPrimary);
    _mAxisRates[1] = new AxisRates(TelescopeAxes.axisSecondary);
    _mAxisRates[2] = new AxisRates(TelescopeAxes.axisTertiary);
    // ... more initialization ...
}
```

**Note:** The constructor creates internal `AxisRates` instances, but these are **NOT used** by the `AxisRates` property which creates new instances on each call.

---

## 2. AxisRates Class Implementation

### File: `GS.Ascom.SkyTelescope\Rates.cs`

#### 2.1 AxisRates Constructor (Lines 99-140)

```csharp
public AxisRates(TelescopeAxes axis)
{
    switch (axis)
    {
        case TelescopeAxes.axisPrimary:
            _mRates = new[] { new Rate(0.0, SkyServer.SlewSpeedEight)};
            break;
        case TelescopeAxes.axisSecondary:
            _mRates = new[] { new Rate(0.0, SkyServer.SlewSpeedEight)};
            break;
        case TelescopeAxes.axisTertiary:
            _mRates = new[] { new Rate(0.0, SkyServer.SlewSpeedEight)};
            break;
        default:
            throw new ArgumentOutOfRangeException(nameof(axis), axis, null);
    }
    _pos = -1;
}
```

**Critical Points:**
- All three axes use identical rate ranges
- Minimum rate: `0.0` degrees/second
- Maximum rate: `SkyServer.SlewSpeedEight` degrees/second
- Single rate range per axis (no multiple rate bands)

#### 2.2 Rate Class (Lines 31-74)

```csharp
public sealed class Rate : IRate, IDisposable
{
    internal Rate(double minimum, double maximum)
    {
        Maximum = maximum;
        Minimum = minimum;
    }

    public double Maximum { get; set; }
    public double Minimum { get; set; }
}
```

**Purpose:** Simple container for min/max rate values.

---

## 3. SlewSpeedEight Calculation

### File: `GS.Server\SkyTelescope\SkyServer.cs`

#### 3.1 SlewSpeedEight Field Declaration (Line 56)

```csharp
public static double SlewSpeedEight;
```

**Scope:** Public static field accessible throughout the server and ASCOM driver.

#### 3.2 SetSlewRates Method (Lines 5795-5824)

```csharp
internal static void SetSlewRates(double maxRate)
{
    // Sky Speeds
    _slewSpeedOne = Math.Round(maxRate * 0.0034, 3);
    _slewSpeedTwo = Math.Round(maxRate * 0.0068, 3);
    _slewSpeedThree = Math.Round(maxRate * 0.047, 3);
    _slewSpeedFour = Math.Round(maxRate * 0.068, 3);
    _slewSpeedFive = Math.Round(maxRate * 0.2, 3);
    _slewSpeedSix = Math.Round(maxRate * 0.4, 3);
    _slewSpeedSeven = Math.Round(maxRate * 0.8, 3);
    SlewSpeedEight = Math.Round(maxRate * 1.0, 3);

    var monitorItem = new MonitorEntry
    {
        Datetime = HiResDateTime.UtcNow,
        Device = MonitorDevice.Server,
        Category = MonitorCategory.Server,
        Type = MonitorType.Information,
        Method = MethodBase.GetCurrentMethod()?.Name,
        Thread = Thread.CurrentThread.ManagedThreadId,
        Message = $"{_slewSpeedOne}|{_slewSpeedTwo}|{_slewSpeedThree}|{_slewSpeedFour}|{_slewSpeedFive}|{_slewSpeedSix}|{_slewSpeedSeven}|{SlewSpeedEight}"
    };
    MonitorLog.LogToMonitor(monitorItem);
}
```

**Calculation Details:**

| Speed Level | Multiplier | Rounded to |
|------------|------------|------------|
| SlewSpeedOne | maxRate × 0.0034 | 3 decimals |
| SlewSpeedTwo | maxRate × 0.0068 | 3 decimals |
| SlewSpeedThree | maxRate × 0.047 | 3 decimals |
| SlewSpeedFour | maxRate × 0.068 | 3 decimals |
| SlewSpeedFive | maxRate × 0.2 | 3 decimals |
| SlewSpeedSix | maxRate × 0.4 | 3 decimals |
| SlewSpeedSeven | maxRate × 0.8 | 3 decimals |
| **SlewSpeedEight** | **maxRate × 1.0** | **3 decimals** |

**Formula:**
```
SlewSpeedEight = Math.Round(maxRate * 1.0, 3)
               = Math.Round(maxRate, 3)
```

---

## 4. Initialization and Settings Loading

### 4.1 Defaults Method Call Chain

**File: `GS.Server\SkyTelescope\SkyServer.cs`** (Lines 6960-7001)

```csharp
private static void Defaults()
{
    // ... other defaults ...
    
    //default hand control and slew rates
    SetSlewRates(SkySettings.MaxSlewRate);
    
    // ... more defaults ...
}
```

**When Called:**
1. From static constructor `SkyServer()` (Line 149)
2. From `MountStart()` method (Line 5044)

#### 4.1.1 Static Constructor Initialization (Lines 121-171)

```csharp
static SkyServer()
{
    try
    {
        // ... logging ...
        
        // load default or user property settings
        SkySettings.Load();

        // load some things
        Defaults();
        
        // ... more initialization ...
    }
    catch (Exception ex)
    {
        // ... error handling ...
    }
}
```

**Initialization Order:**
1. `SkySettings.Load()` - Loads user configuration
2. `Defaults()` - Calls `SetSlewRates(SkySettings.MaxSlewRate)`

---

## 5. MaxSlewRate Configuration

### File: `GS.Server\SkyTelescope\SkySettings.cs`

#### 5.1 MaxSlewRate Property (Lines 1423-1438)

```csharp
private static double _maxSlewRate;
public static double MaxSlewRate
{
    get => _maxSlewRate;
    set
    {
        if (Math.Abs(_maxSlewRate - value) < 0.0000000000001) return;
        _maxSlewRate = value;
        Properties.SkyTelescope.Default.MaximumSlewRate = value;
        LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
        OnStaticPropertyChanged();
        SkyServer.SetSlewRates(value);
    }
}
```

**Behavior:**
- Stores value in `_maxSlewRate` backing field
- Persists to `Properties.SkyTelescope.Default.MaximumSlewRate`
- **Immediately calls `SkyServer.SetSlewRates(value)`** whenever changed
- Triggers property change notification

#### 5.2 Load Method (Lines 2324-2329)

```csharp
MaxSlewRate = Properties.SkyTelescope.Default.MaximumSlewRate;
```

**Context:** Within the `SkySettings.Load()` method, this line reads the persisted setting and assigns it to the property, which triggers the setter and thus calls `SetSlewRates()`.

---

## 6. User Configuration Settings

### File: `GS.Server\Properties\SkyTelescope.settings`

#### 6.1 MaximumSlewRate Setting (Lines ~208)

```xml
<Setting Name="MaximumSlewRate" Type="System.Double" Scope="User">
  <Value Profile="(Default)">3.5</Value>
</Setting>
```

**Configuration Details:**
- **Setting Name:** `MaximumSlewRate`
- **Type:** `System.Double`
- **Scope:** `User` (stored in user.config)
- **Default Value:** `3.5` degrees per second

#### 6.2 user.config Storage

**Location:** The user.config file is typically stored at:
```
C:\Users\[Username]\AppData\Local\[Company]\[AppName]\[Version]\user.config
```

The setting is persisted as:
```xml
<setting name="MaximumSlewRate" serializeAs="String">
    <value>3.5</value>
</setting>
```

---

## 7. Complete Data Flow Diagram

```
[Application Start]
        |
        v
[SkyServer Static Constructor]
        |
        v
[SkySettings.Load()]
        |
        v
[Read Properties.SkyTelescope.Default.MaximumSlewRate]
        |
        | (reads from user.config, default = 3.5)
        v
[MaxSlewRate Property Setter]
        |
        v
[SkyServer.SetSlewRates(MaxSlewRate)]
        |
        | (MaxSlewRate = value from config)
        v
[Calculate SlewSpeedEight = Math.Round(MaxSlewRate * 1.0, 3)]
        |
        | (SlewSpeedEight stored in public static field)
        v
[Telescope.AxisRates(axis) called by ASCOM client]
        |
        v
[new AxisRates(axis) constructor]
        |
        v
[Create Rate(0.0, SkyServer.SlewSpeedEight)]
        |
        v
[Return IAxisRates to ASCOM client]
```

---

## 8. Calculated Examples

### Example 1: Default Configuration

**Input:**
- `MaximumSlewRate` = 3.5 (from user.config default)

**Calculation:**
```
SlewSpeedEight = Math.Round(3.5 * 1.0, 3)
               = Math.Round(3.5, 3)
               = 3.5
```

**Result:**
- AxisRates returns: Rate(Minimum=0.0, Maximum=3.5)

### Example 2: Modified Configuration

**Input:**
- User changes `MaximumSlewRate` to 5.0

**Calculation:**
```
SlewSpeedEight = Math.Round(5.0 * 1.0, 3)
               = Math.Round(5.0, 3)
               = 5.0
```

**Result:**
- AxisRates returns: Rate(Minimum=0.0, Maximum=5.0)

### Example 3: Fractional Value

**Input:**
- User changes `MaximumSlewRate` to 4.12345

**Calculation:**
```
SlewSpeedEight = Math.Round(4.12345 * 1.0, 3)
               = Math.Round(4.12345, 3)
               = 4.123
```

**Result:**
- AxisRates returns: Rate(Minimum=0.0, Maximum=4.123)

---

## 9. All Contributing Settings

### 9.1 Direct Contributors

| Setting | Location | Type | Default | Impact |
|---------|----------|------|---------|---------|
| `MaximumSlewRate` | user.config | Double | 3.5 | Directly determines SlewSpeedEight |

### 9.2 Related Slew Speed Settings

While not directly used in `AxisRates`, these are calculated from the same `MaxSlewRate`:

| Variable | Multiplier | Purpose |
|----------|------------|---------|
| `_slewSpeedOne` | 0.0034 | HC Speed Level 1 |
| `_slewSpeedTwo` | 0.0068 | HC Speed Level 2 |
| `_slewSpeedThree` | 0.047 | HC Speed Level 3 |
| `_slewSpeedFour` | 0.068 | HC Speed Level 4 |
| `_slewSpeedFive` | 0.2 | HC Speed Level 5 |
| `_slewSpeedSix` | 0.4 | HC Speed Level 6 |
| `_slewSpeedSeven` | 0.8 | HC Speed Level 7 |
| `SlewSpeedEight` | 1.0 | HC Speed Level 8 / AxisRates Max |

---

## 10. Code Path Summary

### 10.1 Initialization Path (Application Startup)

1. **Application starts**
2. **`SkyServer` static constructor executes**
3. **`SkySettings.Load()` called**
4. **Reads `Properties.SkyTelescope.Default.MaximumSlewRate`**
   - Value loaded from user.config (default: 3.5)
5. **Assigns to `SkySettings.MaxSlewRate` property**
6. **Property setter invoked**
7. **Calls `SkyServer.SetSlewRates(value)`**
8. **Calculates and stores `SlewSpeedEight = Math.Round(value * 1.0, 3)`**
9. **`Defaults()` method called**
10. **Calls `SetSlewRates(SkySettings.MaxSlewRate)` again** (redundant but harmless)

### 10.2 Runtime Access Path (ASCOM Client Requests AxisRates)

1. **ASCOM client calls `Telescope.AxisRates(axis)`**
2. **Creates new `AxisRates` instance for specified axis**
3. **`AxisRates` constructor executes**
4. **Reads `SkyServer.SlewSpeedEight` (already initialized)**
5. **Creates `Rate(0.0, SlewSpeedEight)`**
6. **Stores in internal `_mRates` array**
7. **Returns `AxisRates` instance as `IAxisRates`**

### 10.3 Runtime Modification Path (User Changes MaxSlewRate)

1. **User modifies MaximumSlewRate setting**
2. **UI or code sets `SkySettings.MaxSlewRate = newValue`**
3. **Property setter executes**
4. **Persists to `Properties.SkyTelescope.Default.MaximumSlewRate`**
5. **Calls `SkyServer.SetSlewRates(newValue)`**
6. **Recalculates `SlewSpeedEight = Math.Round(newValue * 1.0, 3)`**
7. **All subsequent `AxisRates` calls return new maximum rate**

---

## 11. Key Observations

### 11.1 Design Patterns

1. **No Caching:** `AxisRates` creates new instances on every call rather than returning cached instances from constructor
2. **Single Rate Band:** All axes support a single continuous rate range from 0 to max
3. **Immediate Propagation:** Changes to `MaxSlewRate` immediately affect `SlewSpeedEight`
4. **Static Storage:** `SlewSpeedEight` stored as public static field, accessible globally

### 11.2 Calculation Characteristics

1. **Simple Multiplication:** `SlewSpeedEight = MaxSlewRate × 1.0`
2. **Rounding:** Rounded to 3 decimal places
3. **Consistency:** All three axes use identical rate calculation
4. **No Dynamic Adjustment:** No runtime modification based on mount type, alignment mode, or other factors

### 11.3 Configuration Characteristics

1. **Single Source:** Only `MaximumSlewRate` setting affects AxisRates
2. **User Scope:** Setting stored per-user in user.config
3. **Default Value:** 3.5 degrees per second
4. **Type:** System.Double, no range validation in settings file

---

## 12. Potential Areas for Investigation (Not Bugs)

### 12.1 Constructor vs Property Behavior

- `Telescope` constructor creates `AxisRates` instances that are never used
- `AxisRates` property creates new instances each time, not returning the pre-created ones
- May be intentional for COM interop or could be vestigial code

### 12.2 Duplicate SetSlewRates Calls

- `SetSlewRates()` called twice during initialization:
  1. From `SkySettings.MaxSlewRate` setter when loading settings
  2. From `Defaults()` method
- Second call is redundant but harmless (values already set)

### 12.3 Static Field Accessibility

- `SlewSpeedEight` is public static, allowing external modification
- No validation or bounds checking when `SetSlewRates()` is called
- No thread safety mechanisms (though modification during runtime unlikely)

### 12.4 Rate Array Structure

- Each axis has an array with a single Rate entry
- Structure suggests support for multiple rate bands was intended but not implemented
- Comment in code mentions "zero or more Rate objects"

---

## 13. Related Code Components

### 13.1 MoveAxis Rate Ranges

The AxisRates are used to validate rates passed to:
- `Telescope.MoveAxis(TelescopeAxes axis, double rate)`

The driver should validate that the requested rate falls within the range reported by AxisRates.

### 13.2 Hand Controller Speeds

The eight slew speeds are also used for hand controller movement:
- Settings stored in `SkySettings.HcSpeed` (enum SlewSpeed)
- Actual rates calculated by `SetSlewRates()` using multipliers

---

## 14. Files Reference

| File | Lines | Purpose |
|------|-------|---------|
| `Telescope.cs` | 1220-1242 | AxisRates property implementation |
| `Telescope.cs` | 63-88 | Constructor with AxisRates initialization |
| `Rates.cs` | 99-140 | AxisRates constructor |
| `Rates.cs` | 31-74 | Rate class implementation |
| `SkyServer.cs` | 56 | SlewSpeedEight field declaration |
| `SkyServer.cs` | 5795-5824 | SetSlewRates method |
| `SkyServer.cs` | 6960-7001 | Defaults method |
| `SkyServer.cs` | 121-171 | Static constructor |
| `SkySettings.cs` | 1423-1438 | MaxSlewRate property |
| `SkySettings.cs` | 2324-2329 | Load method MaxSlewRate assignment |
| `SkyTelescope.settings` | ~208 | MaximumSlewRate XML setting |

---

## 15. Conclusion

The AxisRates command returns a rate range for axis movement where:

**Maximum Rate = Math.Round(MaximumSlewRate × 1.0, 3)**

Where:
- `MaximumSlewRate` is loaded from user.config (default: 3.5 degrees/second)
- All three axes return identical rate ranges
- The minimum rate is always 0.0
- The maximum rate is always SlewSpeedEight (calculated value)
- Changes to MaximumSlewRate immediately propagate to SlewSpeedEight
- No other settings or calculations contribute to the AxisRates values

The implementation is straightforward with a clear calculation path from configuration setting to ASCOM property return value.

---

**End of Report**
