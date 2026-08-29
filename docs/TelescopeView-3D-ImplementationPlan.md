# Telescope 3D View — Implementation Plan
**Document status:** DRAFT — awaiting Andy's review  
**Last updated:** 2026-08-29 12:30  
**Requirements reference:** `Docs/TelescopeView-3D-Requirements.md` (DRAFT v4)

---

## Approach Assessment

Andy's proposed staging is sound. The four-phase structure correctly orders work by risk and dependency:

| Phase | Rationale |
|---|---|
| Settings infrastructure first | Purely additive C# changes with established patterns. Zero risk to existing 3D stub or mount control. Can be built and verified in isolation. |
| Cylinder prototype as integration gate | Validates the complete JS interop chain — init signature, setAxes, getCameraState, dispose, camera persistence — against real axis data before any model assets exist. The sign convention stubs can be exercised here. This is the highest-complexity single phase and deserves its own stabilisation period. |
| Pause for authoring guide | Phase 3 has a hard dependency on correctly authored `.glb` assets. Without the process guide, assets will likely violate the pivot/scale/orientation contract and cause runtime placement errors. The pause is the right call. |
| Five required assets before Stage 6 | Stage 6 adds new geometry to the prototype and new runtime branching. Proving the five-asset load path first isolates any asset-loading bugs from Stage 6 geometry bugs. |

**One refinement suggested:** Within Phase 2, the JS module rewrite and the razor page update should be separate build-and-verify steps rather than a single combined change. This prevents compound debugging if the interop signature is wrong.

**One pre-flight check flagged:** `GridMaterial` (used for the ground grid in the prototype) is **not** included in `babylon.js` core. It requires a separate CDN script tag (`babylonjs.materials.min.js`). This must be added to the razor page before the JS module rewrite or the grid constructor will throw a silent runtime error. This is Step 1 of Phase 2.

---

## Phase 1 — Settings Infrastructure

**Goal:** Add the three new `TelescopeStateModel` properties, create `ModelSetSettingsService`, and provide a working `modelsets.json` with a `Prototype` entry. No change to the existing 3D view behaviour.

**Gate:** Build succeeds; unit test (or manual verification) that `GetActiveModelSetAsync` returns the Prototype model set and `SaveCameraStateAsync` writes back atomically.

---

### Step 1.1 — Extend `TelescopeStateModel`

**File:** `GreenSwamp.Alpaca.Server/Models/TelescopeStateModel.cs`

Add three properties to the model class and initialise them in the constructor:

| Property | Type | Constructor default |
|---|---|---|
| `SiteLatitude` | `double` | `double.NaN` |
| `AlignmentMode` | `AlignmentMode` (GreenSwamp enum) | `AlignmentMode.GermanPolar` |
| `MountType` | `MountType` (GreenSwamp enum) | `MountType.Simulator` |

> **Enum type note:** `mount.Settings.AlignmentMode` in `BuildSnapshot` is already accessed as the GreenSwamp `AlignmentMode` enum (the existing `IsGermanPolarMode` line compares it to the ASCOM enum value — verify the using statement resolves to the correct GreenSwamp type before committing).

---

### Step 1.2 — Populate new properties in `BuildSnapshot`

**File:** `GreenSwamp.Alpaca.Server/Services/TelescopeStateService.cs`

Add three lines to the `BuildSnapshot` return initialiser alongside the existing `mount.Settings.*` reads:

```csharp
SiteLatitude  = mount.Settings.Latitude,
AlignmentMode = mount.Settings.AlignmentMode,
MountType     = mount.Settings.Mount,
```

Build and verify — no behaviour change, new properties are populated on each tick.

---

### Step 1.3 — Create `modelsets.json` C# model classes

**New file:** `GreenSwamp.Alpaca.Server/Models/ModelSets/ModelSetCollection.cs`

Create the deserialisation types that map 1:1 to the `modelsets.json` JSON structure (see Requirements §9.3):

```csharp
public record ModelSetCollection
{
	public string ActiveModelSet { get; init; } = string.Empty;
	public List<ModelSetEntry> ModelSets { get; init; } = [];
}

public record ModelSetEntry
{
	public string Name { get; init; } = string.Empty;
	public string Description { get; init; } = string.Empty;
	public ModelFiles Models { get; init; } = new();
	public CameraState? Camera { get; init; }
}

public record ModelFiles
{
	public string? Support { get; init; }
	public string? Structural { get; init; }
	public string? PrimaryAxis { get; init; }
	public string? SecondaryAxis { get; init; }
	public string? Ota { get; init; }
	public string? OtaSecondary { get; init; }
	public string? CounterWeight { get; init; }
}

public record CameraState
{
	public double Alpha  { get; init; }
	public double Beta   { get; init; }
	public double Radius { get; init; }
	public CameraTarget Target { get; init; } = new();
}

public record CameraTarget
{
	public double X { get; init; }
	public double Y { get; init; }
	public double Z { get; init; }
}
```

Use `camelCase` JSON serialisation (the default for `System.Text.Json` with `JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase`) so property names round-trip correctly to the JSON file.

---

### Step 1.4 — Create `ModelSetSettingsService`

**New file:** `GreenSwamp.Alpaca.Server/Services/ModelSetSettingsService.cs`

Responsibilities:

- Locate `modelsets.json` at `{webRootPath}/models/modelsets.json` (injected via `IWebHostEnvironment`).
- Load and cache the `ModelSetCollection` on first access.
- Expose `GetActiveModelSetAsync()` → returns the `ModelSetEntry` whose `Name` matches `ActiveModelSet`.
- Expose `SaveCameraStateAsync(string modelSetName, CameraState state)` → updates the matching entry's `Camera` in memory and writes the full file atomically:
  1. Serialise the updated `ModelSetCollection` to a temp file in the same directory.
  2. Replace the live file with the temp file (`File.Move` with overwrite).
- Thread safety: use `SemaphoreSlim(1,1)` around all read/write operations since `SaveCameraStateAsync` can be called from a Blazor circuit thread while the background loop reads it.

> **First-run handling:** If `modelsets.json` does not exist, `GetActiveModelSetAsync` returns a default `Prototype` entry (all model paths null, camera null) so the 3D view degrades gracefully rather than throwing.

---

### Step 1.5 — Register `ModelSetSettingsService` in DI

**File:** `GreenSwamp.Alpaca.Server/Program.cs` (or equivalent service registration file)

```csharp
builder.Services.AddSingleton<ModelSetSettingsService>();
```

Singleton is correct — `modelsets.json` is read once and cached in memory; the cache is updated in place on save.

---

### Step 1.6 — Create `wwwroot/models/modelsets.json`

**New file:** `GreenSwamp.Alpaca.Server/wwwroot/models/modelsets.json`

Provide the initial file with the `Prototype` model set as the active entry. This is the only model set needed until real `.glb` assets are available.

```json
{
  "activeModelSet": "Prototype",
  "modelSets": [
	{
	  "name": "Prototype",
	  "description": "Cylinder placeholders — no .glb files required",
	  "models": {
		"support":       null,
		"structural":    null,
		"primaryAxis":   null,
		"secondaryAxis": null,
		"ota":           null
	  },
	  "camera": null
	}
  ]
}
```

> **Build action:** Verify that `wwwroot/models/` is served as static files and that `modelsets.json` is **not** marked as an embedded resource or copy-never in the project file. It must be writable at runtime.

**Phase 1 build gate:** Build and verify. Confirm `GetActiveModelSetAsync` resolves correctly. Confirm `SaveCameraStateAsync` writes the file atomically. No change to any page or existing service behaviour.

---

## Phase 2 — Cylinder Prototype Wired to Real Data

**Goal:** Replace the current `telescopeViewer.js` stub with a full ES module built from `Universal babylon.js`, wired to real axis values and camera persistence. All geometry uses cylinders (the `Prototype` model set). Real `.glb` loading is **not** implemented in this phase.

**Gate:** The scene renders in the browser, cylinder geometry responds to live `ActualAxisX`/`ActualAxisY` values, camera state roundtrips correctly through Save View, and navigation away/back produces no console errors or memory leaks.

---

### Step 2.1 — Add `babylonjs.materials.min.js` CDN script tag

**File:** `GreenSwamp.Alpaca.Server/Pages/TelescopeView/TelescopeView.razor`

The `GridMaterial` used in the prototype is in the Babylon.js materials package, not in core. Add the CDN script tag **after** the existing `babylon.js` tag and **before** the loaders tag:

```html
<script src="https://cdn.babylonjs.com/babylon.js"></script>
<script src="https://cdn.babylonjs.com/materialsLibrary/babylonjs.materials.min.js"></script>
<script src="https://cdn.babylonjs.com/loaders/babylonjs.loaders.min.js"></script>
```

> Pin to the same version as the core `babylon.js` tag. Mismatched versions can cause silent material errors.

Build only — no JS changes yet. Confirm the page still renders with the existing stub.

---

### Step 2.2 — Rewrite `telescopeViewer.js`

**File:** `GreenSwamp.Alpaca.Server/wwwroot/js/telescopeViewer.js`

Full replacement of the existing stub. The new module is built directly from `Universal babylon.js` with the following structure:

**Module-level state:**
```javascript
let _engine, _scene, _primaryNode, _secondaryNode;
let _primaryAngle = 0, _secondaryAngle = 0;
let _latitude, _alignmentMode, _mountType;
// Babylon coordinate vectors — set during init, used in setAxes and render loop
let _Zp_bab, _Yp_bab;
```

**`export function init(canvasId, latitude, alignmentMode, mountType, models, camera)`**

- Create engine and scene.
- Set `scene.useRightHandedSystem = true`.
- Configure `ArcRotateCamera` from `camera` parameter if non-null, else use prototype defaults.
- Build scene geometry exactly from the `Universal babylon.js` Stage 1–5 blocks. No geometric changes.
- For each of the five required asset keys (`support`, `structural`, `primaryAxis`, `secondaryAxis`, `ota`): if the path in `models` is non-null, queue a `.glb` load (stubbed in this phase — always falls through to cylinder); if null or load fails, use the prototype cylinder for that stage.
- Register `onBeforeRenderObservable` to apply axis rotations from `_primaryAngle` / `_secondaryAngle` each frame (exact Stage 6 block from prototype).
- Store `_latitude`, `_alignmentMode`, `_mountType`, `_Zp_bab`, `_Yp_bab` for use in `setAxes`.
- Add `window.resize` → `engine.resize()` listener.
- Start `engine.runRenderLoop`.

**`export function setAxes(primaryDeg, secondaryDeg)`**

```javascript
export function setAxes(primaryDeg, secondaryDeg) {
	if (!isFinite(primaryDeg) || !isFinite(secondaryDeg)) return;
	_primaryAngle   = BABYLON.Tools.ToRadians(primaryDeg)   * primaryAxisSign(_latitude, _alignmentMode, _mountType);
	_secondaryAngle = BABYLON.Tools.ToRadians(secondaryDeg) * secondaryAxisSign(_latitude, _alignmentMode, _mountType);
}
```

**`export function getCameraState()`**

```javascript
export function getCameraState() {
	if (!_scene) return null;
	const cam = _scene.activeCamera;
	return {
		alpha:  cam.alpha,
		beta:   cam.beta,
		radius: cam.radius,
		target: { x: cam.target.x, y: cam.target.y, z: cam.target.z }
	};
}
```

**`export function dispose()`** — stop render loop, dispose scene and engine, null all module state, remove resize listener.

**Sign convention stubs (internal):**
```javascript
function primaryAxisSign(latitude, alignmentMode, mountType)   { return +1; }
function secondaryAxisSign(latitude, alignmentMode, mountType) { return +1; }
```

> **Important:** The `.glb` loading calls (`BABYLON.SceneLoader.ImportMeshAsync`) are **not implemented** in this step — all five stages use their cylinder fallbacks. The `models` parameter is accepted but effectively ignored until Phase 3. This keeps Phase 2 focused on the interop chain and axis geometry.

Build and verify the module compiles without browser console errors.

---

### Step 2.3 — Update `TelescopeView.razor`

**File:** `GreenSwamp.Alpaca.Server/Pages/TelescopeView/TelescopeView.razor`

Changes applied in this step:

**Injections added:**
```razor
@inject ModelSetSettingsService ModelSetSettings
@inject NavigationManager Nav
```

**New backing fields:**
```csharp
private double _axisX;
private double _axisY;
private string _activeModelSetName = string.Empty;
```

**`OnAfterRenderAsync` — rewritten init sequence:**
1. Load active model set via `ModelSetSettings.GetActiveModelSetAsync()`.
2. Store `_activeModelSetName`.
3. Resolve model paths to absolute URLs using `Nav.BaseUri`.
4. Read initial snapshot: `var s = StateService.GetCurrentState(DeviceNumber)`.
5. Compute `effectiveLatitude`: `s.AlignmentMode == AlignmentMode.AltAz ? 90.0 : s.SiteLatitude`.
6. Call `_module.InvokeVoidAsync("init", "telescopeCanvas", effectiveLatitude, alignmentModeStr, mountTypeStr, modelsObj, cameraObj)`.
7. Subscribe `StateService.StateChanged += OnStateChanged`.
8. Call `await PushStateAsync()` for initial position.

**`PushStateAsync` (renamed from `PushRotationAsync`):**
```csharp
private async Task PushStateAsync()
{
	if (_module is null) return;
	var s = StateService.GetCurrentState(DeviceNumber);
	_axisX = s.ActualAxisX;
	_axisY = s.ActualAxisY;
	try
	{
		await _module.InvokeVoidAsync("setAxes", _axisX, _axisY);
		StateHasChanged();
	}
	catch (JSDisconnectedException) { }
	catch (TaskCanceledException) { }
}
```

**New `SaveViewAsync` method:**
```csharp
private async Task SaveViewAsync()
{
	if (_module is null) return;
	try
	{
		var state = await _module.InvokeAsync<CameraState>("getCameraState");
		await ModelSetSettings.SaveCameraStateAsync(_activeModelSetName, state);
		// Brief confirmation — implementation detail deferred to step
	}
	catch (JSDisconnectedException) { }
	catch (TaskCanceledException) { }
}
```

**`DisposeAsync` — simplified (no camera save):**
```csharp
public async ValueTask DisposeAsync()
{
	StateService.StateChanged -= OnStateChanged;
	if (_module is not null)
	{
		try
		{
			await _module.InvokeVoidAsync("dispose");
			await _module.DisposeAsync();
		}
		catch (JSDisconnectedException) { }
		catch (TaskCanceledException) { }
	}
}
```

**Toolbar markup update:**
- Replace `Alt`/`Az` display with `_axisX.ToString("F2")` (labelled "Axis 1") and `_axisY.ToString("F2")` (labelled "Axis 2").
- Add a `<MudIconButton>` (or `<MudButton>`) for Save View that calls `SaveViewAsync()`.

**Phase 2 build and integration gate:**
1. Build succeeds with no errors.
2. Navigate to `/telescope-view/0` — cylinder scene renders.
3. Observe Axis 1/2 values in toolbar update as the mount moves.
4. Confirm cylinders rotate in response to axis changes.
5. Click Save View — check `modelsets.json` is updated with camera state.
6. Reload page — confirm camera position is restored.
7. Navigate away and back — no console errors; scene re-initialises cleanly.

---

## PAUSE — Model Authoring Process Guide

Before Phase 3 can begin, a **Model Authoring Process Guide** document must be produced. This is a separate deliverable, not part of this implementation plan.

The guide must cover at minimum:

- Required 3D modelling tool(s) and export settings for `.glb` output
- Scale setup: 1 modelling unit = 1 mm
- Pivot placement for each of the six component types (per Requirements §8.5)
- Local Y-axis orientation requirement and how to verify it in the chosen tool
- Rest-pose export (zero rotation baked in) — how to clear baked transforms before export
- How to verify the bounding box origin and extents after export using the Babylon.js sandbox
- File naming and directory structure convention (`wwwroot/models/{ModelSetName}/`)
- Acceptance checklist (Requirements §8.6 authoring checklist)

**Phase 3 cannot start until:**
1. The Model Authoring Process Guide is complete and reviewed.
2. At least one full set of five `.glb` assets (for one mount configuration) has been produced and verified against the authoring checklist.
3. The assets are placed in `wwwroot/models/{ModelSetName}/` and a corresponding model set entry has been added to `modelsets.json`.

---

## Phase 3 — Five-Asset `.glb` Loading

**Goal:** Implement the per-stage `.glb` mesh loading in `telescopeViewer.js`. Each stage loads its asset, reads the bounding box for placement, and falls back to the cylinder if the load fails. Stage 6 (counterpart) is **not** implemented in this phase.

**Gate:** The scene renders with the five real `.glb` assets correctly placed and parented; all Stage 1–5 cylinder fallbacks still work when paths are null; axis rotation drives the loaded meshes correctly.

**Dependency:** Phase 2 stable + Model Authoring Process Guide complete + at least one set of validated `.glb` assets available.

---

### Step 3.1 — Implement per-stage `.glb` loading in `telescopeViewer.js`

For each of the five required stages, replace the "always cylinder" stub with:

```javascript
async function loadOrFallback(path, stage, fallbackFn) {
	if (!path) { fallbackFn(); return; }
	try {
		const result = await BABYLON.SceneLoader.ImportMeshAsync("", dirOf(path), fileOf(path), _scene);
		const mesh = result.meshes[0];
		const bb = mesh.getBoundingInfo();
		// Use bb.minimum / bb.maximum to derive stage-specific placement offsets
		// Parent mesh to the correct node for this stage
		positionStage(stage, mesh, bb);
	} catch (e) {
		console.warn(`telescopeViewer: failed to load ${path}, using fallback.`, e);
		fallbackFn();
	}
}
```

The `positionStage` function uses the bounding-box-derived offsets (per Requirements §8.5) to place and parent each mesh to its correct `TransformNode` in the hierarchy.

`init` converts from synchronous geometry construction to an `async` function, awaiting all five `loadOrFallback` calls before starting the render loop.

### Step 3.2 — Add real model set entry to `modelsets.json`

Add an entry for the first real mount configuration with actual `.glb` file paths. Update `activeModelSet` to point to the new entry.

### Step 3.3 — Build and integration test

Verify with each of:
- Active model set = `"Prototype"` (all five cylinders)
- Active model set = real set (five `.glb` meshes)
- Real set with one path set to `null` (partial fallback)

**Phase 3 gate:** All three configurations render correctly; rotation and camera persistence work unchanged.

---

## Phase 4 — Stage 6 Counterpart Element

**Goal:** Extend `Universal babylon.js` with the Stage 6 geometry block and implement counterpart loading and rendering in `telescopeViewer.js`. Support both `otaSecondary` and `counterWeight` asset types.

**Gate:** The scene renders the counterpart element correctly for both asset types; absent/null counterpart key results in no Stage 6 mesh, with no error; all Phase 3 behaviour is unchanged.

**Dependency:** Phase 3 stable.

---

### Step 4.1 — Add Stage 6 block to `Universal babylon.js`

Extend the prototype with a Stage 6 section directly below Stage 5. The block must:

- Compute `W0 = S0.subtract(Yp_bab.scale(o_w))` (opposite end from OTA).
- Create `counterpartNode` parented to `secondaryNode`, positioned at `W0 - S0`.
- Create `counterpartAxisNode = alignCylinderTo(counterpartDir)` where `counterpartDir` is:
  - `Xp_bab.scale(-1).normalize()` for the `otaSecondary` placeholder
  - `Yp_bab.scale(-1).normalize()` for the `counterWeight` placeholder
- Use a distinct placeholder colour to distinguish it visually from Stage 5.
- `counterpartMesh.parent = counterpartAxisNode`.
- `counterpartAxisNode.parent = counterpartNode`.

This is the **only** permitted addition to `Universal babylon.js`. No other sections are modified.

### Step 4.2 — Implement Stage 6 loading in `telescopeViewer.js`

- After the five required stage loads in `init`, check for `models.otaSecondary` or `models.counterWeight`.
- If neither key is present or both are null, skip Stage 6 entirely.
- If one key is non-null, call `loadOrFallback` with the appropriate `counterpartDir`:
  - `otaSecondary` → `counterpartDir = Xp_bab.scale(-1).normalize()`
  - `counterWeight` → `counterpartDir = Yp_bab.scale(-1).normalize()`
- The cylinder fallback for Stage 6 uses the same direction logic.
- If the key is present but the path is null → show the cylinder fallback (not suppress).

### Step 4.3 — Update `modelsets.json`

Add `counterWeight` or `otaSecondary` key to the relevant model set entries. The `Prototype` model set may optionally include a null counterpart key to exercise the cylinder fallback.

### Step 4.4 — Build and integration test

Verify:
- Model set with `counterWeight` path → counterweight mesh loads and rotates with secondary axis.
- Model set with `otaSecondary` path → secondary OTA loads and points in same direction as primary OTA.
- Model set with no counterpart key → no Stage 6, no error.
- Model set with `counterWeight: null` → cylinder fallback shown.
- Phase 3 model set (no counterpart key) still works unchanged.

---

## Risk Register

| Risk | Phase | Mitigation |
|---|---|---|
| `AlignmentMode` enum type mismatch in `BuildSnapshot` (GreenSwamp vs ASCOM namespace) | Phase 1 | Verify using directive resolves to GreenSwamp type before Step 1.2 commit |
| `GridMaterial` not in Babylon.js core — silent runtime error | Phase 2 | Step 2.1 adds the materials CDN tag before any JS rewrite begins |
| Sign convention stubs (+1) produce incorrect visual rotation direction | Phase 2 | Expected at this stage — the sign convention functions are the designated change point; incorrect signs do not break the architecture |
| `modelsets.json` published as read-only embedded resource | Phase 1 | Verify project file build action on Step 1.6 |
| `.glb` assets violate authoring constraints (wrong pivot, scale, orientation) | Phase 3 | Hard dependency on Model Authoring Process Guide and authoring checklist before Phase 3 begins |
| `getBoundingInfo()` returns degenerate bounds for multi-mesh `.glb` files | Phase 3 | Use `mesh.getHierarchyBoundingVectors()` as fallback if `getBoundingInfo()` returns zero extents |
| Stage 6 geometry addition breaks existing Stage 1–5 geometry | Phase 4 | Prototype addition is isolated below Stage 5; Phase 3 must be fully stable before Phase 4 begins |

---

## Deliverables Summary

| Phase | Primary deliverable | Secondary deliverable |
|---|---|---|
| Phase 1 | `ModelSetSettingsService` + `TelescopeStateModel` additions | `modelsets.json` (Prototype entry) |
| Phase 2 | Rewritten `telescopeViewer.js` (cylinder geometry, full interop) | Updated `TelescopeView.razor` |
| Pause | Model Authoring Process Guide | Validated `.glb` asset set |
| Phase 3 | `.glb` loading in `telescopeViewer.js` | Updated `modelsets.json` (real model set) |
| Phase 4 | Stage 6 in `telescopeViewer.js` + `Universal babylon.js` extension | Updated `modelsets.json` (counterpart examples) |

---

*End of document*
