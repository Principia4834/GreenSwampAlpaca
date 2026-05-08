# Settings Explorer — UI Design Document

**2026-05-08 09:48**

---

## 1. Overview

This document describes the design for the **Settings Explorer** page — a unified, hierarchical view of all application settings marked `Display = Yes` in the Settings Reference. The user browses settings by navigating a collapsible tree on the left panel and edits the selected group's settings in a detail panel on the right.

All design questions (Q1–Q10) have been resolved (see §11). The tooltip / hover-text requirement has been added: every tree node and every settings field must expose the `Description` text from the Settings Reference as a `MudTooltip`.

The page is implemented as a Blazor Server component using **MudBlazor v9** components, consistent with the existing GreenSwamp Alpaca UI patterns.

---

## 2. Scope

### Settings covered

All settings marked `Display = Yes` in `SETTINGS-REFERENCE.md`, drawn from four backing objects:

| Settings Object | Source Type | Service Method |
|----------------|-------------|---------------|
| Observatory Settings | `ObservatorySettings` | `SettingsService.GetObservatorySettings()` |
| Server Configuration | `ServerConfig` | `SettingsService.GetServerConfig()` |
| Monitor / Logging | `MonitorSettings` | `SettingsService.GetMonitorSettings()` |
| Telescope Device Settings | `SkySettings` (per device) | `SettingsService.GetDeviceSettings(n)` / `GetAllDeviceSettings()` |

### Settings excluded (out of scope for this page)

- **Capability flags** — these are read-only, template-driven values that must not be edited at runtime. They are documented in Section 5 of the Settings Reference but excluded from this editor.
- `Password` — must be handled via a dedicated secure change-password flow, not a plain text field.

---

## 3. Tree Structure

The tree is three levels deep: **Root → Section → Group (leaf)**. Clicking a leaf loads its settings into the detail panel. Branch nodes (Root / Section) are not editable — they expand/collapse only.

```
Settings
├── Observatory
│   └── Observatory                       [leaf — 4 settings]
├── Server Configuration
│   ├── Network                           [leaf — 4 settings]
│   ├── Alpaca Behaviour                  [leaf — 3 settings]
│   ├── Identity & UI                     [leaf — 3 settings]
│   └── Authentication                    [leaf — 2 settings; Password excluded]
├── Monitor / Logging
│   ├── Device Filters                    [leaf — 3 settings]
│   ├── Category Filters                  [leaf — 6 settings]
│   ├── Message Type Filters              [leaf — 5 settings]
│   └── Logging Options                   [leaf — 6 settings]
└── Telescope Devices
	└── Device 0 — {DeviceName}           [branch — expands to groups]
		├── Device Identity               [leaf — 5 settings]
		├── Serial Connection             [leaf — 8 settings]
		├── Location                      [leaf — 4 settings]
		├── Optics                        [leaf — 4 settings]
		├── Environmental                 [leaf — 2 settings]
		├── Coordinate System             [leaf — 1 setting]
		├── Tracking                      [leaf — 8 settings]
		├── Custom Gearing                [leaf — 7 settings]
		├── Backlash                      [leaf — 2 settings]
		├── Pulse Guiding                 [leaf — 7 settings]
		├── Sync Limits                   [leaf — 2 settings]
		├── PEC / PPEC                    [leaf — 8 settings]
		├── Encoders                      [leaf — 1 setting]
		├── Hand Controller               [leaf — 7 settings]
		├── GPS                           [leaf — 2 settings]
		├── Performance & Display         [leaf — 7 settings]
		├── Alignment Mode                [leaf — 1 setting]
		├── Home Position                 [leaf — 4 settings]
		├── Park Positions                [leaf — 6 settings]
		├── Axis / Slew Limits            [leaf — 5 settings]
		├── Meridian / Hour Angle Limit   [leaf — 2 settings; GEM & Polar only]
		├── Horizontal Axis Limit         [leaf — 4 settings; AltAz only]
		└── Pier Side                     [leaf — 2 settings]
	└── Device 1 — {DeviceName}           [same sub-tree repeated per device]
```

> **Note:** The **Meridian / Hour Angle Limit** group and **Pier Side** group leaf items will be hidden from the tree when the device's `AlignmentMode` is `AltAz` (Q5 — Option a). The **Horizontal Axis Limit** group leaf will be hidden when the mode is `GermanPolar` or `Polar`. Any tree section where **all** settings have `Display = No` is also hidden entirely (Q10 rule — this is how Capability Flags are excluded).

---

## 4. Page Layout

### 4.1 Overall page structure

```
┌─ Page header (h5) ──────────────────────────────────────────────────┐
│  "Settings"    [Expand All]  [Collapse All]  [Unsaved badge]         │
└──────────────────────────────────────────────────────────────────────┘
┌─ MudGrid ───────────────────────────────────────────────────────────┐
│  xs=12 md=4 (left panel — tree)    xs=12 md=8 (right panel — editor)│
│                                                                      │
│  ┌─ MudPaper ───────────┐          ┌─ MudPaper ──────────────────┐  │
│  │  MudTreeView         │          │  [Group heading + icon]      │  │
│  │  (all nodes)         │          │  [Dirty indicator if unsaved]│  │
│  │                      │          │  [Settings fields]           │  │
│  │                      │          │                              │  │
│  │                      │          │  ─────────────────────────   │  │
│  │                      │          │  [Save Group]  [Reset]       │  │
│  └──────────────────────┘          └─────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────┘
```

- On **mobile** (`xs`): the tree collapses to a `MudSelect`-style dropdown (or the tree stacks above the editor panel).
- On **desktop** (`md+`): the two-panel split is always visible.

### 4.2 Tree panel (left)

| Element | MudBlazor Component | Notes |
|---------|--------------------|-|
| Tree container | `MudTreeView<SettingsNode>` | `Hover`, `Dense`, `ExpandOnClick`, `AutoExpand` |
| Section nodes | `MudTreeViewItem` | Icon identifies the section (see §6); not selectable; tooltip shows section description |
| Group leaf nodes | `MudTreeViewItem` | Clicking selects and loads the detail panel; tooltip shows group description (from Settings Reference `Description` field) |
| Dirty badge | Small `MudBadge` on a group's tree item | Shown when that group has unsaved changes |
| Search | `MudTextField` above the tree | Filters visible leaf nodes using `FilterFunc` |

### 4.3 Detail panel (right)

The detail panel renders the settings for the currently-selected leaf group. It is a `MudCard` containing:

1. **Card header** — group name, section icon, and a `MudChip` showing "Unsaved" in Warning colour when there are pending changes.
2. **Card content** — one settings field per row (see §5 for field types).
3. **Card actions** — `[Save]` (Primary) and `[Reset to defaults]` (outlined) buttons. Both are disabled when there are no unsaved changes (`[Save]`) or when already at defaults (`[Reset]`).

### 4.4 Placeholder state

When no group is selected (initial page load), the right panel shows a `MudAlert` of `Severity.Info`:

> *"Select a settings group from the tree on the left to begin editing."*

---

## 5. Field Rendering by Data Type

Each setting renders as its appropriate MudBlazor input component, consistently matching the patterns already used in `MountSettings.razor` and `Setup.razor`.

| C# Type | Setting examples | MudBlazor Component | Notes |
|---------|-----------------|---------------------|-------|
| `bool` | `AutoTrack`, `Refraction` | `MudCheckBox` | `Dense`, `Color.Primary` |
| `int` (free) | `ReadTimeout`, `MinPulseRa` | `MudNumericField<int>` | Min/Max from validation attributes |
| `int` (enum-mapped) | `BaudRate`, `St4Guiderate` | `MudSelect<int>` | Populated from known value sets |
| `double` | `Latitude`, `FocalLength` | `MudNumericField<double>` | Step size per field |
| `string` (free) | `Port`, `InstrumentName` | `MudTextField` | |
| `string` (enum-mapped) | `Mount`, `Handshake`, `TrackingRate` | `MudSelect<string>` | Known option list per field |
| `TimeSpan` | `UTCOffset` | `MudTextField` with parse/format | Display as `hh\:mm\:ss`; validated on change |
| `ushort` | `ServerPort` | `MudNumericField<ushort>` | Min=1, Max=65535 |
| `double[]` | `ParkAxes` | Two `MudNumericField<double>` inline | Axis 1 / Axis 2 labelled |
| `List<ParkPosition>` | `ParkPositions` | Nested `MudSimpleTable` + add/remove | Each row: Name, X, Y |
| `List<HcPulseGuide>` | `HcPulseGuides` | Nested `MudSimpleTable` + add/remove | Each row: Speed, Duration, Interval, Rate |

Each field row uses a `MudTooltip` (as in `Setup.razor`) to surface the setting's `Description` text from the Settings Reference. The tooltip is applied to the field label (or to a `MudIcon` info button beside it). Tree nodes also carry a tooltip: branch nodes display a short description of the section; leaf group nodes display a summary of the group.

The `SettingsNode` model (§8) includes a `Description` property populated at tree-build time from a static lookup table keyed by `GroupKey`. This lookup table is the canonical source for node-level tooltip text and is defined in the code-behind.

---

## 6. Section Icons

| Section | Icon |
|---------|------|
| Observatory | `Icons.Material.Filled.LocationOn` |
| Server Configuration | `Icons.Material.Filled.Dns` |
| Monitor / Logging | `Icons.Material.Filled.ListAlt` |
| Telescope Devices | `Icons.Material.Filled.TravelExplore` |
| Device branch node | `Icons.Material.Filled.DeviceHub` |
| Group leaf node | `Icons.Material.Filled.Tune` |

---

## 7. Data Flow and State Management

### 7.1 Loading

```
OnInitializedAsync()
  ├── _observatory  = SettingsService.GetObservatorySettings()
  ├── _serverConfig = SettingsService.GetServerConfig()
  ├── _monitor      = SettingsService.GetMonitorSettings()
  └── _devices      = SettingsService.GetAllDeviceSettings()
		  └── BuildTree()  — produces List<SettingsNode>
```

### 7.2 Working copies

All settings objects are **deep-cloned** into page-local fields immediately after load. The originals are retained for dirty-detection and Reset. Changes are applied only to the working copies; the service is not called until **Save** is clicked.

```csharp
// Working copies (page-level fields)
private ObservatorySettings _observatoryWork;
private ServerConfig _serverConfigWork;
private MonitorSettings _monitorWork;
private Dictionary<int, SkySettings> _deviceWork;   // keyed by DeviceNumber

// Originals for dirty detection
private ObservatorySettings _observatoryOrig;
private ServerConfig _serverConfigOrig;
private MonitorSettings _monitorOrig;
private Dictionary<int, SkySettings> _deviceOrig;
```

### 7.3 Dirty detection

A group is "dirty" when `JsonSerializer.Serialize(working) != JsonSerializer.Serialize(original)` for the relevant slice of the working copy. This comparison runs on each Blazor render cycle and drives the `MudBadge` and the enabled state of the Save button.

### 7.4 Saving

When **Save** is clicked for a group:

```
SaveGroupAsync(SettingsNode node)
  switch node.Source:
	Observatory   → SettingsService.SaveObservatorySettingsAsync(_observatoryWork)
	ServerConfig  → SettingsService.SaveServerConfigAsync(_serverConfigWork)
	Monitor       → SettingsService.SaveMonitorSettingsAsync(_monitorWork)
	Device(n)     → SettingsService.SaveDeviceSettingsAsync(n, _deviceWork[n])
  Update _orig snapshot to reflect the saved state
  Show MudAlert Success snackbar
```

Errors surface as `Severity.Error` `MudAlert` (consistent with existing pages).

### 7.5 Reset

Copies the original snapshot back into the working copy for the selected group's slice, then triggers a re-render.

---

## 8. SettingsNode Model

The tree is built from a list of `SettingsNode` objects. This is a local view-model class within the page (not persisted).

```csharp
public enum SettingsNodeSource { Observatory, ServerConfig, Monitor, Device }
public enum SettingsNodeLevel  { Root, Section, Group }

public class SettingsNode
{
	public string             Label       { get; init; }  // Display text in tree
	public string             Icon        { get; init; }  // MudBlazor icon string
	public string             Description { get; init; }  // Tooltip / hover text from Settings Reference
	public SettingsNodeLevel  Level       { get; init; }
	public SettingsNodeSource Source      { get; init; }  // which settings object
	public int                DeviceNumber{ get; init; }  // -1 if not a device node
	public string             GroupKey    { get; init; }  // matches Group column in SETTINGS-REFERENCE
	public List<SettingsNode> Children    { get; init; } = [];
	public bool               IsDirty     { get; set; }
}
```

---

## 9. Component File Plan

| File | Purpose |
|------|---------|
| `Pages/SettingsExplorer.razor` | Main page component — tree + detail panel |
| `Pages/SettingsExplorer.razor.cs` | Code-behind — tree building, data loading, save/reset logic, node tooltip lookup table |
| `Components/SettingsGroups/` | One sub-component per Group leaf (Q3 — dedicated sub-components) |

Route: `/settings-explorer`  
Nav menu entry: between `Mount Settings` and `Server Settings` in `NavMenu.razor`  
Page title / nav label: **Settings Explorer** (Q9)

---

## 10. MudBlazor Component Choices — Rationale

| Decision | Choice | Reason |
|----------|--------|--------|
| Tree component | `MudTreeView<SettingsNode>` with `ItemTemplate` | `ItemTemplate` + `BodyContent` give full control over how each leaf renders its dirty badge without subclassing `TreeItemData<T>` |
| Leaf selection | `SelectionMode.SingleSelection` + `SelectedValueChanged` | Only one group editable at a time; fits a detail-panel pattern |
| Tree expansion | `ExpandOnClick = true`, `AutoExpand = true` | Matches the folder-explorer feel expected from a settings tree |
| Dirty indicators | `MudBadge` (dot variant) on the leaf `BodyContent` | Non-intrusive; visible at a glance without needing to open each group |
| Field layout | `MudStack` (vertical, `Spacing="3"`) inside `MudCardContent` | Matches `MonitorSettings.razor` pattern |
| Save/Reset actions | `MudCardActions` with `MudButton` | Consistent with the rest of the app |
| Unsaved-changes guard | `MudDialog` confirmation on navigation away | Prevents accidental data loss when switching groups with pending changes (Q4 — confirmed) |
| Tooltips on tree nodes | `MudTooltip` wrapping `MudTreeViewItem` `BodyContent` | Uses `SettingsNode.Description`; populated from a static lookup keyed by `GroupKey` |
| Tooltips on fields | `MudTooltip` wrapping each field label or an adjacent info icon | Uses per-field description text from the Settings Reference |

---

## 11. Design Decisions (Resolved)

All questions were resolved by Andy on 2026-05-08. Implementation should treat these as fixed requirements.

| # | Question | Decision |
|---|----------|----------|
| Q1 | Save granularity | **Save the entire backing object** (e.g., `ServerConfig`, `SkySettings`) when any group within it is saved. This matches the service method granularity and avoids partial-save bugs. |
| Q2 | Multi-device tree expansion | **All device trees collapsed by default.** Clicking a device branch node expands it; no summary detail panel for branch-level nodes. |
| Q3 | Detail panel rendering strategy | **Dedicated sub-components** — one `.razor` file per Group leaf in `Components/SettingsGroups/`. |
| Q4 | Unsaved-changes navigation guard | **Yes** — show a `MudDialog` ("You have unsaved changes. Discard them?") when navigating away from a dirty group. |
| Q5 | Mount-type-specific group visibility | **Option (a) — hide from the tree** when the device's `AlignmentMode` does not support the group. |
| Q6 | List editing (ParkPositions, HcPulseGuides) | **Option (b) — add/delete rows with a `MudDialog`** for editing each row. No inline cell editing. |
| Q7 | Observatory location sync to devices | **Option (b) — show a prompt** asking if the user wants to propagate updated location values to all telescope devices. |
| Q8 | Page access control | **Same authentication policy as existing settings pages.** If `UseAuth = true` is adopted app-wide, this page follows. |
| Q9 | Page title / nav label | **"Settings Explorer"** |
| Q10 | Capability flags | **Not shown.** The design rule is: any tree section where all settings have `Display = No` is hidden from the explorer. Capability flags are `Display = No` throughout, so they never appear. |
| New | Tooltip / hover text on tree nodes | **Yes — required.** Every tree node (branch and leaf) must show a `MudTooltip` using the `Description` field from the Settings Reference. Field-level tooltips also required. `SettingsNode.Description` property added to the view-model. |
| Q11 | Show hidden settings | **"Show hidden settings" toggle** — a `MudSwitch` in the page header. When off (default), all `Display = No` fields are hidden from their editors and `Display = No` leaf groups are removed from the tree entirely. When on, hidden fields reappear (visually distinguished) and hidden-leaf tree nodes are restored. See §13. |

---

## 12. Implementation Checklist

- [x] Create `SettingsNode` view-model class with `Description` property
- [x] Create `Pages/SettingsExplorer.razor` and `Pages/SettingsExplorer.razor.cs`
- [x] Implement `BuildTree()` — constructs full node hierarchy; populates `Description` from static lookup
- [x] Add `/settings-explorer` route to `NavMenu.razor` (between Mount Settings and Server Settings)
- [x] Implement dirty detection (JSON-serialisation comparison) and dirty badge on leaf nodes
- [x] Implement `SaveGroupAsync()` — saves entire backing object; updates original snapshot
- [x] Implement `ResetGroup()` — copies original snapshot back to working copy
- [x] Implement unsaved-changes guard dialog (Q4) — fires on leaf-switch and on navigation away
- [x] Implement mount-type-specific group visibility (Q5) — hide inapplicable groups from the tree
- [x] Create `Components/SettingsGroups/` folder; implement one sub-component per leaf group
- [ ] Implement `ParkPositions` / `HcPulseGuides` row add/edit/delete dialog (Q6)
- [x] Implement Observatory → device location propagation prompt (Q7)
- [x] Add `MudTooltip` to every tree node using `SettingsNode.Description` (new requirement)
- [x] Add `MudTooltip` to every settings field using per-field description text
- [ ] **Enforce `Display = No` visibility rules in all editors and in `BuildTree()`** ← see §13
- [ ] **Add `ShowHidden` toggle to page header** ← see §13
- [ ] Smoke test with GEM, Polar, and AltAz device profiles
- [ ] Verify dirty badge clears correctly after save
- [ ] Verify sections with all `Display = No` settings are absent from the tree (Q10)

---

## 13. Display=No Compliance Audit & "Show Hidden Settings" Design

### 13.1 Current compliance gaps

A full audit of every editor component against `SETTINGS-REFERENCE.md` reveals the following violations — settings currently shown that have `Display = No`:

#### Groups that are entirely `Display = No` (must be absent from tree unless `ShowHidden = true`)

| Tree Group | Reason hidden by default |
|---|---|
| **Optics** | All 4 settings (`ApertureDiameter`, `ApertureArea`, `FocalLength`, `EyepieceFS`) are `Display = No`. |
| **PEC / PPEC** | All 8 settings are `Display = No`. |
| **Hand Controller** | All 7 settings are `Display = No`. |
| **GPS** | Both settings (`GpsPort`, `GpsBaudRate`) are `Display = No`. |

> Per the Q10 rule: *any tree section where all settings have `Display = No` is hidden from the explorer.* These four groups must not appear in `BuildDeviceNodes()` unless `ShowHidden = true`.

#### Individual fields within otherwise-visible groups that are `Display = No`

| Group | Hidden fields (Display = No) |
|---|---|
| **Observatory** | `UTCOffset` |
| **Alpaca Behaviour** | `AllowImageBytesDownload` |
| **Serial Connection** | `DataBits`, `Handshake`, `ReadTimeout`, `DTREnable`, `RTSEnable` |
| **Tracking** | `RATrackingOffset`, `AltAzTrackingUpdateInterval` |
| **Performance & Display** | `GotoPrecision` |
| **Pier Side** | `CanSetPierSide` (capability flag — `Display = No`) |

> Note: The **Serial Connection** group retains two `Display = Yes` settings (`Mount` and `Port`) so it remains in the tree. It just hides the five `Display = No` fields by default.

### 13.2 "Show hidden settings" feature design

#### Overview

A `MudSwitch` labelled **"Show hidden settings"** is added to the page header toolbar. Its state is held in a page-local `bool _showHidden` field (default `false`). When toggled it triggers `BuildTree()` (to add/remove whole-group nodes) and a `StateHasChanged()` call (to show/hide individual fields inside currently-open editors).

#### Page header change

Current header row:
```
[AccountTree icon]  Settings Explorer  [spacer]  [Unsaved chip]  [Expand All]  [Collapse All]
```

Updated header row:
```
[AccountTree icon]  Settings Explorer  [spacer]  [Unsaved chip]  [Show hidden settings ⚙️ switch]  [Expand All]  [Collapse All]
```

The switch uses `MudSwitch<bool>` with `@bind-Value="_showHidden" @bind-Value:after="OnShowHiddenChanged"`.

`OnShowHiddenChanged` must:
1. Rebuild the tree (`BuildTree()`) to add or remove whole-hidden-group leaf nodes.
2. Call `StateHasChanged()` to re-render the detail panel (so hidden fields appear/disappear).
3. If the currently selected node is one that would be removed (e.g. user was editing Optics and then toggled `ShowHidden` off), clear `_selectedNode` and `_treeSelectedValue`.

#### Tree visibility rules for leaf group nodes

`BuildDeviceNodes()` uses a helper `bool IsGroupVisible(string groupKey)`:

```csharp
private bool IsGroupVisible(string groupKey) => _showHidden || !_allHiddenGroups.Contains(groupKey);

private static readonly HashSet<string> _allHiddenGroups = new()
{
    "Optics",
    "PEC / PPEC",
    "Hand Controller",
    "GPS"
};
```

Each `DeviceLeaf(...)` call inside `BuildDeviceNodes()` is guarded:
```csharp
if (IsGroupVisible("Optics"))
    deviceLeaves.Add(DeviceLeaf(deviceNumber, "Optics", ...));
```

#### Field visibility inside editor components

Each editor sub-component receives a `bool ShowHidden` parameter:
```csharp
[Parameter] public bool ShowHidden { get; set; }
```

`DynamicGroupEditor` receives the value from the page and forwards it to each editor it instantiates. Fields that are `Display = No` are wrapped in:
```razor
@if (ShowHidden)
{
    <MudTooltip ...>
        <MudTextField ... />   @* or MudCheckBox, MudNumericField, etc. *@
    </MudTooltip>
}
```

When `ShowHidden = true` the hidden fields are rendered with a visual cue to distinguish them from always-visible fields:
- A small `MudChip` label reading **"Hidden setting"** in `Color.Warning` / `Variant.Outlined` placed as a suffix adornment or as an adjacent inline chip.
- Alternatively a `MudAlert Severity="Info" Dense="true"` banner at the top of the editor when `ShowHidden` is true, reading: *"Hidden settings are shown. These are advanced or read-only-like values not normally needed."*

The recommended approach is the **banner** (single placement, low visual noise) plus the `@if (ShowHidden)` guard on each hidden field.

#### `SettingsNode.IsAllHidden` property (optional, for styling)

Optionally, `SettingsNode` can gain a computed or set property `IsAllHidden` that is set to `true` for the four all-hidden groups. When `ShowHidden = true` and a node has `IsAllHidden = true`, the tree item renders its label in `Color.Secondary` (muted) and adds a small `MudChip` suffix `"hidden"` to signal to Andy that this group is normally invisible.

#### Summary of changes required

| File | Change |
|---|---|
| `Pages/SettingsExplorer.razor` | Add `MudSwitch` to header; pass `ShowHidden` to `DynamicGroupEditor`; call `OnShowHiddenChanged` on toggle |
| `Pages/SettingsExplorer.razor.cs` | Add `_showHidden` field; add `_allHiddenGroups` set; add `IsGroupVisible()` helper; guard four all-hidden groups in `BuildDeviceNodes()`; add `OnShowHiddenChanged()` method |
| `Components/SettingsGroups/DynamicGroupEditor.razor` | Add `ShowHidden` parameter; forward to every leaf editor |
| `Components/SettingsGroups/ObservatoryEditor.razor` | Guard `UTCOffset` with `@if (ShowHidden)` |
| `Components/SettingsGroups/AlpacaBehaviourEditor.razor` | Guard `AllowImageBytesDownload` with `@if (ShowHidden)` |
| `Components/SettingsGroups/SerialConnectionEditor.razor` | Guard `DataBits`, `Handshake`, `ReadTimeout`, `DTREnable`, `RTSEnable` with `@if (ShowHidden)` |
| `Components/SettingsGroups/TrackingEditor.razor` | Guard `RATrackingOffset`, `AltAzTrackingUpdateInterval` with `@if (ShowHidden)` |
| `Components/SettingsGroups/PerformanceDisplayEditor.razor` | Guard `GotoPrecision` with `@if (ShowHidden)` |
| `Components/SettingsGroups/PierSideEditor.razor` | Guard `CanSetPierSide` with `@if (ShowHidden)` |
| `Components/SettingsGroups/OpticsEditor.razor` | Entire component content guarded (group is hidden by default; shown only when `ShowHidden = true` brings the node back) |
| `Components/SettingsGroups/PecPpecEditor.razor` | Same as Optics |
| `Components/SettingsGroups/HandControllerEditor.razor` | Same as Optics |
| `Components/SettingsGroups/GpsEditor.razor` | Same as Optics |

> The all-hidden-group editors (Optics, PEC/PPEC, Hand Controller, GPS) do not need per-field `@if` guards because the entire group node is absent from the tree when `ShowHidden = false`. However adding the parameter is still required so they compile when `DynamicGroupEditor` always forwards it.

---

*Prepared for offline review. Source references: `MudBlazor v9.0.0` MCP, `IVersionedSettingsService.cs`, `SkySettings.cs` (Settings project), `SkySettings.cs` (MountControl project), `appsettings.json`, existing pages `MountSettings.razor`, `Setup.razor`, `MonitorSettings.razor`.*
