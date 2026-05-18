# GreenSwamp Alpaca — REST API Reference

**Updated:** 2026-05-18 16:43  
**Base URL:** `http://<host>:<port>` (default port **31416**)  
**Content-Type:** All request and response bodies are `application/json` unless noted.  
**Auth:** Endpoints marked *🔒 optional auth* respect the `UseAuth` server setting — when auth is enabled a valid session cookie is required. All endpoints listed as `AllowAnonymous` in the source are accessible without authentication regardless of that setting.

---

## Table of Contents

1. [ASCOM Alpaca Management API](#1-ascom-alpaca-management-api)
2. [ASCOM Alpaca Telescope Device API — Common](#2-ascom-alpaca-telescope-device-api--common)
3. [ASCOM Alpaca Telescope Device API — Telescope Specific](#3-ascom-alpaca-telescope-device-api--telescope-specific)
4. [Device Management API](#4-device-management-api)
5. [Configuration API](#5-configuration-api)
   - 5.1 [Monitor Settings](#51-monitor-settings)
   - 5.2 [Server Configuration](#52-server-configuration)
   - 5.3 [Observatory Settings](#53-observatory-settings)
   - 5.4 [Alpaca Device Discovery Entries](#54-alpaca-device-discovery-entries)
   - 5.5 [Per-Device Operational Settings](#55-per-device-operational-settings)
6. [Authentication](#6-authentication)
7. [Error Responses](#7-error-responses)
8. [ASCOM Common Response Envelope](#8-ascom-common-response-envelope)

---

## 1. ASCOM Alpaca Management API

These endpoints follow the [ASCOM Alpaca Management API v1 specification](https://ascom-standards.org/api/).  
Base path: `/management`

### Common query parameters (all management endpoints)

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `ClientID` | uint32 | 0 | Client's unique identifier |
| `ClientTransactionID` | uint32 | 0 | Client's transaction ID (echoed back in response) |

---

### `GET /management/apiversions`

Returns the list of supported Alpaca API version numbers.

**Response `200`**

```json
{
  "Value": [1],
  "ClientTransactionID": 0,
  "ServerTransactionID": 1,
  "ErrorNumber": 0,
  "ErrorMessage": ""
}
```

| Field | Type | Description |
|-------|------|-------------|
| `Value` | `int[]` | Supported API versions (currently `[1]`) |

---

### `GET /management/v1/description`

Returns summary information about this Alpaca server.

**Response `200`**

```json
{
  "Value": {
	"ServerName": "GreenSwamp Alpaca",
	"Manufacturer": "GreenSwamp",
	"ManufacturerVersion": "1.0.0",
	"Location": ""
  },
  "ClientTransactionID": 0,
  "ServerTransactionID": 2,
  "ErrorNumber": 0,
  "ErrorMessage": ""
}
```

---

### `GET /management/v1/configureddevices`

Returns all devices served by this Alpaca server (used by discovery clients).

**Response `200`**

```json
{
  "Value": [
	{
	  "DeviceName": "SkyWatcher EQ6-R",
	  "DeviceType": "Telescope",
	  "DeviceNumber": 0,
	  "UniqueID": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
	}
  ],
  "ClientTransactionID": 0,
  "ServerTransactionID": 3,
  "ErrorNumber": 0,
  "ErrorMessage": ""
}
```

---

## 2. ASCOM Alpaca Telescope Device API — Common

These endpoints are inherited by all ASCOM device types. For this server the device type is **Telescope**.  
Base path: `/api/v1/telescope/{DeviceNumber}`

`{DeviceNumber}` is a uint32 (0–99) matching the device slot number.

### Common parameters

All endpoints accept the following query parameters (GET) or form fields (PUT):

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `ClientID` | uint32 | 0 | Client's unique identifier |
| `ClientTransactionID` | uint32 | 0 | Client's transaction ID |

All responses use the [ASCOM response envelope](#8-ascom-common-response-envelope).

---

### `PUT /api/v1/telescope/{DeviceNumber}/connect`

Initiates an asynchronous connection to the device.

**Form body:** `ClientID`, `ClientTransactionID`  
**Response `200`:** Standard ASCOM response (no value)

---

### `PUT /api/v1/telescope/{DeviceNumber}/disconnect`

Initiates an asynchronous disconnection from the device.

**Form body:** `ClientID`, `ClientTransactionID`  
**Response `200`:** Standard ASCOM response (no value)

---

### `GET /api/v1/telescope/{DeviceNumber}/connecting`

Returns `true` while an asynchronous connect or disconnect is in progress.

**Response `200`:** `BoolResponse` — `Value`: `true` | `false`

---

### `GET /api/v1/telescope/{DeviceNumber}/devicestate`

Returns a collection of device state values in a single call.

**Response `200`:** `DeviceStateResponse` — `Value`: array of `{Name, Value}` pairs

---

### `PUT /api/v1/telescope/{DeviceNumber}/action`

Invokes a named device-specific action.

**Form body:**

| Field | Type | Description |
|-------|------|-------------|
| `Action` | string | Action name |
| `Parameters` | string | Action-specific parameters |
| `ClientID` | uint32 | |
| `ClientTransactionID` | uint32 | |

**Response `200`:** `StringResponse`

---

### `PUT /api/v1/telescope/{DeviceNumber}/commandblind`

Transmits a string command without waiting for a response.

**Form body:** `Command` (string), `Raw` (bool), `ClientID`, `ClientTransactionID`  
**Response `200`:** Standard ASCOM response

---

### `PUT /api/v1/telescope/{DeviceNumber}/commandbool`

Transmits a string command and returns a boolean response.

**Form body:** `Command` (string), `Raw` (bool), `ClientID`, `ClientTransactionID`  
**Response `200`:** `BoolResponse`

---

### `PUT /api/v1/telescope/{DeviceNumber}/commandstring`

Transmits a string command and returns a string response.

**Form body:** `Command` (string), `Raw` (bool), `ClientID`, `ClientTransactionID`  
**Response `200`:** `StringResponse`

---

### `GET /api/v1/telescope/{DeviceNumber}/connected`

Returns `true` if the device is connected.

**Response `200`:** `BoolResponse`

---

### `PUT /api/v1/telescope/{DeviceNumber}/connected`

Sets the connection state of the device.

**Form body:** `Connected` (bool), `ClientID`, `ClientTransactionID`  
**Response `200`:** Standard ASCOM response

---

### `GET /api/v1/telescope/{DeviceNumber}/description`

Returns the device description string.

**Response `200`:** `StringResponse`

---

### `GET /api/v1/telescope/{DeviceNumber}/driverinfo`

Returns descriptive and version information about the driver.

**Response `200`:** `StringResponse`

---

### `GET /api/v1/telescope/{DeviceNumber}/driverversion`

Returns the driver version string.

**Response `200`:** `StringResponse`

---

### `GET /api/v1/telescope/{DeviceNumber}/interfaceversion`

Returns the ASCOM Device interface version number.

**Response `200`:** `IntResponse`

---

### `GET /api/v1/telescope/{DeviceNumber}/name`

Returns the device name.

**Response `200`:** `StringResponse`

---

### `GET /api/v1/telescope/{DeviceNumber}/supportedactions`

Returns the list of custom action names supported by this driver.

**Response `200`:** `StringListResponse` — `Value`: `string[]`

---

## 3. ASCOM Alpaca Telescope Device API — Telescope Specific

Base path: `/api/v1/telescope/{DeviceNumber}`

All GET endpoints accept `ClientID` and `ClientTransactionID` as query parameters.  
All PUT endpoints accept them as form fields.

### 3.1 Capabilities (read-only)

| Method | Endpoint | Returns | Description |
|--------|----------|---------|-------------|
| `GET` | `/{n}/alignmentmode` | `IntResponse` | Current alignment mode (0=AltAz, 1=Polar, 2=GermanPolar) |
| `GET` | `/{n}/athome` | `BoolResponse` | True if mount is at the home position |
| `GET` | `/{n}/atpark` | `BoolResponse` | True if mount is parked |
| `GET` | `/{n}/canfindhome` | `BoolResponse` | True if FindHome is supported |
| `GET` | `/{n}/canmoveaxis` | `BoolResponse` | True if MoveAxis is supported; requires `Axis` query param |
| `GET` | `/{n}/canpark` | `BoolResponse` | True if Park is supported |
| `GET` | `/{n}/canpulseguide` | `BoolResponse` | True if pulse guiding is supported |
| `GET` | `/{n}/cansetdeclinationrate` | `BoolResponse` | True if DeclinationRate can be set |
| `GET` | `/{n}/cansetguiderates` | `BoolResponse` | True if guide rates can be set |
| `GET` | `/{n}/cansetpark` | `BoolResponse` | True if park position can be set |
| `GET` | `/{n}/cansetpierside` | `BoolResponse` | True if SideOfPier can be set |
| `GET` | `/{n}/cansetrightascensionrate` | `BoolResponse` | True if RightAscensionRate can be set |
| `GET` | `/{n}/cansettracking` | `BoolResponse` | True if tracking can be toggled |
| `GET` | `/{n}/canslew` | `BoolResponse` | True if equatorial slew is supported |
| `GET` | `/{n}/canslewaltaz` | `BoolResponse` | True if alt/az synchronous slew is supported |
| `GET` | `/{n}/canslewaltazasync` | `BoolResponse` | True if alt/az async slew is supported |
| `GET` | `/{n}/canslewasync` | `BoolResponse` | True if async equatorial slew is supported |
| `GET` | `/{n}/cansync` | `BoolResponse` | True if sync to equatorial coordinates is supported |
| `GET` | `/{n}/cansyncaltaz` | `BoolResponse` | True if sync to alt/az coordinates is supported |
| `GET` | `/{n}/canunpark` | `BoolResponse` | True if Unpark is supported |
| `GET` | `/{n}/equatorialsystem` | `IntResponse` | Equatorial coordinate system used (0=Other, 1=LocalTopocentric, 2=J2000, 3=J2050, 4=B1950) |

### 3.2 Position & Pointing

| Method | Endpoint | Returns | Description |
|--------|----------|---------|-------------|
| `GET` | `/{n}/altitude` | `DoubleResponse` | Current altitude above horizon (degrees) |
| `GET` | `/{n}/azimuth` | `DoubleResponse` | Current azimuth (degrees, N=0, E=90) |
| `GET` | `/{n}/declination` | `DoubleResponse` | Current declination (degrees, J2000) |
| `GET` | `/{n}/declinationrate` | `DoubleResponse` | Declination tracking rate offset (arc-seconds/sec) |
| `PUT` | `/{n}/declinationrate` | Response | Set declination tracking rate offset; form field: `DeclinationRate` (double) |
| `GET` | `/{n}/rightascension` | `DoubleResponse` | Current right ascension (hours, J2000) |
| `GET` | `/{n}/rightascensionrate` | `DoubleResponse` | RA tracking rate offset (seconds of RA/sidereal second) |
| `PUT` | `/{n}/rightascensionrate` | Response | Set RA rate offset; form field: `RightAscensionRate` (double) |
| `GET` | `/{n}/siderealtime` | `DoubleResponse` | Current local sidereal time (hours) |
| `GET` | `/{n}/sideofpier` | `IntResponse` | Current side of pier (0=PierEast, 1=PierWest, -1=Unknown) |
| `PUT` | `/{n}/sideofpier` | Response | Set side of pier; form field: `SideOfPier` (int) |
| `GET` | `/{n}/destinationsideofpier` | `IntResponse` | Predicted pier side for a given RA/Dec; query params: `RightAscension`, `Declination` |
| `GET` | `/{n}/slewing` | `BoolResponse` | True if the mount is currently slewing |

### 3.3 Target Coordinates

| Method | Endpoint | Returns | Description |
|--------|----------|---------|-------------|
| `GET` | `/{n}/targetdeclination` | `DoubleResponse` | Target declination (degrees) |
| `PUT` | `/{n}/targetdeclination` | Response | Set target declination; form field: `TargetDeclination` (double) |
| `GET` | `/{n}/targetrightascension` | `DoubleResponse` | Target right ascension (hours) |
| `PUT` | `/{n}/targetrightascension` | Response | Set target RA; form field: `TargetRightAscension` (double) |

### 3.4 Slew & Sync

| Method | Endpoint | Returns | Description |
|--------|----------|---------|-------------|
| `PUT` | `/{n}/abortslew` | Response | Immediately stop any slew in progress |
| `PUT` | `/{n}/findhome` | Response | Slew to the home position |
| `PUT` | `/{n}/park` | Response | Slew to the park position and halt tracking |
| `PUT` | `/{n}/unpark` | Response | Unpark the mount and resume tracking |
| `PUT` | `/{n}/setpark` | Response | Set the park position to the current position |
| `PUT` | `/{n}/slewtoaltaz` | Response | Synchronous slew to given Alt/Az; form fields: `Azimuth`, `Altitude` (double) |
| `PUT` | `/{n}/slewtoaltazasync` | Response | Asynchronous slew to Alt/Az; same form fields |
| `PUT` | `/{n}/slewtocoordinates` | Response | Synchronous slew to RA/Dec; form fields: `RightAscension`, `Declination` (double) |
| `PUT` | `/{n}/slewtocoordinatesasync` | Response | Asynchronous slew to RA/Dec; same form fields |
| `PUT` | `/{n}/slewtotarget` | Response | Synchronous slew to the current target RA/Dec |
| `PUT` | `/{n}/slewtotargetasync` | Response | Asynchronous slew to the current target RA/Dec |
| `PUT` | `/{n}/synctoaltaz` | Response | Sync mount position to given Alt/Az; form fields: `Azimuth`, `Altitude` |
| `PUT` | `/{n}/synctocoordinates` | Response | Sync mount position to given RA/Dec; form fields: `RightAscension`, `Declination` |
| `PUT` | `/{n}/synctotarget` | Response | Sync mount position to the current target RA/Dec |
| `GET` | `/{n}/slewsettletime` | `IntResponse` | Post-slew settling time (seconds) |
| `PUT` | `/{n}/slewsettletime` | Response | Set post-slew settling time; form field: `SlewSettleTime` (int) |

### 3.5 Axis Control

| Method | Endpoint | Returns | Description |
|--------|----------|---------|-------------|
| `GET` | `/{n}/axisrates` | Response | Returns axis rate ranges for a given axis; query param: `Axis` (0=Primary, 1=Secondary, 2=Tertiary) |
| `PUT` | `/{n}/moveaxis` | Response | Move mount at given rate; form fields: `Axis` (int), `Rate` (double, deg/sec) |
| `PUT` | `/{n}/pulseguide` | Response | Pulse guide; form fields: `Direction` (0=N, 1=S, 2=E, 3=W), `Duration` (int, ms) |
| `GET` | `/{n}/ispulseguiding` | `BoolResponse` | True if a pulse guide command is in progress |

### 3.6 Tracking

| Method | Endpoint | Returns | Description |
|--------|----------|---------|-------------|
| `GET` | `/{n}/tracking` | `BoolResponse` | True if sidereal tracking is active |
| `PUT` | `/{n}/tracking` | Response | Enable/disable tracking; form field: `Tracking` (bool) |
| `GET` | `/{n}/trackingrate` | `IntResponse` | Current tracking rate (0=Sidereal, 1=Lunar, 2=Solar, 3=King) |
| `PUT` | `/{n}/trackingrate` | Response | Set tracking rate; form field: `TrackingRate` (int) |
| `GET` | `/{n}/trackingrates` | Response | List of tracking rates supported by this mount |

### 3.7 Refraction & Optics

| Method | Endpoint | Returns | Description |
|--------|----------|---------|-------------|
| `GET` | `/{n}/doesrefraction` | `BoolResponse` | True if refraction correction is applied |
| `PUT` | `/{n}/doesrefraction` | Response | Enable/disable refraction; form field: `DoesRefraction` (bool) |
| `GET` | `/{n}/aperturearea` | `DoubleResponse` | Effective aperture area (sq metres, accounting for obstruction) |
| `GET` | `/{n}/aperturediameter` | `DoubleResponse` | Effective aperture diameter (metres) |
| `GET` | `/{n}/focallength` | `DoubleResponse` | Focal length (metres) |

### 3.8 Guide Rates

| Method | Endpoint | Returns | Description |
|--------|----------|---------|-------------|
| `GET` | `/{n}/guideratedeclination` | `DoubleResponse` | Guide rate in declination (deg/sec) |
| `PUT` | `/{n}/guideratedeclination` | Response | Set Dec guide rate; form field: `GuideRateDeclination` (double) |
| `GET` | `/{n}/guideraterightascension` | `DoubleResponse` | Guide rate in RA (deg/sec) |
| `PUT` | `/{n}/guideraterightascension` | Response | Set RA guide rate; form field: `GuideRateRightAscension` (double) |

### 3.9 Site Location

| Method | Endpoint | Returns | Description |
|--------|----------|---------|-------------|
| `GET` | `/{n}/sitelatitude` | `DoubleResponse` | Observatory latitude (degrees, +N) |
| `PUT` | `/{n}/sitelatitude` | Response | Set latitude; form field: `SiteLatitude` (double, −90 to +90) |
| `GET` | `/{n}/sitelongitude` | `DoubleResponse` | Observatory longitude (degrees, +E) |
| `PUT` | `/{n}/sitelongitude` | Response | Set longitude; form field: `SiteLongitude` (double, −180 to +180) |
| `GET` | `/{n}/siteelevation` | `DoubleResponse` | Observatory elevation above sea level (metres) |
| `PUT` | `/{n}/siteelevation` | Response | Set elevation; form field: `SiteElevation` (double, −500 to 9000) |

### 3.10 Time

| Method | Endpoint | Returns | Description |
|--------|----------|---------|-------------|
| `GET` | `/{n}/utcdate` | `StringResponse` | Current UTC date/time as ISO 8601 string |
| `PUT` | `/{n}/utcdate` | Response | Set UTC date/time; form field: `UTCDate` (ISO 8601 string) |

---

## 4. Device Management API

Runtime device registration and removal. Changes are reflected immediately in the mount registry.  
Base path: `/setup`

---

### `GET /setup/devices`

Lists all currently registered telescope devices with their live status.

**Response `200`**

```json
[
  {
	"DeviceNumber": 0,
	"DeviceName": "SkyWatcher EQ6-R",
	"Connected": true,
	"AlignmentMode": "GermanPolar",
	"MountType": "EQ6",
	"ComPort": null,
	"BaudRate": null,
	"SerialProtocol": null
  }
]
```

| Field | Type | Description |
|-------|------|-------------|
| `DeviceNumber` | int | Device slot (0–99) |
| `DeviceName` | string | Human-readable name |
| `Connected` | bool | Whether the mount is currently connected |
| `AlignmentMode` | string | `AltAz` \| `Polar` \| `GermanPolar` |
| `MountType` | string | Mount model identifier |
| `ComPort` | string? | Serial port (populated in future phases) |
| `BaudRate` | int? | Baud rate (populated in future phases) |
| `SerialProtocol` | string? | Protocol (populated in future phases) |

---

### `POST /setup/devices`

Adds a new telescope device to the running server at runtime.

**Request body**

```json
{
  "DeviceNumber": 0,
  "DeviceName": "My Telescope",
  "UniqueId": ""
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `DeviceNumber` | int | No | Slot 0–99; set to `0` for auto-assign to the lowest free slot |
| `DeviceName` | string | Yes | Display name for the device |
| `UniqueId` | string | No | ASCOM GUID; a new GUID is generated if omitted or empty |

**Response `200`**

```json
{
  "DeviceNumber": 1,
  "DeviceName": "My Telescope",
  "UniqueId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "Message": "Device added successfully"
}
```

**Response `400`** — device number already in use, or validation failure  
**Response `404`** — specified profile not found

---

### `DELETE /setup/devices/{deviceNumber}`

Removes a telescope device from the running mount registry.

> **Note:** The device remains in `DeviceManager.Telescopes` until the server restarts. The mount registry removal immediately stops the device from responding to Alpaca API calls.

**Path parameter:** `deviceNumber` (int, 0–99)

**Response `200`**

```json
{ "message": "Device 1 removed successfully" }
```

**Response `400`** — cannot remove a reserved slot  
**Response `404`** — device not found

---

### ASCOM Setup Redirect

```
GET /setup/v1/telescope/{DeviceNumber}/setup
```

Redirects to the Blazor Mount Settings UI page, pre-selecting the tab for the given `DeviceNumber`. This endpoint provides backwards compatibility with ASCOM Alpaca clients that navigate to the setup page via the standard Alpaca setup URL.

**Response:** `302 Found` → `/mount-settings/{DeviceNumber}`

---

## 5. Configuration API

Provides CRUD operations and file upload/download for all five persisted configuration resources.  
Base path: `/api/config`

All upload endpoints apply a **7-check validation pipeline** before accepting a file:

1. File must be present and non-empty
2. File size ≤ 1 MB
3. Content-Type or file extension must indicate JSON
4. File content must be non-whitespace
5. Content must parse as valid JSON
6. JSON root must be an object (not an array or scalar)
7. Root object must have at least one property

All write operations are **atomic** (temp-file rename) and take effect immediately without a server restart (except server port changes, which require a restart).

---

### 5.1 Monitor Settings

Persisted in: `%AppData%\GreenSwampAlpaca\{version}\monitor.settings.user.json`  
Controls which device types and event categories appear in the real-time monitoring view.

#### `GET /api/config/monitor`

Returns the current monitor settings.

**Response `200`:** `MonitorSettings` object

---

#### `PUT /api/config/monitor`

Replaces all monitor settings. Full object replacement — omitted boolean properties default to `false`.

**Request body:** Complete `MonitorSettings` object  
**Response `200`:** Saved `MonitorSettings`  
**Response `400`:** Body is null or could not be bound

---

#### `GET /api/config/monitor/download`

Downloads `monitor.settings.user.json` as a file attachment.

**Response `200`:** `application/json` attachment named `monitor.settings.user.json`  
**Response `404`:** File does not exist yet (before first save)

---

#### `POST /api/config/monitor/upload`

Uploads a replacement `monitor.settings.user.json` file (multipart/form-data).

**Form field:** `file` — JSON file  
**Response `200`:** Saved `MonitorSettings`  
**Response `400`:** Validation failure  
**Response `413`:** File exceeds 1 MB  
**Response `415`:** Not a JSON file

---

### 5.2 Server Configuration

Persisted in: `%AppData%\GreenSwampAlpaca\{version}\appsettings.server.user.json`  
Controls Alpaca network port, remote access, authentication, and discovery settings.

> **Note:** Changes to `ServerPort` and bind address only take effect after a server restart.

#### `GET /api/config/server`

Returns the current server configuration.

**Response `200`:** `ServerConfig` object

Key properties:

| Property | Type | Description |
|----------|------|-------------|
| `ServerPort` | int | Alpaca listener port (default 11111) |
| `AllowRemoteAccess` | bool | Permit connections from non-localhost |
| `UseAuth` | bool | Require cookie authentication |
| `ServerName` | string | Advertised server name |
| `Location` | string | Physical location description |

---

#### `PUT /api/config/server`

Replaces the server configuration.

**Request body:** Complete `ServerConfig` object  
**Response `200`:** Saved `ServerConfig`  
**Response `400`:** Body is null

---

#### `GET /api/config/server/download`

Downloads `appsettings.server.user.json` as a file attachment.

**Response `200`:** JSON attachment  
**Response `404`:** File does not exist yet

---

#### `POST /api/config/server/upload`

Uploads a replacement `appsettings.server.user.json` file.

**Form field:** `file`  
**Response `200`:** Saved `ServerConfig`  
**Response `400` / `413` / `415`:** (see validation rules above)

---

### 5.3 Observatory Settings

Persisted in: `%AppData%\GreenSwampAlpaca\{version}\observatory.settings.json`  
Physical site location used when creating new devices. Does **not** automatically propagate to existing device files.

#### `GET /api/config/observatory`

Returns the current observatory settings. Creates the file from defaults (51.476852°N, 0°E, 10 m, UTC+0) if it does not yet exist.

**Response `200`:** `ObservatorySettings` object

| Property | Type | Valid Range | Description |
|----------|------|-------------|-------------|
| `Latitude` | double | −90 to +90 | Degrees north |
| `Longitude` | double | −180 to +180 | Degrees east (negative = west) |
| `Elevation` | double | −500 to 9000 | Metres above sea level |
| `UTCOffset` | double | | Hours offset from UTC |

---

#### `PUT /api/config/observatory`

Replaces the observatory settings.

**Request body:** Complete `ObservatorySettings` object  
**Response `200`:** Saved `ObservatorySettings`  
**Response `400`:** Body is null

---

#### `GET /api/config/observatory/download`

Downloads `observatory.settings.json` as a file attachment.

**Response `200`:** JSON attachment  
**Response `404`:** File does not exist yet

---

#### `POST /api/config/observatory/upload`

Uploads a replacement `observatory.settings.json` file.

**Form field:** `file`  
**Response `200`:** Saved `ObservatorySettings`  
**Response `400` / `413` / `415`:** (see validation rules)

---

### 5.4 Alpaca Device Discovery Entries

Persisted in: `%AppData%\GreenSwampAlpaca\{version}\devices.alpaca.user.json`  
Discovery metadata seen by ASCOM clients (name, type, number, unique GUID). Separate from operational settings.

#### `GET /api/config/alpaca-devices`

Returns all Alpaca discovery entries.

**Response `200`:** `AlpacaDevice[]` (empty array if none registered)

| Field | Type | Description |
|-------|------|-------------|
| `DeviceNumber` | int | Slot (0–99) |
| `DeviceName` | string | Display name |
| `DeviceType` | string | `"Telescope"` |
| `UniqueID` | string | ASCOM GUID |

---

#### `GET /api/config/alpaca-devices/{deviceNumber}`

Returns the discovery entry for a single device.

**Path param:** `deviceNumber` (int)  
**Response `200`:** `AlpacaDevice`  
**Response `404`:** Not found

---

#### `POST /api/config/alpaca-devices`

Adds a new discovery entry to `devices.alpaca.user.json`.

> This writes only the discovery metadata file. To register the device in the live server, also call `POST /setup/devices`.

**Request body:**

```json
{
  "DeviceNumber": 1,
  "DeviceName": "My New Mount",
  "DeviceType": "Telescope",
  "UniqueID": ""
}
```

A new GUID is generated automatically if `UniqueID` is empty.  
Maximum 100 devices. DeviceNumber must be unique.

**Response `201`:** Created `AlpacaDevice`; `Location` header points to the new resource  
**Response `400`:** Body null, device number already exists, or 100-device limit reached

---

#### `DELETE /api/config/alpaca-devices/{deviceNumber}`

Deletes the discovery entry **and** the corresponding `device-nn.settings.json` file (Q2 consistency rule).

> Does not remove the device from the live server registry. Use `DELETE /setup/devices/{n}` for that.

**Path param:** `deviceNumber` (int)  
**Response `204`:** Both files deleted  
**Response `404`:** Not found

---

#### `GET /api/config/alpaca-devices/download`

Downloads `devices.alpaca.user.json` as a file attachment. Format: `{ "AlpacaDevices": [...] }`.

**Response `200`:** JSON attachment  
**Response `404`:** File does not exist yet

---

#### `POST /api/config/alpaca-devices/upload`

Uploads a replacement `devices.alpaca.user.json` file. Accepts either wrapper format `{ "AlpacaDevices": [...] }` or a bare array. Maximum 100 entries. Semantic validation runs after save (errors are logged as warnings, not rejections).

**Form field:** `file`  
**Response `200`:** Saved `AlpacaDevice[]`  
**Response `400` / `413` / `415`:** (see validation rules)

---

### 5.5 Per-Device Operational Settings

Persisted in: `%AppData%\GreenSwampAlpaca\{version}\device-{nn}.settings.json` (one file per device)  
Full operational settings for each mount: alignment mode, mount type, axis rates, limits, PEC data, tracking, guiding, and all other runtime parameters.

#### `GET /api/config/devices`

Returns the full `SkySettings` for every configured device. Initialises one device from factory defaults if no files exist (first run).

**Response `200`:** `SkySettings[]`

---

#### `GET /api/config/devices/{deviceNumber}`

Returns the full `SkySettings` for a single device.

**Path param:** `deviceNumber` (int, 0–99)  
**Response `200`:** `SkySettings`  
**Response `404`:** Settings file not found

---

#### `PUT /api/config/devices/{deviceNumber}`

Replaces all operational settings for a device. **Full replacement only** — retrieve first with GET, modify required fields, then PUT the complete object back.

> The device file must already exist. To create a new device, use `POST /api/config/alpaca-devices` + `POST /setup/devices`.

**Path param:** `deviceNumber` (int)  
**Request body:** Complete `SkySettings` object  
**Response `200`:** Saved `SkySettings`  
**Response `400`:** Body is null  
**Response `404`:** Settings file not found

---

#### `DELETE /api/config/devices/{deviceNumber}`

Deletes `device-nn.settings.json` for the specified device.

> Does **not** remove the Alpaca discovery entry. Use `DELETE /api/config/alpaca-devices/{n}` for atomic removal of both files.

**Path param:** `deviceNumber` (int)  
**Response `204`:** File deleted  
**Response `404`:** File not found

---

#### `GET /api/config/devices/{deviceNumber}/download`

Downloads `device-nn.settings.json` as a file attachment named `device-{nn}.settings.json`.

**Path param:** `deviceNumber` (int)  
**Response `200`:** JSON attachment  
**Response `404`:** File not found

---

#### `POST /api/config/devices/{deviceNumber}/upload`

Uploads a replacement `device-nn.settings.json` file. The target file must already exist. Semantic validation runs after save (errors logged as warnings). Writes are atomic and protected by a per-device lock.

**Path param:** `deviceNumber` (int)  
**Form field:** `file`  
**Response `200`:** Saved `SkySettings`  
**Response `400`:** Validation failure  
**Response `404`:** Settings file not found  
**Response `413`:** File exceeds 1 MB  
**Response `415`:** Not a JSON file

---

#### `GET /api/config/devices/{deviceNumber}/validate`

Validates `device-nn.settings.json` and returns a detailed result. Read-only — does not modify any files.

Validation checks include: required property presence, alignment-mode consistency, axis rate ranges, PEC data integrity, and inter-property constraints.

**Path param:** `deviceNumber` (int)  
**Response `200`:** Always 200 — inspect `IsValid` for outcome

```json
{
  "IsValid": false,
  "Errors": [
	{
	  "ErrorCode": "AXR001",
	  "Severity": "Error",
	  "Message": "Primary axis max rate exceeds mount capability",
	  "Resolution": "Reduce MaxRate1 to ≤ 800 arc-sec/sec"
	}
  ],
  "Warnings": []
}
```

**Response `404`:** Settings file not found

---

## 6. Authentication

### `POST /login`

Authenticates a user and establishes a cookie session.

**Form fields:**

| Field | Type | Description |
|-------|------|-------------|
| `username` | string | Login username |
| `password` | string | Login password |
| `url` | string | Redirect URL after login (default `/`) |

**Response:** `302 Found` → `url` parameter value (redirect)

> When `UseAuth = false` in server configuration, authentication is skipped and the redirect happens immediately.

---

### `GET /logout`

Signs out the current user and clears the session cookie.

**Query parameter:** `returnUrl` (string, default `/`)  
**Response:** `302 Found` → `returnUrl`

---

## 7. Error Responses

All GreenSwamp API endpoints (`/api/config` and `/setup`) return a consistent error object on non-2xx responses:

```json
{
  "Error": "Descriptive error message"
}
```

ASCOM Alpaca endpoints (`/api/v1/telescope` and `/management`) use the standard ASCOM error envelope:

```json
{
  "Value": null,
  "ClientTransactionID": 0,
  "ServerTransactionID": 1,
  "ErrorNumber": 1024,
  "ErrorMessage": "Device not connected"
}
```

The catch-all controller returns an HTTP 400 with a plain-text error message for any `/api/...` path that does not match a registered route.

---

## 8. ASCOM Common Response Envelope

All ASCOM Alpaca endpoints wrap their return value in a standard envelope:

| Field | Type | Description |
|-------|------|-------------|
| `Value` | varies | The returned value (`null` for action-only calls) |
| `ClientTransactionID` | uint32 | Echoed from the request |
| `ServerTransactionID` | uint32 | Server-assigned transaction ID (monotonically increasing) |
| `ErrorNumber` | int | 0 = success; non-zero = ASCOM error code |
| `ErrorMessage` | string | Error description (empty on success) |

Common response types:

| Type | `Value` type |
|------|-------------|
| `BoolResponse` | `bool` |
| `IntResponse` | `int` |
| `DoubleResponse` | `double` |
| `StringResponse` | `string` |
| `StringListResponse` | `string[]` |
| `IntListResponse` | `int[]` |
| `Response` | *(no value field)* |

---

*GreenSwamp Alpaca — API Reference — 2026-05-18 16:43*
