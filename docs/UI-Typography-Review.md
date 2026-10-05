# GreenSwamp Alpaca Server – UI Typography & Font-Size Review

**Prepared for:** Andy
**Date/time:** 2026-10-05 09:38
**Scope:** `GreenSwamp.Alpaca.Server` UI only – `Components\**`, `Pages\**`, `Shared\**`, `Theme\GsTheme.cs`, `App.razor`, `Pages\_Layout.cshtml`, `wwwroot\css\site.css`, `wwwroot\css\fonts.css`. No markdown files, mount, settings or driver code were used as evidence.
**Type:** Review and recommendations only. **No code has been changed.**

---

## 1. Evidence base and caveats

| Source | Used for |
|---|---|
| Code in scope above | All counts, line numbers and findings (evidence-based) |
| MudBlazor MCP (v9.10.0, per your instruction) | `Typo` enum (`h1`–`h6`, `subtitle1/2`, `body1/2`, `button`, `caption`, `overline`); `MudText` (`Typo` default `body1`, `HtmlTag`, `Inline`, `GutterBottom`, `Color`); `MudLink.Typo` (default `inherit`); `MudSimpleTable`/`MudTable`/`MudTh`/`MudTd` parameters (none control font size); `Size` enum |
| Microsoft Learn MCP | `HeadContent`/`HeadOutlet` for injecting head content from a component (used in recommendation R1) |

**Limits of the MudBlazor MCP.** It does not expose the theme `Typography` class model, the generated CSS variables (`--mud-typography-*`), or the internal CSS of table cells, inputs, buttons, tabs and nav links. Statements below about those internals are marked **[verify]**. They come from general MudBlazor knowledge and should be confirmed with browser DevTools (computed `font-size` and the `--mud-typography-*` variables on `:root`) before the change is implemented. This matches your usual DevTools-first practice.

---

## 2. Executive summary

1. **Zoom is applied at the wrong level.** `MainLayout` and `ChartWindowLayout` put `font-size:{px}` and `--gs-global-font-scale` on the `<MudLayout>` element only (`MainLayout.razor:20,108-115`). `site.css` independently hard-codes `html { font-size: 14px }` (line 53). The result is two different font bases in one page. Anything rendered **outside** `<MudLayout>`, or sized in **rem**, does not follow zoom.
2. **Dialogs, dropdown popups, menus, tooltips and snackbars are outside the zoom scope.** `MudPopoverProvider`, `MudDialogProvider` and `MudSnackbarProvider` are siblings of `<MudLayout>` (`MainLayout.razor:14-18`), not children. **[verify in DevTools]** They inherit the 14px `html` base, while the page inherits 16px × scale. This is the most likely cause of the "`Typo.h2` used where `body1` should be" workaround. Dialog body text at `body1` (0.875em × 14px = 12.25px) looked too small next to page text (0.875em × 16px = 14px), so `h2` (1.125em) was used to compensate. This is a hypothesis consistent with the code. Please confirm it by checking the computed size of a dialog `MudText` against a page `MudText`.
3. **The theme mixes units.** `GsTheme.cs` defines every size in `em` (compounds through nesting). `subtitle1`, `subtitle2` and `overline` are not defined in the theme, so they fall back to MudBlazor defaults (rem **[verify]**), which come from a different base again.
4. **The theme's heading scale is inverted.** At 16px base, `h5` = 14px (same as `body1`), `h6` = 13px (smaller than `body1`), `h4` = 15px, `h3` = 16px, `h2` = 18px, `h1` = 24px. Headings are barely larger than body text, so authors reach for `h2`/`h1` to get "bigger text" (body copy, telemetry readouts, a count badge).
5. **Raw HTML tables bypass Typo.** There are 42 `<th>`, 165 `<td>`, and 87 `<tr>` raw elements across 8 files; 18 `MudSimpleTable` and only 1 `MudTable`. Cell text is either unstyled (inherits MudBlazor table CSS **[verify]**), wrapped in `MudText Typo="body1"` (72 `<td>` in `MountStatus.razor` alone), or sized by `site.css` `em` rules (`.ts-table th`).
6. **`site.css` has substantial dead and conflicting content.**
   - About 40% of the 915 lines are unused Bootstrap-era or speculative styles.
   - `gs-font-mono` is used 38 times in Razor but is **never defined** in any CSS. The monospace readouts are not actually monospace. `.gs-monospace` (unused) is the intended-but-misnamed rule.
   - `--gs-global-font-scale` is set but never read by any CSS or Razor.
   - `.gs-panel-heading` overrides the font size of `Typo.overline` with `0.70em`, a direct overlap.
7. **Hard-coded px/rem sizes defeat zoom.** There are 10 inline `font-size` declarations (8 in rem), 18 `font-size` declarations in `site.css`, and 149 fixed px width/height values in Razor. With a root-based zoom these could all follow zoom; with the current layout-level zoom, rem and px values do not.
8. **The Zoom setting duplicates its constants** (min 0.75 / max 1.5 / step 5%) in `MainLayout`, `ChartWindowLayout` and `UserInterfaceEditor`, and the base size differs (16 vs 14 vs css 14).

**Recommended direction (detail in §6):** zoom the **root** (`:root`/`html` font-size) from one shared helper. Convert the theme to a monotonic **rem** scale with every Typo variant defined explicitly. Replace raw font sizes with `Typo` or with `var(--mud-typography-*-size)` references. Delete roughly 400 lines of dead CSS.

---

## 3. How zoom works today (assessment)

### 3.1 Mechanism

| Location | What it does |
|---|---|
| `Shared\MainLayout.razor:20` | `<MudLayout Style="@LayoutStyle">` |
| `MainLayout.razor:106-115` | `BaseFontSizePx = 16.0`; style = `font-size:{16*scale}px;--gs-global-font-scale:{scale};` |
| `Pages\Charts\ChartWindowLayout.razor:14,39-45` | Same pattern but `BaseFontSizePx = 14.0` |
| `wwwroot\css\site.css:53` | `html { font-size: 14px; }` |
| `wwwroot\css\site.css:9` | `--gs-global-font-scale: 1.0` declared; **no consumer anywhere** |
| `Components\SettingsGroups\UserInterfaceEditor.razor:38-65` | UI to pick 75%–150% in 5% steps; stored in `Config.GlobalFontScale` |
| `Theme\GsTheme.cs:59-85` | All typography in `em` |

### 3.2 Why it only partly works

| Element type | Follows zoom today? | Reason |
|---|---|---|
| `MudText` on pages inside `MudLayout` using em-based Typo | **Yes** | `em` resolves against the layout's `font-size` |
| `MudText` with `Typo.subtitle1/2`, `overline` (not in theme) | **Probably not** **[verify]** | MudBlazor default rem resolves against `html` (14px, not zoomed) |
| Inline `font-size: x rem` (8 places) and `site.css` rem/clamp values (home hero, slide caption, `.gs-home-links`) | **No** | rem is relative to `html`, not the layout |
| Dialog content (`MudDialogProvider`) | **No** **[verify]** | Rendered outside `MudLayout`; base is `html` 14px |
| Select/menu/autocomplete popup items (`MudPopoverProvider`) | **No** **[verify]** | Same. `site.css:58-72` forces `font-size: inherit !important`, so they inherit the unzoomed popover host |
| Snackbars, tooltips | **No** **[verify]** | Provider/popover-hosted |
| Padding/margins (`pa-*`, `mb-*`), icon sizes (`Size.Large` etc.), button heights | **No** **[verify]** | MudBlazor spacing and sizes are rem-based, so they scale only if `html` changes |
| Fixed px dimensions (149 occurrences, e.g. `min-width:380px`, `hc-cell-size:50px`, tile 150×120) | **No** | px is never zoomed by font-size |
| `AxisDial` SVG text (`fontSize "12"/"16"/"18"`) | **No** | SVG user units scaled by the px `Size` parameter (default 160) |
| ApexCharts text (separate library) | **No** | Not driven by Typo; out of scope except noting it is independent |

**Net effect:** text in the main page body zooms; boxes, icons, spacing, dialogs, popups and the home page do not. At 150% this produces clipped or wrapped text in fixed-width containers and mismatched proportions.

### 3.3 Verdict on adequacy

The pattern is **not adequate** as a single global zoom lever. The idea (one setting → one CSS font size) is sound, but it is applied on the wrong element, with a different base in each place, and the theme's `em` units compound rather than anchor to it.

---

## 4. Inventory of font-size control methods in use

| # | Method | Count | Where | Zooms? |
|---|---|---|---|---|
| 1 | `MudText Typo=…` (264 `<MudText>`, 227 with Typo) | 227 | 48 files | Yes for theme-defined variants, **[verify]** for undefined ones |
| 2 | `MudText` with **no** Typo (defaults to `body1`) | 37 | `SettingsHealthCheck` 6, `GoToPanel` 4, `ObservatorySingleEditor` 4, `MountConfigurationEditor` 4, `AuthenticationEditor` 3, `SettingsHealthStatus` 3, others | Yes (works, but implicit) |
| 3 | Raw `<td>/<th>` with unstyled text | ~180 | `MountStatus`, `TelescopeSetup`, `CdcDialog`, `GpsFixDialog`, `MonitorSettingsCombinedPanel`, `HomeAndParkEditor`, `HcPulseGuidesEditor`, `DeviceInfoDialog` | Depends on MudBlazor table CSS **[verify]** |
| 4 | `.ts-table th { font-size: .8125em }` | 1 rule, 6 uses | `TelescopeSetup` | Yes, but em-compounds |
| 5 | Inline `style="font-size:…rem"` | 8 | `MonitorSettingsCombinedPanel` ×5 (0.75rem), `GoToPanel:139` (1.1rem), `MonitorSettings:35` (1.00rem), `MainLayout:25` (1.4rem) | **No** (rem vs html) |
| 6 | `site.css` `font-size` declarations | 18 | em, rem, px, clamp, `inherit !important` | Mixed |
| 7 | `font-size: inherit !important` on MudBlazor input/list selectors | 1 block | `site.css:58-72` | Inherits unzoomed in popups **[verify]** |
| 8 | Raw HTML tags (`<strong>` 33, `<span>` 13, `<p>` 3, `<code>` 3, `<pre>` 1, `<h1>` 1, `<figcaption>`) | ~55 | dialogs, `Index.razor`, `HomeCarousel`, `SettingsHealth*` | Inherits |
| 9 | SVG attribute `font-size` | 3 | `AxisDial.razor` (via `SvgText`) | Not via Typo |
| 10 | Fixed px width/height | 149 | 29 files; worst: `ObservatoryConfigurationEditor` 19, `MountConfigurationEditor` 19, `ObservatorySingleEditor` 17, `TrackingAndGuidingEditor` 14, `LimitsEditor` 11 | **No** |
| 11 | Inline `font-weight` | 9 | `MonitorSettingsCombinedPanel` ×5, `DeviceManagerCard`, `GoToPanel`, `MainLayout` ×2 | n/a (weight) |
| 12 | `font-family: monospace` inline / `gs-font-mono` class | 1 inline + 38 class uses | `MonitorSettings:35`; `MountStatus` 32, `MountControl` 5, `TelescopeView` 1 | n/a |
| 13 | `Size.Small` (94), `Dense` (74), `Margin.Dense` (12) | – | Control sizing, not typography; **not** font-size drivers by themselves | n/a |

### 4.1 Typo variant usage (as written in Razor)

| Typo | Uses | Files | Observation |
|---|---|---|---|
| `h1` | 2 | `DeviceInfoDialog` (value), `MountControl:150` (client count) | Used as "big number", not as a heading |
| `h2` | 17 | `AboutDialog` ×4, `ConfirmDialog`, `SettingsExportDialog` ×6, `DeviceTile`, `MountControl` ×5 | **Misuse.** Body text, dialog text, tile name, telemetry readout |
| `h3` | 1 | `MainLayout:47` (version label in drawer) | Misuse; label, not a heading |
| `h4` | 3 | "Simulator" banner on `MountConfiguration`, `MountControl`, `MountStatus` | Banner text |
| `h5` | 6 | Page titles | Reasonable semantics, but 14px = body size |
| `h6` | 27 | 22 files: dialog titles (~15), card/section titles (~10), AppBar clock and brand | Reasonable semantics, but 13px < body |
| `subtitle1` | 4 | Chart window headers, `TelescopeView` | Not in theme → MudBlazor default |
| `subtitle2` | 17 | Sub-section labels (9 files) | Not in theme → MudBlazor default |
| `body1` | 97 | 16 files (70 in `MountStatus` table cells) | Correct, but verbose in tables |
| `body2` | 21 | 13 files | OK |
| `caption` | 21 | 10 files | OK |
| `overline` | 11 | 3 files | Not in theme; overridden by `.gs-panel-heading 0.70em` |
| `button` | 0 | – | Only via MudButton styling (theme `Button` defined) |

### 4.2 Per-file inventory

Columns: MudText total / MudText with no Typo / Typo variants used / inline `font-size` count / raw `<td>` count / fixed-px width-height count / `Size.Small` count.

| File | MudText | No Typo | Typo variants | font-size | `<td>` | px dims | Size.Small |
|---|---|---|---|---|---|---|---|
| `Components\About\AboutDialog.razor` | 4 | 0 | h2=4 | 0 | 0 | 3 | 0 |
| `Components\ButtonBar\AutoHomeDialog.razor` | 3 | 0 | h6=1 subtitle2=1 body2=1 | 0 | 0 | 0 | 0 |
| `Components\ButtonBar\FlipSopDialog.razor` | 3 | 0 | h6=1 body2=1 caption=1 | 0 | 0 | 0 | 0 |
| `Components\ButtonBar\ReSyncDialog.razor` | 2 | 0 | h6=1 body2=1 | 0 | 0 | 0 | 0 |
| `Components\DeviceManagerCard.razor` | 4 | 0 | h6=1 body1=1 caption=2 | 0 | 0 | 2 | 1 |
| `Components\Dialogs\AcceptCoordinatesDialog.razor` | 5 | 0 | h6=1 body1=2 caption=2 | 0 | 0 | 0 | 0 |
| `Components\Dialogs\AddDeviceDialog.razor` | 2 | 1 | h6=1 | 0 | 0 | 0 | 0 |
| `Components\Dialogs\AddObservatoryDialog.razor` | 2 | 1 | h6=1 | 0 | 0 | 0 | 0 |
| `Components\Dialogs\BreakingSettingsChangeDialog.razor` | 5 | 0 | h6=1 body1=1 body2=3 | 0 | 0 | 0 | 0 |
| `Components\Dialogs\CdcDialog.razor` | 3 | 0 | h6=1 body1=1 body2=1 | 0 | 12 | 1 | 2 |
| `Components\Dialogs\ConfirmDialog.razor` | 1 | 0 | **h2=1** | 0 | 0 | 0 | 2 |
| `Components\Dialogs\DeleteDeviceDialog.razor` | 4 | 0 | h6=1 body1=1 body2=2 | 0 | 0 | 0 | 0 |
| `Components\Dialogs\GpsFixDialog.razor` | 2 | 0 | h6=1 body1=1 | 0 | 14 | 1 | 1 |
| `Components\Dialogs\HcPulseGuidesDialog.razor` | 1 | 0 | h6=1 | 0 | 0 | 0 | 0 |
| `Components\Dialogs\HotReloadDevicesDialog.razor` | 5 | 0 | h6=1 body1=1 body2=3 | 0 | 0 | 0 | 0 |
| `Components\Dialogs\ManageParkPositionsDialog.razor` | 2 | 1 | h6=1 | 0 | 0 | 0 | 5 |
| `Components\Dialogs\SettingsExportDialog.razor` | 7 | 0 | **h2=6** body1=1 | 0 | 0 | 0 | 0 |
| `Components\GoToPanel.razor` | 9 | 4 | body1=3 overline=2 | **1 (1.1rem)** | 0 | 2 | 4 |
| `Components\HandControllerPanel.razor` | 1 | 0 | body1=1 | 0 | 0 | 0 | 1 |
| `Components\Home\DeviceInfoDialog.razor` | 2 | 0 | **h1=1** body1=1 | 0 | 1 | 0 | 0 |
| `Components\Home\DeviceTile.razor` | 2 | 0 | **h2=1** caption=1 | 0 | 0 | 0 | 0 |
| `Components\PersistentFloatingWindow.razor` | 2 | 0 | subtitle2=1 body1=1 | 0 | 0 | 2 | 1 |
| `Components\SettingsGroups\AlpacaSettingsEditor.razor` | 4 | 1 | subtitle2=1 body1=2 | 0 | 0 | 8 | 0 |
| `Components\SettingsGroups\AuthenticationEditor.razor` | 3 | 3 | – | 0 | 0 | 4 | 0 |
| `Components\SettingsGroups\HandControllerEditor.razor` | 2 | 2 | – | 0 | 0 | 2 | 0 |
| `Components\SettingsGroups\HcPulseGuidesEditor.razor` | 6 | 0 | body2=1 caption=5 | 0 | 4 | 1 | 0 |
| `Components\SettingsGroups\HomeAndParkEditor.razor` | 3 | 1 | subtitle2=2 | 0 | 9 | 2 | 0 |
| `Components\SettingsGroups\LimitsEditor.razor` | 1 | 0 | subtitle2=1 | 0 | 0 | 11 | 0 |
| `Components\SettingsGroups\MonitorSettingsCombinedPanel.razor` | 2 | 1 | h6=1 | **5 (0.75rem)** | 26 | 6 | 3 |
| `Components\SettingsGroups\MountConfigurationEditor.razor` | 6 | 4 | subtitle2=2 | 0 | 0 | 19 | 0 |
| `Components\SettingsGroups\ObservatoryConfigurationEditor.razor` | 2 | 2 | – | 0 | 0 | 19 | 3 |
| `Components\SettingsGroups\ObservatorySectionCard.razor` | 3 | 0 | h6=1 body2=2 | 0 | 0 | 0 | 4 |
| `Components\SettingsGroups\ObservatorySingleEditor.razor` | 4 | 4 | – | 0 | 0 | 17 | 3 |
| `Components\SettingsGroups\OpticsEditor.razor` | 0 | 0 | – | 0 | 0 | 4 | 0 |
| `Components\SettingsGroups\PerformanceTuningEditor.razor` | 0 | 0 | – | 0 | 0 | 3 | 0 |
| `Components\SettingsGroups\PpecEditor.razor` | 1 | 0 | subtitle2=1 | 0 | 0 | 0 | 0 |
| `Components\SettingsGroups\TrackingAndGuidingEditor.razor` | 0 | 0 | – | 0 | 0 | 14 | 0 |
| `Components\SettingsGroups\UserInterfaceEditor.razor` | 4 | 0 | body2=2 caption=2 | 0 | 0 | 4 | 0 |
| `Components\SettingsGroups\VoiceSettingsEditor.razor` | 0 | 0 | – | 0 | 0 | 4 | 1 |
| `Components\SettingsHealthStatus.razor` | 9 | 3 | h6=1 subtitle2=2 caption=3 | 0 | 0 | 0 | 7 |
| `Pages\Charts\ChartWindowLayout.razor` | 0 | 0 | – | **1 (zoom)** | 0 | 0 | 0 |
| `Pages\Charts\PulseChart.razor` | 1 | 0 | subtitle1=1 | 0 | 0 | 3 | 2 |
| `Pages\Charts\PulseChartDev.razor` | 2 | 0 | subtitle1=1 body2=1 | 0 | 0 | 1 | 4 |
| `Pages\Charts\RaDecChart.razor` | 1 | 0 | subtitle1=1 | 0 | 0 | 2 | 2 |
| `Pages\Devices\TelescopeSetup.razor` | 10 | 2 | h6=3 body1=5 | 0 | 27 | 0 | 0 |
| `Pages\MonitorSettings.razor` | 3 | 0 | h5=1 h6=1 caption=1 | **1 (1.00rem, pre)** | 0 | 2 | 5 |
| `Pages\MountConfiguration.razor` | 2 | 0 | h4=1 h5=1 | 0 | 0 | 0 | 1 |
| `Pages\MountControl.razor` | 15 | 0 | **h1=1 h2=5** h4=1 h5=1 body1=5 overline=2 | 0 | 0 | 3 | 2 |
| `Pages\MountStatus.razor` | 79 | 0 | h4=1 h5=1 **body1=70** overline=7 | 0 | **72** | 3 | 3 |
| `Pages\SettingsExplorer.razor` | 4 | 1 | h5=1 h6=1 body2=1 | 0 | 0 | 5 | 6 |
| `Pages\SettingsHealthCheck.razor` | 21 | 6 | h5=1 h6=3 subtitle2=6 body2=2 caption=3 | 0 | 0 | 1 | 28 |
| `Pages\TelescopeView\TelescopeView.razor` | 2 | 0 | subtitle1=1 caption=1 | 0 | 0 | 0 | 1 |
| `Shared\MainLayout.razor` | 3 | 0 | **h3=1** h6=2 | **2 (1.4rem; zoom)** | 0 | 0 | 0 |

Files with no typography markers (not tabulated): `AxisDial` (SVG text, see §5.9), `NavMenu` (MudNavLink only), `GlobalNotificationsHost`, `MountInfo`, `PersistentFloatingWindow` (listed), `Index`, `Home\HomeCarousel`, `Home\HomeDeviceBar` (class-driven via `site.css`).

---

## 5. Detailed findings

### 5.1 Two font bases (html 14px vs layout 16px × scale)
- `site.css:53` `html { font-size: 14px; }`
- `MainLayout.razor:106` `BaseFontSizePx = 16.0`
- `ChartWindowLayout.razor:39` `BaseFontSizePx = 14.0`

The same `Typo.body1` renders at 14px on a main page, 12.25px in a dialog, and 12.25px in a chart window (at 100%). **Impact:** inconsistent sizes and the `h2` workarounds. All three must collapse to one base.

### 5.2 Providers sit outside the zoom scope
`MainLayout.razor:14-18` renders `MudThemeProvider`, `MudPopoverProvider`, `MudDialogProvider`, `MudSnackbarProvider` and `GlobalNotificationsHost` **before** `<MudLayout Style=…>`. Anything these render does not inherit the zoomed `font-size`. `PersistentFloatingWindow` (line 60) is also outside `MudLayout`. **[verify]** with DevTools: `$0.closest('.mud-layout')` on a dialog, a select popup and a snackbar should return `null`.

### 5.3 Theme: em units, missing variants, inverted scale
`Theme\GsTheme.cs:59-85`:

| Variant | Theme value | px at base 16 | Note |
|---|---|---|---|
| Default | 0.875em | 14 | |
| h1 | 1.5em | 24 | |
| h2 | 1.125em | 18 | |
| h3 | 1em | 16 | |
| h4 | 0.9375em | 15 | |
| h5 | 0.875em | 14 | = body1 |
| h6 | 0.8125em | 13 | **< body1** |
| body1 | 0.875em | 14 | |
| body2 | 0.8125em | 13 | |
| button | 0.8125em | 13 | |
| caption | 0.75em | 12 | |
| subtitle1 / subtitle2 / overline | *not defined* | MudBlazor default **[verify]** | rem-based **[verify]** |

`em` units compound: a `MudText` nested inside another element that has its own em-sized font (for example `.ts-table th` at 0.8125em, or `.gs-panel-heading` at 0.70em) shrinks multiplicatively. `rem` is anchored to `html` and is the right unit for a root-based zoom.

### 5.4 Typo used as a size workaround
Most of the cases you suspected, all confirmed in the code:
- **Dialog body text at h2:** `ConfirmDialog.razor:6`, `AboutDialog.razor:3,15,26,38` (×3 content + 1 commented title), `SettingsExportDialog.razor:12,19,22,30,52`. All should be `body1` (or `body2` for secondary text).
- **Telemetry readouts at h2:** `MountControl.razor:63,67,71,75,79`. They sit next to `body1` labels, so they intentionally want a larger readout. This needs a deliberate "readout" style (see R4).
- **Tile name at h2:** `DeviceTile.razor:15`.
- **h1 as big number:** `MountControl.razor:150` (client count, with `Style="line-height:1"`) and `DeviceInfoDialog.razor:32`.
- **h3 as version label:** `MainLayout.razor:47`.
- **Banner "Simulator" at h4:** three pages.
- **AppBar:** `MainLayout.razor:25` is `h6` with inline `font-size:1.4rem; font-weight:300; letter-spacing:0.01em`. This overrides almost the entire variant, so it is a custom style masquerading as a Typo.

### 5.5 Raw tables
- `MountStatus.razor` (72 `<td>`) wraps every cell value in `<MudText Typo="Typo.body1" …>` and every label in `<MudText Typo.body1 Color.Primary>`. This is verbose, bloats the render tree (~79 MudText components) and sets a Typo that may differ from the table's native cell typography **[verify]**. Column widths are set inline (`width:50px`, `90px`, `150px`), which do not zoom.
- `TelescopeSetup.razor` uses raw `<th>` and `<td>` with `.ts-table`. `site.css:241-249` gives `th` `0.8125em` but `td` has no size, so label and value sizes differ by design or by accident, and there are 5 `MudText` with no Typo mixed in.
- `MonitorSettingsCombinedPanel.razor` uses 5 inline-styled `<span>` labels (`font-size:0.75rem; font-weight:500`) inside raw `<td>` (26 cells). These are `caption`-equivalent and do not zoom (rem).
- `CdcDialog.razor` and `GpsFixDialog.razor`: 12 and 14 raw `<td>`, with inline `style="border-bottom:none; border-top:none; padding:0 2px"`.
- `HcPulseGuidesEditor.razor` uses `<th>` with `MudText Typo.caption` (correct pattern; headers via Typo).
- `ObservatorySectionCard.razor` is the only `MudTable` (with `MudTh`). Note that `MudTh`/`MudTd` have no typography parameters; table cell typography is a MudBlazor CSS concern.

### 5.6 `site.css` – redundant, overlapping, dead

915 lines. 37 `!important`. 18 `font-size` declarations.

**Dead (no usage anywhere in scope):**

| Rule(s) | Lines | Evidence |
|---|---|---|
| `.gs-card`, `.gs-card-header` | 147-164 | 0 references |
| `.btn-gs-danger` | 166-180 | 0 |
| `.gs-badge` + 5 variants | 182-198 | 0 |
| `.gs-monitor`, `-row`, `-index`, `-time`, `-message` | 200-226 | 0 |
| `.gs-table` family incl. `.gs-value` | 228-277 (except `.ts-table` 235-256, which is used) | 0 |
| `.gs-monospace` | 138-145 | 0 (intended-but-misnamed rule for `gs-font-mono`) |
| `.gs-live` + `@keyframes gs-live-pulse` | 293-303 | 0 |
| `.valid.modified`, `.invalid`, `.validation-message` | 279-291 | no `EditForm` in scope |
| `.material-symbols-outlined` (**twice**: `fonts.css:56-71` 24px; `site.css:108-115` 1.25em) | – | 0 uses; icons are MudBlazor SVG icons |
| `.nav-item ::deep .material-symbols-outlined` | 117-119 | `::deep` is only valid in scoped CSS, so it is inert in a global stylesheet |
| `.gs-home-links` (+ `a`, `:hover`, `:focus`, `:focus-visible`) | 406-445 | Only reference is inside a commented-out `@* … *@` block in `Index.razor` |
| `main, .content, article` background rule; `.content { padding-top }`; `a, .btn-link` | 90-106 | Bootstrap-era; `.content` used only by `Error.cshtml:17` |
| `.hc-speed-value` | 752-754 | 0 (selector for a class never applied) |
| `fonts.css` Roboto Mono 400 `@font-face` | – | Used (`--gs-font-mono`); keep. Swamp Witch used (`gs-home-footer-text`); keep |

**Missing (used, not defined):** `gs-font-mono`, 38 uses (`MountStatus` 32, `MountControl` 5, `TelescopeView` 1). The CSS variable `--gs-font-mono` exists (line 49) and `.gs-monospace` has the right content, but the class actually applied has no rule. The telemetry values are currently rendered in the default proportional font.

**Overlapping/redundant:**
- `.gs-panel-heading` (75-80) sets `font-size: 0.70em` on top of `Typo.overline` (10 uses). Typo is then partly overridden: a direct conflict with the goal.
- Lines 58-72: one comma-list of 12 selectors (`.mud-input-slot` appears twice; `.mud-input-root-input` overlaps `.mud-input-slot.mud-input-root-input`; `.mud-textfield .mud-input-slot` overlaps `.mud-input-slot`) forcing `font-size: inherit !important`. This blanket override disables MudBlazor's own input typography and is the reason inputs need no Typo. It is also what stops popup list items from zooming.
- `--gs-global-font-scale` (line 9): defined and set inline, **never consumed**.
- `--gs-text-*`, `--gs-bg-*`, `--gs-accent-*`, `--gs-success/warning/error/info`, `--gs-divider`: duplicated in `GsTheme.cs` palette (acknowledged in its comments). Only `--gs-accent-500`, `--gs-accent-500-rgb`, `--gs-bg-sidebar`, `--gs-divider` are referenced from Razor; the rest are referenced only inside `site.css` itself.
- Spacing tokens `--gs-space-*` (40-46): referenced only by dead rules.
- Unit mix in `font-size`: em (11), rem (4), px (1), clamp (2), inherit (1).
- Two `html`-level concerns in one file (`html {font-size:14px}`, `body {font-family…}`) when MudBlazor/theme already supply them.

**Live and legitimate (keep):** home hero/carousel/tile styles (348-662), hand-controller grid (669-789), `gs-icon-button-rounded`, goto-overlay, ApexCharts toolbar overrides (818-915), blazor error UI/boundary, `code`, `.gs-tabs-no-transform`, `.shutdown-nav-link`.

Estimated removable: ≈ 300-350 lines (dead + Bootstrap leftovers + duplicate font-face rule).

### 5.7 Hard-coded rem/px outside zoom
- Inline rem: `GoToPanel.razor:139` (1.1rem), `MainLayout.razor:25` (1.4rem), `MonitorSettings.razor:35` (1.00rem), `MonitorSettingsCombinedPanel.razor:37,52,68,84,120` (0.75rem).
- `site.css` rem/clamp: `.gs-home-title` `clamp(1.75rem,4vw,3rem)`, `.gs-home-footer-text` `clamp(1.1rem,2.5vw,1.8rem)`, `.gs-slide-caption` 1rem, `.gs-home-links a` 0.875rem. Home hero uses vw-based clamp, which by design ignores zoom. Decide whether the home hero should zoom (R8).
- Fixed px layout: tiles `150×120`, hand controller `--hc-cell-size: 50px`, `--hc-speed-column-width: 72px`, `.gs-icon-button-rounded 50px`, dialog `min-width: 380px/320px`, tooltip `max-width:260px` (repeated 8+ times in `AlpacaSettingsEditor`, `AuthenticationEditor`, `UserInterfaceEditor`, etc.), `AboutDialog` `height:400px`, `MountControl` `Style="height:64px"`, `min-width:300px`.

### 5.8 Configuration duplication
`MinGlobalFontScale 0.75`, `MaxGlobalFontScale 1.5` and `DefaultGlobalFontScale 1.0` are repeated in `MainLayout.razor:63-65`, `ChartWindowLayout.razor:21-23`, and `UserInterfaceEditor.razor:38-40` (percent form). `BuildLayoutStyle` is duplicated verbatim in two layouts.

### 5.9 Non-Typo text
- `AxisDial.razor` SVG `fontSize "12"/"16"/"18"` inside a `viewBox 0 0 240 260`, scaled by the `Size` px parameter (`MountInfo.DialSize = 160`). Text scales with the dial but not with zoom unless `DialSize` is zoom-aware.
- `<strong>` (33), `<code>` (3), `<pre>` (1), `<figcaption>`, `<h1 class="gs-home-title">` rely on inherited or ad hoc sizes. `<strong>` and `<code>` inherit correctly if the parent is a `MudText`; `code` has `font-size: 0.85em` (line 312, em; acceptable relative sizing).
- `PersistentFloatingWindow`: correctly uses `MudText` Typo; fixed px frame.

---

## 6. Recommendations

Ordered by impact. Each item notes the files affected.

### R1. Move zoom to the root and make it the only zoom lever (highest impact)
**What:** Set the zoom on `:root`/`html` (`font-size: calc(16px * scale)`), not on `<MudLayout>`. Because everything (theme typography, MudBlazor spacing, icons, dialogs, popovers, snackbars, tooltips) resolves rem against `html`, one value scales the whole UI, including the providers rendered outside `MudLayout` **[verify]**.

**How (Blazor-native, no JS needed):** Use `<HeadContent>` from `MainLayout` and `ChartWindowLayout` to emit `<style>:root{font-size:Npx}</style>` (Microsoft Learn: `HeadContent` + `HeadOutlet`; `HeadOutlet` is already in `_Layout.cshtml:15`). Because `HeadOutlet` is `ServerPrerendered`, confirm the `<style>` updates on interactive re-render when the setting changes. If it does not, fall back to a one-line JS interop setting `document.documentElement.style.fontSize`, which is a UI-only helper alongside `profileUtils.js`.

**Also:**
- Remove `html { font-size: 14px }` from `site.css:52-54` (or set it to `100%`). The 14px page-level look is retained by making `body1` = 0.875rem (see R2), not by shrinking `html`.
- Remove `Style="@LayoutStyle"` from `<MudLayout>`; delete `--gs-global-font-scale` unless a consumer is introduced.
- Create **one** shared helper (e.g. a static `GsZoom` class beside `GsTheme`) holding `Min/Max/Step/Default/BasePx` and `BuildRootStyle(scale)`. Reference it from `MainLayout`, `ChartWindowLayout` and `UserInterfaceEditor`. This removes three copies of the constants and two copies of `BuildLayoutStyle`.
- Decision for Andy: browsers' own zoom (Ctrl +/-) is unaffected by this; root font-size is cumulative with it.

**Alternative considered:** CSS `zoom:` on `body`/`#app`. It would zoom px dimensions too (solving §5.7 without edits), but it behaves inconsistently with fixed-position popovers/dialogs, and it is a non-standard-until-recently property. Not recommended as the primary mechanism, but it is a possible fallback for the chart window.

### R2. Rebuild the theme typography in rem, define every variant, restore a monotonic scale
In `GsTheme.cs`:
- Change all `em` to `rem`.
- Explicitly define `Subtitle1`, `Subtitle2`, `Overline` (currently implicit MudBlazor defaults).
- Make the heading scale descend monotonically and sit **above** `body1`, so that nobody needs a "bigger body" workaround.

Proposed scale (**Andy to confirm; see Open Decisions**):

| Variant | Proposed | px @100% | Intended role |
|---|---|---|---|
| h1 | 1.75rem | 28 | Hero/big display (rare) |
| h2 | 1.5rem | 24 | Major numeric display |
| h3 | 1.25rem | 20 | Page title |
| h4 | 1.125rem | 18 | Banner / telemetry readout |
| h5 | 1rem | 16 | Section title |
| h6 | 0.9375rem | 15 | Dialog / card title |
| subtitle1 | 1rem, w500 | 16 | Panel header |
| subtitle2 | 0.875rem, w500 | 14 | Sub-section label |
| body1 | 0.875rem | 14 | **Default text** |
| body2 | 0.8125rem | 13 | Secondary text |
| button | 0.8125rem | 13 | (unchanged) |
| caption | 0.75rem | 12 | Table headers, hints |
| overline | 0.75rem, uppercase, tracking .08em | 12 | Panel group headings |

Keep `Default` at 0.875rem. This preserves today's visible 14px body at 100% on the main page.

**Visual impact (be aware):** dialogs grow from 12.25px to 14px at 100% (they were mis-based); chart windows likewise. Headings get larger than today, which is the point.

### R3. Remove the `h2`/`h1`/`h3` workarounds (concrete mapping)

| Location | Current | Replace with |
|---|---|---|
| `ConfirmDialog:6` | h2 | `body1` |
| `AboutDialog:3` (commented), `15, 26, 38` | h2 | `body1` (text blocks) |
| `SettingsExportDialog:12,19,22,30,52` | h2 | `body1` (labels/values), `subtitle2` for headings |
| `DeviceTile:15` | h2 | `subtitle1` (or `h5`), tile-fit check needed |
| `DeviceInfoDialog:32` | h1 | `h4` (value) / `body1` (label) |
| `MountControl:150` | h1 + `Style="line-height:1"` | `h4`, drop inline line-height if possible |
| `MountControl:63-79` | h2 + `gs-font-mono` | `h4` + monospace class (R4) |
| `MainLayout:47` | h3 | `caption` or `body2` (version text) |
| `MainLayout:25` | h6 + 3 inline overrides | `h5` + a single `gs-brand` class (weight/tracking only; size from Typo) |
| `GoToPanel:139` | body1 + inline `1.1rem; 500` | `subtitle1` |
| `MonitorSettingsCombinedPanel` ×5 spans | inline 0.75rem/500 | `<MudText Typo="Typo.caption" Color="Color.Secondary" HtmlTag="span">` |
| Page titles (`h5` ×6) | h5 | `h3` under the new scale |
| Simulator banner (`h4` ×3) | h4 | `h5` / `h4` per taste |
| Dialog titles (`h6` ×~15) | h6 | unchanged (matches new `h6` role) |

### R4. Fix monospace once, without touching size
Replace the missing `gs-font-mono` and the unused `.gs-monospace` with one rule that sets **only** the family:
```css
.gs-font-mono { font-family: var(--gs-font-mono); }
```
No `font-size` (inherits from the `MudText` Typo). Delete `.gs-monospace`. If tabular figures help alignment, add `font-variant-numeric: tabular-nums` here, not a size.

### R5. Tables: one approach per table type
- **Key/value tables** (`MountStatus`, `TelescopeSetup`, `CdcDialog`, `GpsFixDialog`): remove the per-cell `MudText` wrapper and let the table cell inherit typography, then (a) verify that MudBlazor cell font size matches your intended `body1`/`body2` **[verify]**, and (b) where it does not, set it once per table with a class that **references the theme variable**:
  ```css
  .gs-kv-table th, .gs-kv-table td { font-size: var(--mud-typography-body1-size); }
  .gs-kv-table th               { font-size: var(--mud-typography-caption-size); }
  ```
  The `--mud-typography-*` names must be confirmed in DevTools **[verify]**. This keeps a single source of truth (the theme) and zooms automatically, replacing `.ts-table th { 0.8125em }`.
- Alternatively, introduce a tiny `GsValue`/`GsKeyValueRow` component (label as `caption`, value as `body1` + mono) used by `MountStatus` to remove ~140 repeated `MudText` lines. This is a refactor suggestion and optional.
- Replace inline column widths (`width:50px/90px/150px`) with `rem` or percent widths.
- Replace `border-bottom:none; border-top:none; padding:0 2px` repeated inline styles (`CdcDialog`) with one scoped class.
- Prefer `MudTable`/`MudTh`/`MudTd` for genuinely tabular data (only `ObservatorySectionCard` uses it); keep `MudSimpleTable` for layout grids.
- `th` that contain only text (`TelescopeSetup`) should either be wrapped in `MudText Typo.caption` (as `HcPulseGuidesEditor` does) or styled via the class above. Choose one.

### R6. Rationalise `site.css`
Target structure (in this order), roughly 500-550 lines from 915:

1. **Tokens** (`:root`): keep only tokens actually referenced (`--gs-font-mono`, `--gs-accent-500`, `--gs-accent-500-rgb`, `--gs-bg-sidebar`, `--gs-divider`, plus `--gs-accent-300` and the rgba values used in home/Apex rules). Delete `--gs-global-font-scale`, `--gs-space-*`, grey ramp, and text/status variables that are duplicated in the theme palette.
2. **Base:** `body` font-family only (theme supplies it; consider removing). No `html` font-size.
3. **Typography helpers:** `.gs-font-mono`, `.gs-panel-heading` (**color + letter-spacing only; drop `font-size: 0.70em`**), `.gs-tabs-no-transform`, `.gs-brand` if adopted.
4. **Layout/feature blocks:** home hero/carousel/tiles, hand controller, goto overlay, nav.
5. **Third-party overrides:** ApexCharts (isolated; consider moving into `chart.css`).
6. **Framework UI:** blazor error UI / boundary.

Delete: all items in the §5.6 "Dead" table, the 12-selector `font-size: inherit !important` block (after R1/R2 verification that input and popup text inherits the theme correctly), and the duplicate `.material-symbols-outlined` (both copies; `fonts.css` keeps only the `@font-face` rules that are actually used).

Where a non-MudText element needs typography (home hero title/caption, `figcaption`, `pre`), use `var(--mud-typography-*-size)` or a `rem` value, never `em` and never `px`.

### R7. Make px dimensions zoom-friendly where text lives
Convert intrinsic component dimensions to `rem` where they must grow with text: dialog `min-width` (`380px/320px` → `23.75rem/20rem`), tooltip `max-width:260px` (also: move to one shared class instead of 8+ inline repeats), `AboutDialog` `height:400px`, hand-controller cell/column sizes, tile 150×120, `MountControl` `height:64px` bars. Keep px for 1-2px borders and for icon/image intrinsic assets.

### R8. Decide how the home page and special surfaces zoom
`vw`-based `clamp()` on the hero title and footer is deliberately viewport-relative. Options: leave as is (documented exception) or replace with `clamp(…rem, …vw, …rem)` so zoom still affects min/max. `AxisDial`: make `DialSize` = `rem * 10` equivalent or accept it as a fixed visual. ApexCharts font sizes are configured in C# chart options; leave out of this scope but note they do not follow zoom.

### R9. Convention for contributors (to prevent regression)
Add to the UI conventions (your preferred location):
1. Visible text in Razor uses `MudText Typo=…`; default `body1` (omit `Typo` only deliberately).
2. No `font-size`, `em`, `px` or `rem` in `style=` attributes. Size changes are a Typo change.
3. Raw HTML text elements (`th`, `td`, `pre`, `figcaption`) get size only from a shared CSS class that references `--mud-typography-*` variables.
4. Heading variants express hierarchy, never "make this bigger". If a bigger readout is needed, use the designated variant (R2 table).
5. New dimensions that contain text use rem.
6. `site.css` must not target `.mud-*` internals for font-size.

### R10. Smaller cleanups
- Add explicit `Typo` to the 37 bare `MudText` (optional; they default to `body1`).
- `MudLink`: set `Typo` explicitly if links appear inside body text (default is `inherit`, which is already zoom-safe).
- `ChartWindowLayout` and `MainLayout` both declare `MudThemeProvider`; after R1 they share the helper, not copies.

---

## 7. Suggested implementation sequence

| Phase | Work | Risk | Verify (DevTools + eyeball) |
|---|---|---|---|
| 0 | Confirm §5.2 and the `--mud-typography-*` variable names, and MudBlazor's table-cell, input, and button font sizes | None | `$0.closest('.mud-layout')` on dialog/popup/snackbar; computed `font-size` on dialog `MudText`, `td`, `th`, input |
| 1 | `GsZoom` shared helper; root-level zoom via `HeadContent`; remove `html{14px}` and layout-level `Style`; unify base to 16px | Low-medium (dialogs and chart windows grow) | Zoom 75/100/150 on main page, dialog, select popup, snackbar, chart window |
| 2 | Theme: em→rem, define all variants, new scale | Medium (visible re-flow) | Page titles, dialogs, tiles, status page |
| 3 | Replace `h2/h1/h3` workarounds and inline `font-size` (R3) | Low (mechanical) | Each file in §5.4 |
| 4 | Monospace fix (R4) and `.gs-panel-heading` size removal | Low | `MountStatus`, `MountControl` readouts now actually monospace |
| 5 | Table normalisation (R5) | Medium | `MountStatus`, `TelescopeSetup`, `CdcDialog`, `GpsFixDialog`, monitor panel |
| 6 | `site.css` pruning (R6) | Low if dead-list verified | Home, hand controller, nav, charts |
| 7 | px→rem conversions (R7/R8) | Medium | 150% zoom on settings dialogs and HC panel |

Commit after each phase.

---

## 8. Open decisions for Andy

1. **Heading scale (R2):** adopt the proposed larger conventional scale (visible change to page/section titles), or retune the theme to **keep today's pixel sizes** and only fix the semantics/units? The latter is lower visual risk but keeps headings ≈ body size.
2. **Base size:** is 16px at 100% zoom (so body1 = 14px) acceptable as the root base? This preserves the current main-page look; dialogs and chart windows will change.
3. **Telemetry readouts (`MountControl`):** which variant should represent "readout" (proposed `h4`, 18px)?
4. **Home page:** should the hero (vw-based) follow zoom?
5. **Tables:** accept letting MudBlazor cell typography apply (plus a `--mud-typography-*` class) instead of per-cell `MudText`, after DevTools confirms the cell sizes?
6. **Chart window:** same root zoom (recommended), but note ApexCharts text is configured separately and will not zoom.

---

## 9. Items I could not confirm from the MudBlazor MCP

- Whether `.mud-table` cells, `.mud-input`, `.mud-button`, `.mud-tab`, `.mud-nav-link` and `.mud-list-item` take their size from the Typography theme variables or from fixed rem.
- The exact CSS variable names emitted by `MudThemeProvider` for typography (`--mud-typography-<variant>-size/weight/lineheight/family`).
- The exact rem defaults for `subtitle1`, `subtitle2` and `overline` when absent from the theme.
- Whether `MudPopoverProvider`/`MudDialogProvider` DOM output is outside `.mud-layout` in this app (the Razor order in `MainLayout.razor` strongly suggests it).

All four are quick DevTools checks (Phase 0) and should be done before Phase 1.

---

*Report generated 2026-10-05 09:38. No source files were modified.*
