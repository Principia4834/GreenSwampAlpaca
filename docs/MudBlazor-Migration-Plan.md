# GS Alpaca — MudBlazor Migration Plan
**Author:** GitHub Copilot  
**Prepared for:** Andy  
**Date:** 2026-05-05 13:58  
**Status:** Pre-implementation planning document

---

## 1. Executive Summary

This document is a complete, step-by-step plan for migrating the GS Alpaca Blazor Server UI from its
current Bootstrap 5 + hand-written GS CSS stack to **MudBlazor 9.x** — a Blazor-native Material Design
component library that completely replaces Bootstrap.

The migration is a **deliberate rewrite of UI layer only**. All C# services, DI registrations, ASCOM
logic, settings models, and API controllers remain 100% unchanged. Only `.razor` markup, `.razor.css`
scoped stylesheets, `_Layout.cshtml`, and global CSS files are affected.

**Estimated effort:** 2–3 focused development days.  
**Risk level:** Medium — functional regressions are possible if forms or device loops are not tested  
after each phase.  
**Rollback strategy:** Each phase ends with a git commit. Any phase can be reverted independently.

---

## 2. Current State Inventory

### 2.1 Pages and Components

| File | Bootstrap classes used | Complexity |
|------|----------------------|------------|
| `Pages/Index.razor` | None | ⭐ Trivial |
| `Pages/Setup.razor` | `<fieldset>` / `<input>` raw HTML, no Bootstrap | ⭐ Trivial |
| `Pages/TelescopeState.razor` | `.btn`, `.btn-primary`, `.btn-outline-secondary`, `.btn-sm`, `.device-selector` | ⭐⭐ Low |
| `Pages/SettingsHealthCheck.razor` | `.container-fluid`, `.row`, `.col`, `.col-lg-*`, `.col-xl-*`, `.card`, `.card-header`, `.card-body`, `.alert`, `.alert-info` | ⭐⭐ Low |
| `Pages/Devices/TelescopeSetup.razor` | `.container-fluid`, `.row`, `.col-md-6`, `.card`, `.card-header`, `.card-body`, `.table`, `.table-sm`, `.badge`, `.bg-primary`, `.bg-info`, `.bg-secondary` | ⭐⭐⭐ Medium |
| `Pages/DeviceManager.razor` | `.container-fluid`, `.row`, `.card`, `.card-header`, `.card-body`, `.btn`, `.btn-primary`, `.btn-sm`, `.table`, `.table-striped`, `.table-hover`, `.alert`, `.alert-danger`, `.alert-warning`, `.spinner-border`, `.d-flex`, `.justify-content-between`, `.align-items-center` | ⭐⭐⭐ Medium |
| `Pages/MonitorSettings.razor` | `.container-fluid`, `.row`, `.col`, `.col-md-*`, `.card`, `.card-header`, `.card-body`, `.btn`, `.btn-outline-*`, `.w-100`, `.d-block`, `.alert`, `<small>` | ⭐⭐⭐ Medium |
| `Pages/MountSettings.razor` | `.nav-tabs`, `.nav-item`, `.nav-link`, `.alert`, `.alert-dismissible`, `.btn-close`, `.btn`, `.btn-primary`, `.btn-info`, `<InputNumber>` + `.form-control`, `<label class="form-label">` (×many) | ⭐⭐⭐⭐ High |
| `Components/SettingsHealthStatus.razor` | `.card`, `.card-header`, `.card-body`, `.border-*`, `.bg-danger`, `.bg-warning`, `.bg-success`, `.alert`, `.spinner-border`, `oi oi-*` icons | ⭐⭐⭐ Medium |
| `Shared/NavMenu.razor` | GS-class only (already cleaned) | ⭐ Trivial |
| `Shared/MainLayout.razor` | `.page`, `.sidebar`, `.top-row`, `article.content` | ⭐⭐ Low |
| `Pages/_Layout.cshtml` | Bootstrap CDN link, `data-bs-theme` | ⭐ Trivial |

### 2.2 Remaining Legacy Items After Clean-Slate Audit

| Item | Location | Status |
|------|----------|--------|
| Bootstrap CSS (226 KB) | `wwwroot/css/bootstrap/` | ⚠️ Still loaded |
| Bootstrap source map (438 KB) | `wwwroot/css/bootstrap/*.map` | ⚠️ Still loaded |
| `--bs-*` variable bridge (~30 lines) | `site.css` | ⚠️ Still present |
| `data-bs-theme="dark"` | `_Layout.cshtml` | ⚠️ Still present |
| `oi oi-*` Open Iconic references | `SettingsHealthStatus.razor`, `DeviceManager.razor`, `SettingsHealthCheck.razor`, `TelescopeSetup.razor` | ⚠️ Still used |
| `.spinner-border` Bootstrap spinner | `DeviceManager.razor`, `SettingsHealthStatus.razor` | ⚠️ Still used |
| `bg-primary text-white`, `bg-info` BS colour classes | `TelescopeSetup.razor`, `SettingsHealthStatus.razor` | ⚠️ Still used |

### 2.3 Key Observations

1. **`Setup.razor`** uses raw HTML `<fieldset>` / `<input>` with inline styles — already partially
   aligned with the GS design but not using any framework. MudBlazor migration here is straightforward.

2. **`MountSettings.razor`** (775 lines) is the most complex page. It has a multi-tab layout using
   Bootstrap `.nav-tabs`, dozens of `<InputNumber>` + `form-control` pairs, and inline validation.
   This page should be tackled last when the MudBlazor patterns are established.

3. **`SettingsHealthStatus.razor`** still uses `oi oi-*` (Open Iconic) icons which were thought to be
   fully removed. These will be replaced with Material Symbols or `MudIcon` during migration.

4. **`DeviceManager.razor`** contains a Bootstrap `.spinner-border` loading indicator which maps
   cleanly to `<MudProgressCircular>`.

5. The `@code` blocks in all pages are **zero-change** — only the HTML/Razor markup sections are
   rewritten.

---

## 3. MudBlazor Architecture Overview

### 3.1 What MudBlazor Replaces

| Current | MudBlazor Equivalent |
|---------|---------------------|
| `wwwroot/css/bootstrap/bootstrap.min.css` | MudBlazor's own CSS (loaded via NuGet) |
| `data-bs-theme="dark"` on `<html>` | `<MudThemeProvider Theme="@_gsTheme" IsDarkMode="true" />` |
| `[data-bs-theme], :root { --bs-* }` bridge block | Deleted entirely |
| `MainLayout.razor` `.page`/`.sidebar` shell | `MudLayout` + `MudAppBar` + `MudDrawer` |
| `NavMenu.razor` custom nav | `MudNavMenu` + `MudNavLink` |
| `.row` / `.col-*` | `MudGrid` + `MudItem` |
| `.card` / `.card-header` / `.card-body` | `MudCard` / `MudCardHeader` / `MudCardContent` |
| `.btn btn-primary` | `<MudButton Color="Color.Primary" Variant="Variant.Filled">` |
| `.btn btn-outline-secondary` | `<MudButton Color="Color.Default" Variant="Variant.Outlined">` |
| `.form-control` / `<InputText>` | `<MudTextField>` |
| `<InputNumber class="form-control">` | `<MudNumericField>` |
| `.form-select` / `<InputSelect>` | `<MudSelect>` |
| `<input type="checkbox">` / `.form-check` | `<MudCheckBox>` |
| `.nav-tabs` + `.nav-link` | `<MudTabs>` + `<MudTabPanel>` |
| `.alert alert-success/danger/info/warning` | `<MudAlert Severity="Severity.Success/Error/Info/Warning">` |
| `.table` / `.table-striped` | `<MudTable>` or `<MudDataGrid>` |
| `.spinner-border` | `<MudProgressCircular>` |
| `oi oi-*` Open Iconic icons | `<MudIcon Icon="@Icons.Material.Filled.*">` or Material Symbols span |
| `.badge bg-primary` | `<MudChip>` or `<MudBadge>` |

### 3.2 The GS MudTheme Object

All GS colour tokens translate to a single C# object. This is defined once in a shared location
(e.g., `GsTheme.cs`) and passed to `MudThemeProvider`:

```csharp
// GreenSwamp.Alpaca.Server/Theme/GsTheme.cs
using MudBlazor;

namespace GreenSwamp.Alpaca.Server.Theme;

public static class GsTheme
{
	public static readonly MudTheme Instance = new()
	{
		PaletteDark = new PaletteDark
		{
			// Surfaces
			Background        = "#121212",   // --gs-bg-app
			Surface           = "#1e1e1e",   // --gs-bg-paper
			BackgroundGray    = "#2a2a2a",   // --gs-bg-elevated
			AppbarBackground  = "#0d1117",   // --gs-bg-sidebar

			// Accent / Primary
			Primary           = "#4caf50",   // --gs-accent-500
			PrimaryLighten    = "#81c784",   // --gs-accent-300
			PrimaryDarken     = "#388e3c",   // --gs-accent-700

			// Text
			TextPrimary       = "rgba(255,255,255,0.87)",
			TextSecondary     = "rgba(255,255,255,0.60)",
			TextDisabled      = "rgba(255,255,255,0.38)",

			// Lines and dividers
			Divider           = "rgba(255,255,255,0.12)",
			DividerLight      = "rgba(255,255,255,0.06)",
			TableLines        = "rgba(255,255,255,0.12)",
			OverlayDark       = "rgba(0,0,0,0.5)",

			// Status
			Success           = "#66bb6a",   // --gs-success
			Warning           = "#ffa726",   // --gs-warning
			Error             = "#f44336",   // --gs-error
			Info              = "#42a5f5",   // --gs-info

			// Action states
			ActionDefault     = "rgba(255,255,255,0.60)",
			ActionDisabled    = "rgba(255,255,255,0.26)",
			DrawerBackground  = "#0d1117",
			DrawerText        = "rgba(255,255,255,0.87)",
			DrawerIcon        = "rgba(255,255,255,0.60)",
		},
		Typography = new Typography
		{
			Default = new DefaultTypography
			{
				FontFamily = ["Roboto", "Helvetica Neue", "Arial", "sans-serif"],
				FontSize   = "0.875rem",
				FontWeight = "400",
				LineHeight = "1.43",
			},
			H1 = new H1Typography { FontSize = "1.5rem",    FontWeight = "300" },
			H2 = new H2Typography { FontSize = "1.125rem",  FontWeight = "400" },
			H3 = new H3Typography { FontSize = "1rem",      FontWeight = "500" },
			H4 = new H4Typography { FontSize = "0.9375rem", FontWeight = "500" },
			Body1 = new Body1Typography { FontSize = "0.875rem" },
			Body2 = new Body2Typography { FontSize = "0.8125rem" },
			Button = new ButtonTypography { FontSize = "0.8125rem", FontWeight = "500",
											TextTransform = "uppercase", LetterSpacing = "0.06em" },
		},
		LayoutProperties = new LayoutProperties
		{
			DrawerWidthLeft  = "250px",
			AppbarHeight     = "3.5rem",
		}
	};
}
```

### 3.3 Fonts — Self-Hosted Roboto Retained

MudBlazor defaults to loading Roboto from Google Fonts CDN. Since the app already has self-hosted
Roboto woff2 files and `fonts.css`, the CDN load will be **suppressed** via `MudThemeProvider`:

```razor
<MudThemeProvider Theme="@GsTheme.Instance" IsDarkMode="true" />
```

And in `_Layout.cshtml`, keep `<link rel="stylesheet" href="css/fonts.css" />` — MudBlazor will use
whatever Roboto is already available in the browser's font cache.

### 3.4 Icons — Material Icons vs Material Symbols

MudBlazor uses **Material Icons** (the older, non-variable font set) via its built-in `Icons.Material.*`
constants. The app currently uses **Material Symbols Outlined** (the newer variable font) via text
ligatures in `<span>` elements.

**Decision:** Keep both during migration.
- `MudIcon` / `MudNavLink` icons use MudBlazor's built-in Material Icons string constants
- Custom spans `<span class="material-symbols-outlined">` continue to work wherever needed
- `fonts.css` and `MaterialSymbolsOutlined.woff2` are retained

This avoids a separate font decision blocking the migration. A later cleanup pass can unify them.

---

## 4. Pre-Migration Preparation

### Phase 0: Preparation (no functional changes)

**Goal:** Get MudBlazor installed and rendering alongside Bootstrap. Verify the app still builds and
runs before touching any page markup.

#### Step 0.1 — Install MudBlazor NuGet package

```powershell
cd T:\source\repos\GreenSwampAlpaca
dotnet add GreenSwamp.Alpaca.Server/GreenSwamp.Alpaca.Server.csproj package MudBlazor
```

Confirm version 9.x is installed (targets .NET 8 ✅).

#### Step 0.2 — Register MudBlazor services in `Program.cs`

In `Program.cs`, locate the `builder.Services` section and add:

```csharp
builder.Services.AddMudServices();
```

#### Step 0.3 — Add MudBlazor CSS and JS to `_Layout.cshtml`

MudBlazor requires its own CSS and a small JS interop file.
Update `_Layout.cshtml` head section:

```html
<!-- Keep existing -->
<link rel="stylesheet" href="css/fonts.css" />

<!-- MudBlazor CSS (replaces Bootstrap eventually; for now both coexist) -->
<link href="_content/MudBlazor/MudBlazor.min.css" rel="stylesheet" />

<!-- Keep Bootstrap during migration (will be removed in Phase 6) -->
<link rel="stylesheet" href="css/bootstrap/bootstrap.min.css" />
<link href="css/site.css" rel="stylesheet" />
<link href="GreenSwamp.Alpaca.Server.styles.css" rel="stylesheet" />
```

Add before `</body>`:
```html
<script src="_content/MudBlazor/MudBlazor.min.js"></script>
<!-- existing scripts below -->
<script src="_framework/blazor.server.js"></script>
<script src="js/profileUtils.js"></script>
```

#### Step 0.4 — Add MudBlazor namespace to `_Imports.razor`

```razor
@using MudBlazor
```

#### Step 0.5 — Create `GsTheme.cs`

Create `GreenSwamp.Alpaca.Server/Theme/GsTheme.cs` with the theme object from section 3.2.

#### Step 0.6 — Add MudBlazor providers to `MainLayout.razor`

Wrap the existing layout with MudBlazor's required providers (these are invisible rendering aids):

```razor
@inherits LayoutComponentBase

<MudThemeProvider Theme="@GsTheme.Instance" IsDarkMode="true" />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />

<!-- existing layout markup stays unchanged for now -->
<div class="page">
	...
</div>
```

#### Step 0.7 — Build and run baseline

```powershell
dotnet build GreenSwamp.Alpaca.Server/GreenSwamp.Alpaca.Server.csproj
```

**Expected result:** Build succeeds. App runs. Visual appearance is unchanged (Bootstrap still active).

#### Step 0.8 — Commit Phase 0

```
git commit -m "feat: install MudBlazor 9.x alongside Bootstrap - Phase 0 preparation"
```

---

## 5. Phase-by-Phase Migration

---

### Phase 1: Shell Layout (MainLayout + NavMenu)

**Goal:** Replace the Blazor template `.page`/`.sidebar`/`.top-row` shell with MudBlazor's `MudLayout`
system. After this phase the chrome (sidebar and top bar) is MudBlazor; pages remain unchanged.

**Files changed:**
- `Shared/MainLayout.razor`
- `Shared/MainLayout.razor.css` — most rules deleted
- `Shared/NavMenu.razor`
- `Shared/NavMenu.razor.css` — most rules deleted

#### MainLayout.razor — Before (current)
```razor
@inherits LayoutComponentBase
<PageTitle>GS Alpaca</PageTitle>
<div class="page">
	<div class="sidebar">
		<NavMenu />
	</div>
	<main>
		<div class="top-row px-4">
			<a href="https://greenswamp.org/" target="_blank">About</a>
		</div>
		<article class="content px-4">
			@Body
		</article>
	</main>
</div>
```

#### MainLayout.razor — After
```razor
@inherits LayoutComponentBase

<MudThemeProvider Theme="@GsTheme.Instance" IsDarkMode="true" />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />

<MudLayout>
	<MudAppBar Elevation="0" Dense="true" Color="Color.Dark"
			   Style="border-bottom: 1px solid var(--gs-divider);">
		<MudIconButton Icon="@Icons.Material.Filled.Menu" Color="Color.Inherit"
					   Edge="Edge.Start" OnClick="ToggleDrawer" />
		<MudText Typo="Typo.h6" Style="font-weight:300;">
			<span style="font-weight:700; color:var(--gs-accent-500);">GS</span>Alpaca
		</MudText>
		<MudSpacer />
		<MudLink Href="https://greenswamp.org/" Target="_blank" Color="Color.Default"
				 Typo="Typo.caption">About</MudLink>
	</MudAppBar>

	<MudDrawer @bind-Open="_drawerOpen" Elevation="0" Variant="DrawerVariant.Responsive"
			   ClipMode="DrawerClipMode.Always" Color="Color.Dark"
			   Style="border-right: 1px solid var(--gs-divider);">
		<NavMenu />
	</MudDrawer>

	<MudMainContent>
		<MudContainer MaxWidth="MaxWidth.False" Class="pa-4">
			@Body
		</MudContainer>
	</MudMainContent>
</MudLayout>

@code {
	private bool _drawerOpen = true;
	private void ToggleDrawer() => _drawerOpen = !_drawerOpen;
}
```

#### NavMenu.razor — After
```razor
<MudNavMenu>
	<MudNavLink Href="/" Match="NavLinkMatch.All"
				Icon="@Icons.Material.Filled.Home">Home</MudNavLink>

	@foreach (var device in ASCOM.Alpaca.DeviceManager.Cameras)
	{
		<MudNavLink Href="@($"/setup/v1/Camera/{device.Key}/setup")"
					Icon="@Icons.Material.Filled.PhotoCamera">Camera @device.Key</MudNavLink>
	}
	@foreach (var device in ASCOM.Alpaca.DeviceManager.CoverCalibrators)
	{
		<MudNavLink Href="@($"/setup/v1/covercalibrator/{device.Key}/setup")"
					Icon="@Icons.Material.Filled.Contrast">Cover Cal. @device.Key</MudNavLink>
	}
	@foreach (var device in ASCOM.Alpaca.DeviceManager.Domes)
	{
		<MudNavLink Href="@($"/setup/v1/dome/{device.Key}/setup")"
					Icon="@Icons.Material.Filled.Lens">Dome @device.Key</MudNavLink>
	}
	@foreach (var device in ASCOM.Alpaca.DeviceManager.FilterWheels)
	{
		<MudNavLink Href="@($"/setup/v1/filterwheel/{device.Key}/setup")"
					Icon="@Icons.Material.Filled.FilterCenterFocus">Filter Wheel @device.Key</MudNavLink>
	}
	@foreach (var device in ASCOM.Alpaca.DeviceManager.Focusers)
	{
		<MudNavLink Href="@($"/setup/v1/focuser/{device.Key}/setup")"
					Icon="@Icons.Material.Filled.Adjust">Focuser @device.Key</MudNavLink>
	}
	@foreach (var device in ASCOM.Alpaca.DeviceManager.ObservingConditions)
	{
		<MudNavLink Href="@($"/setup/v1/ObservingConditions/{device.Key}/setup")"
					Icon="@Icons.Material.Filled.Cloud">Obs. Conditions @device.Key</MudNavLink>
	}
	@foreach (var device in ASCOM.Alpaca.DeviceManager.Rotators)
	{
		<MudNavLink Href="@($"/setup/v1/rotator/{device.Key}/setup")"
					Icon="@Icons.Material.Filled.RotateRight">Rotator @device.Key</MudNavLink>
	}
	@foreach (var device in ASCOM.Alpaca.DeviceManager.SafetyMonitors)
	{
		<MudNavLink Href="@($"/setup/v1/SafetyMonitor/{device.Key}/setup")"
					Icon="@Icons.Material.Filled.Shield">Safety Monitor @device.Key</MudNavLink>
	}
	@foreach (var device in ASCOM.Alpaca.DeviceManager.Switches)
	{
		<MudNavLink Href="@($"/setup/v1/switch/{device.Key}/setup")"
					Icon="@Icons.Material.Filled.ToggleOn">Switch @device.Key</MudNavLink>
	}
	@foreach (var device in ASCOM.Alpaca.DeviceManager.Telescopes)
	{
		<MudNavLink Href="@($"/setup/v1/telescope/{device.Key}/setup")"
					Icon="@Icons.Material.Filled.TravelExplore">Telescope @device.Key</MudNavLink>
	}

	<MudDivider Class="my-2" />

	<MudNavLink Href="/setup"          Icon="@Icons.Material.Filled.Settings">Server Settings</MudNavLink>
	<MudNavLink Href="/device-manager" Icon="@Icons.Material.Filled.DeviceHub">Device Manager</MudNavLink>
	<MudNavLink Href="/mountsettings"  Icon="@Icons.Material.Filled.Tune">Mount Settings</MudNavLink>
	<MudNavLink Href="/settings-health"Icon="@Icons.Material.Filled.MonitorHeart">Settings Health</MudNavLink>
	<MudNavLink Href="/monitorsettings"Icon="@Icons.Material.Filled.ListAlt">Monitor Settings</MudNavLink>
	<MudNavLink Href="/telescope-state"Icon="@Icons.Material.Filled.DisplaySettings">Telescope State</MudNavLink>
</MudNavMenu>
```

**NavMenu.razor.css** — entire file can be deleted (MudNavMenu handles all styling via the theme).

**MainLayout.razor.css** — entire file can be deleted (MudLayout handles the shell).

**Commit:** `feat: migrate MainLayout and NavMenu to MudBlazor - Phase 1`

---

### Phase 2: Trivial Pages (Index, Setup, TelescopeState)

**Goal:** Migrate the three simplest pages. These establish the MudBlazor component patterns before
tackling heavier pages.

#### 2.1 Index.razor
Replace raw `<h1>`, `<p>` with `MudText`:
```razor
@page "/"
<PageTitle>Index</PageTitle>
<MudText Typo="Typo.h5" GutterBottom="true">Hello, from Alpaca!</MudText>
<MudText>Welcome to your new Alpaca Driver. Make sure to customize...</MudText>
<MudText>Often you would put links to documentation and support here.</MudText>
<MudText>Thank you for developing a new Alpaca Driver...</MudText>
```

#### 2.2 Setup.razor
Replace `<fieldset>`/`<input>` with `MudCard` + `MudTextField`/`MudSwitch`/`MudNumericField`.
Example pattern:
```razor
<MudCard Class="mb-4">
	<MudCardHeader><MudText Typo="Typo.h6">Server Settings</MudText></MudCardHeader>
	<MudCardContent>
		<MudTextField Label="Server Location" @bind-Value="Location" />
		<MudSwitch Label="Allow Remote Access" @bind-Value="RemoteAccess" Color="Color.Primary" />
		<MudNumericField Label="Server (Alpaca) Port" @bind-Value="ServerPort" Min="1" Max="65535" />
		<MudSwitch Label="Open Browser on Start" @bind-Value="StartBrowser" Color="Color.Primary" />
		<MudSwitch Label="Require Strict API Compliance" @bind-Value="StrictAPIMode" Color="Color.Primary" />
	</MudCardContent>
	<MudCardActions>
		<MudButton OnClick="SaveServerSettings" Variant="Variant.Filled" Color="Color.Primary">Save</MudButton>
	</MudCardActions>
</MudCard>
```

#### 2.3 TelescopeState.razor
Replace Bootstrap button group for device selector with `MudButtonGroup`:
```razor
<MudButtonGroup Variant="Variant.Outlined" Class="mb-4">
	@foreach (var kvp in MountRegistry.GetAllInstances())
	{
		var dn = kvp.Key;
		<MudButton Color="@(dn == DeviceNumber ? Color.Primary : Color.Default)"
				   Variant="@(dn == DeviceNumber ? Variant.Filled : Variant.Outlined)"
				   Href="@($"/telescope-state/{dn}")">Device @dn</MudButton>
	}
</MudButtonGroup>
```

The `.state-container`, `.state-section`, `.state-grid` custom CSS grid layout in
`TelescopeState.razor.css` is already clean GS code with no Bootstrap dependencies — it stays
unchanged.

**Commit:** `feat: migrate Index, Setup, TelescopeState to MudBlazor - Phase 2`

---

### Phase 3: SettingsHealthCheck + SettingsHealthStatus Component

**Goal:** Migrate the health check page and its child component. This also eliminates the remaining
`oi oi-*` Open Iconic references in `SettingsHealthStatus.razor`.

#### 3.1 SettingsHealthCheck.razor
Replace `.container-fluid` / `.row` / `.col-*` with `MudGrid`/`MudItem`, `.card` with `MudCard`,
`.alert` with `MudAlert`:

```razor
@page "/settings-health"
<PageTitle>Settings Health Check</PageTitle>

<MudText Typo="Typo.h5" GutterBottom="true">Settings Health Check</MudText>
<MudText Class="mb-4" Color="Color.Secondary">
	Validate and repair your settings configuration...
</MudText>

<MudGrid>
	<MudItem xs="12" lg="10" xl="8">
		<SettingsHealthStatus />
	</MudItem>

	<MudItem xs="12" lg="10" xl="8">
		<MudCard>
			<MudCardHeader>
				<CardHeaderContent>
					<MudText Typo="Typo.h6">About Settings Validation</MudText>
				</CardHeaderContent>
			</MudCardHeader>
			<MudCardContent>
				<!-- existing informational content -->
			</MudCardContent>
		</MudCard>
	</MudItem>
</MudGrid>
```

#### 3.2 SettingsHealthStatus.razor
Replace `oi oi-*` icons with `MudIcon`, `.spinner-border` with `<MudProgressCircular>`,
Bootstrap colour modifier classes with MudBlazor equivalents:

```razor
<!-- Card header - use Color parameter instead of bg-danger/bg-success class -->
<MudCard Outlined="true">
	<MudCardHeader Style="@GetHeaderStyle()">
		<CardHeaderAvatar>
			<MudIcon Icon="@GetStatusIcon()" />
		</CardHeaderAvatar>
		<CardHeaderContent>
			<MudText Typo="Typo.h6">Settings Health Status</MudText>
		</CardHeaderContent>
	</MudCardHeader>
	<MudCardContent>
		@if (_loading)
		{
			<MudStack Row="true" AlignItems="AlignItems.Center">
				<MudProgressCircular Size="Size.Small" Indeterminate="true" />
				<MudText>Validating settings...</MudText>
			</MudStack>
		}
		<!-- ... rest of content using MudAlert for status summary ... -->
	</MudCardContent>
</MudCard>
```

Helper method to get icon:
```csharp
private string GetStatusIcon() => _validationResult?.HasErrors == true
	? Icons.Material.Filled.Warning
	: _validationResult?.HasWarnings == true
		? Icons.Material.Filled.Info
		: Icons.Material.Filled.CheckCircle;
```

**Commit:** `feat: migrate SettingsHealthCheck and SettingsHealthStatus to MudBlazor - Phase 3`

---

### Phase 4: DeviceManager.razor

**Goal:** Migrate the device management page. This page uses a table, cards, alerts, spinner, and
action buttons — all with direct MudBlazor equivalents.

Key replacements:
- `.spinner-border` → `<MudProgressCircular Indeterminate="true" />`
- `.table.table-striped.table-hover` → `<MudTable Items="@_devices" Hover="true" Striped="true">`
- `oi oi-reload` → `Icon="@Icons.Material.Filled.Refresh"`
- `.alert.alert-danger` → `<MudAlert Severity="Severity.Error" ShowCloseIcon="true">`
- `.alert.alert-warning` → `<MudAlert Severity="Severity.Warning">`
- `.btn.btn-primary.btn-sm` → `<MudButton Size="Size.Small" Color="Color.Primary" Variant="Variant.Filled">`
- `.card` card shell → `MudCard` + `MudCardHeader` + `MudCardContent`
- `.d-flex .justify-content-between .align-items-center` → `<MudStack Row="true" Justify="Justify.SpaceBetween" AlignItems="AlignItems.Center">`

MudTable pattern for the device list:
```razor
<MudTable Items="@_devices" Hover="true" Striped="true" Dense="true"
		  Style="background:var(--mud-palette-surface)">
	<HeaderContent>
		<MudTh>Device #</MudTh>
		<MudTh>Name</MudTh>
		<MudTh>Mount</MudTh>
		<MudTh>Port</MudTh>
		<MudTh>Enabled</MudTh>
		<MudTh>Status</MudTh>
		<MudTh>Actions</MudTh>
	</HeaderContent>
	<RowTemplate>
		<MudTd>@context.DeviceNumber</MudTd>
		<!-- ... -->
	</RowTemplate>
</MudTable>
```

**Commit:** `feat: migrate DeviceManager to MudBlazor - Phase 4`

---

### Phase 5: MonitorSettings.razor

**Goal:** Migrate the monitor settings page. This page uses preset buttons arranged in a grid, cards
for each logging category, and alerts for feedback.

Key replacements:
- Preset button grid `.row.g-2` / `.col-md-3` → `MudGrid` + `MudItem xs=12 sm=6 md=3`
- Per-preset `<button class="btn btn-outline-success w-100">` → `<MudButton FullWidth="true" Variant="Variant.Outlined" Color="Color.Success">`
- Category filter cards → `MudCard` per category
- Feedback alerts → `MudAlert` with `@bind-Visible`

**Commit:** `feat: migrate MonitorSettings to MudBlazor - Phase 5`

---

### Phase 6: TelescopeSetup.razor

**Goal:** Migrate the device-specific telescope setup page. Currently uses Bootstrap cards with
`bg-primary text-white` headers and `oi oi-info` / `oi oi-check` icons.

Key replacements:
- `oi oi-info` → `Icons.Material.Filled.Info`
- `oi oi-check` → `Icons.Material.Filled.Check`
- `.badge.bg-secondary` → `<MudChip Size="Size.Small" Color="Color.Default">`
- `.badge.bg-info` → `<MudChip Size="Size.Small" Color="Color.Info">`
- `.card-header.bg-primary.text-white` → `MudCardHeader` with `Style="background:var(--mud-palette-primary); color:white;"`
- `.table.table-sm` → `<MudSimpleTable Dense="true">`
- `.row` / `.col-md-6` → `MudGrid` / `MudItem xs=12 md=6`

**Commit:** `feat: migrate TelescopeSetup to MudBlazor - Phase 6`

---

### Phase 7: MountSettings.razor (Largest Page)

**Goal:** Migrate the most complex page. 775 lines, multi-tab form with dozens of inputs.

This phase is the most impactful for usability. MudBlazor tabs are significantly cleaner than
the Bootstrap `.nav-tabs` pattern currently used.

#### 7.1 Tab Navigation
Replace the entire Bootstrap nav-tabs block with:
```razor
<MudTabs Elevation="0" Rounded="false" ApplyEffectsToContainer="true"
		 PanelClass="pt-4" Color="Color.Primary">
	<MudTabPanel Text="Connection">
		<!-- Connection tab content -->
	</MudTabPanel>
	<MudTabPanel Text="Location">
		<!-- Location tab content -->
	</MudTabPanel>
	<!-- etc. -->
</MudTabs>
```

#### 7.2 Form Inputs
Every `<InputNumber @bind-Value="..." class="form-control" />` + `<label class="form-label">` pair
becomes a single `<MudNumericField>`:

```razor
<!-- Before -->
<label class="form-label">Alt-Az Update Interval (ms)</label>
<InputNumber @bind-Value="_settings.AltAzTrackingUpdateInterval" class="form-control" />

<!-- After -->
<MudNumericField Label="Alt-Az Update Interval (ms)"
				 @bind-Value="_settings.AltAzTrackingUpdateInterval"
				 Variant="Variant.Outlined" Margin="Margin.Dense" />
```

#### 7.3 Alert and Dismiss
```razor
<!-- Before -->
<div class="alert alert-@(_isError ? "danger" : "success") alert-dismissible fade show">
	@_message
	<button type="button" class="btn-close" @onclick="@(() => _message = null)"></button>
</div>

<!-- After -->
<MudAlert Severity="@(_isError ? Severity.Error : Severity.Success)"
		  ShowCloseIcon="true" CloseIconClicked="@(() => _message = null)">
	@_message
</MudAlert>
```

#### 7.4 Save / Reload Buttons
```razor
<!-- Before -->
<button type="submit" class="btn btn-primary" disabled="@_saving">Save Settings</button>
<button type="button" class="btn btn-info" @onclick="ReloadSettings">Reload</button>

<!-- After -->
<MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled" Color="Color.Primary"
		   Disabled="@_saving" StartIcon="@Icons.Material.Filled.Save">Save Settings</MudButton>
<MudButton OnClick="ReloadSettings" Variant="Variant.Outlined" Color="Color.Info"
		   StartIcon="@Icons.Material.Filled.Refresh">Reload</MudButton>
```

**Note:** `<EditForm>` and `<DataAnnotationsValidator>` are Blazor-native and remain unchanged.
MudBlazor form fields work inside `EditForm` naturally.

**Commit:** `feat: migrate MountSettings to MudBlazor - Phase 7`

---

### Phase 8: Remove Bootstrap

**Goal:** Now that all pages are MudBlazor, remove Bootstrap entirely.

#### 8.1 Delete Bootstrap assets
```powershell
Remove-Item -Recurse -Force GreenSwamp.Alpaca.Server\wwwroot\css\bootstrap\
```

#### 8.2 Remove Bootstrap link from `_Layout.cshtml`
Delete:
```html
<link rel="stylesheet" href="css/bootstrap/bootstrap.min.css" />
```
Also remove `data-bs-theme="dark"` from `<html>` — no longer meaningful.

#### 8.3 Clean `site.css`
Remove the entire `[data-bs-theme="dark"], :root { --bs-* }` bridge block (~30 lines).
Remove Bootstrap-specific overrides:
- `.card`, `.card-header`, `.card-body` override rules (MudBlazor owns these now)
- `.table`, `.table-striped` override rules
- `.alert-*` override rules
- `.btn-close` filter rule
- `.text-muted !important` rule

Retain in `site.css`:
- `:root` GS token block (still used by `TelescopeState.razor.css` and any inline styles)
- `.gs-card`, `.gs-table`, `.gs-monitor`, `.gs-badge-*`, `.gs-monospace` utility classes
- `#blazor-error-ui` styling
- `@keyframes gs-live-pulse`
- Base `html, body` dark theme rules (belt-and-braces for any raw HTML)

#### 8.4 Remove `NavMenu.razor.css` and `MainLayout.razor.css`
Both files were already largely cleaned in the audit. After Phase 1 they should be empty or
near-empty. Delete them entirely if nothing remains.

#### 8.5 Build and full regression test

```powershell
dotnet build GreenSwamp.Alpaca.Server/GreenSwamp.Alpaca.Server.csproj
```

Manually verify:
- [ ] App shell renders (sidebar, top bar, drawer toggle)
- [ ] All nav links navigate correctly
- [ ] MountSettings tabs and form inputs render
- [ ] DeviceManager table and buttons render
- [ ] Alerts and spinners render
- [ ] TelescopeState status grid renders
- [ ] Dark theme applied throughout
- [ ] GS green accent visible on primary actions

**Commit:** `refactor: remove Bootstrap - Phase 8 complete`

---

## 6. File Change Matrix

| File | Phase | Action |
|------|-------|--------|
| `Program.cs` | 0 | Add `builder.Services.AddMudServices()` |
| `_Imports.razor` | 0 | Add `@using MudBlazor` |
| `Pages/_Layout.cshtml` | 0, 8 | Add MudBlazor CSS/JS in Phase 0; remove Bootstrap in Phase 8 |
| `Theme/GsTheme.cs` | 0 | **Create new file** |
| `Shared/MainLayout.razor` | 1 | Rewrite to MudLayout |
| `Shared/MainLayout.razor.css` | 1 | Delete |
| `Shared/NavMenu.razor` | 1 | Rewrite to MudNavMenu |
| `Shared/NavMenu.razor.css` | 1 | Delete |
| `Pages/Index.razor` | 2 | MudText wrappers |
| `Pages/Setup.razor` | 2 | MudCard + form fields |
| `Pages/TelescopeState.razor` | 2 | MudButtonGroup for device selector |
| `Pages/TelescopeState.razor.css` | — | No change (already clean) |
| `Pages/SettingsHealthCheck.razor` | 3 | MudGrid + MudCard |
| `Components/SettingsHealthStatus.razor` | 3 | Remove `oi oi-*`, add MudIcon/MudAlert |
| `Pages/DeviceManager.razor` | 4 | MudTable + MudAlert + MudButton |
| `Pages/MonitorSettings.razor` | 5 | MudGrid preset buttons + MudCard categories |
| `Pages/Devices/TelescopeSetup.razor` | 6 | MudGrid + MudCard + MudChip + MudIcon |
| `Pages/MountSettings.razor` | 7 | MudTabs + MudNumericField + MudAlert |
| `wwwroot/css/bootstrap/` | 8 | **Delete directory** |
| `wwwroot/css/site.css` | 8 | Remove Bootstrap bridge block and overrides |

**Zero changes required:**
- All `@code` blocks in all pages
- All C# service files
- All model files
- `Program.cs` (except one line in Phase 0)
- `wwwroot/css/fonts.css`
- `wwwroot/fonts/` directory
- `wwwroot/js/profileUtils.js`
- All controller files
- All settings-related projects

---

## 7. Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| `EditForm` + MudBlazor field validation display differences | Medium | Low | Test MountSettings form submission in Phase 7; `ValidationMessage` still renders |
| MudBlazor `MudNavLink` active state detection differs from Blazor `NavLink` | Low | Low | MudNavLink uses `NavLink` internally; behaviour should match |
| Self-hosted Roboto conflicts with MudBlazor's font expectations | Low | Low | MudBlazor uses whatever Roboto is available; no CDN call if already loaded |
| `MudDrawer` responsive behaviour on small screens | Medium | Medium | Test drawer open/close on narrow viewport; adjust `Breakpoint` parameter if needed |
| MudBlazor `MudTable` vs custom `.gs-table` styling conflicts | Low | Low | MudTable uses theme tokens; `gs-table` class is still available for custom pages |
| `Variant.Outlined` on `MudNumericField` changes form density in MountSettings | Low | Medium | Use `Margin="Margin.Dense"` to compact 775-line form |
| Phase ordering — a page might be visited before its phase is done | Low | Low | During development both Bootstrap and MudBlazor CSS coexist; pages not yet migrated still render |

---

## 8. Testing Checklist

After each phase commit, verify the following manually:

### Phase 1 (Shell)
- [ ] Drawer opens and closes on hamburger click
- [ ] Drawer auto-collapses on mobile viewport (<641px)
- [ ] All nav links navigate to correct pages
- [ ] Active nav link is highlighted green
- [ ] Brand "GS Alpaca" renders with correct weight and green prefix
- [ ] "About" link in app bar works

### Phase 2 (Trivial pages)
- [ ] Index page renders text
- [ ] Setup page saves settings (verify via reload)
- [ ] TelescopeState device selector buttons switch device
- [ ] TelescopeState status values update on live data

### Phase 3 (Health Check)
- [ ] Health check page loads and shows status component
- [ ] Spinner shows while loading
- [ ] Success/error/warning states render with correct colours
- [ ] No `oi oi-*` text fallbacks visible

### Phase 4 (Device Manager)
- [ ] Device table renders with correct data
- [ ] Refresh button works
- [ ] Error/warning alerts dismiss on close
- [ ] No spinner stuck visible after load

### Phase 5 (Monitor Settings)
- [ ] Preset buttons apply settings
- [ ] Save feedback alert shows and dismisses

### Phase 6 (Telescope Setup)
- [ ] Card headers have green/info/secondary colouring
- [ ] Badge/chip elements render correctly

### Phase 7 (Mount Settings)
- [ ] All tabs are clickable and content switches
- [ ] Form inputs accept values
- [ ] Save succeeds and success alert shows
- [ ] Validation errors show on invalid input
- [ ] Reload button restores from disk

### Phase 8 (Bootstrap Removal)
- [ ] Build succeeds with 0 errors
- [ ] No `Cannot resolve` CSS errors in browser console
- [ ] All pages from Phases 1–7 re-tested

---

## 9. Recommended MudBlazor Documentation References

| Topic | URL |
|-------|-----|
| Getting started (Blazor Server) | https://mudblazor.com/getting-started/installation |
| MudThemeProvider | https://mudblazor.com/components/themeprovider |
| MudLayout + MudDrawer | https://mudblazor.com/components/layout |
| MudNavMenu | https://mudblazor.com/components/navmenu |
| MudTable | https://mudblazor.com/components/table |
| MudTabs | https://mudblazor.com/components/tabs |
| MudTextField / MudNumericField | https://mudblazor.com/components/textfield |
| MudAlert | https://mudblazor.com/components/alert |
| MudCard | https://mudblazor.com/components/card |
| MudButton | https://mudblazor.com/components/button |
| MudGrid | https://mudblazor.com/components/grid |
| Icons reference | https://mudblazor.com/features/icons |
| Dark mode / theming | https://mudblazor.com/customization/overview |

---

## 10. Summary Timeline

| Phase | Scope | Estimated Time |
|-------|-------|---------------|
| 0 — Install and wire up | NuGet, services, providers, theme object | 30 min |
| 1 — Shell layout | MainLayout + NavMenu | 45 min |
| 2 — Trivial pages | Index, Setup, TelescopeState | 45 min |
| 3 — Health components | SettingsHealthCheck + SettingsHealthStatus | 45 min |
| 4 — DeviceManager | Table, spinner, alerts | 45 min |
| 5 — MonitorSettings | Grid layout, preset buttons | 45 min |
| 6 — TelescopeSetup | Cards, badges, icons | 45 min |
| 7 — MountSettings | Tabs, form fields (largest) | 90 min |
| 8 — Bootstrap removal | Delete assets, clean CSS | 30 min |
| **Total** | | **~8 hours** |
