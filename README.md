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

Open the DMG and drag **GreenSwamp Alpaca Server** to **Applications**. Launch the application from the **Applications** folder, then browse to `http://localhost:31416`.

There is no application menu entry on macOS. The DMG does not install an automatic background service. Launch the application manually after restarting your Mac, or configure `launchd` as described below.

#### Optional background startup with launchd

The server supports `--service` mode under **launchd**, the macOS service manager analogous to systemd. A **LaunchAgent** runs while your account is logged in; a **LaunchDaemon** can run at boot without anyone logging in. The following setup uses a LaunchAgent and requires no administrator privileges.

1. Close any manually running GreenSwamp Alpaca Server instance before enabling background startup.
2. In **macOS Terminal** (not Windows PowerShell), run `mkdir -p "$HOME/Library/LaunchAgents"`.
3. Using a plain-text editor, save the following as `~/Library/LaunchAgents/com.greenswamp.alpaca.server.plist`. These paths assume the application was installed in `/Applications` with its original name.

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
  <dict>
    <key>Label</key>
    <string>com.greenswamp.alpaca.server</string>
    <key>ProgramArguments</key>
    <array>
      <string>/Applications/GreenSwamp Alpaca Server.app/Contents/MacOS/GreenSwampAlpacaLauncher</string>
      <string>--service</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>StandardOutPath</key>
    <string>/tmp/greenswamp-alpaca.out.log</string>
    <key>StandardErrorPath</key>
    <string>/tmp/greenswamp-alpaca.err.log</string>
  </dict>
</plist>
```

4. In **macOS Terminal**, validate the file, enable the agent, and load it to start the server:

```sh
plutil -lint "$HOME/Library/LaunchAgents/com.greenswamp.alpaca.server.plist"
launchctl enable "gui/$(id -u)/com.greenswamp.alpaca.server"
launchctl bootstrap "gui/$(id -u)" "$HOME/Library/LaunchAgents/com.greenswamp.alpaca.server.plist"
```

Proceed only if validation reports `OK`. Once loaded, the server starts at subsequent logins and launchd restarts it if it exits. Open `http://localhost:31416` yourself; service mode does not launch a browser. Do not start a second server instance from Applications while the agent is running.

To check status, run `launchctl print "gui/$(id -u)/com.greenswamp.alpaca.server"`. To stop it and prevent future automatic startup, run these commands in **macOS Terminal**:

```sh
launchctl disable "gui/$(id -u)/com.greenswamp.alpaca.server"
launchctl bootout "gui/$(id -u)" "$HOME/Library/LaunchAgents/com.greenswamp.alpaca.server.plist"
```

Service mode uses `~/GreenSwampServer` for settings, with version subfolders, and `~/GreenSwampServer/Logs` for application logs. This differs from interactive mode, so existing settings may need to be transferred while the server is stopped. Launch output and errors are recorded in the `/tmp` files specified above.

The repository's older launchd template uses a different application path; use the current DMG paths shown here. A system-wide LaunchDaemon requires a separately configured service account, writable settings/log locations and administrator installation; it is not installed by the DMG or covered by the per-user steps above. See [Apple's launchd documentation](https://developer.apple.com/library/archive/documentation/MacOSX/Conceptual/BPSystemStartup/Chapters/CreatingLaunchdJobs.html) for the agent/daemon distinction.

