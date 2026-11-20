# GreenSwamp Alpaca

**ASCOM Alpaca Server for Telescope Mount Control**

A modern .NET 10 Blazor-based ASCOM Alpaca server that provides remote control of astronomical telescope mounts via a RESTful HTTP API. Supports both simulated mounts and physical SkyWatcher/Orion mounts.

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/download)
[![ASCOM Alpaca](https://img.shields.io/badge/ASCOM-Alpaca-00A4EF)](https://ascom-standards.org/)

---

## Features

### 🔭 **Mount Support**
- **SkyWatcher/Orion Mounts** - Full support for SkyWatcher protocol mounts via serial/USB connection
- **Built-in Simulator** - Test and develop without physical hardware
- **Multiple Mount Types** - Supports German Equatorial (GEM), Alt-Az, and Polar mounts

### 🌐 **ASCOM Alpaca Compliance**
- **ASCOM Alpaca v1 API** - Full ITelescopeV4 interface implementation
- **RESTful HTTP API** - Industry-standard JSON-based communication
- **Discovery Protocol** - Automatic device discovery on local network
- **Multi-client Support** - Connect multiple astronomy applications simultaneously

### 🎯 **Advanced Features**
- **Precision GOTO** - Multi-stage slewing with sub-arcsecond precision
- **Pulse Guiding** - Full support for autoguiding applications (PHD2, MetaGuide)
- **Tracking Modes** - Sidereal, Lunar, Solar, and King rates
- **Park Positions** - Multiple configurable park positions
- **Auto Home** - Automated homing using mount sensors
- **Coordinate Systems** - J2000, JNOW, topocentric conversions
- **Rate Offsets** - Custom tracking rate offsets for RA and Dec

### 🖥️ **Modern Web UI**
- **Blazor Server Interface** - Real-time responsive web interface
- **Mount Control Panel** - Manual slewing, GOTO, parking controls
- **Position Display** - Live RA/Dec, Alt/Az, and status information
- **Configuration Management** - Web-based settings and calibration
- **Monitoring & Logging** - Real-time operation logs and diagnostics

## Launching the application

### Windows Start menu

The MSI installer adds a **GreenSwamp Alpaca Server** folder to the Start menu with two shortcuts:

- **GreenSwamp Alpaca Server** launches the server application. Use this when the server is not already running as a service.
- **GreenSwamp Alpaca Server - Management UI** opens `http://localhost:31416` in the default browser. It does not start the server.

With automatic service startup selected, the installer starts the service during installation and configures it to start on future boots. If manual startup is selected, start **GreenSwamp Alpaca Server** in Windows Services before opening the management UI.

### Linux and Raspberry Pi application menu

The DEB installer adds **main menu > Astronomy > GreenSwamp Alpaca Server** on desktops that support XDG menu merging, including Raspberry Pi OS Bookworm's labwc desktop. It installs a menu fragment under `/etc/xdg/menus/applications-merged/` without replacing system or personal menu layouts. Menu ordering remains controlled by the desktop and personal customisations; other desktops may also list the launcher under Science.

The launcher opens `http://localhost:31416` in the default browser; it does not start the server. After installation, reboot to start the enabled service, or start `greenswamp-alpaca.service` manually before using the launcher.

### macOS Applications folder

Open the DMG and drag the entire **GreenSwamp Alpaca Server** folder to **Applications**. Keep the application and **Configure Background Startup.command** together. Open the installed folder and launch **GreenSwamp Alpaca Server**, then browse to `http://localhost:31416`. There is no application menu entry on macOS.

The DMG does not automatically enable a background service. Launch the application manually after restarting your Mac, or use the optional helper below.

#### Optional background startup with launchd

The bundled helper configures a per-user **LaunchAgent** using **launchd**, the macOS service manager analogous to systemd. No administrator privileges are required. The server starts at login, is restarted if it exits, and stops at logout. This is not a system-wide LaunchDaemon and does not start before login.

1. Close any manually running GreenSwamp Alpaca Server instance.
2. In **Applications > GreenSwamp Alpaca Server**, double-click **Configure Background Startup.command**. It opens Terminal with **Enable**, **Disable**, **Status** and **Cancel** options.
3. Select **Enable**, read the actions and warnings, then type **ENABLE** to accept. Pressing Return without acceptance cancels without changes.
4. The helper generates and validates `~/Library/LaunchAgents/com.greenswamp.alpaca.server.plist`, enables and starts the agent, and checks the management UI at the default port 31416. It refuses to enable an already loaded agent, a detected running server, or an occupied default port rather than stopping unidentified processes.
5. Open `http://localhost:31416` yourself; background service mode does not launch a browser. Do not launch a second instance from Applications while the agent is running.

Use **Status** to display launchd information and check the default management address. Use **Disable** and type **DISABLE** to stop the background server and prevent future automatic startup. Disabling interrupts connected clients but keeps the app, plist, settings and logs. To enable it again, rerun the helper and choose **Enable**.

The helper must be run from the installed folder under `/Applications` or your personal `~/Applications`, not from the DMG. Do not move or rename that folder while startup is enabled. During upgrades, disable startup before replacing the folder, then enable it using the new helper. If macOS blocks the downloaded helper, review the warning and use **System Settings > Privacy & Security > Open Anyway** only if you trust the release; do not disable Gatekeeper globally.

Service settings use `~/GreenSwampServer`, with version subfolders, and application logs use `~/GreenSwampServer/Logs`. These settings differ from interactive mode; the helper warns but does not migrate or overwrite existing settings. Launch output and errors are appended to `~/Library/Logs/GreenSwampAlpaca/launchd.out.log` and `launchd.err.log`.

If startup fails, the helper reports an error rather than claiming success. Once registration has occurred, the agent remains enabled until you choose **Disable**. Check **Status** and the launch logs; if you configured a different port, open that address instead of the default used by the helper's checks.

