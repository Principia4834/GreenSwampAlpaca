# Green Swamp Alpaca Server — User Guide

**2026-07-09 22:51**

---

## Table of Contents

1. [Overview and Purpose](#1-overview-and-purpose)
2. [Key Concepts](#2-key-concepts)
   - 2.1 [ASCOM and Alpaca](#21-ascom-and-alpaca)
   - 2.2 [Mount Types Supported](#22-mount-types-supported)
   - 2.3 [SkyWatcher and Simulator Modes](#23-skywatcher-and-simulator-modes)
   - 2.4 [Multiple Mounts in Parallel](#24-multiple-mounts-in-parallel)
3. [Getting Started](#3-getting-started)
   - 3.1 [First Launch](#31-first-launch)
   - 3.2 [Default Port and URL](#32-default-port-and-url)
   - 3.3 [Connecting an ASCOM Client](#33-connecting-an-ascom-client)
4. [Navigating the User Interface](#4-navigating-the-user-interface)
   - 4.1 [Navigation Menu](#41-navigation-menu)
5. [Page Reference](#5-page-reference)
   - 5.1 [Home](#51-home)
   - 5.2 [Mount Status](#52-mount-status)
   - 5.3 [Mount Settings](#53-mount-settings)
   - 5.4 [Monitor](#54-monitor)
   - 5.5 [Settings Explorer](#55-settings-explorer)
   - 5.6 [Alpaca Settings](#56-alpaca-settings)
   - 5.7 [Check Settings](#57-check-settings)
   - 5.8 [RA / Dec Chart](#58-ra--dec-chart)
   - 5.9 [Pulse Guide Chart](#59-pulse-guide-chart)
6. [Settings Explorer In Depth](#6-settings-explorer-in-depth)
   - 6.1 [Tree Structure](#61-tree-structure)
   - 6.2 [Dirty-State Tracking and Saving](#62-dirty-state-tracking-and-saving)
   - 6.3 [Observatory Settings](#63-observatory-settings)
   - 6.4 [Server Configuration](#64-server-configuration)
   - 6.5 [Logging Settings](#65-logging-settings)
   - 6.6 [Telescope Device Settings](#66-telescope-device-settings)
7. [Managing Telescope Devices](#7-managing-telescope-devices)
   - 7.1 [Adding a Device](#71-adding-a-device)
   - 7.2 [Removing a Device](#72-removing-a-device)
   - 7.3 [Device Settings Groups](#73-device-settings-groups)
8. [Mount Type Configuration Detail](#8-mount-type-configuration-detail)
   - 8.1 [German Equatorial Mount (GEM)](#81-german-equatorial-mount-gem)
   - 8.2 [Polar / Fork Equatorial Mount](#82-polar--fork-equatorial-mount)
   - 8.3 [Alt-Azimuth Mount](#83-alt-azimuth-mount)
9. [SkyWatcher Driver Configuration](#9-skywatcher-driver-configuration)
10. [Simulator Mode](#10-simulator-mode)
11. [Settings Files and Persistence](#11-settings-files-and-persistence)
12. [ASCOM Client Compatibility](#12-ascom-client-compatibility)
13. [Troubleshooting](#13-troubleshooting)
14. [Appendix A — File and Directory Locations](#14-appendix-a--file-and-directory-locations)

---

## 1. Overview and Purpose

**Green Swamp Alpaca Server** is a local-network telescope mount driver server built on the [ASCOM Alpaca](https://ascom-standards.org) open standard. It exposes one or more physical or simulated telescope mounts as Alpaca-compliant REST devices so that any ASCOM-aware
planetarium, imaging, or automation application can connect to them over a standard HTTP connection — no COM ports, no local driver installation required on the client machine.

### What the application does

| Capability | Detail |
|---|---|
| **Alpaca REST API** | Full ASCOM Telescope v3 interface over HTTP/JSON |
| **Multi-mount** | Drive several mounts simultaneously, each with its own device number |
| **Mount types** | German Equatorial (GEM), Polar / Fork Equatorial, Alt-Azimuth |
| **Hardware driver** | SkyWatcher motor controllers via serial (RS-232 / USB-Serial) |
| **Simulator** | Built-in software simulator for every mount type — no hardware required |
| **Web UI** | Browser-based dashboard for real-time status, settings, and diagnostics |
| **Logging** | Configurable real-time monitor with session and file logging |
| **Settings health** | Automatic validation and repair of the configuration file |

The server runs as a Windows process and hosts a Blazor web application. Once running, you interact with it entirely through a web browser — either on the same machine (`http://localhost:31416`) or from any device on your local network when remote access is enabled.

---

## 2. Key Concepts

### 2.1 ASCOM and Alpaca

**ASCOM** (Astronomy Common Object Model) is the industry-standard interface specification for astronomy equipment. The traditional COM-based driver requires the driver software to be installed on every client PC.

**ASCOM Alpaca** is the modern, platform-independent version: it uses HTTP/REST so any client on the same network can connect without installing drivers. Green Swamp Alpaca Server implements the Alpaca Telescope interface and also supports Alpaca UDP discovery, allowing compatible clients tofind the server automatically.

### 2.2 Mount Types Supported

The server supports three ASCOM alignment modes. Each determines how the mount tracks the sky
and how coordinates are transformed.

| Alignment Mode | Typical Hardware | Notes |
|---|---|---|
| **GermanPolar** (GEM) | German Equatorial mounts (e.g. EQ5, EQ6, AZ-EQ) | RA and Dec axes; meridian flip required; pier-side awareness; hour angle limit |
| **Polar** | Fork or barn-door equatorial mounts | RA and Dec axes; no meridian flip; polar primary side selectable |
| **AltAz** | Dobsonian, single-arm, computerised alt-az | Altitude and azimuth axes; horizontal tracking limit; no pier-side concept |

### 2.3 SkyWatcher and Simulator Modes

The **Mount Type** field in Mount Configuration selects the driver backend for each device:

- **SkyWatcher** — Communicates with a physical SkyWatcher mount via a serial port using the SynScan/SkyWatcher binary protocol. Requires a COM port (RS-232 or USB-to-Serial adapter) to be configured.
  
- **Simulator** — A pure software simulation of any of the three mount types. The simulated mount tracks, slews, parks, and pulse-guides just as a real mount would, making it ideal for testing planetarium software integrations, developing automation scripts, or learning the application without any hardware.

Both modes are full Alpaca-compatible telescope devices. A single running instance of the server can host SkyWatcher devices and simulator devices at the same time.

### 2.4 Multiple Mounts in Parallel

The server supports any number of telescope devices running concurrently, each identified by a unique integer **Device Number** (0, 1, 2, …). Each device:

- Has its own complete set of settings (serial port, alignment mode, limits, gearing, etc.)
- Appears as a separate Alpaca telescope endpoint:
  `http://{host}:{port}/api/v1/telescope/{deviceNumber}/...`
- Has its own tab in the **Mount Status** and **Mount Settings** pages
- Can be driven by one or more simultaneous ASCOM client connections independently

There is no theoretical limit to the number of devices; in practice it is limited by available serial ports (for SkyWatcher devices) or system resources (for simulator devices).

---

## 3. Getting Started

### 3.1 First Launch

On first launch the server creates a default configuration containing a single **Simulator (GEM)** device. This means the application is immediately usable without any hardware. You will see the home page and the device will appear in the status and settings views.

### 3.2 Default Port and URL

| | Value |
|---|---|
| Default Alpaca port | **31416** |
| Local UI URL | `http://localhost:31416` |
| Alpaca base URL | `http://localhost:31416/api/v1/telescope/{deviceNumber}/` |
| Setup redirect (legacy) | `http://localhost:31416/setup/v1/Telescope/{deviceNumber}/setup` → redirects to Mount Status |

The port can be changed in **Alpaca Settings** → Network. A server restart is required for a port change to take effect.

### 3.3 Connecting an ASCOM Client

In your planetarium or imaging application (e.g. Cartes du Ciel, Sequence Generator Pro, N.I.N.A., Voyager):

1. Choose **ASCOM Alpaca** as the telescope type.
2. Use Alpaca UDP Auto-Discovery **or** enter the server IP and port manually.
3. Select the device number matching the mount you want to control.
4. Connect — the server will accept the connection and the mount will begin reporting status.

> **Tip:** Multiple ASCOM clients can connect to the same device simultaneously. The server
> tracks connected client count per device and displays it on the Status page.

---

## 4. Navigating the User Interface

The application is a single-page web application. All pages are reachable from the left-side navigation drawer without needing to reload the browser.

### 4.1 Navigation Menu

```
Home
─────────────────────
Status              ← Real-time mount telemetry
Settings            ← Per-device quick settings overview
Monitor             ← Live log stream + filter controls
─────────────────────
RA / Dec Chart      ← Opens live position chart in a new window
Pulse Chart         ← Opens live pulse-guide chart in a new window
─────────────────────
Settings Explorer   ← Full settings tree (all groups)
Alpaca Settings     ← Server-level network and auth settings
Check Settings      ← Settings validation and auto-repair
```

The divider separates the everyday operational pages (top) from administrative / configuration pages (bottom). Device links (Camera, Dome, etc.) are added automatically if those ASCOM device types are registered.

Clicking a navigation link navigates immediately; there is no page reload.

---

## 5. Page Reference

### 5.1 Home

The landing page. Summarises what the server does and provides links to the Green Swamp Software website and the ASCOM Standards site. No user action is required here.

---

### 5.2 Mount Status

**URL:** `/mount-status` or `/mount-status/{deviceNumber}`

Displays real-time telemetry for all registered telescope devices. If more than one device is registered, each appears as a separate **tab**. Clicking a tab navigates to `/mount-status/{deviceNumber}` so the view is bookmarkable and deep-linkable.

The status for each device is arranged in four panels:

| Panel | Content |
|---|---|
| **Position** | RA, Dec, Altitude, Azimuth, Local Hour Angle (LHA), Pier Side |
| **Status** | Mount connected / COM port, active client count, Slewing, Pulse Guiding (RA and Dec separately), Tracking rate, At Park, At Home |
| **Axis Positions** | Actual and apparent axis angles in degrees (X = RA/Az, Y = Dec/Alt), raw motor step counts |
| **Mount** | Mount type string, alignment mode, current local date/time |
| **Axis Dials** | Graphical circular dials showing the current Azimuth and Altitude angles |

The page subscribes to live state updates and refreshes automatically; you do not need to reload
the browser.

---

### 5.3 Mount Settings

**URL:** `/mount-settings` or `/mount-settings/{deviceNumber}`

Provides a quick read-only overview of each device's configuration, one tab per device. Each tab shows the device identity, serial connection details, and mount hardware summary.

An **Edit in Settings Explorer** button navigates directly to that device's Mount Configuration group in the Settings Explorer, where all settings can be changed.

---

### 5.4 Monitor

**URL:** `/monitor`

A two-part page:

**Top half — Monitor Records**
A scrollable, monospaced log buffer showing up to the last _N_ records captured since the server started (or since the buffer was cleared). Records are colour-coded by message type. Controls:

| Button | Action |
|---|---|
| Clear (bin icon) | Empties the display buffer (does not affect the log file) |
| Copy to Clipboard | Copies the entire visible buffer as plain text |
| Reload Settings | Re-reads filter settings from disk |
| Write Buffer to File | (Fast Monitor mode only) Flushes the in-memory buffer to a log file |

**Bottom half — Logging Settings**
Inline filter controls (identical to those in Settings Explorer → Logging). Changes here are saved to disk with the **Save** button and take effect immediately. Four filter categories:

- **Device Filters** — which device types produce log entries (Server, Telescope, UI)
- **Category Filters** — which subsystem categories appear (Driver, Interface, Server, Mount, etc.)
- **Message Type Filters** — which severity levels appear (Info, Warning, Error, Debug, Data)
- **Logging Control** — enable/disable the session log file, monitor log file, and charting data file

Preset buttons at the top of the logging section apply commonly used filter combinations
(Development, Production, Troubleshooting, Profile Debug) in one click.

---

### 5.5 Settings Explorer

**URL:** `/settings-explorer`

The primary settings management page. All application settings are accessible here, organised into a hierarchical tree on the left, with the editor for the selected group displayed on the right.

See [Section 6](#6-settings-explorer-in-depth) for a full description.

---

### 5.6 Alpaca Settings

**URL:** `/setup`

A focused card-based editor for the most common server-level settings that users need to adjust when first setting up the server. Organised into four cards:

| Card | Settings |
|---|---|
| **Network** | Server location label, Alpaca port, remote access on/off, UDP discovery on/off, loopback-only discovery |
| **Runtime Configuration** | Strict Alpaca mode, prevent remote disconnects, image-bytes download |
| **UI Options** | Auto-start browser on launch, Swagger/OpenAPI UI |
| **Authentication** | Enable HTTP Basic auth, username |

The current server IP address(es) are shown live in the Network card when remote access is enabled.

> **Note:** The password is never stored in plain text. Use the password-change facility
> (if available in your build) to set or update it. The Alpaca Settings page shows the
> username field only.

A **Save** button at the bottom of each card saves that card's settings immediately.

---

### 5.7 Check Settings

**URL:** `/check-settings`

Validates the entire settings file and reports any problems found. Results are displayed as:

- **Errors** — settings that are invalid and will prevent correct operation (shown in red)
- **Warnings** — settings that are unusual but not necessarily wrong (shown in amber)

Each issue includes:
- An error code chip
- The device number it affects (if device-specific)
- A plain-English description of the problem
- A suggested resolution
- An **Auto-repairable** chip if the server can fix it automatically

An **Auto-repair** button is shown when at least one auto-repairable issue exists. Clicking it applies all available automatic fixes and re-validates. You can also re-run validation manually at any time with the **Validate** button.

---

### 5.8 RA / Dec Chart

**URL:** `/charts/radec`

Opens in a **separate browser window** when clicked in the navigation menu. The window hosts an
independent Blazor circuit connected to the server via SignalR.

| Control | Purpose |
|---|---|
| **Scale** drop-down | Switches the Y-axis between Steps, Degrees, and Arc-seconds |
| **Axis 1 / Axis 2** toggles | Show or hide each position series |
| **Record** (●) button | Starts writing received data points to a `GSChartingLogRaDec` file in the log directory |
| **Stop** (■) button | Stops disk logging for this session |
| **Clear** (🧹) button | Removes all plotted points from the current view |

On opening, the chart pre-populates with up to 5 000 buffered historical points from the server
before live streaming begins. Settings (scale, series visibility) are saved automatically and
restored the next time the chart window is opened.

> **Popup blocker:** If the window does not open, a warning snackbar is shown in the main
> application window. See [Troubleshooting — Chart window is blocked](#chart-window-is-blocked-by-the-browser).

---

### 5.9 Pulse Guide Chart

**URL:** `/charts/pulse`

Opens in a **separate browser window** identical in structure to the RA/Dec chart. Shows four
series for guide pulse data received from the mount driver:

| Series | Colour | Description |
|---|---|---|
| **RA** | Green | Accepted RA pulse durations |
| **RA rej.** | Amber | Rejected RA pulses (scatter plot) |
| **Dec** | Blue | Accepted Dec pulse durations |
| **Dec rej.** | Red | Rejected Dec pulses (scatter plot) |

The **Scale** drop-down switches the Y-axis between Milliseconds (pulse duration), Arc-seconds
(rate), and Steps.

All toolbar controls (logging, clear, series toggles) are identical to the RA/Dec chart.

---

## 6. Settings Explorer In Depth

### 6.1 Tree Structure

The settings tree has three levels:

```
Section
  └─ Group (leaf node — clicking shows the editor)
```

The top-level sections are:

| Section | Description |
|---|---|
| **Observatory** | Shared physical site properties (latitude, longitude, elevation, UTC offset) |
| **Logging** | Monitor filter settings — device, category, message type filters and log file targets |
| **Server Configuration** | HTTP server behaviour, network binding, authentication, identity |
| **Telescope Devices** | Per-device mount settings, one section per registered device |

Within **Telescope Devices**, each registered device has its own section node. Selecting a device
section shows the **Device Manager** card (add/remove devices). Selecting a leaf group below a
device shows the editor for that group.

**Toolbar controls:**

| Control | Action |
|---|---|
| Expand All (↕ icon) | Opens all tree nodes |
| Collapse All (↕ icon) | Closes all tree nodes |
| Search box | Filters the tree to matching group names |
| Show Hidden (toggle) | Reveals advanced groups normally hidden from casual use (Optics, PEC/PPEC, Performance & Tuning, Hand Controller) |

### 6.2 Dirty-State Tracking and Saving

Any change you make to a setting is held in memory as a **working copy** and is not written to disk until you explicitly save. The tree indicates unsaved changes with:

- A small **amber dot badge** (●) next to any leaf group that has unsaved changes
- **Save** and **Reset** buttons that appear in the editor card header when any change is pending

**Save** writes the working copy to disk. **Reset** discards all unsaved changes and reverts to
the last saved state.

The dirty-state badge covers all settings within the device: changing any field in any group of a device marks the device's leaf nodes as dirty. Saving from any leaf saves the entire device's settings in one operation.

> **Warning:** Navigating away from a dirty (unsaved) node will show a confirmation dialog
> asking whether you want to discard changes. Confirm to continue navigating, or cancel to
> stay and save first.

### 6.3 Observatory Settings

**Group: Observatory Settings**

Shared observatory location used as defaults when new devices are created. Fields:

| Field | Description |
|---|---|
| Latitude | Observatory latitude in decimal degrees (−90 to +90) |
| Longitude | Observatory longitude in decimal degrees (−180 to +180) |
| Elevation | Elevation above sea level in metres |
| UTC Offset | Local time offset from UTC (e.g. `01:00:00` for UTC+1) |

### 6.4 Server Configuration

Four groups, each covering a different aspect of the Alpaca HTTP server:

| Group | Key Settings |
|---|---|
| **Network** | Port, remote access, UDP discovery, loopback discovery |
| **Alpaca Behaviour** | Strict mode, remote disconnects, image-bytes |
| **Identity & UI** | Location label, browser auto-start, Swagger |
| **Authentication** | Enable auth, username |

### 6.5 Logging Settings

When the **Logging** section node is selected, a special presets card is shown instead of a plain editor. Four quick-action buttons allow bulk filter changes (select all devices, all categories, all types, or clear all). Four pre-set buttons load commonly used filter profiles.

The individual filter leaf groups (Device Filters, Category Filters, Message Type Filters, Logging Control) each open a dedicated editor with checkboxes for fine-grained control.

### 6.6 Telescope Device Settings

Each device has the following leaf groups. Groups marked *(hidden by default)* are only shown when the **Show Hidden** toggle is on.

| Group | Description |
|---|---|
| **Mount Configuration** | Device name, enabled flag, mount type, alignment mode, polar primary side, coordinate type, serial port, baud rate, backlash, encoders, custom gearing |
| **Observatory Configuration** | Per-device latitude, longitude, elevation, UTC offset, environmental inputs, GPS |
| **Tracking & Guiding** | Sidereal/lunar/solar/king tracking rates, RA offset, pulse guiding parameters, ST4 guide rate |
| **Home and Park** | Home axis positions (X/Y), auto-home positions, named park positions, active park selection |
| **Limits** | Axis/slew limits (upper/lower Dec/Alt, RA/Az axis), sync limit, no-sync-past-meridian, meridian/hour angle limit *(GEM/Polar only)*, horizontal axis limit *(AltAz only)* |
| **Optics** *(hidden)* | Aperture diameter, aperture area, focal length |
| **Performance & Tuning** *(hidden)* | Loop update interval, GOTO precision, trace logging |
| **PEC / PPEC** *(hidden)* | Periodic error correction and predictive PEC settings |
| **Hand Controller** *(hidden)* | HC speed, mode, flip behaviour, anti-backlash |

---

## 7. Managing Telescope Devices

### 7.1 Adding a Device

1. Open **Settings Explorer** from the navigation menu.
2. Select **Telescope Devices** in the tree (the top-level section node).
3. Click **Add Device** in the Device Manager card.
4. In the dialog, enter:
   - **Device Number** — a unique integer (0, 1, 2, …); must not already be in use
   - **Device Name** — a descriptive name (e.g. "SkyWatcher EQ6-R" or "Simulator AltAz")
   - **Alignment Mode** — GermanPolar, Polar, or AltAz
5. Click **Add**. The new device appears immediately in the tree and in the Status/Settings tabs.
6. Select the device's **Mount Configuration** leaf to complete the configuration (serial port,
   mount type, etc.) and save.

### 7.2 Removing a Device

1. Open **Settings Explorer** and select **Telescope Devices**.
2. Find the device in the list and click the **Delete** (bin) icon on the right.
3. Confirm in the dialog. The device and all its settings are permanently removed.

> **Note:** Removing a device while an ASCOM client is connected to it will cause that client
> to receive a disconnect error. Disconnect clients before removing a device.

### 7.3 Device Settings Groups

After adding a device, configure it fully before connecting a client. At minimum:

1. **Mount Configuration** — Set the Mount Type (`SkyWatcher` or `Simulator`), Alignment Mode,
   and for SkyWatcher devices, the serial Port and Baud Rate.
2. **Observatory Configuration** — Set your site latitude, longitude and elevation so coordinate
   transforms are accurate.
3. **Limits** — Review axis limits and, for GEM/Polar mounts, set the Hour Angle Limit to a
   safe value for your mount.

---

## 8. Mount Type Configuration Detail

### 8.1 German Equatorial Mount (GEM)

Select **Alignment Mode: German Equatorial (GEM)** in Mount Configuration.

**Key settings unique to GEM:**

| Setting | Location | Notes |
|---|---|---|
| Polar Primary Side | Mount Configuration | Which pier side is the primary pointing side (Left or Right) |
| Hour Angle Limit | Limits → Meridian / Hour Angle Limit | Hour angle (0–12 h) at which tracking stops to prevent over-rotation; 0 disables the limit |
| No Sync Past Meridian | Limits → Sync Limits | Rejects sync commands when the scope is past the meridian |

The GEM tracks past the meridian up to the configured hour angle limit then stops. The client application is responsible for issuing a meridian flip (a GOTO to the same sky position on the opposite pier side) before the limit is reached.

**Status page** will report the current Pier Side (East/West) alongside the RA/Dec position.

### 8.2 Polar / Fork Equatorial Mount

Select **Alignment Mode: Polar / Fork** in Mount Configuration.

A polar mount behaves similarly to a GEM but with no meridian flip — the RA axis can rotate freely. The **Polar Primary Side** setting (Left or Right) determines the default orientation.

The Hour Angle Limit in the **Limits** group is still applicable for polar mounts to prevent cable wrap.

### 8.3 Alt-Azimuth Mount

Select **Alignment Mode: Alt-Az** in Mount Configuration.

**Key settings unique to AltAz:**

| Setting | Location | Notes |
|---|---|---|
| Horizontal Axis Limit | Limits → Horizontal Axis Limit | Enables a tracking stop at a configured altitude angle |
| Hz Tracking Limit (°) | Limits → Horizontal Axis Limit | Altitude angle at which tracking stops |
| Park at Hz Limit | Limits → Horizontal Axis Limit | Automatically parks the mount when the limit is reached |
| Park Hz Limit Position | Limits → Horizontal Axis Limit | Named park position used for the auto-park at limit |
| AltAz Update Interval | Tracking & Guiding *(hidden)* | How often the tracking correction is issued (ms) |

AltAz mounts do not have RA/Dec axes so the **Meridian / Hour Angle Limit** panel is automatically hidden for AltAz devices.

---

## 9. SkyWatcher Driver Configuration

To connect a physical SkyWatcher mount:

1. Connect the mount to the PC via a serial or USB-to-serial cable.
2. Note the COM port number assigned by Windows (e.g. `COM3`).
3. In Settings Explorer, navigate to the device's **Mount Configuration**.
4. Set **Mount Type** to `SkyWatcher`.
5. Set **Port** to the COM port (e.g. `COM3`).
6. Set **Baud Rate** — SkyWatcher typically uses `9600`; some hand controllers use `115200`.
7. Leave other serial settings (Data Bits, Handshake, DTR/RTS) at their defaults unless you
   have a specific reason to change them.
8. Save and connect from your ASCOM client.

**Hardware settings** (Mount Hardware panel within Mount Configuration):

| Setting | Notes |
|---|---|
| Allow Advanced Commands | Enables extended SkyWatcher command set for additional features |
| Encoders Enabled | Activates absolute encoder feedback if your mount supports it |
| Max Slew Rate (°/s) | Cap on the maximum slew speed sent to the hardware |
| RA / Dec Backlash (steps) | Compensation steps applied when reversing direction |

**Custom Gearing** (within Mount Configuration, Show Hidden):
For modified or non-standard gearing ratios — step counts per revolution and worm-wheel tooth counts for both axes. Leave at defaults for standard SkyWatcher mounts.

---

## 10. Simulator Mode

The simulator is activated by setting **Mount Type** to `Simulator` in Mount Configuration (no serial port is required). It is available for all three alignment modes.

**What the simulator does:**

- Responds to all standard Alpaca Telescope commands (slew, track, park, unpark, pulse guide,
  sync, abort, etc.)
- Simulates realistic motion — a slew takes time proportional to the angular distance
- Tracks at sidereal rate while tracking is enabled
- Respects configured limits (axis limits, hour angle limit, horizontal axis limit)
- Reports realistic position coordinates based on the observatory location

**Typical uses:**

| Use case | How |
|---|---|
| Testing a new planetarium or automation script | Create a simulator device and point the script at it |
| Learning the settings explorer | Explore and change settings without risk to hardware |
| Developing or debugging this application | All UI features work identically with a simulator device |
| Running multiple virtual devices | Add several simulator devices with different alignment modes |

The default configuration ships with one Simulator (GEM) device (Device 0) so the application is
immediately functional without any hardware or configuration change.

---

## 11. Settings Files and Persistence

All settings are stored as JSON files in a versioned folder under the Windows AppData directory:

```
%AppData%\GreenSwampAlpaca\{version}\
  appsettings.user.json         ← main settings (devices, server config, observatory)
  appsettings.server.user.json  ← server configuration (port, auth, network)
  monitor.settings.json         ← logging filter settings
```

Settings are not written to disk until you click **Save** in the Settings Explorer or a settings card. The **Check Settings** page can detect and repair common problems with the settings file (missing required fields, out-of-range values, duplicate device numbers, etc.).

> **Backup tip:** Copy the entire `%AppData%\GreenSwampAlpaca\{version}\` folder to preserve
> a complete working configuration.

---

## 12. ASCOM Client Compatibility

The server implements the full ASCOM Alpaca Telescope v3 interface. It has been designed for compatibility with:

- **Cartes du Ciel (Skychart)**
- **Sequence Generator Pro (SGP)**
- **N.I.N.A.**
- **Voyager**
- **TheSkyX** (via Alpaca bridge)
- **Any application using the ASCOM Platform 6.6+ Alpaca chooser**

Legacy clients that use the ASCOM COM telescope driver continue to work if they are configured
to connect via the Alpaca Chooser in the ASCOM Platform.

**Legacy setup URL redirect:** Some ASCOM clients open the setup page via the URL
`/setup/v1/Telescope/{N}/setup`. The server automatically redirects this URL to
`/mount-status/{N}` so the device status page is displayed instead of an error.

---

## 13. Troubleshooting

### The browser does not open automatically on startup

Enable **Auto-start Browser** in Alpaca Settings → UI Options, or navigate manually to
`http://localhost:31416`.

### An ASCOM client cannot find the server via discovery

- Ensure **Allow Discovery** is checked in Alpaca Settings → Network.
- If the client is on the same machine, ensure **Allow Remote Access** is off (localhost only)
  or on (all interfaces) as appropriate.
- Check that no firewall is blocking UDP port 32227 (Alpaca discovery) or TCP port 31416.

### A SkyWatcher mount does not connect

- Verify the COM port in Device Manager matches the value in Mount Configuration.
- Try baud rate 9600 first; some newer mounts and hand controllers use 115200.
- Check that no other application (e.g. EQMod, Cartes du Ciel direct serial) is holding the
  COM port open.
- Enable the **Monitor** page and set Driver and Mount category filters on to see detailed
  serial communication messages.

### Settings appear to have been reset or lost

- Open **Check Settings** and run validation. Look for errors indicating missing or corrupted
  settings sections.
- Click **Auto-repair** if auto-repairable issues are shown.
- If the problem persists, the settings file can be restored from a backup copy of the
  `%AppData%\GreenSwampAlpaca\{version}\` folder.

### The Status page shows stale or zero position data

- The mount must be **connected** (Mount Connected = Yes on the Status page).
- For SkyWatcher devices, confirm the serial connection settings are correct.
- Check the Monitor page for error messages from the mount driver.

### A setting change has no effect

- Confirm you clicked **Save** after making the change. Unsaved changes are shown with an amber
  dot on the tree node.
- Some server settings (e.g. port number) require a server restart to take effect.

### Chart window is blocked by the browser

Chart windows open via `window.open()`. Most modern browsers block pop-ups by default for sites
that have not been explicitly allowed.

**Symptoms:** Clicking *RA / Dec Chart* or *Pulse Chart* in the navigation menu shows a
yellow **"Chart window was blocked"** snackbar at the top of the main window. The browser may
also show a pop-up blocked indicator in the address bar.

**Resolution:**

1. Click the pop-up blocked icon in the browser's address bar.
2. Choose **Always allow pop-ups from `http://localhost:31416`** (or whichever port is configured).
3. Click the chart link again — the window will open normally.

> Charts can also be opened directly by navigating to `/charts/radec` or `/charts/pulse` in
> any browser tab; the launcher in the navigation menu is a convenience shortcut only.

---

## 14. Appendix A — File and Directory Locations

This appendix lists every directory and file that the application reads or writes at runtime,
covering both the Windows installer deployment and the Linux `.deb` package deployment.

---

### A.1 Application Binaries

The executable, supporting assemblies, and static web assets that make up the server itself.

#### Windows (MSI installer)

```
C:\Program Files\GreenSwamp\Alpaca Server\
  GreenSwamp.Alpaca.Server.exe          ← main executable
  *.dll                                  ← runtime assemblies
  appsettings.json                       ← built-in default settings (read-only)
  wwwroot\                               ← Blazor static web assets
```

The installer registers a Windows Service named **GreenSwampAlpacaServer** pointing at
`GreenSwamp.Alpaca.Server.exe` in the directory above.

#### Linux (.deb package)

```
/opt/greenswamp/alpaca-server/
  GreenSwamp.Alpaca.Server               ← main executable (chmod +x)
  *.dll                                  ← runtime assemblies
  appsettings.json                       ← built-in default settings (read-only)
  wwwroot/                               ← Blazor static web assets
```

The package installs a systemd unit at `/lib/systemd/system/greenswamp-alpaca.service`.
The service runs as the dedicated system account **greenswamp**.

---

### A.2 User Configuration Files

All settings that you can change through the UI are stored in a versioned sub-folder whose
name matches the application version (e.g. `1.2.3`). The folder is created automatically on
first run.

| File | Contents |
|---|---|
| `appsettings.user.json` | Main settings: all telescope devices, observatory location, and most server options |
| `appsettings.server.user.json` | Server configuration: port, network binding, authentication, identity |
| `monitor.settings.json` | Logging filter settings: device, category, and message-type filters; log file targets |

#### Interactive user — Windows

```
%AppData%\GreenSwampAlpaca\{version}\
  appsettings.user.json
  appsettings.server.user.json
  monitor.settings.json
```

Typical resolved path:
```
C:\Users\{YourName}\AppData\Roaming\GreenSwampAlpaca\1.2.3\
```

#### Interactive user — Linux

```
~/.config/GreenSwampAlpaca/{version}/
  appsettings.user.json
  appsettings.server.user.json
  monitor.settings.json
```

Typical resolved path:
```
/home/{yourname}/.config/GreenSwampAlpaca/1.2.3/
```

#### Windows Service (MSI installer)

When running as a Windows SCM service the settings root moves to the shared-documents area
so all Windows user accounts can share the same configuration:

```
C:\Users\Public\Documents\GreenSwampServer\{version}\
  appsettings.user.json
  appsettings.server.user.json
  monitor.settings.json
```

#### Linux systemd service (.deb package)

When running as the **greenswamp** system account the settings root is that account's home
directory:

```
/home/greenswamp/GreenSwampServer/{version}/
  appsettings.user.json
  appsettings.server.user.json
  monitor.settings.json
```

> **Override:** The settings root can be redirected independently of the run mode using the
> `GREENSWAMP_SETTINGS_PATH` environment variable or the
> `--service-settings-path=<path>` command-line argument. When either override is active
> the log files also move into a `Logs\` sub-folder of that root (see Section A.3).

> **Backup tip:** Copy the entire versioned folder to preserve a complete working
> configuration. Restoring it to the same path will fully recover all settings.

---

### A.3 Log and Monitor Files

Log files record session activity, errors, and detailed mount communication for
diagnostics. All log file names include a `YYYY-MM-DD` date stamp appended before the
extension, so a new file is created each day.

| File prefix | Contents | Enabled by |
|---|---|---|
| `GSSessionLog` | Information, Warning, and Error entries — the primary session record | **Log Session** toggle in Monitor → Logging Control |
| `GSErrorLog` | Warnings and errors only — a focused error-only record | Always written when monitoring is active |
| `GSMonitorLog` | All filtered monitor entries — verbose full log | **Log Monitor** toggle in Monitor → Logging Control |

#### Interactive user — Windows

```
%USERPROFILE%\Documents\GreenSwamp\Server\
  GSSessionLog_YYYY-MM-DD.txt
  GSErrorLog_YYYY-MM-DD.txt
  GSMonitorLog_YYYY-MM-DD.txt
```

Typical resolved path:
```
C:\Users\{YourName}\Documents\GreenSwamp\Server\
```

#### Interactive user — Linux

```
~/GreenSwamp/Server/
  GSSessionLog_YYYY-MM-DD.txt
  GSErrorLog_YYYY-MM-DD.txt
  GSMonitorLog_YYYY-MM-DD.txt
```

#### Windows Service / Linux systemd service (or path override)

When a service or path override is active the log files move into a `Logs\` sub-folder of
the settings root:

**Windows service:**
```
C:\Users\Public\Documents\GreenSwampServer\Logs\
  GSSessionLog_YYYY-MM-DD.txt
  GSErrorLog_YYYY-MM-DD.txt
  GSMonitorLog_YYYY-MM-DD.txt
```

**Linux systemd service:**
```
/home/greenswamp/GreenSwampServer/Logs/
  GSSessionLog_YYYY-MM-DD.txt
  GSErrorLog_YYYY-MM-DD.txt
  GSMonitorLog_YYYY-MM-DD.txt
```

On Linux, systemd also captures `stdout` and `stderr` from the process and sends them
to the journal. Use `journalctl -u greenswamp-alpaca` to view these entries.

> **Custom log path:** The log directory can be redirected by setting a custom path in
> **Settings Explorer → Logging → Logging Control → Log Path**. When the field is blank
> the defaults described above apply.

---

### A.4 Summary Table

| Item | Windows (interactive) | Windows (service) | Linux (interactive) | Linux (systemd service) |
|---|---|---|---|---|
| **Binaries** | `%ProgramFiles%\GreenSwamp\Alpaca Server\` | same | — | `/opt/greenswamp/alpaca-server/` |
| **Config files** | `%AppData%\GreenSwampAlpaca\{ver}\` | `%PUBLIC%\Documents\GreenSwampServer\{ver}\` | `~/.config/GreenSwampAlpaca/{ver}/` | `/home/greenswamp/GreenSwampServer/{ver}/` |
| **Log files** | `%USERPROFILE%\Documents\GSServer\` | `%PUBLIC%\Documents\GreenSwampServer\Logs\` | `~/GSServer/` | `/home/greenswamp/GreenSwampServer/Logs/` |
| **systemd unit** | — | — | — | `/lib/systemd/system/greenswamp-alpaca.service` |
| **Service account** | LocalSystem (SCM) | LocalSystem (SCM) | — | `greenswamp` (system user) |

---

*Green Swamp Alpaca Server — User Guide — 2026-07-09 22:51*
