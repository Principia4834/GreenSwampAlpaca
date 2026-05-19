# Cross-Platform Publishing Plan
# GreenSwamp Alpaca Server — Windows / Linux / Raspberry Pi

**Last updated: 2026-05-19 09:24**

---

## 1. Scope

Produce self-contained, single-file publish artefacts for the following runtime targets:

| Platform | RID | Notes |
|---|---|---|
| Windows x64 | `win-x64` | Primary dev target; replaces current `win-x86` profile |
| Windows x86 | `win-x86` | Retained for legacy 32-bit hosts |
| Linux x64 | `linux-x64` | Ubuntu / Debian desktop / server |
| Raspberry Pi 4/5 (64-bit OS) | `linux-arm64` | Recommended: Raspberry Pi OS 64-bit |
| Raspberry Pi 3/4 (32-bit OS) | `linux-arm` | Optional: Raspberry Pi OS 32-bit |

---

## 2. What the Publish Pipeline Needs to Do

```
dotnet publish GreenSwamp.Alpaca.Server\GreenSwamp.Alpaca.Server.csproj \
  -c Release \
  -r <RID> \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=false   ← keep false until trimming is validated
```

**Publish trimming is intentionally left OFF** until each blocker below has been resolved and tested.
Blazor Server, reflection-heavy ASCOM libraries, and Newtonsoft.Json are all hostile to aggressive trimming.

---

## 3. Architectural Blockers — Must Fix Before Cross-Platform Publish

### BLOCKER 1 — Windows Multimedia Timer (`MediaTimer` + `NativeMethods`)
**Severity: Critical**
**Files:**
- `GreenSwamp.Alpaca.Principles\NativeMethods.cs` — P/Invokes `winmm.dll!timeGetDevCaps`, `timeSetEvent`, `timeKillEvent`
- `GreenSwamp.Alpaca.Principles\MediaTimer.cs` — entire implementation wraps `NativeMethods`

**Impact:** `MediaTimer` is the high-frequency mount pulse timer. It will throw `DllNotFoundException` on Linux and Raspberry Pi the moment a mount connection is attempted.

**Recommended Fix:**
1. Extract `IMediaTimer` interface from `MediaTimer`.
2. Keep `MediaTimer` (Windows implementation) guarded with `[SupportedOSPlatform("windows")]`.
3. Add `LinuxMediaTimer` backed by `System.Threading.PeriodicTimer` or `System.Timers.Timer` with `AutoReset = true`.
   - Note: `PeriodicTimer` (available in .NET 6+) provides ~1 ms precision on Linux with `SCHED_FIFO` — sufficient for SkyWatcher comms (typical poll period ≥ 10 ms).
4. Register the correct implementation in DI based on `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)`.

---

### BLOCKER 2 — Windows High-Resolution Clock (`HiResDateTime`)
**Severity: High**
**Files:**
- `GreenSwamp.Alpaca.Principles\NativeMethods.cs` — P/Invokes `Kernel32.dll!GetSystemTimePreciseAsFileTime`
- `GreenSwamp.Alpaca.Principles\HiResDateTime.cs` — calls `NativeMethods.GetSystemTimePreciseAsFileTime()` on the `IsPrecise` fast-path

**Impact:** `GetSystemTimePreciseAsFileTime` is available only on Windows 8+ / Server 2012+.
On Linux the code already falls through to the `Stopwatch`-based fallback — **this blocker is lower priority than Blocker 1** because the fallback path exists.

**Recommended Fix:**
- Guard the P/Invoke call with `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` (or add `[SupportedOSPlatform("windows")]` to `NativeMethods.GetSystemTimePreciseAsFileTime`).
- On Linux/ARM the `Stopwatch`-based path in `HiResDateTime.UtcNow` is already correct; just ensure `IsPrecise` is `false` on non-Windows.
- `Stopwatch.IsHighResolution` is `true` on modern Linux kernels (uses `CLOCK_MONOTONIC`) — no accuracy loss in practice.

---

### BLOCKER 3 — `SetLocalTime` P/Invoke (`NativeMethods`)
**Severity: Medium**
**File:** `GreenSwamp.Alpaca.Principles\NativeMethods.cs` line 39

```csharp
[DllImport("kernel32.dll", SetLastError = true)]
internal static extern bool SetLocalTime(ref Time.SystemTime time);
```

**Impact:** Attempting to set system time on Linux will throw `DllNotFoundException`.

**Recommended Fix:**
- Guard all call sites with `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)`.
- On Linux, document that system time sync is the responsibility of `chronyd`/`ntpd`; expose a warning in the UI if time drift is detected rather than attempting to set it.

---

### BLOCKER 4 — `user32.dll` P/Invokes (Window Management)
**Severity: Low — UI only, non-critical path**
**File:** `GreenSwamp.Alpaca.Principles\NativeMethods.cs` lines 55–70
- `SetForegroundWindow`, `ShowWindowAsync`, `IsIconic`

**Impact:** These are called only from `NativeMethods.SetForegroundWindow(string name)` which is a "bring to front" helper. They are not in the critical mount control path.

**Recommended Fix:**
- Add `if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;` guards at the entry of `NativeMethods.SetForegroundWindow(string name)`.
- Add `[SupportedOSPlatform("windows")]` attributes.

---

### BLOCKER 5 — Duplicate-Process Detection via `IPGlobalProperties` (Linux path broken)
**Severity: Medium**
**File:** `GreenSwamp.Alpaca.Server\Program.cs` lines 87–95

```csharp
else
{
	Assembly? entryAssembly = Assembly.GetEntryAssembly();
	if (entryAssembly != null)
	{
		if(Process.GetProcessesByName(entryAssembly.Location).Length > 1)
```

**Impact:** `Assembly.Location` returns an empty string for single-file published apps on Linux. `Process.GetProcessesByName("")` returns **all** processes — causing a false-positive "already running" detection and immediate exit on first launch.

**Recommended Fix:**
```csharp
var processName = Path.GetFileNameWithoutExtension(
	Environment.ProcessPath ?? entryAssembly.Location);
if (!string.IsNullOrWhiteSpace(processName) &&
	Process.GetProcessesByName(processName).Length > 1)
```
Use `Environment.ProcessPath` (.NET 6+) which works correctly for single-file executables.

---

### BLOCKER 6 — `GSFile` File/Folder Pickers
**Severity: Low — already stubbed**
**File:** `GreenSwamp.Alpaca.Shared\GSFile.cs`

Both `GetFileName` and `GetFolderName` have `// ToDo implement a cross-platform file picker` comments and return stub values.

**Impact:** No runtime exception, but the feature is non-functional on all platforms. On a headless Raspberry Pi this is correct behaviour — file selection must be done via the Blazor web UI.

**Recommended Fix (deferred):**
- For the Blazor UI path, implement file selection using an `<InputFile>` component (MudBlazor `MudFileUpload`) — no desktop dialog required.
- Keep the stubs; mark them `[Obsolete]` to surface the gap.

---

### BLOCKER 7 — Assembly Signing Key (`ASCOM.snk`)
**Severity: Medium**
**Files:** `GreenSwamp.Alpaca.MountControl.csproj`, `GreenSwamp.Alpaca.Principles.csproj`, `GreenSwamp.Alpaca.Mount.SkyWatcher.csproj`

All three projects use `<SignAssembly>True</SignAssembly>` with `<AssemblyOriginatorKeyFile>Resources\ASCOM.snk</AssemblyOriginatorKeyFile>`.

**Impact:** The `.snk` file must be present on the build agent for all CI/CD cross-platform builds (GitHub Actions, etc.). If it is `.gitignore`d (check), CI builds will fail.

**Recommended Fix:**
- Confirm `ASCOM.snk` is committed to the repo or stored as a repository secret.
- In CI, restore the key file from a secret before `dotnet publish`.

---

## 4. Design Concerns (Non-Blocking but Should Be Addressed)

### DC-1 — `MediaTimer` Timing Precision on Linux/Raspberry Pi
The SkyWatcher protocol is time-sensitive (motion commands rely on accurate periodic polling).
On Raspberry Pi with a standard kernel, `System.Timers.Timer` jitter is typically 1–5 ms.
A real-time kernel patch (`PREEMPT_RT`) brings this to < 100 µs.
**Recommendation:** Document the minimum kernel requirement for mount control; suggest PREEMPT_RT kernel for users experiencing tracking inaccuracy.

### DC-2 — Serial Port Naming
`System.IO.Ports` is fully cross-platform. However:
- Windows names: `COM3`, `COM4`
- Linux names: `/dev/ttyUSB0`, `/dev/ttyACM0`, `/dev/serial0` (Pi GPIO UART)

The settings UI currently accepts free-text port input. On Linux, users must also be in the `dialout` group.
**Recommendation:** Add a port discovery helper that calls `SerialPort.GetPortNames()` and presents the result in the port picker dropdown.

### DC-3 — `AutoStartBrowser` on Headless Linux / Raspberry Pi
`Process.Start` with `UseShellExecute = true` will fail silently (or throw) on a headless Pi with no desktop session.
**Recommendation:** Catch the exception (already partially done), log a clear message: *"Headless mode — navigate to http://{host}:{port} from another device"*, and set `AutoStartBrowser = false` as the default for Linux/ARM builds.

### DC-4 — `%AppData%` Settings Path
`Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)` correctly resolves to:
- Windows: `C:\Users\<user>\AppData\Roaming`
- Linux: `$HOME/.config`
- Raspberry Pi: `$HOME/.config`

This is already cross-platform-safe. No change needed.

### DC-5 — `JPLEPH` Data File
The JPL Ephemeris binary file is included both as a content file in the project and via the `ASCOM.AstrometryTools` NuGet package. Verify the publish profile copies it alongside the output binary (`CopyToOutputDirectory = Always`). This file has no platform dependency.

### DC-6 — Single-File Publish and `Assembly.Location`
When `PublishSingleFile=true`, `Assembly.Location` returns `""`. Any code using `Assembly.GetExecutingAssembly().Location` to resolve relative paths **will break** on all platforms.
**Search for all usages:**
```powershell
Get-ChildItem -Path . -Recurse -Filter '*.cs' | 
	Select-String -Pattern 'Assembly.*Location|GetExecutingAssembly'
```
Replace with `AppContext.BaseDirectory` or `Environment.ProcessPath`.

---

## 5. Publish Profile Matrix

Create one `.pubxml` file per target in `GreenSwamp.Alpaca.Server\Properties\PublishProfiles\`:

| File | RID | SelfContained | SingleFile | Trimmed |
|---|---|---|---|---|
| `win-x64.pubxml` | `win-x64` | true | true | false |
| `win-x86.pubxml` | `win-x86` | true | true | false |
| `linux-x64.pubxml` | `linux-x64` | true | true | false |
| `linux-arm64.pubxml` | `linux-arm64` | true | true | false |
| `linux-arm.pubxml` | `linux-arm` | true | true | false |

> **Note:** `PublishTrimmed=false` across all profiles until trimming compatibility is audited.
> Blazor Server, MudBlazor, Newtonsoft.Json, and ASCOM reflection-heavy libraries all require
> trim suppressions before trimming is safe.

---

## 6. Recommended Implementation Order

| Priority | Task | Blocker Ref |
|---|---|---|
| 1 | Fix `Assembly.Location` / duplicate-process detection | BLOCKER 5 |
| 2 | Guard `user32.dll` P/Invokes | BLOCKER 4 |
| 3 | Guard `SetLocalTime` / `GetSystemTimePreciseAsFileTime` P/Invokes | BLOCKERS 2, 3 |
| 4 | Extract `IMediaTimer` + Linux implementation | BLOCKER 1 |
| 5 | Confirm `ASCOM.snk` availability in CI | BLOCKER 7 |
| 6 | Create publish profiles for all 5 RIDs | Section 5 |
| 7 | Validate serial port naming in settings UI | DC-2 |
| 8 | Suppress `AutoStartBrowser` on headless Linux | DC-3 |
| 9 | Audit `Assembly.Location` usages across solution | DC-6 |
| 10 | Document Linux setup: `dialout` group, port names, optional PREEMPT_RT | DC-1 |

---

## 7. CI/CD Notes (GitHub Actions)

```yaml
# Example matrix publish job
strategy:
  matrix:
	rid: [win-x64, win-x86, linux-x64, linux-arm64, linux-arm]

steps:
  - name: Restore signing key
	run: echo "${{ secrets.ASCOM_SNK_B64 }}" | base64 -d > Resources/ASCOM.snk
	working-directory: GreenSwamp.Alpaca.MountControl

  - name: Publish
	run: |
	  dotnet publish GreenSwamp.Alpaca.Server/GreenSwamp.Alpaca.Server.csproj `
		-c Release `
		-r ${{ matrix.rid }} `
		--self-contained true `
		-p:PublishSingleFile=true `
		-p:PublishTrimmed=false
```

> PowerShell backtick line continuation is used above; adjust for bash runners.

---

## 8. Raspberry Pi Deployment Notes

1. **OS:** Raspberry Pi OS 64-bit (Bookworm) is recommended — matches `linux-arm64` RID.
2. **Runtime:** Self-contained publish bundles the .NET 8 runtime — no separate install needed.
3. **Serial access:** Add the service account to the `dialout` group:
   ```bash
   sudo usermod -aG dialout $USER
   ```
4. **Autostart:** Use a `systemd` unit file to start the server on boot.
   ```ini
   [Unit]
   Description=GreenSwamp Alpaca Server
   After=network.target

   [Service]
   ExecStart=/opt/greenswamp/GreenSwamp.Alpaca.Server
   WorkingDirectory=/opt/greenswamp
   Restart=on-failure
   User=pi

   [Install]
   WantedBy=multi-user.target
   ```
5. **Firewall:** Open port `31426` (default) in `ufw`:
   ```bash
   sudo ufw allow 31426/tcp
   ```
6. **Timing:** For demanding tracking workloads, consider a PREEMPT_RT patched kernel to reduce timer jitter below 1 ms.

---

## 9. Implementation Status

### Completed (steps 1–10)

| Step | Commit / File | Status |
|------|--------------|--------|
| 1 — Duplicate-process detection | `d32b27e` | ✅ |
| 2 — `user32.dll` P/Invoke guards | `b0f34fe` | ✅ |
| 3 — `kernel32` time-set guards + `HiResDateTime` safe fallback | `37b6a95` | ✅ |
| 4 — `IMediaTimer` / `LinuxMediaTimer` / `MediaTimerFactory` | `0e4e415` | ✅ |
| 5 — CI workflow (`.github/workflows/publish.yml`) | this commit | ✅ |
| 6 — Publish profiles (win-x64/x86, linux-x64/arm64/arm) | this commit | ✅ |
| 7 — `SerialPortDiscovery` helper + port dropdown in UI | this commit | ✅ |
| 8 — `AutoStartBrowser` & `StartBrowser` Windows-only guards | this commit | ✅ |
| 9 — `Assembly.Location` audit (no remaining usages found) | n/a | ✅ |
| 10 — Plan document final update | this commit | ✅ |

### Known remaining considerations

- **`ASCOM.snk`** — strong-name signing keys are committed to each project's `Resources\` folder. No action required for local builds. If the key is ever removed from source control, add it as a CI secret `ASCOM_SNK_B64` and uncomment the restore step in `.github/workflows/publish.yml`.
- **Trimming** — `PublishTrimmed=false` is intentional; ASCOM reflection-heavy code is not trim-safe. Revisit when/if ASCOM libraries publish trim metadata.
- **Serial port on Pi** — the service user must be a member of the `dialout` group (see § 8.3 above).
- **Browser auto-start** — suppressed on all non-Windows targets. On Linux/Pi, navigate to `http://<host>:31426` from any browser on the network.

---

*End of plan — GreenSwamp Alpaca Server cross-platform publishing.*
