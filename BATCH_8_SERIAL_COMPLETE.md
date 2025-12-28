# Batch 8: Serial Communication Settings - COMPLETE ?

## Summary
Successfully implemented synchronization for **7 serial communication properties** with proper handling for:
- Enum/string conversions (Handshake, SerialSpeed)
- Private setter properties (reflection-based access)
- Port number/string conversions (GPS)

---

## Properties Implemented (7 total)

### 1. **Handshake** (string ? enum)
- **Old**: `SkySettings.HandShake` (System.IO.Ports.Handshake enum, **private setter**)
- **New**: `newSettings.Handshake` (string)
- **Conversion**: `ParseHandshake()` - maps "None", "XOnXOff", "RequestToSend", "RequestToSendXOnXOff"
- **Special Handling**: Uses reflection via `SetPrivateProperty()` to set private backing field

### 2. **ReadTimeout** (int, read-only)
- **Old**: `SkySettings.ReadTimeout` (int, **private setter**)
- **New**: `newSettings.ReadTimeout` (int)
- **Note**: Read-only in old system, only syncs NEW ? OLD
- **Special Handling**: Uses reflection via `SetPrivateProperty()` to set private backing field

### 3. **DataBits** (int, read-only)
- **Old**: `SkySettings.DataBits` (int, **private setter**)
- **New**: `newSettings.DataBits` (int)
- **Note**: Read-only in old system, only syncs NEW ? OLD
- **Special Handling**: Uses reflection via `SetPrivateProperty()` to set private backing field

### 4. **DTREnable** (bool, read-only)
- **Old**: `SkySettings.DtrEnable` (bool, **private setter**)
- **New**: `newSettings.DTREnable` (bool)
- **Note**: Read-only in old system, only syncs NEW ? OLD
- **Special Handling**: Uses reflection via `SetPrivateProperty()` to set private backing field

### 5. **RTSEnable** (bool, read-only)
- **Old**: `SkySettings.RtsEnable` (bool, **private setter**)
- **New**: `newSettings.RTSEnable` (bool)
- **Note**: Read-only in old system, only syncs NEW ? OLD
- **Special Handling**: Uses reflection via `SetPrivateProperty()` to set private backing field

### 6. **GpsPort** (int ? string)
- **Old**: `SkySettings.GpsComPort` (string, e.g., "COM3")
- **New**: `newSettings.GpsPort` (int, e.g., 3)
- **Conversion NEW ? OLD**: `ParseGpsPortNumber(portNumber)` - converts `3` ? `"COM3"`
- **Conversion OLD ? NEW**: `ParseGpsPortString(portString)` - converts `"COM3"` ? `3`

### 7. **GpsBaudRate** (string ? enum)
- **Old**: `SkySettings.GpsBaudRate` (SerialSpeed enum, e.g., `SerialSpeed.ps9600`)
- **New**: `newSettings.GpsBaudRate` (string, e.g., "9600")
- **Conversion NEW ? OLD**: `ParseSerialSpeed(ParseGpsBaudRateString(baudRateString))`
- **Conversion OLD ? NEW**: `((int)SkySettings.GpsBaudRate).ToString()`

---

## Key Implementation Details

### Private Property Handling
Some properties have private setters in the old system and can only be set during initialization:
```csharp
private static void SetPrivateProperty(Type type, string propertyName, object value)
{
    var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);
    if (property != null)
    {
        var backingField = type.GetField($"_{char.ToLower(propertyName[0])}{propertyName.Substring(1)}", 
            BindingFlags.NonPublic | BindingFlags.Static);
        
        if (backingField != null)
        {
            backingField.SetValue(null, value);
        }
    }
}
```

### Handshake Enum Mapping
```csharp
private static Handshake ParseHandshake(string value)
{
    return Enum.TryParse<Handshake>(value, true, out var result) 
        ? result 
        : Handshake.None;
}
```

Maps to `System.IO.Ports.Handshake`:
- "None" ? `Handshake.None`
- "XOnXOff" ? `Handshake.XOnXOff`
- "RequestToSend" ? `Handshake.RequestToSend`
- "RequestToSendXOnXOff" ? `Handshake.RequestToSendXOnXOff`

### GPS Port Conversion
```csharp
// NEW ? OLD: Convert int to COM port string
private static string ParseGpsPortNumber(int portNumber)
{
    return portNumber > 0 ? $"COM{portNumber}" : string.Empty;
}

// OLD ? NEW: Convert COM port string to int
private static int ParseGpsPortString(string portString)
{
    if (string.IsNullOrEmpty(portString)) return 0;
    var cleaned = portString.Replace("COM", "", StringComparison.OrdinalIgnoreCase).Trim();
    return int.TryParse(cleaned, out var portNum) ? portNum : 0;
}
```

### Serial Speed (Baud Rate) Mapping
```csharp
private static SerialSpeed ParseSerialSpeed(int value)
{
    return value switch
    {
        300 => SerialSpeed.ps300,
        1200 => SerialSpeed.ps1200,
        2400 => SerialSpeed.ps2400,
        4800 => SerialSpeed.ps4800,
        9600 => SerialSpeed.ps9600,
        14400 => SerialSpeed.ps14400,
        19200 => SerialSpeed.ps19200,
        28800 => SerialSpeed.ps28800,
        38400 => SerialSpeed.ps38400,
        57600 => SerialSpeed.ps57600,
        115200 => SerialSpeed.ps115200,
        230400 => SerialSpeed.ps230400,
        _ => SerialSpeed.ps9600  // Default
    };
}
```

---

## Synchronization Flow

### NEW ? OLD (SyncNewToOld)
```csharp
// Phase 4 Batch 8: Serial Communication Settings
try 
{
    var handshakeValue = ParseHandshake(newSettings.Handshake);
    SetPrivateProperty(typeof(SkySettings), nameof(SkySettings.HandShake), handshakeValue);
    SetPrivateProperty(typeof(SkySettings), nameof(SkySettings.ReadTimeout), newSettings.ReadTimeout);
    SetPrivateProperty(typeof(SkySettings), nameof(SkySettings.DataBits), newSettings.DataBits);
    SetPrivateProperty(typeof(SkySettings), nameof(SkySettings.DtrEnable), newSettings.DTREnable);
    SetPrivateProperty(typeof(SkySettings), nameof(SkySettings.RtsEnable), newSettings.RTSEnable);
}
catch (Exception ex)
{
    LogBridge($"Warning: Could not sync read-only serial properties: {ex.Message}");
}

SkySettings.GpsComPort = ParseGpsPortNumber(newSettings.GpsPort);
SkySettings.GpsBaudRate = ParseSerialSpeed(ParseGpsBaudRateString(newSettings.GpsBaudRate));
```

### OLD ? NEW (SyncOldToNew)
```csharp
// Phase 4 Batch 8: Serial Communication Settings
// Read-only properties are NOT synced back (they shouldn't change)
newSettings.GpsPort = ParseGpsPortString(SkySettings.GpsComPort);
newSettings.GpsBaudRate = ((int)SkySettings.GpsBaudRate).ToString();

// The following are read-only in old system, so not synced from old ? new:
// HandShake, ReadTimeout, DataBits, DTREnable, RTSEnable
```

**Important**: Properties with private setters can only be synchronized NEW ? OLD, not OLD ? NEW. This is by design since these properties are typically set once during initialization and shouldn't change at runtime.

---

## Keys Added to Constants
```csharp
// Phase 4 Batch 8: Serial Communication Settings
public const string Handshake = "Handshake";
public const string ReadTimeout = "ReadTimeout";
public const string DataBits = "DataBits";
public const string DtrEnable = "DtrEnable";
public const string RtsEnable = "RtsEnable";
public const string GpsComPort = "GpsComPort";
public const string GpsBaudRate = "GpsBaudRate";
```

---

## Testing Checklist

### ? Verify Enum Conversions
- [ ] Handshake: "None" correctly maps to `Handshake.None`
- [ ] Handshake: "XOnXOff" correctly maps to `Handshake.XOnXOff`
- [ ] Handshake: "RequestToSend" correctly maps to `Handshake.RequestToSend`
- [ ] Serial Speed: 9600 correctly maps to `SerialSpeed.ps9600`
- [ ] Serial Speed: 115200 correctly maps to `SerialSpeed.ps115200`

### ? Verify Port Conversions
- [ ] GPS Port: Integer 3 converts to string "COM3"
- [ ] GPS Port: String "COM3" converts to integer 3
- [ ] GPS Port: Empty string converts to 0
- [ ] GPS Port: 0 converts to empty string

### ? Verify Private Property Access
- [ ] ReadTimeout can be set via reflection
- [ ] DataBits can be set via reflection
- [ ] DTREnable can be set via reflection
- [ ] RTSEnable can be set via reflection
- [ ] Handshake can be set via reflection

### ? Verify Bidirectional Sync
- [ ] GPS settings sync from new ? old
- [ ] GPS settings sync from old ? new
- [ ] Read-only properties only sync new ? old (as expected)

### ? Verify Error Handling
- [ ] Invalid handshake string defaults to "None"
- [ ] Invalid baud rate defaults to 9600
- [ ] Invalid GPS port string returns 0
- [ ] Reflection failures are caught and logged

---

## Known Limitations

1. **Read-Only Properties**: `ReadTimeout`, `DataBits`, `DTREnable`, `RTSEnable`, and `HandShake` have private setters in the old system. They are synchronized NEW ? OLD using reflection, but NOT synchronized OLD ? NEW since they're not intended to be modified after initialization.

2. **Reflection Risks**: Using reflection to set private fields bypasses encapsulation. This is necessary for the migration but should be monitored for issues.

3. **GPS Port 0 Handling**: An integer port of `0` is treated as "not set" and converts to an empty string.

---

## Build Status
? **Build Successful** - No errors or warnings

---

## Progress Summary
- **Total Properties Synced**: 85 (8 Phase 2 + 18 Phase 3 + 59 Phase 4)
- **Phase 4 Batches Complete**: 8 of ~10-12
- **Properties Remaining**: ~15-17 (including complex objects/arrays)

---

## Next Batch Suggestions

### **Batch 9: UI & Display Settings (3 properties)**
- FrontGraphic (string ? enum)
- RaGaugeFlip (bool)
- TraceLogger (bool - new system only)

### **Batch 10: Mount Behavior Settings (6 properties)**
- DisconnectOnPark (bool - new system only)
- AutoTrack (bool - read-only in old)
- ModelType (string)
- VersionOne (bool - read-only in old)
- NumMoveAxis (int - read-only in old)
- Pressure (double - new system only)

### **Batch 11: Tracking Offsets & Remaining Limits (5 properties)**
- RATrackingOffset (int - casing difference, read-only)
- CustomRaTrackingOffset (int)
- CustomDecTrackingOffset (int)
- AxisTrackingLimit (double)
- SyncLimit (int - read-only)

### **Batch 12: Cartes du Ciel Integration (2 properties)**
- CdCip (string)
- CdCport (int)

---

## Commit Message Suggestion
```
Phase 4 Batch 8: Serial communication settings synchronized (7 properties)

- Added Handshake (enum/string conversion with reflection for private setter)
- Added ReadTimeout (read-only, reflection-based sync)
- Added DataBits (read-only, reflection-based sync)
- Added DTREnable (read-only, reflection-based sync)
- Added RTSEnable (read-only, reflection-based sync)
- Added GpsPort (int/string port number conversion)
- Added GpsBaudRate (SerialSpeed enum/string conversion)
- Implemented SetPrivateProperty() for read-only property access
- Added ParseHandshake(), ParseSerialSpeed() converters
- Added ParseGpsPortNumber()/ParseGpsPortString() converters

Total properties synced: 85/~100
Phase 4 Batch 8: COMPLETE ?
```

---

**Status**: Ready for testing and commit ?
