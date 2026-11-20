<!-- greenswamp-installation:start -->
## Installation

Updated: 2026-11-20 19:03

Download the installer for your operating system from **Assets** below. These installers include the .NET runtime; a separate .NET installation is not required.

### Windows

1. Download `GreenSwamp.Alpaca.Server_x64.msi` for 64-bit Windows, or `GreenSwamp.Alpaca.Server_x86.msi` for 32-bit Windows.
2. Open the MSI, follow the installation wizard, and approve the administrator prompt. Keep automatic service startup selected to start the server during installation and on future boots.
3. Open **Start > GreenSwamp Alpaca Server > GreenSwamp Alpaca Server - Management UI**, or browse to http://localhost:31416.

If you select manual service startup, start **GreenSwamp Alpaca Server** in Windows Services before opening the management UI.

### Linux and Raspberry Pi

Use a Debian-based distribution with systemd, such as Raspberry Pi OS, Debian or Ubuntu. Choose the package that matches the installed operating system, not just the processor:

| Operating system | Download ending |
| --- | --- |
| 64-bit Intel/AMD Linux | `_amd64.deb` |
| 64-bit Raspberry Pi OS / ARM Linux | `_arm64.deb` |
| 32-bit Raspberry Pi OS / ARM Linux | `_armhf.deb` |

1. Download the matching DEB from **Assets**.
2. Open it with your software/package installer, select **Install**, and approve the administrator prompt. Alternatively, in a **Linux terminal** opened in the download folder, run `sudo apt install ./PACKAGE.deb`, replacing `PACKAGE.deb` with the exact downloaded filename.
3. Reboot. The installer enables the server for startup at boot but does not start it immediately. To start without rebooting, run `sudo systemctl start greenswamp-alpaca.service` in the Linux terminal.
4. On Raspberry Pi, open **main menu > Astronomy > GreenSwamp Alpaca Server**. You can also browse to http://localhost:31416. Other desktops may also list the launcher under Science.

The menu entry opens the management UI; it does not start the service. On a headless installation, use a browser on another computer as described below.

### macOS

Requires macOS 12 or later.

1. Download the DMG ending in `osx-arm64.dmg` for an Apple silicon Mac, or `osx-x64.dmg` for an Intel Mac. Check **Apple menu > About This Mac** if unsure.
2. Open the DMG and drag the entire **GreenSwamp Alpaca Server** folder to **Applications**. Keep the application and its command helper together.
3. Open that folder in Applications and launch **GreenSwamp Alpaca Server**, then browse to http://localhost:31416.

For optional startup at login, close any manually running server, then double-click **Configure Background Startup.command** in the installed folder. Choose **Enable**, read the warnings, and type **ENABLE** to accept. This creates a LaunchAgent for your account without administrator access; it runs while you are logged in, not at boot before login. Service settings use `~/GreenSwampServer` and are not automatically copied from interactive settings. Use the same helper to check status or disable startup; disabling requires typing **DISABLE** and stops the background server without deleting settings. Do not move or rename the installed folder while enabled.

Without this opt-in setup, launch the application manually after restarting your Mac. During upgrades, disable background startup before replacing the installed folder, then enable it again using the new helper. If macOS blocks the downloaded helper, review the warning and use **System Settings > Privacy & Security > Open Anyway** only if you trust this release; do not disable Gatekeeper globally.

### Access from another computer

Keep the server running and browse to `http://SERVER-IP:31416`, replacing `SERVER-IP` with the server computer's local-network IP address. If a firewall blocks access, allow TCP port 31416 on your trusted local network. Do not expose the server directly to the internet.

---
<!-- greenswamp-installation:end -->
