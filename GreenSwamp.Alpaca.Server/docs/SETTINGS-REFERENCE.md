# GreenSwamp Alpaca — Settings Reference

**2026-05-08 08:13**

This document describes every application setting, its default value, and which mount types it applies to. Settings are persisted as JSON files in the versioned AppData folder (`%AppData%\GreenSwampAlpaca\{version}\`).

---

## Table of Contents

1. [Observatory Settings](#1-observatory-settings)
2. [Server Configuration](#2-server-configuration)
3. [Monitor / Logging Settings](#3-monitor--logging-settings)
4. [Telescope Device Settings](#4-telescope-device-settings)
   - [4.1 Common Settings (all mount types)](#41-common-settings-all-mount-types)
   - [4.2 Mount-Type-Specific Settings](#42-mount-type-specific-settings)
5. [Capability Flags](#5-capability-flags)

---

## 1. Observatory Settings

Persisted in `observatory.settings.json`. These are the shared physical location properties used as defaults when a new telescope device is created.

| Setting | Type | Default | Display | Group | Description |
|---------|------|---------|---------|-------|-------------|
| `Latitude` | double | `51.21135` | Yes | Observatory | Observatory latitude in decimal degrees. Range: −90 to +90. |
| `Longitude` | double | `-1.459816` | Yes | Observatory | Observatory longitude in decimal degrees. Range: −180 to +180. |
| `Elevation` | double | `10.0` | Yes | Observatory | Observatory elevation above sea level in metres. Range: −500 to +9000. |
| `UTCOffset` | TimeSpan | `00:00:00` | No | Observatory | UTC offset for local time display (e.g. `01:00:00` for UTC+1). |

---

## 2. Server Configuration

Persisted in `appsettings.server.user.json`. Controls the Alpaca HTTP server behaviour and security.

### Network

| Setting | Type | Default | Display | Group | Description |
|---------|------|---------|---------|-------|-------------|
| `ServerPort` | ushort | `11111` | Yes | Network | TCP port the Alpaca server listens on. |
| `AllowRemoteAccess` | bool | `true` | Yes | Network | Bind to all network interfaces; when `false`, binds to localhost only. |
| `LocalRespondOnlyToLocalHost` | bool | `true` | Yes | Network | Respond to localhost-sourced Alpaca UDP discovery on the loopback adapter only. |
| `AllowDiscovery ` | bool | `true` | Yes | Network | Advertise this server via Alpaca UDP discovery. |

### Alpaca Behaviour

| Setting | Type | Default | Display | Group | Description |
|---------|------|---------|---------|-------|-------------|
| `RunInStrictAlpacaMode` | bool | `true` | Yes | Alpaca Behaviour | Reject non-compliant Alpaca requests. |
| `PreventRemoteDisconnects` | bool | `false` | Yes | Alpaca Behaviour | Prevent remote clients from disconnecting devices. |
| `AllowImageBytesDownload` | bool | `true` | No | Alpaca Behaviour | Allow the image-bytes binary download endpoint. |

### Identity & UI

| Setting | Type | Default | Display | Group | Description |
|---------|------|---------|---------|-------|-------------|
| `Location` | string | `"Unknown"` | Yes | Identity & UI | Human-readable location shown in discovery and the setup page. |
| `AutoStartBrowser` | bool | `true` | Yes | Identity & UI | Open the default browser automatically when the server starts. |
| `RunSwagger` | bool | `true` | Yes | Identity & UI | Expose the OpenAPI / Swagger UI at `/swagger`. |

### Authentication

| Setting | Type | Default | Display | Group | Description |
|---------|------|---------|---------|-------|-------------|
| `UseAuth` | bool | `false` | Yes | Authentication | Require HTTP Basic / Cookie authentication. |
| `UserName` | string | `"User"` | Yes | Authentication | Login user name (only used when `UseAuth` is `true`). |
| `Password` | string | `""` | Yes | Authentication | Hashed password (produced by `Hash.GetStoragePassword()`). Never store plain text. |

---

## 3. Monitor / Logging Settings

Persisted within the main settings file under `MonitorSettings`. Controls what is captured in the real-time monitor window and log files.

### Device Filters

| Setting | Type | Default | Display | Group | Description |
|---------|------|---------|---------|-------|-------------|
| `ServerDevice` | bool | `true` | Yes | Device Filters | Include server device entries in the monitor. |
| `Telescope` | bool | `true` | Yes | Device Filters | Include telescope device entries in the monitor. |
| `Ui` | bool | `false` | Yes | Device Filters | Include UI device entries in the monitor. |

### Category Filters

| Setting | Type | Default | Display | Group | Description |
|---------|------|---------|---------|-------|-------------|
| `Other` | bool | `false` | Yes | Category Filters | Include entries from support/shared projects. |
| `Driver` | bool | `true` | Yes | Category Filters | Include simulator and SkyWatcher driver entries. |
| `Interface` | bool | `true` | Yes | Category Filters | Include interface layer entries. |
| `Server` | bool | `true` | Yes | Category Filters | Include core server process entries. |
| `Mount` | bool | `true` | Yes | Category Filters | Include mount command entries. |
| `Alignment` | bool | `false` | Yes | Category Filters | Include alignment entries. |

### Message Type Filters

| Setting | Type | Default | Display | Group | Description |
|---------|------|---------|---------|-------|-------------|
| `Information` | bool | `true` | Yes | Message Type Filters | Include informational messages (also written to session log). |
| `Data` | bool | `false` | Yes | Message Type Filters | Include raw data messages. |
| `Warning` | bool | `true` | Yes | Message Type Filters | Include warnings (also written to session log). |
| `Error` | bool | `true` | Yes | Message Type Filters | Include errors (written to error log and session log). |
| `Debug` | bool | `false` | Yes | Message Type Filters | Include debug/troubleshooting messages. |

### Logging Options

| Setting | Type | Default | Display | Group | Description |
|---------|------|---------|---------|-------|-------------|
| `LogMonitor` | bool | `false` | Yes | Logging Options | Write monitor entries to `GSMonitorLog` file. Requires `StartMonitor = true`. |
| `LogSession` | bool | `true` | Yes | Logging Options | Write session entries (Information/Warning/Error) to `GSSessionLog` file. |
| `LogCharting` | bool | `false` | Yes | Logging Options | Write charting data to a log file. |
| `StartMonitor` | bool | `true` | Yes | Logging Options | Start the monitor automatically and enable file logging. |
| `Language` | string | `"en-US"` | Yes | Logging Options | UI language / locale code. |
| `LogPath` | string | `""` | Yes | Logging Options | Custom log file path; empty string uses the default AppData path. |

---

## 4. Telescope Device Settings

Each telescope device has its own settings file (`device-{n}.settings.json`). When a new device is created its settings are initialised from the `DeviceTemplates` section of `appsettings.json` using the template that matches the device's `AlignmentMode`.

### 4.1 Common Settings (all mount types)

These settings apply equally to **GermanPolar (GEM)**, **Polar**, and **AltAz** mounts.

#### Device Identity

| Setting | Type | Default | Display | Group | Description |
|---------|------|---------|---------|-------|-------------|
| `DeviceNumber` | int | `0` | Yes | Device Identity | Zero-based ASCOM device index. |
| `DeviceName` | string | `"Telescope"` | Yes | Device Identity | Human-readable device name. |
| `Enabled` | bool | `true` | Yes | Device Identity | Whether this device is active and served by the Alpaca server. |
| `InstrumentName` | string | `"Simulator"` | Yes | Device Identity | Short instrument name returned by the ASCOM `Name` property. |
| `InstrumentDescription` | string | `"GreenSwamp ASCOM Alpaca Telescope Simulator"` | Yes | Device Identity | Full instrument description returned by `Description`. |

#### Serial Connection

| Setting | Type | Default | Display | Group | Description |
|---------|------|---------|---------|-------|-------------|
| `Mount` | string | `"Simulator"` | Yes | Serial Connection | Mount hardware type (e.g. `Simulator`, `SkyWatcher`). |
| `Port` | string | `"COM1"` | Yes | Serial Connection | Serial port name. |
| `BaudRate` | int | `9600` | Yes | Serial Connection | Serial baud rate. |
| `DataBits` | int | `8` | No | Serial Connection | Number of serial data bits. |
| `Handshake` | string | `"None"` | No | Serial Connection | Serial handshake mode (`None`, `XOnXOff`, `RequestToSend`, `RequestToSendXOnXOff`). |
| `ReadTimeout` | int | `1000` | No | Serial Connection | Serial read timeout in milliseconds. |
| `DTREnable` | bool | `false` | No | Serial Connection | Enable the Data Terminal Ready (DTR) serial signal. |
| `RTSEnable` | bool | `false` | No | Serial Connection | Enable the Request to Send (RTS) serial signal. |

#### Location

| Setting | Type | Default | Display | Group | Description |
|---------|------|---------|---------|-------|-------------|
| `Latitude` | double | `51.21135` | Yes | Location | Site latitude in decimal degrees (−90 to +90). Seeded from Observatory Settings. |
| `Longitude` | double | `-1.459816` | Yes | Location | Site longitude in decimal degrees (−180 to +180). Seeded from Observatory Settings. |
| `Elevation` | double | `10.0` | Yes | Location | Site elevation in metres (−500 to +9000). Seeded from Observatory Settings. |
| `UTCOffset` | TimeSpan | `00:00:00` | Yes | Location | UTC offset for local time display. |

#### Optics

| Setting | Type | Default | Display | Group | Description |
|---------|------|---------|---------|-------|-------------|
| `ApertureDiameter` | double | `0.2` | No | Optics | Telescope aperture diameter in metres. |
| `ApertureArea` | double | `0.0314` | No | Optics | Effective aperture area in square metres. |
| `FocalLength` | double | `1.26` | No | Optics | Telescope focal length in metres. |
| `EyepieceFS` | double | `0.0` | No | Optics | Eyepiece field size in degrees (0 = not set). |

#### Environmental

| Setting | Type | Default | Display | Group | Description |
|---------|------|---------|---------|-------|-------------|
| `Refraction` | bool | `true` | Yes | Environmental | Apply atmospheric refraction correction to coordinates. |
| `Temperature` | double | `20.0` | Yes | Environmental | Ambient temperature in °C used for refraction calculation. |

#### Coordinate System

| Setting | Type | Default | Display | Group | Description |
|---------|------|---------|---------|-------|-------------|
| `EquatorialCoordinateType` | string | `"Topocentric"` | Yes | Coordinate System | Equatorial coordinate epoch (`Topocentric`, `J2000`, `Other`). |

#### Tracking

| Setting | Type | Default | Display | Group | Description |
|---------|------|---------|---------|-------|-------------|
| `AutoTrack` | bool | `false` | Yes | Tracking | Start tracking automatically after unpark. |
| `TrackingRate` | string | `"Sidereal"` | Yes | Tracking | Default tracking rate (`Sidereal`, `Lunar`, `Solar`, `King`). |
| `SiderealRate` | double | `15.0410671787` | Yes | Tracking | Sidereal tracking rate in arc-seconds per second. |
| `LunarRate` | double | `14.685` | Yes | Tracking | Lunar tracking rate in arc-seconds per second. |
| `SolarRate` | double | `15.0` | Yes | Tracking | Solar tracking rate in arc-seconds per second. |
| `KingRate` | double | `15.0369` | Yes | Tracking | King tracking rate in arc-seconds per second. |
| `RATrackingOffset` | int | `0` | No | Tracking | Manual RA tracking rate offset in steps. |
| `AltAzTrackingUpdateInterval` | int | `2500` | No | Tracking | Interval in milliseconds between AltAz tracking position updates. |

#### Custom Gearing

| Setting | Type | Default | Display | Group | Description |
|---------|------|---------|---------|-------|-------------|
| `CustomGearing` | bool | `false` | Yes | Custom Gearing | Use custom gear ratios instead of mount defaults. |
| `CustomRa360Steps` | int | `0` | Yes | Custom Gearing | Custom steps per full RA revolution (0 = use mount default). |
| `CustomRaWormTeeth` | int | `0` | Yes | Custom Gearing | Custom RA worm gear tooth count. |
| `CustomDec360Steps` | int | `0` | Yes | Custom Gearing | Custom steps per full Dec revolution. |
| `CustomDecWormTeeth` | int | `0` | Yes | Custom Gearing | Custom Dec worm gear tooth count. |
| `CustomRaTrackingOffset` | int | `0` | Yes | Custom Gearing | Additional RA tracking step offset for custom gearing. |
| `CustomDecTrackingOffset` | int | `0` | Yes | Custom Gearing | Additional Dec tracking step offset for custom gearing. |

#### Backlash

| Setting | Type | Default | Display | Group | Description |
|---------|------|---------|---------|-------|-------------|
| `RaBacklash` | int | `0` | Yes | Backlash | RA axis backlash compensation in steps. |
| `DecBacklash` | int | `0` | Yes | Backlash | Dec axis backlash compensation in steps. |

#### Pulse Guiding

| Setting | Type | Default | Display | Group | Description |
|---------|------|---------|---------|-------|-------------|
| `MinPulseRa` | int | `20` | Yes | Pulse Guiding | Minimum RA pulse guide duration in milliseconds. |
| `MinPulseDec` | int | `20` | Yes | Pulse Guiding | Minimum Dec pulse guide duration in milliseconds. |
| `DecPulseToGoTo` | bool | `false` | Yes | Pulse Guiding | Implement Dec pulse guides as short GoTo moves. |
| `St4Guiderate` | int | `2` | Yes | Pulse Guiding | ST4 guide rate index (1=0.25×, 2=0.5×, 3=0.75×, 4=1×). |
| `GuideRateOffsetX` | double | `0.5` | Yes | Pulse Guiding | RA guide rate as a fraction of the sidereal rate. |
| `GuideRateOffsetY` | double | `0.5` | Yes | Pulse Guiding | Dec guide rate as a fraction of the sidereal rate. |
| `HcPulseGuides` | list | `[]` | Yes | Pulse Guiding | Named hand-controller pulse guide presets (`Speed`, `Duration`, `Interval`, `Rate`). |

#### Sync Limits

| Setting | Type | Default | Display | Group | Description |
|---------|------|---------|---------|-------|-------------|
| `SyncLimitOn` | bool | `false` | Yes | Sync Limits | Enable maximum allowable sync offset enforcement. |
| `SyncLimit` | int | `0` | Yes | Sync Limits | Maximum allowed sync offset in arc-minutes (0 = no limit). |

#### PEC / PPEC

| Setting | Type | Default | Display | Group | Description |
|---------|------|---------|---------|-------|-------------|
| `PecOn` | bool | `false` | No | PEC / PPEC | Enable Periodic Error Correction playback. |
| `PpecOn` | bool | `false` | No | PEC / PPEC | Enable Permanent Periodic Error Correction. |
| `AlternatingPPEC` | bool | `false` | No | PEC / PPEC | Use alternating PPEC algorithm. |
| `PecOffSet` | int | `0` | No | PEC / PPEC | PEC phase offset in steps. |
| `PecMode` | string | `""` | No | PEC / PPEC | PEC mode selection (`PecWorm`, `Pec360`, or empty). |
| `PecWormFile` | string | `""` | No | PEC / PPEC | Path to the worm-period PEC data file. |
| `Pec360File` | string | `""` | No | PEC / PPEC | Path to the full-revolution PEC data file. |
| `PolarLedLevel` | int | `8` | No | PEC / PPEC | Polar scope LED brightness level (0–15). |

#### Encoders

| Setting | Type | Default | Display | Group | Description |
|---------|------|---------|---------|-------|-------------|
| `EncodersOn` | bool | `false` | Yes | Encoders | Enable absolute encoder feedback (hardware support required). |

#### Hand Controller

| Setting | Type | Default | Display | Group | Description |
|---------|------|---------|---------|-------|-------------|
| `HcSpeed` | string | `"8"` | No | Hand Controller | Hand controller slew speed index (1–8). |
| `HcMode` | string | `"Guiding"` | No | Hand Controller | Hand controller mode (`Guiding`, `Slewing`). |
| `HcAntiRa` | bool | `false` | No | Hand Controller | Reverse RA hand-controller direction buttons. |
| `HcAntiDec` | bool | `false` | No | Hand Controller | Reverse Dec hand-controller direction buttons. |
| `HcFlipEW` | bool | `false` | No | Hand Controller | Swap East/West hand-controller buttons. |
| `HcFlipNS` | bool | `false` | No | Hand Controller | Swap North/South hand-controller buttons. |
| `DisableKeysOnGoTo` | bool | `false` | No | Hand Controller | Disable hand-controller keys while a GoTo slew is in progress. |

#### GPS

| Setting | Type | Default | Display | Group | Description |
|---------|------|---------|---------|-------|-------------|
| `GpsPort` | int | `0` | No | GPS | COM port number for GPS receiver (0 = disabled). |
| `GpsBaudRate` | string | `"9600"` | No | GPS | Baud rate for GPS serial connection. |

#### Performance & Display

| Setting | Type | Default | Display | Group | Description |
|---------|------|---------|---------|-------|-------------|
| `MaximumSlewRate` | double | `8.0` | Yes | Performance & Display | Maximum slew rate in degrees per second. |
| `GotoPrecision` | double | `0.001` | No | Performance & Display | GoTo completion threshold in degrees. |
| `FullCurrent` | bool | `false` | Yes | Performance & Display | Run motors at full current (higher torque, higher heat). |
| `NumMoveAxis` | int | `2` | Yes | Performance & Display | Number of axes exposed by `MoveAxis`. |
| `DisplayInterval` | int | `200` | Yes | Performance & Display | UI display refresh interval in milliseconds. |
| `TraceLogger` | bool | `false` | Yes | Performance & Display | Enable ASCOM trace logging to file. |
| `AllowAdvancedCommandSet` | bool | `true` | Yes | Performance & Display | Allow extended hardware command set beyond the standard Alpaca protocol. |

---

### 4.2 Mount-Type-Specific Settings

The settings below differ by `AlignmentMode`. Values are read from the matching `DeviceTemplates` template in `appsettings.json` and written to each device's settings file at creation time.

#### Alignment Mode

| Setting | Applies To | Display | Group | Description |
|---------|-----------|---------|-------|-------------|
| `AlignmentMode` | **All** (distinguishes type) | Yes | Alignment Mode | `GermanPolar` (GEM), `Polar`, or `AltAz`. |

---

#### Home Position

| Setting | GEM default | Polar default | AltAz default | Display | Group | Description |
|---------|-------------|---------------|---------------|---------|-------|-------------|
| `HomeAxisX` | `90.0` | `180.0` | `0.0` | Yes | Home Position | Axis 1 (RA/Az) home position in degrees. |
| `HomeAxisY` | `90.0` | `5.0` | `0.0` | Yes | Home Position | Axis 2 (Dec/Alt) home position in degrees. |
| `AutoHomeAxisX` | `90.0` | `90.0` | `0.0` | Yes | Home Position | Axis 1 target angle for the auto-home routine. |
| `AutoHomeAxisY` | `90.0` | `90.0` | `0.0` | Yes | Home Position | Axis 2 target angle for the auto-home routine. |

---

#### Park Positions

| Setting | GEM default | Polar default | AltAz default | Display | Group | Description |
|---------|-------------|---------------|---------------|---------|-------|-------------|
| `AtPark` | `false` | `false` | `false` | Yes | Park Positions | Whether the mount is currently parked. Saved on park/unpark. |
| `ParkName` | `"Default"` | `"Default"` | `"Default"` | Yes | Park Positions | Name of the currently selected park position. |
| `ParkAxes` | `[90.0, 90.0]` | `[180.0, 0.0]` | `[0.0, 0.0]` | Yes | Park Positions | Axis 1/2 angles (degrees) for the current park position. |
| `ParkPositions` | `[{Default:90,90},{Home:90,90}]` | `[{Default:180,0},{Home:180,5}]` | `[{Default:0,0},{Home:0,0}]` | Yes | Park Positions | Named park position list. Each entry has `Name`, `X`, `Y`. |
| `LimitPark` | `false` | `false` | `false` | Yes | Park Positions | Park the mount when the meridian / axis limit is reached. |
| `ParkLimitName` | `"Default"` | `"Default"` | `"Default"` | Yes | Park Positions | Park position name used for limit parking. |

---

#### Axis / Slew Limits

| Setting | GEM default | Polar default | AltAz default | Display | Group | Description |
|---------|-------------|---------------|---------------|---------|-------|-------------|
| `AxisLimitX` | `180.0` | `115.0` | `210.0` | Yes | Axis / Slew Limits | Maximum Axis 1 travel in degrees (one side). |
| `AxisUpperLimitY` | `180.0` | `90.0` | `80.0` | Yes | Axis / Slew Limits | Maximum Axis 2 angle in degrees. |
| `AxisLowerLimitY` | `-180.0` | `-90.0` | `-10.0` | Yes | Axis / Slew Limits | Minimum Axis 2 angle in degrees. |
| `AxisTrackingLimit` | `0.0` | `0.0` | `0.0` | Yes | Axis / Slew Limits | Axis 1 position at which tracking is stopped (0 = disabled). |
| `LimitTracking` | `false` | `false` | `false` | Yes | Axis / Slew Limits | Stop tracking when `AxisTrackingLimit` is reached. |

---

#### Meridian / Hour Angle Limit (GEM & Polar only)

| Setting | GEM default | Polar default | AltAz | Display | Group | Description |
|---------|-------------|---------------|-------|---------|-------|-------------|
| `HourAngleLimit` | `15.0` | `0.0` | N/A | Yes | Meridian / Hour Angle Limit | Hour-angle past meridian at which a flip is triggered (degrees). |
| `NoSyncPastMeridian` | `false` | `false` | N/A | Yes | Meridian / Hour Angle Limit | Refuse sync commands when the target is past the meridian. |

---

#### Horizontal Axis Limit (AltAz only)

| Setting | GEM | Polar | AltAz default | Display | Group | Description |
|---------|-----|-------|---------------|---------|-------|-------------|
| `HzLimitTracking` | `false` | `false` | `false` | Yes | Horizontal Axis Limit | Stop tracking when the horizontal axis limit is reached. |
| `HzLimitPark` | `false` | `false` | `false` | Yes | Horizontal Axis Limit | Park the mount when the horizontal axis limit is reached. |
| `AxisHzTrackingLimit` | `0.0` | `0.0` | `0.0` | Yes | Horizontal Axis Limit | Horizontal axis limit position in degrees (0 = disabled). |
| `ParkHzLimitName` | `"Default"` | `"Default"` | `"Default"` | Yes | Horizontal Axis Limit | Park position name used when the horizontal limit parks the mount. |

---

#### Pier Side (GEM only)

| Setting | GEM default | Polar | AltAz | Display | Group | Description |
|---------|-------------|-------|-------|---------|-------|-------------|
| `CanSetPierSide` | `true` | `true` | `false` | No | Pier Side | Whether the client may command a pier-side flip. AltAz mounts set this `false`. |
| `PolarMode` | `"Right"` | `"Left"` | `"Right"` | Yes | Pier Side | Default pointing side for a polar/GEM mount (`Left` or `Right`). |

---

## 5. Capability Flags

These flags are set per-template in `appsettings.json` and declare which ASCOM Alpaca features the device supports. They are read-only at runtime — they are set by the device template during creation and must not be changed manually unless you are defining a custom device profile.

| Flag | GEM | Polar | AltAz | Display | Group | Description |
|------|-----|-------|-------|---------|-------|-------------|
| `CanAlignMode` | `true` | `true` | `true` | No | Capability Flags | Supports `AlignmentMode` property. |
| `CanAltAz` | `true` | `true` | `true` | No | Capability Flags | Can return Altitude/Azimuth coordinates. |
| `CanDoesRefraction` | `true` | `true` | `true` | No | Capability Flags | Can apply atmospheric refraction. |
| `CanEquatorial` | `true` | `true` | `true` | No | Capability Flags | Can return equatorial (RA/Dec) coordinates. |
| `CanFindHome` | `true` | `true` | `true` | No | Capability Flags | Supports the `FindHome` command. |
| `CanLatLongElev` | `true` | `true` | `true` | No | Capability Flags | Site latitude/longitude/elevation are readable/settable. |
| `CanOptics` | `true` | `true` | `true` | No | Capability Flags | Supports optics properties (focal length, aperture). |
| `CanPark` | `true` | `true` | `true` | No | Capability Flags | Supports `Park` / `Unpark`. |
| `CanPierSide` | `true` | `true` | `true` | No | Capability Flags | Can report the current pier side. |
| `CanPulseGuide` | `true` | `true` | `true` | No | Capability Flags | Supports `PulseGuide`. |
| `CanSetDeclinationRate` | `true` | `true` | `true` | No | Capability Flags | Dec offset tracking rate is settable. |
| `CanSetEquRates` | `true` | `true` | `true` | No | Capability Flags | Equatorial tracking offset rates are settable. |
| `CanSetGuideRates` | `true` | `true` | `true` | No | Capability Flags | Guide rates are settable. |
| `CanSetPark` | `true` | `true` | `true` | No | Capability Flags | Park position is settable by the client. |
| `CanSetPierSide` | `true` | `true` | **`false`** | No | Capability Flags | Client can command a pier-side flip. AltAz does not support this. |
| `CanSetRightAscensionRate` | `true` | `true` | `true` | No | Capability Flags | RA offset tracking rate is settable. |
| `CanSetTracking` | `true` | `true` | `true` | No | Capability Flags | Tracking on/off is controllable by the client. |
| `CanSiderealTime` | `true` | `true` | `true` | No | Capability Flags | Local sidereal time is available. |
| `CanSlew` | `true` | `true` | `true` | No | Capability Flags | Supports synchronous `SlewToCoordinates`. |
| `CanSlewAltAz` | `true` | `true` | `true` | No | Capability Flags | Supports slewing to AltAz coordinates. |
| `CanSlewAltAzAsync` | `true` | `true` | `true` | No | Capability Flags | Supports asynchronous slewing to AltAz coordinates. |
| `CanSlewAsync` | `true` | `true` | `true` | No | Capability Flags | Supports asynchronous `SlewToCoordinatesAsync`. |
| `CanSync` | `true` | `true` | `true` | No | Capability Flags | Supports `SyncToCoordinates`. |
| `CanSyncAltAz` | `true` | `true` | `true` | No | Capability Flags | Supports sync to AltAz coordinates. |
| `CanTrackingRates` | `true` | `true` | `true` | No | Capability Flags | Multiple tracking rates are supported. |
| `CanUnpark` | `true` | `true` | `true` | No | Capability Flags | Supports `Unpark`. |

---

*Generated from source: `GreenSwamp.Alpaca.Settings\Models\SkySettings.cs`, `ObservatorySettings.cs`, `MonitorSettings.cs`, `ServerConfig.cs` and `GreenSwamp.Alpaca.Server\appsettings.json`.*
