# Home Page Redesign – Requirements

**Document status:** Draft for offline review by Andy
**Last updated:** 2026-10-01 08:16
**Target:** `GreenSwamp.Alpaca.Server` (Blazor Server, .NET 10, MudBlazor 9.11.0)
**Primary files affected:** `Pages/Index.razor`, `wwwroot/css/site.css`, `GreenSwamp.Alpaca.Settings` (`ServerConfig`, new manifest model/service)

---

## 1. Purpose and scope

Replace the content of the default home page (`/`) while retaining the existing title header and hero background image. The new page has:

1. A horizontal bar pinned to the bottom of the page containing a row of telescope device tiles and, beneath it, the "Green Swamp Alpaca Server" wordmark.
2. A rolling image carousel with two user-selectable shows (Server reference screenshots, Astronomical images). The images and captions are developer-configurable and are not compiled into the build.

---

## 2. Decisions recorded from Andy

| # | Question | Decision |
|---|----------|----------|
| D1 | Bullet list and link buttons on current page | **Remove the bullet list. Keep the two link buttons** (Online Documentation, ASCOM Standards). |
| D2 | Where images/manifest live | **Images in `wwwroot/images/...` (deployed content, replaceable without a rebuild); slide manifest in the versioned settings folder.** |
| D3 | Who supplies images and captions | **Andy supplies the jpg files and captions.** None exist in the repo today. |
| D4 | Dwell-time setting storage | **Property on `ServerConfig` (`appsettings.server.user.json`), with no editor field, so Settings Explorer never shows it.** |
| D5 | Show selection | **Toggle on the carousel; the selected show is persisted in settings (`ServerConfig`) across restarts.** |
| D6 | Devices with `Enabled = false` | **Show a tile for every defined device; dim/mark disabled ones.** |
| D7 | Tile settings-cog target | **`/mount-settings/{n}`** (existing per-device Mount Settings page). |
| D8 | Carousel placement | **Fills the hero area between the title/link buttons and the bottom bar.** |
| D9 | Telescope icon | **Use `Icons.Material.Filled.TravelExplore`.** MudBlazor 9.11 has no telescope icon (checked the Filled and Outlined sets via reflection). The app already uses this icon for telescope devices. |

---

## 3. Current state (evidence)

- `Pages/Index.razor` (46 lines): hero wrapper `gs-home-hero-wrap` > `gs-home-hero` > overlay, content (h1 title with version, 7-item bullet list, two link buttons), and a `<footer class="gs-home-footer">` containing the text "Green Swamp Alpaca Server".
- `wwwroot/css/site.css` lines 334-474 define `gs-home-*`. The background is `url('https://greenswamp.org/wp-content/uploads/2022/03/GreenSwamp1.jpg')` (remote URL). `.gs-home-hero` uses `min-height: calc(100vh - 48px)` and `margin: -1rem` on the wrapper cancels `MudContainer` padding. The wordmark uses the `Swamp Witch` font (`.gs-home-footer-text`).
- Layout: `Shared/MainLayout.razor` uses `MudLayout` + `MudAppBar` (dense) + responsive `MudDrawer` (200px) + `MudMainContent` > `MudContainer pa-4`.
- Device sources:
  - `IVersionedSettingsService.GetAlpacaDevices()` returns `AlpacaDevice` (`DeviceNumber`, `DeviceName`, `DeviceType`, `UniqueId`). This list drives the existing Mount Control and Mount Settings tabs.
  - `IVersionedSettingsService.GetAllDeviceSettings()` / `GetDeviceSettings(n)` returns `SkySettings` (`DeviceName`, `Enabled`, `Mount`, `AlignmentMode`, `DeviceDescription`).
- Target routes already exist: `/mount-control/{DeviceNumber:int}` and `/mount-settings/{DeviceNumber:int}`.
- Sample data in `%AppData%\GreenSwampAlpaca\0.2.0`: 6 devices (0-5), e.g. `Simulator PC (GEM) | Simulator | GermanPolar | "GreenSwamp ASCOM Alpaca Telescope Simulator"`.
- `ServerConfig` is persisted via `GetServerConfig()` / `SaveServerConfigAsync()` and raises `ServerConfigChanged`. First run seeds from the `ServerConfig` section of `appsettings.json`. `MainLayout` already subscribes to `ServerConfigChanged`.
- Settings Explorer editors are hand-written Razor (for example `IdentityUiEditor.razor`). A `ServerConfig` property with no editor field is therefore never displayed, and no reflection-driven display exists.
- Static files are already served from `wwwroot` (`app.UseStaticFiles`, `Program.cs` ~line 584).
- The installer (`GreenSwamp.Alpaca.Installer/ProductFiles.wxs`) lists published `wwwroot` files individually.

---

## 4. Functional requirements

### 4.1 Page layout

| ID | Requirement |
|----|-------------|
| HP-L1 | Retain the hero background image, dark gradient overlay and the h1 title ("Welcome to Green Swamp Alpaca Server (vX.Y.Z)"). |
| HP-L2 | Remove the 7-item bullet list. Retain the "Online Documentation" and "ASCOM Standards" link buttons below the title (D1). |
| HP-L3 | The hero is a full-height flex column: **title + link buttons (top)**, **carousel (fills remaining space, D8)**, **bottom bar (pinned to the bottom)**. The page must not scroll vertically at normal desktop sizes. |
| HP-L4 | The bottom bar spans the full width of the content area (it does not sit under or behind the navigation drawer, whether the drawer is open or closed). |
| HP-L5 | The existing `gs-home-footer` element is removed from its current position and its wordmark text moves into the bottom bar (see HP-B2). |

### 4.2 Bottom bar

| ID | Requirement |
|----|-------------|
| HP-B1 | The bar contains two items stacked vertically and horizontally centred: (a) the row of device tiles, (b) the "Green Swamp Alpaca Server" wordmark below it. |
| HP-B2 | The wordmark keeps the current styling (`Swamp Witch` font, accent green, glow). |
| HP-B3 | The tile row is centred when it fits. If tiles exceed the available width (up to 100 devices are permitted by the settings service), the row scrolls horizontally within the bar. It must not wrap, must not push the carousel off screen, and must not clip the first tile when scrolling. |
| HP-B4 | The bar has a semi-transparent dark background so tiles and wordmark remain legible over the hero image. |

### 4.3 Device tiles

| ID | Requirement |
|----|-------------|
| HP-T1 | One tile per entry returned by `GetAlpacaDevices()`, ordered by device number. Device details (mount, alignment mode, description, enabled flag) are joined from `SkySettings` by device number. |
| HP-T2 | Tile content: telescope icon (`TravelExplore`, D9), device name, a **settings cog in the lower-right** and an **information icon in the lower-left**. |
| HP-T3 | Device name is the `AlpacaDevice.DeviceName`, falling back to `Device {n}` if blank. Long names truncate with ellipsis and show the full name as a tooltip. |
| HP-T4 | Tiles for devices whose settings have `Enabled = false` are visually dimmed and carry a "Disabled" indicator (D6). |
| HP-T5 | If a device has an `AlpacaDevice` entry but no settings file (or the reverse), the tile still renders. Missing fields display "Unknown" instead of throwing. Devices that exist only as a settings file are not shown (consistent with the Mount Control page). |
| HP-T6 | If there are no devices, show a single message in the tile row: "No telescope devices are configured." with a link to Settings Explorer. |

### 4.4 Tile actions

| ID | Requirement |
|----|-------------|
| HP-A1 | Clicking the tile body navigates to `/mount-control/{n}`. |
| HP-A2 | Clicking the settings cog navigates to `/mount-settings/{n}` (D7). It must not also trigger HP-A1. |
| HP-A3 | Clicking the information icon displays a read-only list of: **Device number, Device name, Description, Mount type, Alignment mode**. It must not also trigger HP-A1. |
| HP-A4 | Alignment mode is shown as the friendly text already used by `DeviceManagerCard` (`GermanPolar` becomes "German Equatorial (GEM)", `Polar` becomes "Polar / Fork", `AltAz` becomes "Alt-Azimuth"). Mount type is shown as stored (`Simulator`, `SkyWatcher`). |
| HP-A5 | Tooltips on the cog and info icons ("Mount settings", "Device information"). Tiles are keyboard-operable (Tab, Enter/Space) and icons have `aria-label`s. |
| HP-A6 | Disabled devices (HP-T4) remain navigable. This is an assumption; see Section 9, item 2. |

### 4.5 Carousel

| ID | Requirement |
|----|-------------|
| HP-C1 | A carousel with **left and right arrows** and the **standard bullet indicator** of the current slide (MudCarousel `ShowArrows` and `ShowBullets`). |
| HP-C2 | Each slide shows one jpg image with **one line of caption text displayed below the image**. |
| HP-C3 | Slide changes use a **fade-out / fade-in transition of 0.5 s** for both image and caption. |
| HP-C4 | The carousel **auto-advances continuously** and wraps from the last slide to the first. |
| HP-C5 | Dwell time per slide is configurable (HP-S1), default **5 seconds**. Dwell is the time from one slide change to the next and includes the 0.5 s fade. |
| HP-C6 | Manual arrow or bullet navigation changes the slide and restarts the dwell timer. Auto-advance continues afterwards. |
| HP-C7 | Images are shown in full (`object-fit: contain`, no cropping) within the available area, maintaining aspect ratio. Captions are single-line with ellipsis and show the full text as a tooltip. |
| HP-C8 | The next slide's image is preloaded so the fade-in never shows an empty frame. |
| HP-C9 | If a show has no slides, or an image file is missing, the carousel shows a neutral placeholder rather than throwing. The problem is logged once (not on every render). |
| HP-C10 | Bullets must not overlap the caption. Space is reserved at the bottom of each slide. |

### 4.6 Show selection

| ID | Requirement |
|----|-------------|
| HP-SH1 | Two shows are available: **Green Swamp Server reference screenshots** and **Astronomical images**. The user selects between them with a toggle control on the carousel (for example `MudToggleGroup`). |
| HP-SH2 | Individual images are not user-selectable apart from carousel navigation (arrows and bullets). |
| HP-SH3 | The selected show is persisted in `ServerConfig` (D5) and restored on the next visit and server restart. Default is the Server reference show. |
| HP-SH4 | Changing show restarts the carousel at slide 1. |
| HP-SH5 | The toggle is built from the manifest (HP-M1). If only one show has slides, the toggle is hidden. |

### 4.7 Settings

| ID | Requirement |
|----|-------------|
| HP-S1 | New `ServerConfig.CarouselDwellSeconds` (default `5`). Values outside **2-60 s** are clamped on read (minimum chosen so dwell always exceeds the 0.5 s fade). |
| HP-S2 | New `ServerConfig.CarouselActiveShow` (string show id, default `"reference"`). An unknown id falls back to the first show in the manifest. |
| HP-S3 | Neither property is displayed in Settings Explorer (D4). No editor control is added. Their values are preserved when Settings Explorer saves `ServerConfig` (the working copy is a full deserialised `ServerConfig`). |
| HP-S4 | Defaults are added to `appsettings.json` (`ServerConfig` section) and `appsettings.schema.json` so first-run seeding works. |
| HP-S5 | A developer changes the dwell time by editing `appsettings.server.user.json`. The page picks up the change via `ServerConfigChanged` or on the next page load. |

### 4.8 Developer-managed content (manifest)

| ID | Requirement |
|----|-------------|
| HP-M1 | A JSON manifest in the versioned settings folder defines the shows, slide order, image file names and captions. Nothing about images or captions is hardcoded in C#, Razor or CSS. Proposed file: `carousel.settings.json`. |
| HP-M2 | Images are jpg files in `wwwroot/images/carousel/{showFolder}/` and are served by the existing static-file middleware. Adding, removing, reordering or re-captioning slides requires only file and manifest edits, with no rebuild. |
| HP-M3 | Image file names in the manifest must be plain file names (no path separators or `..`). Anything else is rejected and logged. |
| HP-M4 | The manifest is read through the settings service (new `GetCarouselManifest()` and `CarouselManifestChanged` on `IVersionedSettingsService`, following the `ChartSettings` pattern) so there is one access route to settings files. |
| HP-M5 | If the manifest is absent on first run, it is seeded from a factory default shipped with the build, in the same way `ServerConfig` seeds from `appsettings.json`. |
| HP-M6 | A corrupt or invalid manifest does not break the home page. The carousel is hidden or shows the placeholder, and the error is logged. |

**Proposed manifest shape**

```json
{
  "SchemaVersion": 1,
  "Shows": [
	{
	  "Id": "reference",
	  "Title": "Server reference",
	  "Folder": "reference",
	  "Slides": [
		{ "File": "mount-control.jpg", "Caption": "Mount Control - hand controller and GoTo" }
	  ]
	},
	{
	  "Id": "astronomy",
	  "Title": "Astronomical images",
	  "Folder": "astronomy",
	  "Slides": [
		{ "File": "m31.jpg", "Caption": "M31 - Andromeda Galaxy" }
	  ]
	}
  ]
}
```

---

## 5. Non-functional requirements

| ID | Requirement |
|----|-------------|
| NF1 | Pure Blazor Server and MudBlazor. No new JavaScript interop is required for the carousel, tiles or toggle. |
| NF2 | Dark theme and `GsTheme` colours and CSS variables (`--gs-accent-500` etc.). Must respect `GlobalFontScale` (layout already applies it). |
| NF3 | Desktop (1280x720 and up) is the primary target. The layout must degrade gracefully at narrower widths (tile row scrolls; carousel shrinks). Touch swipe on the carousel remains enabled. |
| NF4 | The 0.5 s transition is honoured. Honour `prefers-reduced-motion` by shortening the transition (optional, low priority). |
| NF5 | Home page rendering must not block on device I/O. Device data is read once on initialisation (settings service reads are local file reads) and refreshed on `DeviceSettingsChanged`. |
| NF6 | Recommended image guidance for Andy: about 1600-1920 px wide, jpg, ideally under 500 KB each; about 20 slides per show or fewer (one bullet per slide). |
| NF7 | Existing pages, routes (`/` stays the home route), the nav menu and the MountControl / MountSettings pages are unchanged. |

---

## 6. Design notes (MudBlazor MCP and Microsoft Learn findings)

These are verified against MudBlazor 9.x source (MudCarousel / MudCarouselItem) and the Learn documentation.

1. **Fade timing:** `Transition.Fade` in MudBlazor uses `0.5s` keyframes for both fade-in and fade-out, which exactly matches the requirement. No custom CSS keyframes are needed. The outgoing and incoming slides cross-fade at the same time (exit slide held at z-index 1, entering at z-index 2).
2. **`ItemsSource` cannot be used:** when `ItemsSource` is set, MudCarousel creates `<MudCarouselItem>` with defaults (**`Transition = Slide`**), so per-item `Transition.Fade` cannot be applied. The carousel must therefore be built with explicit `<MudCarouselItem Transition="Transition.Fade">` children generated by a `@foreach` over the slides, with `TData="object"`.
3. **Dwell time:** `AutoCycleTime` (`TimeSpan`, default 5 s) is a native parameter, so `TimeSpan.FromSeconds(config.CarouselDwellSeconds)` is bound directly. The timer is reset whenever the selection changes (including arrow or bullet clicks), which satisfies HP-C6.
4. **Wrap-around:** MudCarousel `Next()` restarts at index 0, giving continuous looping.
5. **Show switching:** to guarantee a clean rebuild when the show changes, put `@key="activeShowId"` on the carousel (items register themselves on initialisation and only unregister on dispose).
6. **Item background:** `MudCarouselItem` with the default colour can paint a theme background (`mud-carousel-item-default`). Override with a transparent item class so the hero image shows through. Verify in browser DevTools during implementation.
7. **Bullets and caption overlap:** arrows and bullets are overlaid on the carousel (z-index 3). Slide content needs bottom padding (about 40-48 px) so the caption sits above the bullets (HP-C10). Arrows use `ArrowsPosition.Center` by default.
8. **No nested interactive elements:** a clickable tile containing cog and info buttons creates invalid HTML (button inside button/anchor) and event bubbling. Recommended structure: a relatively positioned tile container holding (a) a full-size tile button/link for navigation and (b) the cog and info `MudIconButton`s as **absolutely positioned siblings** (bottom-right, bottom-left). If any button is nested inside a clickable element, use `@onclick:stopPropagation` (documented in Learn: ASP.NET Core Blazor event handling).
9. **Info display:** `MudDialog` (the app already has many dialogs under `Components/Dialogs`) is the simplest robust option from a bar pinned at the bottom of the viewport. A `MudPopover` anchored to the icon is an alternative. Proposed: **dialog** (see Section 9, item 3).
10. **Toggle control:** `MudToggleGroup<T>` (`Value`/`ValueChanged`, `SelectionMode.SingleSelection`) is available in 9.x and suits two choices. `MudButtonGroup` is the fallback.
11. **Pinning the bar:** the hero already has a fixed-height flex column with `min-height: calc(100vh - 48px)`. Making the bar the last flex child (rather than `position: fixed`) avoids having to compensate for the 200 px responsive drawer. This is a layout choice for the implementation phase.
12. **Static files:** Learn (Serve static files in ASP.NET Core) confirms files under `wwwroot` are served by `UseStaticFiles` with no further configuration. `.jpg` is mapped by the default content-type provider (the app uses a custom `FileExtensionContentTypeProvider` that keeps the defaults).
13. **Compiled-in vs deployed:** `wwwroot` files are part of the project content. Replacing files in the *published* `wwwroot/images/carousel` folder changes the content without a rebuild, which meets the "not hardcoded into the build" intent.

---

## 7. Impact assessment

| Area | Change |
|------|--------|
| `Pages/Index.razor` | Rewrite markup (hero + link buttons + carousel + bottom bar). May split into components. |
| New components (proposed) | `Components/Home/DeviceTile.razor`, `DeviceInfoDialog.razor` (in `Components/Dialogs`), `HomeCarousel.razor`. |
| `wwwroot/css/site.css` | Update/remove `gs-home-bullets` and `gs-home-footer` rules; add bottom bar, tile and carousel slide styles. Remove the remote hero URL only if Andy wants it localised (not requested; left unchanged). |
| `GreenSwamp.Alpaca.Settings` | `ServerConfig` + 2 properties. New `CarouselManifest` model(s). `IVersionedSettingsService` + `VersionedSettingsService`: `GetCarouselManifest()`, path property, change event, seeding. |
| `appsettings.json`, `appsettings.schema.json` | Add the two new `ServerConfig` keys. |
| Settings migration | `TryMigrateFromPreviousVersion` copies top-level `*.json` from the previous version, so the manifest will migrate automatically (see Risk R1). |
| Installer (`ProductFiles.wxs`) | Lists wwwroot files individually; new image files and folders must be included or the images will not be deployed (see Risk R2). |
| Server `.csproj` | Follow the existing `Content Update ... CopyToOutputDirectory` pattern for the new image folder, if needed for local runs. |
| Tests | Unit tests for manifest parsing, validation (HP-M3), dwell clamping (HP-S1), and show fallback (HP-S2), if a suitable test project exists in the solution. |

---

## 8. Risks and observations

| ID | Risk / observation | Proposed handling |
|----|--------------------|-------------------|
| R1 | **Manifest vs version migration.** Existing migration copies the previous version's JSON files only when the new version folder has no device files, and never overwrites existing files. A manifest edited for a new release could be shadowed by an older migrated copy. | Include `SchemaVersion` in the manifest. Consider exempting the manifest from migration (always seeded from the build's factory default) or having the loader prefer the shipped default when the schema version is older. **Needs Andy's decision before implementation.** |
| R2 | **Installer enumerates files explicitly.** New image files will not be installed unless added (or harvested). Also, the manifest filenames and images must stay consistent. | Add `images\carousel\**` to the installer as a dedicated, non-versioned component. Out of scope for the page itself; track as a follow-up task. |
| R3 | **Stale `ServerConfig` overwrite.** Settings Explorer holds a working copy of `ServerConfig`. If Andy changes the carousel show on the home page while the Explorer is open elsewhere and then saves, the Explorer's older copy could overwrite `CarouselActiveShow`. | Low likelihood. Accept (document), or have the home page save via a read-modify-write of the latest `ServerConfig`. The latter is planned. |
| R4 | **Persisted show is server-wide.** `ServerConfig` is shared by all browser sessions. One user changing the show changes it for everyone's next visit. | Consistent with D5. Noted for awareness. |
| R5 | **Many bullets.** One bullet per slide. A show with a very large number of slides makes the indicator row wide. | NF6 guidance (about 20 slides or fewer). |
| R6 | **Deviation from original wording.** The original brief said images are "stored in the settings folder"; D2 places them in `wwwroot/images`. The manifest is in the settings folder. | Confirm acceptable (this is what Andy selected). |
| R7 | **First-frame load.** Fade-in of a not-yet-downloaded image can show blank. | HP-C8 preloading of the next slide. |
| R8 | **Hot reload of devices.** Devices can be added or deleted at runtime. The interface has `DeviceSettingsChanged` but no add/remove event. | Tiles refresh on page load and on `DeviceSettingsChanged`. Add/remove flows navigate through Settings Explorer, so the home page reloads when returning. |

---

## 9. Assumptions to confirm

1. **Tile source** is `GetAlpacaDevices()` (matches Mount Control and Mount Settings tabs); settings details are looked up by device number.
2. **Disabled device tiles** are dimmed but still navigable (the brief said "dim/mark" only).
3. **Information display** is a small modal dialog rather than a popover.
4. **Dwell** includes the 0.5 s fade; clamped to 2-60 s.
5. Property names `CarouselDwellSeconds` and `CarouselActiveShow`, manifest file name `carousel.settings.json`, and image folder `wwwroot/images/carousel/{showFolder}/` are acceptable.
6. Hero background URL remains the remote `greenswamp.org` image (unchanged).
7. The two shows are fixed in purpose (reference and astronomy) but the toggle is generated from the manifest.

---

## 10. Out of scope

- Editing carousel images, captions or dwell from the UI.
- Pause-on-hover, per-slide durations, per-slide links.
- Any change to non-home pages, the nav menu, or the app bar.
- A custom SVG telescope icon (D9 uses the built-in icon).
- Installer packaging of the image folders (tracked as R2).
- Supplying the images and captions (Andy, D3).

---

## 11. Acceptance criteria

1. The home page shows title and link buttons at the top, the carousel in the remaining space, and a pinned bottom bar with a centred tile row above the "Green Swamp Alpaca Server" wordmark. No bullet list is present.
2. With the current 6 devices there are 6 tiles. Each shows the icon, the name, a cog (lower-right) and an info icon (lower-left).
3. Tile click goes to `/mount-control/{n}`; cog goes to `/mount-settings/{n}`; info lists device number, name, description, mount type and alignment mode. Cog and info clicks do not also navigate to the control page.
4. A disabled device tile is visibly dimmed.
5. The carousel auto-advances every 5 s by default, loops, shows a caption below each image, fades over 0.5 s, has working left/right arrows and bullets, and restarts the timer after manual navigation.
6. Changing `CarouselDwellSeconds` in `appsettings.server.user.json` changes the dwell time. The setting does not appear in Settings Explorer. Saving other Settings Explorer changes does not reset it.
7. Switching show restarts at slide 1 and the choice survives a server restart.
8. Editing `carousel.settings.json` and replacing jpg files changes the carousel with no rebuild.
9. Missing image, missing or corrupt manifest, zero devices, and a show with no slides all render without exceptions.
10. The solution builds with no new errors or warnings.

---

## 12. Proposed implementation phases (for planning only)

1. Settings: `ServerConfig` properties, appsettings and schema, manifest model and service, seeding, validation (plus tests).
2. Carousel component (`HomeCarousel`) with fade, bullets, arrows, preload, show toggle, persistence.
3. Device tiles and info dialog component(s).
4. `Index.razor` and CSS restructure (hero, bottom bar, wordmark).
5. Manual verification in browser (layout with drawer open and closed, 1 and 100 tiles, missing files) and installer follow-up.

*Each phase ends with a build check and a commit.*
