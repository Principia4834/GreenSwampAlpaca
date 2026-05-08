# GreenSwamp Alpaca Server — UI High-Level Design Document

**Document version:** 1.0  
**Generated:** 2026-05-07 08:49  
**Prepared for:** Andy  
**Target project:** `GreenSwamp.Alpaca.Server` (.NET 8 Blazor Server)  
**UI framework:** MudBlazor (dark theme, responsive drawer)

---

## 1. Technology Overview

The UI is a **Blazor Server** application hosted over SignalR. All pages are Razor
components (`.razor`) rendered server-side and pushed to the browser in real time.
The component library used throughout is **MudBlazor**, providing the app bar,
drawer, navigation, cards, tables, tabs, forms and dialogs.

---

## 2. Application Shell

### 2.1 Entry Point — `App.razor`

| File | Role |
|------|------|
| `GreenSwamp.Alpaca.Server\App.razor` | Root Blazor router. Maps all `@page` routes to `MainLayout`. Displays a generic "not found" message for unrecognised URLs. |

### 2.2 Layout — `Shared\MainLayout.razor`

The single layout applied to every page. Renders three structural regions:

| Region | Component | Detail |
|--------|-----------|--------|
| **App Bar** | `<MudAppBar>` | Dense, dark. Contains the hamburger toggle for the drawer, the **GSAlpaca** brand heading, and an external *About* link to `https://greenswamp.org/`. |
| **Side Drawer** | `<MudDrawer>` | Responsive (collapses on small screens). Hosts `<NavMenu />`. Toggled by the app bar button via `_drawerOpen`. |
| **Main Content** | `<MudMainContent>` | Full-width `MudContainer` (no max-width cap). Renders `@Body` — the active page. |

Theme: `GsTheme.Instance` (dark mode), defined in `Theme\GsTheme.cs`.

### 2.3 Host Pages

| File | Role |
|------|------|
| `Pages\_Host.cshtml` | Razor Page that bootstraps the Blazor circuit (`<component type="typeof(App)" ...>`). |
| `Pages\_Layout.cshtml` | Shared MVC/Razor layout used by `_Host` and the error page (loads CSS/JS). |
| `Pages\Error.cshtml` / `Error.cshtml.cs` | Standard ASP.NET error page, rendered outside the Blazor circuit for fatal errors. |

---

## 3. Navigation Menu — `Shared\NavMenu.razor`

The navigation menu renders a `<MudNavMenu>` inside the drawer. Items are grouped
into two logical sections separated by a `<MudDivider>`.

### 3.1 Dynamic Device Links (Section 1 — above divider)

These links are generated at runtime by iterating `ASCOM.Alpaca.DeviceManager`
collections. A nav link is added **only if at least one device of that type is
registered**.

| Device Type | Route Pattern | MudBlazor Icon |
|-------------|---------------|----------------|
| Home | `/` | `Home` |
| Camera (n) | `/setup/v1/Camera/{n}/setup` | `PhotoCamera` |
| CoverCalibrator (n) | `/setup/v1/covercalibrator/{n}/setup` | `Contrast` |
| Dome (n) | `/setup/v1/dome/{n}/setup` | `Circle` |
| Filter Wheel (n) | `/setup/v1/filterwheel/{n}/setup` | `FilterCenterFocus` |
| Focuser (n) | `/setup/v1/focuser/{n}/setup` | `Adjust` |
| ObservingConditions (n) | `/setup/v1/ObservingConditions/{n}/setup` | `Cloud` |
| Rotator (n) | `/setup/v1/rotator/{n}/setup` | `RotateRight` |
| SafetyMonitor (n) | `/setup/v1/SafetyMonitor/{n}/setup` | `Shield` |
| Switch (n) | `/setup/v1/switch/{n}/setup` | `ToggleOn` |
| **Mount Settings** | `/mount-devices` | `TravelExplore` |

> Note: "Telescope" devices are reached via the Mount Settings page rather than having
> individual top-level nav links. The ASCOM `/setup/v1/Telescope/{n}/setup` route is
> linked from the Device Manager and Mount Settings pages.

### 3.2 Static Administrative Links (Section 2 — below divider)

| Label | Route | MudBlazor Icon |
|-------|-------|----------------|
| Server Settings | `/setup` | `Settings` |
| Device Manager | `/device-manager` | `DeviceHub` |
| Settings Health | `/settings-health` | `MonitorHeart` |
| Monitor Settings | `/monitorsettings` | `ListAlt` |
| Mount State | `/mount-state` | `DisplaySettings` |

---

## 4. Page Inventory

### 4.1 Home — `Pages\Index.razor`

| Attribute | Value |
|-----------|-------|
| **Route** | `/` |
| **Nav label** | Home |
| **Page title** | Home – Green Swamp Alpaca Server |

**Responsibility:** Marketing / welcome landing page. Displays a hero section with the
product name, a four-bullet feature summary, and two external links (Green Swamp
Software, ASCOM Standards). No state, no services injected, fully static markup.

**Navigates to:** Nothing (external links only).

---

### 4.2 Server Settings — `Pages\Setup.razor`

| Attribute | Value |
|-----------|-------|
| **Route** | `/setup` |
| **Nav label** | Server Settings |
| **Page title** | Server Settings |
| **Services** | `IVersionedSettingsService`, `NavigationManager` |

**Responsibility:** Configure and save global Alpaca server behaviour. The page is
divided into three `<MudCard>` groups:

1. **Network card** — Server Location (text), Alpaca Port (numeric), Allow Remote
   Access (checkbox), Allow Discovery (checkbox), Respond to localhost on loopback only
   (checkbox). Also displays a live list of bound server addresses.
2. **Security / Auth card** — Require Authorization (checkbox), Username, Password,
   Confirm Password fields; strict API mode; prevent remote disconnects.
3. **Browser / Swagger / Shutdown card** — Auto-start browser (checkbox), run Swagger
   UI (checkbox), server shutdown button.

Dirty-state tracking prevents accidental saves. On save, if `RequireAuthorization` is
enabled the user is redirected back to `/`.

**Navigates to:** `/` (post-save when auth is enabled), otherwise stays on page.

---

### 4.3 Device Manager — `Pages\DeviceManager.razor`

| Attribute | Value |
|-----------|-------|
| **Routes** | `/device-manager`, `/devices` |
| **Nav label** | Device Manager |
| **Page title** | Connected Devices |
| **Services** | `IVersionedSettingsService`, `UnifiedDeviceRegistry` |
| **Implements** | `IDisposable` |

**Responsibility:** Lifecycle management of ASCOM Alpaca telescope devices. Two sections:

1. **All Devices table** — Lists every configured `SkySettings` device with columns:
   Device #, Name, Mount, Port, Enabled chip, Connection Status chip. Per-row action
   buttons:
   - **Connect / Disconnect** — toggles live hardware connection.
   - **Setup** (link) — opens `/setup/v1/telescope/{n}/setup` in a new tab.
   - **Settings** (link) — navigates to `/mountsettings/{n}`.
   - **Remove** — deletes device and its settings file.
2. **Add Device form** (collapsible) — Device Number, Device Name, Mount Type (Simulator
   / SkyWatcher), Serial Port, Alignment Mode. Creates the `device-nn.settings.json`
   file immediately; a restart is required to activate the device at runtime.

A periodic refresh timer (via `IDisposable`) keeps the status column up to date.

**Navigates to:**
- `/setup/v1/telescope/{n}/setup` (new tab, per-device ASCOM setup)
- `/mountsettings/{n}` (full mount settings editor)

---

### 4.4 Mount Settings (Overview) — `Pages\MountDevices.razor`

| Attribute | Value |
|-----------|-------|
| **Route** | `/mount-devices` |
| **Nav label** | Mount Settings |
| **Page title** | Mount Settings |
| **Services** | `IVersionedSettingsService` |

**Responsibility:** A tabbed overview of all registered telescope devices. One
`<MudTabPanel>` per device (tab label = device name from settings, fallback to
"Device N"). Each tab renders a read-only summary card using the embedded
`<TelescopeSetup>` component and provides an **Edit Settings** button.

**Navigates to:** `/mountsettings/{n}` (via `EditDevice()` method → `NavigationManager`).

---

### 4.5 Mount Settings (Editor) — `Pages\MountSettings.razor`

| Attribute | Value |
|-----------|-------|
| **Routes** | `/mountsettings`, `/mountsettings/{DeviceNumber:int}` |
| **Nav label** | Reached from Device Manager / Mount Devices |
| **Page title** | Mount Settings — Device N |
| **Services** | `IVersionedSettingsService`, `NavigationManager` |

**Responsibility:** Full editable settings for a single telescope device. Uses
`<EditForm>` with `<DataAnnotationsValidator>`. Settings are organised into
**eight `<MudTabPanel>` tabs**:

| Tab | Icon | Key Fields |
|-----|------|------------|
| **Connection** | `Cable` | Mount Type (Simulator/SkyWatcher), Serial Port, Baud Rate, Data Bits, Handshake, Read Timeout, DTR, RTS |
| **Location** | `LocationOn` | Latitude, Longitude, Elevation, UTC Offset, Refraction correction, Temperature |
| **Mount Config** | `Tune` | Alignment Mode, Coordinate Type, Auto-track on connect, Home position X/Y, Polar Mode |
| **Park & Positions** | `FlagCircle` | Park Name, Park Limit Name, Park Horizon Limit, Park Axes (JSON), Park Positions (JSON), HC Pulse Guides (JSON) |
| **Optics** | `Visibility` | Aperture Diameter, Aperture Area, Focal Length, Eyepiece Field Stop |
| **Tracking** | `TrackChanges` | Tracking Rate (Sidereal/Lunar/Solar/King), individual rate values, Custom Gearing (RA/Dec steps, worm teeth) |
| **Guiding** | `MyLocation` | Min Pulse RA/Dec, Dec Pulse→GOTO, ST4 Guide Rate, Guide Rate Offsets, RA/Dec Backlash, PEC/PPEC toggles, PEC Mode |
| **Limits** | `Block` | Tracking limits enable, Hour Angle limit, Axis X/Y upper/lower limits, Sync limit, safety interlock settings |

A persistent save button and success/error alert appear at page level outside the tabs.

**Navigates to:** Stays on page after save.

---

### 4.6 Mount State — `Pages\MountState.razor`

| Attribute | Value |
|-----------|-------|
| **Routes** | `/mount-state`, `/mount-state/{DeviceNumber:int}` |
| **Nav label** | Mount State |
| **Page title** | Mount State |
| **Services** | `TelescopeStateService`, `IVersionedSettingsService` |
| **Implements** | `IDisposable` |

**Responsibility:** Real-time read-only dashboard of live mount telemetry. One
`<MudTabPanel>` per registered mount instance. Each tab is laid out in a responsive
`<MudGrid>` of four panels:

| Panel | Content |
|-------|---------|
| **Position** | RA (HMS), Dec (DMS), Altitude (DMS), Azimuth (DMS), Local Hour Angle (HMS), Pier Side |
| **Status** | Boolean status chips: Mount Running, Slewing, Tracking, At Park, At Home, Pulse Guiding |
| **Axis Positions** | Actual X/Y, Applied X/Y (degrees) |
| **Tracking & Guiding** | Tracking rate, guide pulse status |

State is polled from `TelescopeStateService`; the component disposes its subscription on
teardown.

**Navigates to:** Nothing (display only).

---

### 4.7 Settings Health Check — `Pages\SettingsHealthCheck.razor`

| Attribute | Value |
|-----------|-------|
| **Route** | `/settings-health` |
| **Nav label** | Settings Health |
| **Page title** | Settings Health Check |
| **Services** | `IVersionedSettingsService`, `IHttpContextAccessor` |

**Responsibility:** Validate and repair the `appsettings.user.json` configuration file.
Laid out in two columns:

- **Left column** — validation result display: green success alert when clean, red error
  alert listing error codes (each with MudChip colour coding), yellow warning list.
  Repair buttons trigger auto-fix for each known error code.
- **Right column** — settings file metadata: current version, file path, file size, last
  modified date; buttons to reload settings from disk and to reset to defaults.

Also embeds the `<SettingsHealthStatus>` component (see §5.1) as a summary widget.

**Navigates to:** Nothing (stays on page after repair actions).

---

### 4.8 Monitor Settings — `Pages\MonitorSettings.razor`

| Attribute | Value |
|-----------|-------|
| **Route** | `/monitorsettings` |
| **Nav label** | Monitor Settings |
| **Page title** | Monitor Settings Configuration |
| **Services** | `IVersionedSettingsService`, `NavigationManager` |

**Responsibility:** Configure the internal logging / telemetry filter settings. Persisted
to `MonitorSettingsPath` (shown in the subtitle). The page has:

1. **Preset buttons** — Development, Production, Troubleshooting, Profile Debug.
   Each button calls a method that sets the full settings object and saves.
2. **Device Filters card** — Checkboxes: Server Device, Telescope, UI.
3. **Category Filters card** — Checkboxes: Other, Driver, Interface, Server, Mount,
   Alignment.
4. **Type Filters (Log Levels) card** — Checkboxes for each log level / type.
5. **Save Settings button** with success/error feedback alert.

**Navigates to:** Stays on page after save.

---

### 4.9 ASCOM Device Setup Pages (dynamic) — `Pages\Devices\TelescopeSetup.razor`

| Attribute | Value |
|-----------|-------|
| **Route** | `/setup/v1/Telescope/{InstanceID:int}/setup` |
| **Nav label** | Rendered from NavMenu (dynamic) / linked from Device Manager |
| **Page title** | Telescope Setup - Device N |

**Responsibility:** Read-only ASCOM-standard device information card for a single
telescope instance. Rendered in two `<MudCard>` columns:

- **Device Information** — Device Number, Device Name, Mount Type, Alignment Mode,
  Connection Status (colour-coded).
- **Observatory Settings** — Site Latitude, Longitude, Elevation, UTC Offset.

Also used as an *embedded component* (without route) inside `MountDevices.razor`.

> The same ASCOM `/setup/v1/{deviceType}/{n}/setup` route pattern applies to all other
> device types (Camera, Dome, etc.) — those pages are served by the `ASCOM.Alpaca.Razor`
> library's own Razor components, not custom GreenSwamp pages.

---

### 4.10 Empty / Placeholder — `Pages\TelescopeSettings.razor`

| Attribute | Value |
|-----------|-------|
| **File** | `GreenSwamp.Alpaca.Server\Pages\TelescopeSettings.razor` |
| **Status** | **Empty file — no route or content defined yet.** |

This file exists in the project but contains no markup or `@page` directive. It is a
reserved placeholder for future per-telescope settings UI.

---

## 5. Reusable Components

### 5.1 `Components\SettingsHealthStatus.razor`

| Attribute | Value |
|-----------|-------|
| **Type** | Embedded component (no `@page` route) |
| **Services** | `IVersionedSettingsService` |

A compact summary card indicating the overall health of the settings file. Displays a
`<MudCard>` with a colour-coded border and icon:

- 🟢 Green / CheckCircle → all valid
- 🟡 Yellow / Info → valid with warnings
- 🔴 Red / Warning → validation errors present

Lists errors and warnings inline. Used embedded within `SettingsHealthCheck.razor` and
potentially elsewhere as a status widget.

### 5.2 `ASCOM.Alpaca.Razor\Shared\LoginControl.razor`

| Attribute | Value |
|-----------|-------|
| **Type** | Embedded component (no `@page` route) |
| **Used by** | Conditionally, when `RequireAuthorization` is enabled |

Renders a simple username/password `<form>` that POSTs to `/login?url={returnUrl}`.
When the user is already authenticated, displays the logged-in username and a Logout
link. Implemented with `<AuthorizeView>` for context-aware rendering.

---

## 6. Between-Page Navigation Map

```
Home (/)
  └── (external) greenswamp.org
  └── (external) ascom-standards.org

NavMenu
  ├── / ──────────────────────────────────────── Home
  ├── /mount-devices ─────────────────────────── Mount Settings (overview)
  │     └── /mountsettings/{n} ──────────────── Mount Settings (editor) [Edit Settings btn]
  ├── /setup ─────────────────────────────────── Server Settings
  │     └── / (redirect after save w/ auth)
  ├── /device-manager ────────────────────────── Device Manager
  │     ├── /setup/v1/telescope/{n}/setup ───── Telescope Setup (new tab) [Setup btn]
  │     └── /mountsettings/{n} ──────────────── Mount Settings (editor) [Settings btn]
  ├── /settings-health ───────────────────────── Settings Health Check
  ├── /monitorsettings ───────────────────────── Monitor Settings
  └── /mount-state ───────────────────────────── Mount State (real-time dashboard)

Dynamic device links (generated per registered device)
  └── /setup/v1/{deviceType}/{n}/setup ─────── ASCOM device setup pages
		(Camera, Dome, Focuser, Filter Wheel, etc. — served by ASCOM.Alpaca.Razor)
```

---

## 7. Route Summary Table

| Route | Razor File | Nav Entry | Auth Required |
|-------|-----------|-----------|---------------|
| `/` | `Pages\Index.razor` | Home | No |
| `/setup` | `Pages\Setup.razor` | Server Settings | Configurable |
| `/device-manager` or `/devices` | `Pages\DeviceManager.razor` | Device Manager | Configurable |
| `/mount-devices` | `Pages\MountDevices.razor` | Mount Settings | Configurable |
| `/mountsettings` | `Pages\MountSettings.razor` | — (linked) | Configurable |
| `/mountsettings/{n:int}` | `Pages\MountSettings.razor` | — (linked) | Configurable |
| `/mount-state` | `Pages\MountState.razor` | Mount State | Configurable |
| `/mount-state/{n:int}` | `Pages\MountState.razor` | Mount State | Configurable |
| `/settings-health` | `Pages\SettingsHealthCheck.razor` | Settings Health | Configurable |
| `/monitorsettings` | `Pages\MonitorSettings.razor` | Monitor Settings | Configurable |
| `/setup/v1/Telescope/{n:int}/setup` | `Pages\Devices\TelescopeSetup.razor` | Dynamic nav | Configurable |
| `/setup/v1/{type}/{n}/setup` | ASCOM.Alpaca.Razor (library) | Dynamic nav | Configurable |
| `/login` | ASCOM.Alpaca.Razor `LoginController` | — | N/A |
| `/logout` | ASCOM.Alpaca.Razor `LoginController` | — | N/A |

> "Configurable" means controlled by the `RequireAuthorization` / `UseAuth` setting on
> the Server Settings page, enforced via `AuthorizationFilter` in `ASCOM.Alpaca.Razor`.

---

## 8. Service Dependencies Summary

| Page | Key Injected Services |
|------|-----------------------|
| `Index.razor` | None |
| `Setup.razor` | `IVersionedSettingsService`, `NavigationManager` |
| `DeviceManager.razor` | `IVersionedSettingsService`, `UnifiedDeviceRegistry` |
| `MountDevices.razor` | `IVersionedSettingsService` |
| `MountSettings.razor` | `IVersionedSettingsService`, `NavigationManager` |
| `MountState.razor` | `TelescopeStateService`, `IVersionedSettingsService` |
| `SettingsHealthCheck.razor` | `IVersionedSettingsService`, `IHttpContextAccessor` |
| `MonitorSettings.razor` | `IVersionedSettingsService`, `NavigationManager` |
| `TelescopeSetup.razor` | None (reads `MountRegistry` static) |
| `SettingsHealthStatus.razor` | `IVersionedSettingsService` |

---

## 9. File Reference Index

| File | Purpose |
|------|---------|
| `App.razor` | Blazor router root |
| `_Imports.razor` | Global `@using` statements |
| `Shared\MainLayout.razor` | App shell: AppBar + Drawer + Content |
| `Shared\NavMenu.razor` | Sidebar navigation menu |
| `Components\SettingsHealthStatus.razor` | Reusable health status widget |
| `Pages\_Host.cshtml` | Blazor Server bootstrap page |
| `Pages\_Layout.cshtml` | MVC layout for host/error pages |
| `Pages\Error.cshtml` / `Error.cshtml.cs` | Fatal error page |
| `Pages\Index.razor` | Home / landing page |
| `Pages\Setup.razor` | Server Settings page |
| `Pages\DeviceManager.razor` | Device lifecycle management |
| `Pages\MountDevices.razor` | Mount device overview (tabs) |
| `Pages\MountSettings.razor` | Full mount settings editor (tabs) |
| `Pages\MountState.razor` | Real-time mount telemetry dashboard |
| `Pages\SettingsHealthCheck.razor` | Settings validation & repair |
| `Pages\MonitorSettings.razor` | Logging filter configuration |
| `Pages\TelescopeSettings.razor` | **Placeholder — no content yet** |
| `Pages\Devices\TelescopeSetup.razor` | ASCOM device info (routed + embedded) |
| `Theme\GsTheme.cs` | MudBlazor dark theme definition |
| `ASCOM.Alpaca.Razor\Shared\LoginControl.razor` | Login/logout component |

---

*End of document — GreenSwamp Alpaca Server UI High-Level Design*  
*Generated: 2026-05-07 08:49*
