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

## Linux and Raspberry Pi application menu

Updated: 2026-11-20 18:40

The DEB installer adds **main menu > Astronomy > GreenSwamp Alpaca Server** on desktops that support XDG menu merging, including Raspberry Pi OS Bookworm's labwc desktop. It installs a menu fragment under `/etc/xdg/menus/applications-merged/` without replacing system or personal menu layouts. Menu ordering remains controlled by the desktop and personal customisations; other desktops may also list the launcher under Science.

The launcher opens `http://localhost:31416` in the default browser; it does not start the server. After installation, reboot to start the enabled service, or start `greenswamp-alpaca.service` manually before using the launcher.

