# GreenSwamp Alpaca Server – UI Typography & Font-Size Review

**Prepared for:** Andy
**Date/time:** 2026-10-05 10:15 (rev 2: all [verify] items resolved against MudBlazor v9.11.0 source)
**Scope:** `GreenSwamp.Alpaca.Server` UI only – `Components\**`, `Pages\**`, `Shared\**`, `Theme\GsTheme.cs`, `App.razor`, `Pages\_Layout.cshtml`, `wwwroot\css\site.css`, `wwwroot\css\fonts.css`. No markdown files, mount, settings or driver code were used as evidence.
**Type:** Review and recommendations only. **No code has been changed.**

---

## 1. Evidence base and caveats

| Source | Used for |
|---|---|
| Code in scope above | All counts, line numbers and findings (evidence-based) |
| MudBlazor MCP (v9.10.0, per your instruction) | `Typo` enum (`h1`–`h6`, `subtitle1/2`, `body1/2`, `button`, `caption`, `overline`); `MudText` (`Typo` default `body1`, `HtmlTag`, `Inline`, `GutterBottom`, `Color`); `MudLink.Typo` (default `inherit`); `MudSimpleTable`/`MudTable`/`MudTh`/`MudTd` parameters (none control font size); `Size` enum |
| MudBlazor GitHub source, tag `v9.11.0` (authorised by Andy; used to resolve every former **[verify]** item) | `MudThemeProvider.razor.cs` (variable generation, scope), `Typography.cs`/`Typo.cs` (defaults, variants), `_base.scss`, `_typography.scss`, `_simpletable.scss`, `_table.scss`, `_input.scss`, `_inputcontrol.scss`, `_icons.scss`, `_spacing.scss`, `_tooltip.scss`, `_chip.scss`, `_button.scss`, `MudListItem.razor.cs`, `MudDialogProvider.razor`, `MudPopoverProvider.razor`, `MudSnackbarProvider.razor`, `MudDialogContainer.razor` |
| Microsoft Learn MCP | `HeadContent`/`HeadOutlet`

**Limits of the MudBlazor MCP and how they were closed.** The MCP does not expose the theme `Typography` class model, the generated CSS variables (`--mud-typography-*`), or the internal CSS of table cells, inputs, buttons, tabs and nav links. In revision 1 those statements were marked **[verify]**. In this revision each one has been checked against the MudBlazor **v9.11.0** source on GitHub (per your instruction, the 9.10.0/9.11.0 difference is ignored). Findings are labelled **Confirmed (source)**. Two residual items are *derived* from the source plus `site.css` rather than observed in a browser; they are listed in §9 as optional DevTools checks (computed pixel values; specificity of `.ts-table th`).

---

## 2. Executive summary

1. **Zoom is applied at the wrong level.** `MainLayout` and `ChartWindowLayout` put `font-size:{px}` and `--gs-global-font-scale` on the `<MudLayout>` element only (`MainLayout.razor:20,108-115`). `site.css` independently hard-codes `html { font-size: 14px }` (line 53). The result is two different font bases in one page. Anything rendered **outside** `<MudLayout>`, or sized in **rem**, does not follow zoom.
**Confirmed (source):** `MudDialogProvider`, `MudPopoverProvider` and `MudSnackbarProvider` render their own containers and are not children of `.mud-layout`. They therefore do not receive the layout's inline `font-size`; they inherit from `body`, which MudBlazor sets to `--mud-typography-default-size` (`_base.scss`). In `GsTheme` that is `0.875em` against `html` 14px, i.e. about 12.25px, while page text is 0.875em × 16px × scale = 14px at 100%. This is the most likely cause of the "`Typo.h2` used where `body1` should be" workaround: dialog body text at `body1` looked too small next to page text, so `h2` (1.125em) was used to compensate. The mechanism is confirmed by source; the causal link to the workaround is still an inference from the code (the pixel values can be confirmed in DevTools, §9).
`subtitle1`, `subtitle2` and `overline` are not defined in the theme, so they fall back to MudBlazor's defaults, which are **rem** (1rem, 0.875rem, 0.75rem; **confirmed**, `Typography.cs`). Those resolve against `html` (14px, not zoomed), a different base again.
4. **The theme's heading scale is inverted.** At 16px base, `h5` = 14px (same as `body1`), `h6` = 13px (smaller than `body1`), `h4` = 15px, `h3` = 16px, `h2` = 18px, `h1` = 24px. Headings are barely larger than body text, so authors reach for `h2`/`h1` to get "bigger text" (body copy, telemetry readouts, a count badge).
Cell text is either unstyled (and then styled by MudBlazor's own `MudSimpleTable` rules: raw `td` = `body2`, raw `th` = `subtitle2`, **confirmed**, `_simpletable.scss`), wrapped
6. **`site.css` has substantial dead and conflicting content.**
   - About 40% of the 915 lines are unused Bootstrap-era or speculative styles.
   - `gs-font-mono` is used 38 times in Razor but is **never defined** in any CSS. The monospace readouts are not actually monospace. `.gs-monospace` (unused) is the intended-but-misnamed rule.
   - `--gs-global-font-scale` is set but never read by any CSS or Razor.
   - `.gs-panel-heading` overrides the font size of `Typo.overline` with `0.70em`, a direct overlap.
With a root-based zoom the rem values would follow zoom; with the current layout-level zoom they do not. **px values never follow a root font-size change** (see §3.2 and the R1 caveat).
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
| `MudText` with `Typo.subtitle1/2`, `overline` (not in theme) | **No** (confirmed, source) | MudBlazor default is rem (1rem / 0.875rem / 0.75rem), which resolves against `html` (14px, not zoomed) |
| Inline `font-size: x rem` (8 places) and `site.css` rem/clamp values (home hero, slide caption, `.gs-home-links`) | **No** | rem is relative to `html`, not the layout |
| Dialog content (`MudDialogProvider`) | **No** (confirmed, source) | Rendered outside `.mud-layout`; text takes `body` size (0.875em × html 14px ≈ 12.25px). Dialog title is a fixed `Typo.h6` (`MudDialogContainer`) |
| Select/menu/autocomplete popup items (`MudPopoverProvider`) | **No** (confirmed, source) | Same. `site.css:58-72` forces `font-size: inherit !important`, so they inherit the unzoomed popover host |
| Snackbars | **No** (confirmed, source) | `#mud-snackbar-container` is outside `.mud-layout` |
| Tooltips | **No, even with root zoom** | Fixed `font-size: 12px` in `_tooltip.scss` |
| Chips | **No, even with root zoom** | Fixed 12/14/16px by size (`_chip.scss`) |
| Padding/margins (`pa-*`, `mb-*`, `mx-*` …) | **No, even with root zoom** (confirmed, source) | Utility classes are fixed px (4px × n, `!important`) in `_spacing.scss`. **Correction to rev 1, which said they were rem-based.** |
| Icons (`MudIcon`/`Size.*`) | Only with root zoom | `Small/Medium/Large` = 1.25/1.5/2.25rem; default icon `1em`. Snackbar/alert icons fixed 22px; table sort icon 18px |
| Buttons | Only with root zoom | Text uses `--mud-typography-button-size` (theme, 0.8125em today). `Size.Small`/`Size.Large` use fixed 0.8125rem / 0.9375rem and ignore the `Button` variant |
| Inputs (text, numeric, select) | Follows the nearest ancestor | `.mud-input > input` and `.mud-input-slot` use `font: inherit` (`_input.scss`). Label uses `--mud-typography-subtitle1-size`; helper text is fixed 0.75rem |
| List/menu/select items | Follows the ancestor | `MudListItem` text is `mud-typography-body1` (`body2` when dense), secondary text `subtitle2`. Menu, tab and nav link set no font-size and inherit |
| Layout metrics (`LayoutProperties`) | **Partly** | MudBlazor defaults are px (AppBar 64px, drawer 240px). `GsTheme` sets `AppbarHeight` 3.5rem (zooms with `html`) but drawer width 250px (does not zoom) |
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
| Yes for theme-defined (em) variants inside `MudLayout`; **No** for undefined ones (`subtitle1/2`, `overline`: rem fallback, confirmed) |
| 2 | `MudText` with **no** Typo (defaults to `body1`) | 37 | `SettingsHealthCheck` 6, `GoToPanel` 4, `ObservatorySingleEditor` 4, `MountConfigurationEditor` 4, `AuthenticationEditor` 3, `SettingsHealthStatus` 3, others | Yes (works, but implicit) |
| Follows MudBlazor `MudSimpleTable` rules: `td` = theme `body2`, `th` = theme `subtitle2` (confirmed, `_simpletable.scss`). The raw `<table>` in `MonitorSettingsCombinedPanel` is **not** inside a `MudSimpleTable`, so it gets no MudBlazor table typography |
| 4 | `.ts-table th { font-size: .8125em }` | 1 rule, 6 uses | `TelescopeSetup` | Yes, but em-compounds |
| 5 | Inline `style="font-size:…rem"` | 8 | `MonitorSettingsCombinedPanel` ×5 (0.75rem), `GoToPanel:139` (1.1rem), `MonitorSettings:35` (1.00rem), `MainLayout:25` (1.4rem) | **No** (rem vs html) |
| 6 | `site.css` `font-size` declarations | 18 | em, rem, px, clamp, `inherit !important` | Mixed |
| Inherits unzoomed in popups (confirmed). Mostly redundant for inputs (MudBlazor already uses `font: inherit`), and it breaks the label's `subtitle1` size and list-item `body1/body2` |
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
| Not in theme → MudBlazor default 1rem (confirmed) |
| `subtitle2` | 17 | Sub-section labels (9 files) | Not in theme → MudBlazor default 0.875rem, weight 500 (confirmed) |
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
Anything these render does not inherit the zoomed `font-size`. `PersistentFloatingWindow` (line 60) is also outside `MudLayout`.

**Confirmed (source):** `MudDialogProvider.razor` renders the dialog container directly, `MudPopoverProvider.razor` renders its own `<div class="@ContainerClass">`, and `MudSnackbarProvider` renders `#mud-snackbar-container`. None is a descendant of `.mud-layout`. `MudThemeProvider` emits its variables on `:root` (default `PseudoCss.Scope`), so the variables are available everywhere, but the theme's `em` values resolve against the consuming element's parent. For these providers the parent chain is `body` → `html`, not the zoomed layout. Optional DevTools confirmation: `$0.closest('.mud-layout')` returns `null` on a dialog, a select popup and a snackbar.

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
| subtitle1 | *not defined* | 1rem = 14px (html) | MudBlazor default, **rem** (confirmed); ignores zoom |
| subtitle2 | *not defined* | 0.875rem = 12.25px (html), w500 | MudBlazor default, **rem** (confirmed) |
| overline | *not defined* | 0.75rem = 10.5px (html), lineheight 2.66, tracking .08333em | MudBlazor default, **rem** (confirmed) |

`rem` is anchored to `html` and is the right unit for a root-based zoom. **Confirmed (source):** MudBlazor's own default typography (`Typography.cs`) is entirely rem-based (Default .875rem; h1 6rem … h6 1.25rem; body1 1rem; body2 .875rem; button .875rem; caption .75rem), and variables are emitted on `:root` as `--mud-typography-{variant}-{family|size|weight|lineheight|letterspacing|text-transform}`. Moving `GsTheme` from em to rem therefore aligns with the library rather than fighting it. `.mud-typography-{variant}` (`_typography.scss`) simply applies those six variables, so an `em` value resolves against the parent element's font-size at the point of use, which is why nested em Typo elements compound.

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
sets a Typo (`body1`) that is larger than the table's native cell typography (`body2` for `td`, confirmed), so the wrapper is a deliberate-or-accidental size bump rather than a no-op.
- `TelescopeSetup.razor` uses raw `<th>` and `<td>` with `.ts-table`. `site.css:241-249` gives `th` `0.8125em` but `td` has no size. Inside a `MudSimpleTable`, MudBlazor already styles `.mud-simple-table table * tr > th` with `subtitle2` and `> td` with `body2` (`_simpletable.scss`, size, family, weight, line-height, letter-spacing). That selector is more specific than `.ts-table th`, so the custom `th` font-size is **probably dead or partly dead** (not measured; see §9). 5 `MudText` with no Typo are mixed in.
- `MonitorSettingsCombinedPanel.razor` uses a raw `<table>` (line 24, `table-layout:fixed`) that is **not** inside a `MudSimpleTable`, so it receives **no** MudBlazor table typography and its cells inherit the surrounding font-size. It has 5 inline-styled `<span>` labels (`font-size:0.75rem; font-weight:500`) inside raw `<td>` (26 cells). These are `caption`-equivalent and do not zoom today (rem against an unzoomed `html`).
- `CdcDialog.razor` and `GpsFixDialog.razor`: 12 and 14 raw `<td>`, with inline `style="border-bottom:none; border-top:none; padding:0 2px"`.
- `HcPulseGuidesEditor.razor` uses `<th>` with `MudText Typo.caption` (correct pattern; headers via Typo).
`MudTh`/`MudTd` have no typography parameters. **Confirmed (source, `_table.scss`):** `.mud-table-head .mud-table-cell` uses `subtitle2`, `.mud-table-body .mud-table-cell` uses `body2`, pagination is 0.875rem and the footer cell 0.75rem.

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
**Confirmed (source):** it is largely **redundant for inputs**, because MudBlazor already sets `font: inherit` on `.mud-input > input` and `.mud-input-slot` (`_input.scss`). Where it does have an effect it is harmful: it replaces the input label's `subtitle1` size (`_inputcontrol.scss`) and the list item's `body1`/`body2` class (`MudListItem`) with whatever the popover host inherits (the unzoomed `body` size). It is what stops popup items from zooming. The trailing comma after `.mud-input-label,` followed by a comment is valid CSS; leave it alone or delete the whole block.
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
Because the theme typography (after R2), MudBlazor icons (1.25/1.5/2.25rem), buttons, dialogs, popovers and snackbars resolve against `html`/`body`, one value scales all text including the providers rendered outside `MudLayout` (**confirmed**: `html` is the only common ancestor of the layout and the providers).

**Caveat: what a root font-size does *not* scale (confirmed, source).** Rev 1 overstated this. These stay fixed:

| Item | Fixed value | Source |
|---|---|---|
| Spacing utilities `pa-*`, `ma-*`, `mb-*`… | px (4px × n) | `_spacing.scss` |
| Tooltip text | 12px | `_tooltip.scss` |
| Chip text | 12/14/16px | `_chip.scss` |
| Dialog container padding | 32px | MudBlazor dialog styles |
| Snackbar/alert icons | 22px | MudBlazor snackbar/alert styles |
| Table sort icon | 18px | `_table.scss` |
| `Size.Small`/`Size.Large` button text | 0.8125rem / 0.9375rem (scales with root, but ignores the `Button` variant) | `_button.scss` |
| Input helper text | 0.75rem (scales with root, but ignores `caption`) | `_inputcontrol.scss` |
| `LayoutProperties` (drawer 250px in `GsTheme`; MudBlazor defaults are px) | px | `GsTheme.cs`, `LayoutProperties` |

Text that is px (tooltip, chip) stays the same size while everything around it grows. Options: (a) accept it; (b) override these few selectors in `site.css` with `rem` (a small, justified exception list, e.g. `.mud-tooltip { font-size: var(--mud-typography-caption-size) }` and chip sizes) ; (c) use CSS `zoom` (see below), which scales px as well.

**How (Blazor-native, no JS needed):** Use `<HeadContent>` from `MainLayout` and `ChartWindowLayout` to emit `<style>:root{font-size:Npx}</style>` (Microsoft Learn: `HeadContent` + `HeadOutlet`; `HeadOutlet` is already in `_Layout.cshtml:15`). Because `HeadOutlet` is `ServerPrerendered`, confirm the `<style>` updates on interactive re-render when the setting changes. If it does not, fall back to a one-line JS interop setting `document.documentElement.style.fontSize`, which is a UI-only helper alongside `profileUtils.js`.

**Also:**
- Remove `html { font-size: 14px }` from `site.css:52-54` (or set it to `100%`). The 14px page-level look is retained by making `body1` = 0.875rem (see R2), not by shrinking `html`.
- Remove `Style="@LayoutStyle"` from `<MudLayout>`; delete `--gs-global-font-scale` unless a consumer is introduced.
- Create **one** shared helper (e.g. a static `GsZoom` class beside `GsTheme`) holding `Min/Max/Step/Default/BasePx` and `BuildRootStyle(scale)`. Reference it from `MainLayout`, `ChartWindowLayout` and `UserInterfaceEditor`. This removes three copies of the constants and two copies of `BuildLayoutStyle`.
- Decision for Andy: browsers' own zoom (Ctrl +/-) is unaffected by this; root font-size is cumulative with it.

Not recommended as the primary mechanism, but it is a possible fallback for the chart window. *Note:* because spacing utilities and tooltip/chip text are px (see caveat above), CSS `zoom` is the only single-lever way to scale them. This trade-off is listed in §8 as a decision for Andy. (The behaviour of `zoom` with fixed-position popovers is general browser knowledge, not verified here.)

### R2. Rebuild the theme typography in rem, define every variant, restore a monotonic scale
In `GsTheme.cs`:
- Change all `em` to `rem`.
- Explicitly define `Subtitle1`, `Subtitle2`, `Overline` (currently implicit MudBlazor defaults, rem: 1rem / 0.875rem / 0.75rem, confirmed). Note `Overline` has `lineheight 2.66`, `letter-spacing .08333em` and uppercase by default, so set these explicitly if you want to keep them. Any variant whose `FontFamily` is null uses `Default.FontFamily`.
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
- **Key/value tables inside `MudSimpleTable`** (`MountStatus`, `TelescopeSetup`, `CdcDialog`, `GpsFixDialog`): **Confirmed (source):** MudBlazor already styles raw `td` as theme `body2` and raw `th` as theme `subtitle2`, including family, weight, line-height and letter-spacing, via `--mud-typography-*` variables. So the simplest, most MudBlazor-native approach is: (a) delete the per-cell `MudText` wrappers where `body2` is acceptable; (b) delete `.ts-table th { font-size }`; (c) where a table needs a different size, either keep a `MudText Typo=…` on that cell or add one class that references the confirmed variable names:
  ```css
  .gs-kv-table td { font-size: var(--mud-typography-body1-size); }
  .gs-kv-table th { font-size: var(--mud-typography-caption-size); }
  ```
  Match or exceed MudBlazor's specificity (`.mud-simple-table table * tr > td`), e.g. `.gs-kv-table.mud-simple-table table * tr > td`, rather than using `!important`. Variable names `--mud-typography-{variant}-{size|weight|lineheight|family|letterspacing|text-transform}` are confirmed from `MudThemeProvider.razor.cs`.
  `MountStatus` currently wraps every cell in `body1`, i.e. a size bump over the native `body2`; decide whether that is wanted (Open Decision 5).
- **`MonitorSettingsCombinedPanel`** uses a raw `<table>` outside `MudSimpleTable` and so gets nothing from MudBlazor: either switch it to `MudSimpleTable` (gaining `body2`/`subtitle2`) or give it the `gs-kv-table` class.
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
Keep px for 1-2px borders and for icon/image intrinsic assets. Also convert `GsTheme` `DrawerWidthLeft/Right` (250px) to rem; `AppbarHeight` is already `3.5rem`. MudBlazor's own defaults for these are px, so they only zoom if the theme sets rem. Spacing utilities (`pa-*`, `mb-*`) cannot be converted; they are fixed px in MudBlazor (see R1 caveat). Dialog container padding is fixed 32px.

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
| 0 | Already confirmed from MudBlazor v9.11.0 source (§9). Remaining optional DevTools sanity checks only | None | Computed `font-size` on dialog `MudText` vs page `MudText`; `.ts-table th` computed size (specificity) |
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
5. **Tables:** accept MudBlazor's native `td` = `body2` / `th` = `subtitle2` (confirmed) instead of per-cell `MudText body1`? `MountStatus` would shrink from `body1` to `body2` unless a class (R5) restores it.
6. **Chart window:** same root zoom (recommended), but note ApexCharts text is configured separately and will not zoom.
7. **px items that root zoom cannot scale** (spacing utilities, tooltip 12px, chip text): accept, override a short list in `site.css`, or use CSS `zoom` instead of root font-size (R1)?

---

## 9. Verification results (MudBlazor v9.11.0 source)

All four rev 1 unknowns are now answered from the MudBlazor repository (tag `v9.11.0`). No markdown files were used.

| Former [verify] item | Result | Source file(s) |
|---|---|---|
| Table cells, inputs, buttons, tabs, nav links, list items: theme variables or fixed? | **Mixed.** `MudSimpleTable` td=body2, th=subtitle2 and `MudTable` body=body2, head=subtitle2 (theme variables). Buttons use `--mud-typography-button-size` (except `Size.Small`/`Large`, fixed rem). Inputs `font: inherit`; labels subtitle1; helper 0.75rem. List items `mud-typography-body1` (`body2` dense). Tabs, menus, nav links inherit. Tooltip 12px and chips 12/14/16px are fixed px | `_simpletable.scss`, `_table.scss`, `_button.scss`, `_input.scss`, `_inputcontrol.scss`, `MudListItem.razor.cs`, `_tooltip.scss`, `_chip.scss` |
| CSS variable names | `--mud-typography-{default,h1..h6,subtitle1,subtitle2,body1,body2,button,caption,overline}-{family,size,weight,lineheight,letterspacing,text-transform}`, emitted on `:root` by default | `MudThemeProvider.razor.cs` |
| Defaults for `subtitle1`/`subtitle2`/`overline` | 1rem / 0.875rem (w500) / 0.75rem (lineheight 2.66, tracking .08333em) | `Typography.cs` |
| Providers outside `.mud-layout`? | **Yes**; dialog, popover and snackbar containers are not descendants of the layout; text takes `body` size (`--mud-typography-default-size`) | `MudDialogProvider.razor`, `MudPopoverProvider.razor`, `MudSnackbarProvider.razor`, `_base.scss` |

**Corrections to rev 1:** (1) spacing utilities are fixed px, not rem; (2) raw `td`/`th` in `MudSimpleTable` are *not* unstyled; they get body2/subtitle2; (3) the `font-size: inherit !important` block is mostly redundant for inputs, not the reason inputs work; (4) tooltips and chips do not follow root font-size.

**Residual optional DevTools checks** (derived from source plus `site.css`, not observed in a browser):
1. Computed `font-size` of a dialog `MudText` (expected ≈ 12.25px at 100%) vs a page `MudText` (expected 14px).
2. Whether `.ts-table th { font-size: .8125em }` is overridden by `.mud-simple-table table * tr > th`. Specificity was not computed precisely.
3. Optional: `getComputedStyle(document.documentElement).getPropertyValue('--mud-typography-body1-size')` to see the em value passed through unchanged.

The Phase 1 root-zoom plan does not depend on these checks.

---

*Report revised 2026-10-05 10:15 (rev 2, MudBlazor v9.11.0 source verification). No source files were modified.*
