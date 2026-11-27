# Multi-Language Support — Analysis & Conclusions

**Updated: 2026-06-18 18:56**

---

## Background

This document records the findings of a multi-session feasibility study into adding
multi-language (i18n/l10n) support to the Green Swamp Alpaca Server Blazor UI.
The study covered framework feasibility, MudBlazor-specific concerns, re-use of
existing WPF translation assets, and the impact of the `MudBlazor.Translations`
NuGet package.

---

## 1. Framework Feasibility

### Verdict: ✅ Fully Feasible — Medium Effort

The .NET 10 + Blazor Server stack has complete, first-class support for the
`IStringLocalizer<T>` + `.resx` pattern. No third-party framework additions are
needed beyond standard ASP.NET Core packages.

### What Is Supported vs. Not Supported in Blazor

| Feature | Status |
|---|---|
| `IStringLocalizer<T>` / `@inject IStringLocalizer<T> Loc` | ✅ Fully supported |
| Shared `.resx` via `IStringLocalizer<SharedResource>` | ✅ Supported |
| Per-component `.resx` co-location | ✅ Supported |
| Culture cookie persistence (for SignalR) | ✅ **Recommended** for Blazor Server |
| `Accept-Language` header auto-detection | ✅ Supported |
| `IHtmlLocalizer` / `IViewLocalizer` | ❌ MVC-only — not available in Blazor |

### Current State of the Workspace

- `Properties/Resources.resx` and `Resources.Designer.cs` already exist but use
  the **old `ResourceManager` pattern** (for embedded assets). They are **not**
  connected to the DI-based `IStringLocalizer` pipeline.
- No `AddLocalization()`, no `UseRequestLocalization`, no
  `@using Microsoft.Extensions.Localization` currently present.
- All UI strings are hardcoded inline in ~20 Razor pages and components as
  MudBlazor attribute values (`Label="..."`, `Text="..."`, `Placeholder="..."`,
  `HelperText="..."`, tooltip text, etc.).

### How It Would Work (Blazor Server Architecture)

Because this is a **classic Blazor Server app** using `_Host.cshtml`
(`render-mode="ServerPrerendered"`), the correct culture persistence approach is:

```
Browser request → _Host.cshtml reads/writes culture cookie
→ UseRequestLocalization middleware reads it
→ Sets CultureInfo.CurrentUICulture for the Blazor circuit
```

> **Key rule**: Use a culture cookie — not URL or querystring — because those
> do not survive SignalR WebSocket reconnects.

### Recommended Resource File Strategy

Use a **single shared resource file** (`Localization/SharedResource.resx`) with a
marker class, given the current scale of the application:

```csharp
// Localization/SharedResource.cs — marker class only, no designer file
namespace GreenSwamp.Alpaca.Server.Localization;
public class SharedResource { }
```

In each component:

```razor
@inject IStringLocalizer<SharedResource> Loc

<MudNumericField Label="@Loc["Network_ServerPort"]" ... />
<MudTooltip Text="@Loc["Network_ServerPortTooltip"]" ... />
```

> ⚠️ **Do not set `LocalizationOptions.ResourcesPath`** when using the shared
> resource approach. The Microsoft docs explicitly warn these two options cannot
> coexist.

### Infrastructure Changes Required

| Change | Location | Notes |
|---|---|---|
| `builder.Services.AddLocalization()` | `Program.cs` | Registers `IStringLocalizer` in DI |
| `app.UseRequestLocalization(...)` | `Program.cs` | Before `MapBlazorHub` / fallback route |
| Culture cookie write | `Pages/_Host.cshtml` | Persists culture across SignalR reconnects |
| `CultureController` | `Controllers/CultureController.cs` | Redirect-based culture setter |
| `CultureSelector` component | `Shared/CultureSelector.razor` | User-facing language picker |
| `@using Microsoft.Extensions.Localization` | `_Imports.razor` | Makes `IStringLocalizer` available in all components |
| `Localization/SharedResource.cs` | New file | DI marker class |
| `Localization/SharedResource.resx` | New file | Default (English) strings |
| `Localization/SharedResource.{culture}.resx` | New files per language | Translated strings |

### Effort Estimate

| Task | Effort |
|---|---|
| `Program.cs` + `_Host.cshtml` setup | ~45 min |
| `CultureController` + `CultureSelector` component | ~2 hrs |
| `SharedResource.cs` + `SharedResource.resx` scaffolding | ~30 min |
| **String extraction from ~20 components/pages** | **~6–10 hrs** (bulk of work) |
| Translating `.resx` content to target languages | Variable (external, or see §3) |

---

## 2. MudBlazor-Specific Concerns

### Two Separate Localisation Systems

MudBlazor introduces a **second, independent localisation system** for its own
internal component strings. These must be treated separately from the app's own
strings.

| System | What it covers | Mechanism |
|---|---|---|
| **System A — App strings** | `Label=`, `Text=`, tooltips, dialog headings, page content | `IStringLocalizer<SharedResource>` + `.resx` |
| **System B — MudBlazor chrome** | ARIA labels, validation messages, button text generated internally | `ILocalizationInterceptor` + `LanguageResource.*` |

### MudBlazor Internal Keys Relevant to This App

From the 140 `LanguageResource.*` keys in MudBlazor 9.5.0, the ones relevant to
components used in this application are:

| Key | Where it appears |
|---|---|
| `Converter_InvalidNumber` / `Converter_ConversionError` | Error shown by `MudNumericField` on bad input |
| `MudInput_Clear` / `Increment` / `Decrement` | ARIA labels on `MudNumericField` +/- and clear buttons |
| `MudDialog_Close` | ARIA label on dialog X button |
| `MudAlert_Close` | Close button on alerts |
| `MudTabs_ScrollLeft` / `ScrollRight` | Tab panel overflow scroll |
| `MudNavGroup_ToggleExpand` | Collapsible nav groups |
| `MudExitPrompt_*` | Navigation-away confirmation |

Keys **not** relevant (components not used): `MudDataGrid_*`, `MudColorPicker_*`,
`MudDateRangePicker_*`, `MudStepper_*`, `MudCarousel_*`, `MudTimePicker_*`,
`MudFileUpload_*`.

### Risk Table (Before MudBlazor.Translations)

| Risk | Severity |
|---|---|
| Two resource sets to maintain | ⚠️ Medium |
| MudBlazor ships NO non-English translations natively | ⚠️ Medium |
| `MudNumericField` error messages default to English | ⚠️ Medium |
| MudBlazor ARIA strings default to English | ℹ️ Low (accessibility concern) |

---

## 3. WPF XAML Translation Files as a Source

### Location

```
T:\source\repos\Principia4834\GSServer\GS.Shared\LanguageFiles\
```

### Available Files

| File | Keys | Language |
|---|---|---|
| `GSServer_en-US.xaml` | 728 | English (source) |
| `GSServer_de-DE.xaml` | 726 | German |
| `GSServer_fr-FR.xaml` | 725 | French |
| `GSServer_it-IT.xaml` | 726 | Italian |
| `GSServer_zh-CN.xaml` | 728 | Chinese Simplified |

Key synchronisation is excellent — only 2 EN-only keys out of 728 across all
five files. Translations are professionally maintained under GPL v3.

Also present: `GSUtil_*.xaml` (utility strings, ~70 keys) and
`GSChart_*.xaml` (chart strings, ~45 keys) across all five cultures.

### Domain Overlap with the Blazor App

| Domain | Overlap | Examples confirmed in XAML |
|---|---|---|
| Mount status (RA/Dec/Az/Alt) | ✅ High | `topRa→"Rektaszension:"`, `topDec→"Deklination:"` |
| Park / Home / Tracking / Slew | ✅ High | `btnPark→"Parken"`, `btnTracking→"Nachführung"` |
| PPEC | ✅ High | `ppPec`, `ppOnOff`, `ppTraining` |
| Guiding rates (Sidereal/Lunar/Solar/King) | ✅ High | `gpSidereal`, `gpLunar`, `gpSolar`, `gpKing` |
| Backlash | ✅ High | `bacDecLash`, `bacRaLash`, `bacLashComp` |
| Slew limits | ✅ High | `botTipAxisLimit`, `goTargetLimits` |
| Mount connect/disconnect | ✅ High | `skyConnect→"Verbinden"`, `skyDisconnect` |
| Sync | ✅ Good | `goSync`, `vceSyncAz`, `vceSyncCoords` |
| **Alpaca network settings** | ❌ None | New to Blazor — no WPF equivalent |
| **Observatory configuration** | ❌ None | New to Blazor — no WPF equivalent |
| **Authentication** | ❌ None | New to Blazor — no WPF equivalent |
| **Settings health check / backup** | ❌ None | New to Blazor — no WPF equivalent |

**Estimated usable overlap: ~150–200 of 728 WPF keys map directly to Blazor UI
strings**, providing free 4-language translations for that subset.

### XAML → ResX Conversion

The format transformation is mechanical and scriptable in PowerShell:

```
XAML:  <system:String x:Key="btnPark">Park</system:String>
ResX:  <data name="btnPark" xml:space="preserve"><value>Park</value></data>
```

### Recommended Key Naming Strategy

Adopt explicit scoped names in the Blazor `.resx` (not the WPF shorthand style),
with a mapping table linking the two:

```
WPF key          →  Blazor ResX key
-----------         -----------------------
btnPark          →  MountStatus_ParkButton
topRa            →  MountStatus_RightAscensionLabel
ppPec            →  Settings_PpecLabel
skyConnect       →  MountStatus_ConnectButton
bacDecLash       →  Settings_DecBacklashLabel
```

The mapping table drives the PowerShell conversion script that generates the
initial `SharedResource.{culture}.resx` files from the XAML sources.

### Licence Note

WPF translations are GPL v3. Since this is the same GreenSwamp project family,
reuse is unambiguous. Revisit only if the Alpaca Server is ever distributed under
a different licence.

---

## 4. `MudBlazor.Translations` NuGet Package

### Package Details

| Property | Value |
|---|---|
| Package ID | `MudBlazor.Translations` |
| Version installed | `3.3.0` |
| MudBlazor dependency | `>= 9.0.0` (compatible with 9.5.0) ✅ |
| net10.0 target | Dedicated satellite assemblies present ✅ |
| Licence | MIT ✅ |
| Source | `github.com/MudBlazor/Translations` |

### What It Provides

- A pre-built `MudTranslationsInterceptor` implementing `ILocalizationInterceptor`
- `AddMudTranslations()` extension method on `IServiceCollection`
- Pre-built satellite assemblies covering all 140 `LanguageResource.*` keys for
  **40 languages**: af, ar, ca, ckb, cs, da, **de**, **es**, fa, fi, **fr**, hr,
  hu, id, **it**, ja, ka, ko, lb, lv, ms, nb-NO, ne, nl, nn-NO, pl, pt, pt-BR,
  ro, ru, sk, sl, sv, ta, tr, uk, ur, uz, vi, **zh-Hans**, zh-Hant

All four WPF target languages are covered: `de` ✅, `fr` ✅, `it` ✅,
`zh-Hans` ✅ (plus 36 additional bonus languages).

### How to Activate

One additional line in `Program.cs`:

```csharp
builder.Services.AddMudTranslations();
```

The interceptor automatically reads `CultureInfo.CurrentUICulture` at runtime and
loads the correct satellite assembly. No `.resx` files to create or maintain.

### Impact on the Migration

| Concern | Before Package | After Package |
|---|---|---|
| System B (MudBlazor internal strings) | ⚠️ Custom interceptor + manual `.resx` sets | ✅ **Fully resolved**, zero effort |
| `MudNumericField` error messages in wrong language | ⚠️ Would appear English-only | ✅ Resolved |
| MudBlazor ARIA labels | ⚠️ English-only | ✅ 40 languages, automatic |
| Dual resource-set maintenance burden | ⚠️ Ongoing | ✅ Eliminated |
| Language coverage of MudBlazor chrome | ❌ Manual, 4 languages | ✅ Automatic, 40 languages |

**System B is completely eliminated as a concern.** The only remaining
localisation work is System A — extracting app strings into `SharedResource.resx`.

### Version Compatibility Note

`MudBlazor.Translations` version numbers (`3.x`) are independent of MudBlazor
core version numbers (`9.x`). If MudBlazor core is upgraded in future, verify
that a compatible `MudBlazor.Translations` update is available before upgrading,
as new `LanguageResource.*` keys added in a new MudBlazor release will fall back
to English until the translations package is updated.

---

## 5. Consolidated Conclusions

### Overall Verdict

Multi-language support is **fully feasible** on this stack. The combination of
standard .NET 10 localisation infrastructure, `MudBlazor.Translations`, and the
existing WPF XAML assets significantly reduces what would otherwise be a large
translation project.

### What Comes For Free

| Asset | Benefit |
|---|---|
| `MudBlazor.Translations` 3.3.0 | All MudBlazor internal strings in 40 languages — zero effort |
| WPF `GSServer_*.xaml` files | ~150–200 app strings pre-translated into de/fr/it/zh |
| .NET 10 culture fallback | Neutral-culture fallback (e.g. `de` → `de-DE`) is automatic |

### What Still Requires Work

| Task | Notes |
|---|---|
| Platform setup (Program.cs, _Host.cshtml, DI) | ~1 hr |
| `CultureController` + `CultureSelector` component | ~2 hrs |
| `SharedResource.cs` + `SharedResource.resx` | ~30 min |
| **String extraction from ~20 Razor components** | **~6–10 hrs** — the bulk |
| PowerShell XAML→ResX conversion script | ~1 hr to build, then automated |
| Fresh translations for Blazor-new strings | ~500–600 strings — external work |

### Recommended Phases

**Phase 1 — Infrastructure (no visible user impact)**
Set up the pipeline, extract all strings to English `SharedResource.resx`,
activate `AddMudTranslations()`. Application continues to run in English only
but is now fully wired for localisation.

**Phase 2 — WPF asset conversion**
Run the XAML→ResX conversion script for the ~150–200 matching keys. This
delivers `de`, `fr`, `it`, `zh-Hans` translations for the mount control and
astronomy domain strings at no translation cost.

**Phase 3 — Remaining translations**
The ~500–600 Blazor-new strings (Alpaca settings, observatory config,
authentication, health check, backup UI) need fresh translation. These can be
handed to translators or community contributors independently, since the
infrastructure and English baseline are already complete from Phase 1.

### Supported Languages (at Phase 3 completion)

| Language | App strings source | MudBlazor chrome |
|---|---|---|
| English (en-US) | `SharedResource.resx` (baseline) | MudBlazor built-in |
| German (de-DE) | WPF XAML + new strings | `MudBlazor.Translations` `de/` |
| French (fr-FR) | WPF XAML + new strings | `MudBlazor.Translations` `fr/` |
| Italian (it-IT) | WPF XAML + new strings | `MudBlazor.Translations` `it/` |
| Chinese Simplified (zh-CN) | WPF XAML + new strings | `MudBlazor.Translations` `zh-Hans/` |
| Any other language | New translations only | `MudBlazor.Translations` (if covered) |

---

## 6. Key Technical References

| Reference | URL |
|---|---|
| ASP.NET Core Blazor globalization and localization | https://learn.microsoft.com/aspnet/core/blazor/globalization-localization?view=aspnetcore-10.0 |
| ASP.NET Core localization fundamentals | https://learn.microsoft.com/aspnet/core/fundamentals/localization?view=aspnetcore-10.0 |
| MudBlazor.Translations on GitHub | https://github.com/MudBlazor/Translations |
| WPF translation source files | `T:\source\repos\Principia4834\GSServer\GS.Shared\LanguageFiles\` |

---

*Document generated from analysis sessions conducted 2026-06-18.*
*Updated: 2026-06-18 18:56*
