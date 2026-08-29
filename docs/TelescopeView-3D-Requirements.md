# Telescope 3D View — Requirements
**Document status:** DRAFT v4 — awaiting Andy's review  
**Last updated:** 2026-08-29 12:17

---

## 1. Purpose and Scope

This document defines the requirements for the production implementation of the **Telescope 3D View** — a real-time, browser-rendered 3D representation of a physical telescope mount, hosted inside the GreenSwamp Alpaca Server Blazor application.

The 3D view visualises the mount's current mechanical position using live axis data from the mount controller. It replaces the current stub implementation in `telescopeViewer.js` with a geometrically accurate model driven by the prototype in `Universal babylon.js`.

---

## 2. System Context

| Component | Role |
|---|---|
| `TelescopeView.razor` | Blazor Server page (`/telescope-view/{DeviceNumber}`) that hosts the canvas and wires up JS interop |
| `telescopeViewer.js` | ES module (`wwwroot/js/`) that owns the Babylon.js engine, scene, model loading, and axis rotation |
| `TelescopeStateService` | Background service (250 ms tick) that fires `StateChanged` events with fresh mount snapshots |
| `TelescopeStateModel` | Snapshot DTO carrying all mount state passed from C# to JS via interop |
| `ModelSetSettingsService` | New C# service responsible for reading and writing `modelsets.json` (see §9) |
| `modelsets.json` | JSON configuration file mapping named model sets to `.glb` asset paths, per-set camera state, and counterpart element configuration |
| `Universal babylon.js` | Prototype — the sole authoritative reference for all geometric transformations and scene construction |

---

## 3. Reference Prototype

All geometric transformations in `Universal babylon.js` are **correct and must not be modified** in any implementation work. The production `telescopeViewer.js` must reproduce the same transformation chain, converted from the playground `createScene` function pattern into a proper ES module structure.

The prototype defines the following stages, extended to six for the production implementation:

- **Stage 1** — Vertical support pillar (fixed)
- **Stage 2** — Structural axis (tilted by observer latitude toward the celestial pole) — `latNode`
- **Stage 3** — Primary axis assembly (RA for EQ mounts; Az for AltAz) — `primaryNode` rotated by `primaryAngle`
- **Stage 4** — Secondary axis assembly (Dec for EQ mounts; Alt for AltAz) — `secondaryNode` rotated by `secondaryAngle`
- **Stage 5** — Telescope origin point and OTA tube — origin at `T0 = S0 + Yp_bab * o_s`
- **Stage 6** — *(New — not in prototype)* Counterpart element (secondary OTA or counterweight) — origin at `W0 = S0 - Yp_bab * o_w`, opposite end of the secondary axis from Stage 5; geometric code pattern identical to Stage 5

Rotation is applied every frame via `scene.onBeforeRenderObservable`, updating `primaryNode.rotationQuaternion` and `secondaryNode.rotationQuaternion` from the `primaryAngle` and `secondaryAngle` variables.

> **Prototype update required:** `Universal babylon.js` must be extended with a Stage 6 block mirroring the Stage 5 OTA code, using `W0` as the origin. This is the only permitted addition to the prototype geometry. See §8.3 for the detailed specification.

---

## 4. Coordinate System

The scene uses **Babylon.js right-handed coordinates** (`scene.useRightHandedSystem = true`) to match astronomical conventions, as established in the prototype:

| Axis | World direction |
|---|---|
| X | East |
| Y | Up (zenith) |
| Z | North |

This must be preserved in the production implementation.

---

## 5. Alignment Mode Support

The implementation must support all three ASCOM alignment modes without changes to the geometric transformation chain. The mode is selected by varying the `latitude` parameter passed to `init`:

| Alignment mode | Latitude passed to JS | Effect |
|---|---|---|
| `GermanPolar` | Observer's actual latitude | Polar axis tilted toward celestial pole |
| `Polar` | Observer's actual latitude | Same tilt, different geometry label |
| `AltAz` | `90.0` (fixed) | Structural axis vertical — degenerates to AltAz behaviour |

> **Key invariant:** The geometric code does not branch on alignment mode. AltAz behaviour is an emergent property of setting `latitude = 90`.

---

## 6. Data Sources and C# Changes

### 6.1 New Properties Required in `TelescopeStateModel`

The following properties must be added to `TelescopeStateModel` so the razor page can pass them as `init` parameters without taking a direct dependency on `IVersionedSettingsService`:

| Property | Type | Source | Notes |
|---|---|---|---|
| `SiteLatitude` | `double` | `SkySettings.Latitude` | Degrees, range -90 to +90 |
| `AlignmentMode` | `AlignmentMode` (enum) | `SkySettings.AlignmentMode` | `GermanPolar`, `Polar`, or `AltAz` |
| `MountType` | `MountType` (enum) | `SkySettings.Mount` | `Simulator` or `SkyWatcher` |

These must be populated in `TelescopeStateService.BuildSnapshot()`.

**Lifetime:** `SiteLatitude`, `AlignmentMode`, and `MountType` are read **once at `init` time** and are not refreshed on subsequent state ticks. The scene is constructed around the values supplied at initialisation.

> **V2 note:** A change-notification mechanism to detect settings changes that require the scene to be torn down and re-initialised is deferred to V2.

### 6.2 Axis Rotation Data

The mount's physical position is provided by:

| Property | Meaning | Units |
|---|---|---|
| `ActualAxisX` | Primary axis physical encoder position (RA for EQ; Az for AltAz) | Degrees |
| `ActualAxisY` | Secondary axis physical encoder position (Dec for EQ; Alt for AltAz) | Degrees |

`ActualAxisX` is always the primary axis and `ActualAxisY` is always the secondary axis for all connected mount types.

These are already present in `TelescopeStateModel`. The razor page passes them to the JS module on every `StateChanged` event. They are converted to radians inside the JS module before applying the sign convention (see §7.3).

### 6.3 No Fallback / No Alt-Az Override

There are no fallback coordinate sources. If `ActualAxisX` or `ActualAxisY` is `NaN` (mount disconnected or not yet valid), the model freezes silently at its last valid position. No error state or special visual is shown in V1.

### 6.4 New C# Service: `ModelSetSettingsService`

A new injectable C# service `ModelSetSettingsService` is responsible for:

- Reading `modelsets.json` at startup and on demand.
- Writing `modelsets.json` when the camera state is persisted on page dispose (see §11).
- Providing a typed model (`ModelSetCollection`) to avoid raw JSON handling in the razor page.

The service is registered as a singleton and is the single point of file I/O for `modelsets.json`. Thread safety must be considered as the razor page may call `SaveCameraStateAsync` from a Blazor circuit thread.

---

## 7. JavaScript Module Interface

`telescopeViewer.js` must be a standard ES module (using `export` keywords) compatible with Blazor JS interop via `IJSObjectReference`.

### 7.1 Exported Functions

#### `init(canvasId, latitude, alignmentMode, mountType, models, camera)`

Initialises the Babylon.js engine, creates the scene, loads the 3D models, restores the persisted camera state, and starts the render loop.

| Parameter | Type | Description |
|---|---|---|
| `canvasId` | `string` | DOM id of the `<canvas>` element |
| `latitude` | `number` | Observer latitude in degrees. Set to `90.0` for AltAz mounts (see §5) |
| `alignmentMode` | `string` | `"GermanPolar"`, `"Polar"`, or `"AltAz"` — passed to the sign convention functions (see §7.3) |
| `mountType` | `string` | `"Simulator"` or `"SkyWatcher"` — passed to the sign convention functions (see §7.3) |
| `models` | `object` | Resolved model set containing paths to the six `.glb` asset files (see §8 and §9) |
| `camera` | `object \| null` | Persisted camera state from `modelsets.json` (`{ alpha, beta, radius, target }`) or `null` to use prototype defaults |

If `camera` is `null` or any required field is missing, the prototype default camera values are used (alpha=135°, beta=60°, radius=300, target=(0,40,0)).

#### `setAxes(primaryDeg, secondaryDeg)`

Updates the two axis rotation angles. Called by the razor page on every `StateChanged` tick. Values are in degrees; the module converts to radians and applies the axis sign convention functions before storing.

| Parameter | Axis |
|---|---|
| `primaryDeg` | Primary axis — RA (EQ) or Azimuth (AltAz) — sourced from `ActualAxisX` |
| `secondaryDeg` | Secondary axis — Dec (EQ) or Altitude (AltAz) — sourced from `ActualAxisY` |

Calls where either value is `NaN` or `null` are silently ignored; the model holds its last valid position.

#### `getCameraState()`

Returns the current camera state as a plain object suitable for JSON serialisation:

```javascript
{
	alpha:  number,   // current camera.alpha (radians)
	beta:   number,   // current camera.beta (radians)
	radius: number,   // current camera.radius
	target: { x: number, y: number, z: number }
}
```

Called by the razor page when the user explicitly triggers a **Save View** action (see §10.5). Returns the current camera values for persistence via `ModelSetSettingsService`. May also be called before `dispose()` if a pending save is required during page teardown, but the primary trigger is the user action.

#### `dispose()`

Stops the render loop and releases all Babylon.js resources (engine, scene, textures, meshes). Safe to call even if `init` was never successfully completed.

### 7.2 Internal Render Loop

The module uses `engine.runRenderLoop(() => scene.render())` for continuous rendering. The `onBeforeRenderObservable` callback (matching the prototype's Stage 6 pattern) updates `primaryNode.rotationQuaternion` and `secondaryNode.rotationQuaternion` from the current `primaryAngle` and `secondaryAngle` module-level variables each frame.

### 7.3 Axis Sign Convention Functions

The mapping from raw encoder degrees to scene rotation direction is mount-dependent and may vary by hemisphere. Two internal (non-exported) functions encapsulate this uncertainty:

```javascript
function primaryAxisSign(latitude, alignmentMode, mountType) {
	// Returns +1 or -1 to correct the rotation direction of the primary axis.
	// Factors that may influence the sign in future versions:
	//   - Southern hemisphere (latitude < 0)
	//   - GEM pier side
	//   - Mount type (SkyWatcher vs Simulator)
	return +1;  // placeholder — correct value to be determined from hardware testing
}

function secondaryAxisSign(latitude, alignmentMode, mountType) {
	// Returns +1 or -1 to correct the rotation direction of the secondary axis.
	return +1;  // placeholder — correct value to be determined from hardware testing
}
```

Applied within `setAxes`:

```javascript
primaryAngle   = BABYLON.Tools.ToRadians(primaryDeg)   * primaryAxisSign(latitude, alignmentMode, mountType);
secondaryAngle = BABYLON.Tools.ToRadians(secondaryDeg) * secondaryAxisSign(latitude, alignmentMode, mountType);
```

> **Note:** These are the single designated change points for sign correction. The placeholder `+1` values will be replaced after hardware testing without any structural code change.

### 7.4 Removed Features (vs. prototype)

The following prototype features must **not** be present in the production module:

- `mountMode` toggle (`C` key)
- Arrow-key axis angle control (`ArrowLeft`, `ArrowRight`, `ArrowUp`, `ArrowDown` key handlers for mount rotation)

Arrow keys revert to standard Babylon.js `ArcRotateCamera` camera orbit behaviour.

---

## 8. 3D Model Assets

### 8.1 File Format

All model assets use the **`.glb`** format (binary glTF). This bundles geometry, materials, and textures into a single self-contained file.

### 8.2 Asset List

Six separate `.glb` files are required, one per structural stage:

| Asset key | Scene role | Prototype equivalent | Required? |
|---|---|---|---|
| `support` | Fixed vertical pillar | Stage 1 cylinder | Yes |
| `structural` | Structural axis / latitude-tilt element | Stage 2 cylinder | Yes |
| `primaryAxis` | Primary axis assembly (RA head / Az bearing) | Stage 3 cylinder | Yes |
| `secondaryAxis` | Secondary axis assembly (Dec cradle / Alt bearing) | Stage 4 cylinder | Yes |
| `ota` | Primary optical tube assembly | Stage 5 cylinder + sphere | Yes |
| `otaSecondary` **or** `counterWeight` | Secondary OTA or counterweight — at opposite end of secondary axis from primary OTA | Stage 6 *(new)* | **Optional** |

For the sixth element, the JSON key name in `modelsets.json` declares the type: use `otaSecondary` for a secondary telescope, or `counterWeight` for a counterweight bar/disc assembly. Only one of these keys should be non-null in any given model set.

### 8.3 Stage 6 Geometry — Counterpart Element

The sixth element is geometrically symmetric to the OTA (Stage 5) about the secondary axis midpoint `S0`:

| Item | Stage 5 (OTA) | Stage 6 (Counterpart) |
|---|---|---|
| Origin | `T0 = S0 + Yp_bab * o_s` | `W0 = S0 - Yp_bab * o_w` |
| Parent node | `secondaryNode` | `secondaryNode` |
| Position in parent space | `T0 - S0` | `W0 - S0` |
| Tube/body direction (`otaSecondary`) | `tubeDir = Xp_bab.scale(-1).normalize()` | Same as Stage 5 — secondary OTA points in the same direction as the primary OTA |
| Tube/body direction (`counterWeight`) | `tubeDir = Xp_bab.scale(-1).normalize()` | `counterpartDir = Yp_bab.scale(-1).normalize()` — counterweight extends directly away from `S0` along `-Yp_bab` |
| Alignment helper | `alignCylinderTo(tubeDir)` | `alignCylinderTo(counterpartDir)` — direction chosen based on asset key |
| Mesh parent | `tubeNode` / `counterpartNode` | Same pattern |

The JavaScript code structure for Stage 6 must be a direct analogue of the Stage 5 block, parameterised by `W0` and `o_w`. The offset `o_w` is read from the model's bounding box at load time (see §8.5). The direction used for `alignCylinderTo` and midpoint placement is chosen at runtime based on which key is present in the model set:

- **`otaSecondary`** → `counterpartDir = Xp_bab.scale(-1).normalize()` (same as primary OTA `tubeDir`)
- **`counterWeight`** → `counterpartDir = Yp_bab.scale(-1).normalize()` (directly away from `S0`)

### 8.4 Scale Convention

**1 Babylon.js scene unit = 1 millimetre (mm).**

All `.glb` files must be authored at real-world scale in millimetres.

### 8.5 Model Pivot and Orientation Constraints (Authoring Requirements)

Each asset has a strict authoring contract. These constraints allow the JS code to position and parent each asset correctly from bounding-box information alone.

| Constraint | Rule |
|---|---|
| **Pivot / mesh origin** | At the component's axis of rotation or connection point (per-asset details below) |
| **Local Y-axis orientation** | Rotation axis aligned with mesh's local +Y at rest pose |
| **Rest pose** | Exported with zero rotation baked in |

Per-asset pivot positions:

| Asset | Pivot location |
|---|---|
| `support` | Base of pillar at ground level; extends upward along local +Y |
| `structural` | Top of support pillar connection point (`M0`) |
| `primaryAxis` | Intersection with structural axis (`M1`) |
| `secondaryAxis` | Midpoint of primary axis span (`S0`) |
| `ota` | Telescope rotation point (`T0`); tube extends in the correct perpendicular direction |
| `otaSecondary` / `counterWeight` | Secondary axis opposite rotation point (`W0`); body extends away from `S0` |

### 8.6 Bounding-Box Auto-Derivation (No Preprocessing Required)

The JS module derives placement dimensions at runtime:

```javascript
const boundingInfo = mesh.getBoundingInfo();
const min = boundingInfo.minimum;
const max = boundingInfo.maximum;
```

Because scale and pivot constraints are met at authoring time, all required offsets (e.g. pillar height = `max.y - min.y`) are computable directly from the bounding box. **No offline preprocessing step or metadata sidecar file is required.**

> **Authoring checklist (mandatory before handing off a `.glb` file):**
> 1. Pivot at the rotation/connection point specified in §8.5.
> 2. Rotation axis aligns with local +Y at rest pose.
> 3. Exported with zero rotation baked in.
> 4. Scale is 1 unit = 1 mm.
> 5. File opens without errors in the Babylon.js sandbox (https://sandbox.babylonjs.com).

### 8.7 Load Failure Fallback

If any `.glb` file fails to load (path is `null` in the model set or HTTP error), the corresponding stage falls back to the prototype's cylinder geometry. A `null` path is a deliberate way to invoke the fallback (see `"Prototype"` model set in §9.3). If the `otaSecondary` / `counterWeight` key is absent or `null`, Stage 6 is simply not rendered — no fallback cylinder is shown.

### 8.8 Asset Location

Model files are served from `wwwroot/models/` in the Blazor Server project, organised into subdirectories per model set. Paths in `modelsets.json` are relative to the application base URL and resolved to absolute URLs by the razor page before being passed to `init()`.

---

## 9. 3D Model Settings

### 9.1 Purpose

`modelsets.json` maps **named model sets** to `.glb` asset paths, per-set camera state, and counterpart element type. It is the sole configuration file for the 3D view and is read and written at runtime by `ModelSetSettingsService`.

### 9.2 Settings File Location

```
wwwroot/models/modelsets.json
```

This file is served as a static file and is also writable at runtime by the Blazor Server process. This is appropriate for the target deployment scenario (locally-hosted observatory control application). The file must not be published as a read-only embedded resource.

### 9.3 JSON Structure

```json
{
  "activeModelSet": "SkyWatcherEQ6",
  "modelSets": [
	{
	  "name": "SkyWatcherEQ6",
	  "description": "SkyWatcher EQ6-R Pro with 80mm refractor",
	  "models": {
		"support":       "models/SkyWatcherEQ6/support.glb",
		"structural":    "models/SkyWatcherEQ6/structural.glb",
		"primaryAxis":   "models/SkyWatcherEQ6/primaryAxis.glb",
		"secondaryAxis": "models/SkyWatcherEQ6/secondaryAxis.glb",
		"ota":           "models/SkyWatcherEQ6/ota.glb",
		"counterWeight": "models/SkyWatcherEQ6/counterWeight.glb"
	  },
	  "camera": {
		"alpha":  2.356,
		"beta":   1.047,
		"radius": 300.0,
		"target": { "x": 0.0, "y": 40.0, "z": 0.0 }
	  }
	},
	{
	  "name": "SkyWatcherEQ6_DualScope",
	  "description": "SkyWatcher EQ6-R Pro with 80mm refractor and secondary guide scope",
	  "models": {
		"support":       "models/SkyWatcherEQ6/support.glb",
		"structural":    "models/SkyWatcherEQ6/structural.glb",
		"primaryAxis":   "models/SkyWatcherEQ6/primaryAxis.glb",
		"secondaryAxis": "models/SkyWatcherEQ6/secondaryAxis.glb",
		"ota":           "models/SkyWatcherEQ6/ota.glb",
		"otaSecondary":  "models/SkyWatcherEQ6/guideScope.glb"
	  },
	  "camera": null
	},
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

**Key notes:**

- `activeModelSet` — name of the model set loaded on `init`.
- Model paths are relative to the application base URL.
- A `null` path triggers the cylinder fallback for that stage (see §8.7).
- `otaSecondary` and `counterWeight` are mutually exclusive optional keys. If neither is present, Stage 6 is not rendered.
- `camera` — per-model-set persisted camera state. `null` means use prototype defaults. Written back by `ModelSetSettingsService` when the user saves the view via the Save View action (see §9.5).
- `"Prototype"` model set (all paths null, no counterpart) renders the full prototype cylinder scene.

### 9.4 Settings Loading

The razor page calls `ModelSetSettingsService.GetActiveModelSetAsync()` during `OnAfterRenderAsync`. The service returns the resolved model set including camera state. Model paths are resolved to absolute URLs using the Blazor `NavigationManager` base path.

### 9.5 Camera State Write-Back

Camera state is persisted when the user explicitly clicks the **Save View** button in the toolbar (see §10.5). The save sequence:

1. User clicks **Save View**.
2. The razor page calls `getCameraState()` on the JS module to retrieve current camera values.
3. The razor page calls `ModelSetSettingsService.SaveCameraStateAsync(modelSetName, cameraState)`.
4. The service updates the `camera` field of the named model set in memory and writes the entire `modelsets.json` back to disk atomically (write to a temp file, then replace).
5. A brief confirmation indicator is shown to the user (e.g. a transient MudSnackbar or icon state change).

Camera state is **not** saved automatically on page dispose or navigation away in V1.

> **V2 note:** In V2 a debounced auto-save on camera idle (triggered after the user stops orbiting) may replace or supplement the explicit save action.

### 9.6 "Current View" Scope

In V1 **camera state only** is persisted: `alpha`, `beta`, `radius`, and `target`. Visibility toggles for scene decorations (grid, world axes) are not persisted in V1.

### 9.7 V2: Settings UI

A UI for selecting the active model set and managing model set entries is **deferred to V2**. In V1 the JSON file is edited by hand.

---

## 10. Razor Page Changes (`TelescopeView.razor`)

### 10.1 Script Tags

- Babylon.js core CDN script tag: **retain** (pinned version)
- Babylon.js loaders CDN script tag: **retain** (required for `.glb` loading)

### 10.2 `OnAfterRenderAsync` (first render)

The initialisation sequence:

1. Call `ModelSetSettingsService.GetActiveModelSetAsync()` to get the resolved model set (paths + camera state).
2. Resolve model paths to absolute URLs using `NavigationManager`.
3. Read the initial `TelescopeStateModel` snapshot for `SiteLatitude`, `AlignmentMode`, `MountType`.
4. Compute `effectiveLatitude` = actual `SiteLatitude` if `AlignmentMode != AltAz`, else `90.0`.
5. Call `init(canvasId, effectiveLatitude, alignmentModeString, mountTypeString, modelsObject, cameraObject)`.
6. Subscribe to `StateService.StateChanged` and call `PushStateAsync()` for the initial position.

### 10.3 State Update Method

`PushRotationAsync` is renamed `PushStateAsync`. Its JS call changes from:
```csharp
await _module.InvokeVoidAsync("updateRotation", _alt, _az);
```
to:
```csharp
await _module.InvokeVoidAsync("setAxes", _axisX, _axisY);
```

Backing fields change from `_alt` / `_az` to `_axisX` / `_axisY`, populated from `state.ActualAxisX` / `state.ActualAxisY`.

### 10.4 `DisposeAsync` Sequence

`DisposeAsync` tears down the JS module. Camera state is **not** saved here — it is saved only via the explicit Save View user action (see §10.5). The pattern from the existing razor page stub is retained:

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

### 10.5 Toolbar

The toolbar is updated as follows:

| Element | Change |
|---|---|
| Axis 1 / Axis 2 readout | Replace current `Alt` / `Az` display with `ActualAxisX` (labelled **"Axis 1"**) and `ActualAxisY` (labelled **"Axis 2"**) |
| **Save View** button | New button added to the toolbar. On click: calls `getCameraState()` on the JS module, calls `ModelSetSettingsService.SaveCameraStateAsync()`, shows a brief confirmation indicator (e.g. MudSnackbar or icon flash) |

The Save View button is the sole mechanism for persisting camera state in V1.

---

## 11. Camera

### 11.1 Initial State

On `init`, the camera is initialised from the `camera` object passed from the model set's persisted state. If `camera` is `null` (no saved state, or `"Prototype"` model set), the prototype defaults are used:

| Setting | Default value |
|---|---|
| Alpha (horizontal orbit) | 135° (2.356 rad) |
| Beta (vertical orbit) | 60° (1.047 rad) |
| Radius | 300 units |
| Target | `(0, 40, 0)` |

### 11.2 Persistence

Camera state is persisted per model set in `modelsets.json`. The values are saved when the user clicks the **Save View** button (see §9.5 and §10.5). The next page load restores the saved state. Camera changes are lost on page navigation if the user has not clicked Save View.

### 11.3 Controls

| Control | Behaviour |
|---|---|
| Mouse orbit / touch | Enabled via `attachControl` |
| Arrow keys | Standard Babylon.js camera orbit (mount-control mode removed) |
| Scroll wheel | Zoom (radius change) |

---

## 12. Scene Decorations

The following scene elements from the prototype are retained in V1:

| Element | Requirement |
|---|---|
| Ground grid | Retained; `GridMaterial` with 400×400 unit extent |
| World axes (X/East, Y/Up, Z/North) | Retained; labelled with `DynamicTexture` text planes |
| Hemisphere light | Retained |

> **V2 note (ground plane):** Version 2 will replace the procedural grid with a custom ground plane that may be textured or photo-realistic and will include a compass rose.

> **V2 note (sky background):** Version 2 will implement a sky background that simulates a star field, replacing the flat `clearColor` background.

---

## 13. Mount State Display

Status overlay (Slewing, Tracking, AtPark, SideOfPier indicators on the canvas) is **deferred to V2**. No status-related rendering is required in V1.

---

## 14. Non-Functional Requirements

| ID | Requirement |
|---|---|
| NFR-1 | Render loop must maintain >= 30 fps on a mid-range workstation GPU when the mount is tracking |
| NFR-2 | `dispose()` must release all GPU resources without memory leaks; verified by Blazor page navigation away and back |
| NFR-3 | The module must not throw unhandled exceptions to the Blazor circuit; all JS errors must be caught and logged to the browser console |
| NFR-4 | `JSDisconnectedException` and `TaskCanceledException` caught in `PushStateAsync` and `DisposeAsync` (pattern already present; must be retained) |
| NFR-5 | The module must handle `window.resize` events and call `engine.resize()` to keep the canvas filling its container |
| NFR-6 | Touch input on the canvas must work for orbit/zoom (`attachControl` with touch; canvas `touch-action:none` style already set) |
| NFR-7 | `modelsets.json` write-back must be atomic (write to temp file, then replace) to prevent file corruption if the process is interrupted |

---

## 15. Out of Scope (V1)

| Item | Deferred to |
|---|---|
| Mount status overlay on the 3D canvas (Slewing, Tracking, AtPark, SideOfPier) | V2 |
| Settings UI for selecting and managing named model sets | V2 |
| Settings change notification / scene re-init on latitude or alignment mode change | V2 |
| Custom textured ground plane with compass rose | V2 |
| Simulated star field sky background | V2 |
| Debounced / auto-save camera state on camera idle | V2 |
| Persistence of scene decoration visibility (grid, world axes toggles) | V2 |
| Meridian flip visualisation | V2 |
| Slew path animation or trajectory rendering | V2 |
| Multiple simultaneous device views | V2 |

---

## 16. Open Questions

| # | Question | Owner | Status |
|---|---|---|---|
| OQ-5 | What are the correct sign values for `primaryAxisSign` and `secondaryAxisSign` for each combination of alignment mode, hemisphere (N/S), and mount type? | Andy — requires hardware testing | **Open** |

---

## 17. Resolved Questions

| # | Question | Resolution |
|---|---|---|
| OQ-1 | Scale unit | 1 Babylon unit = 1 mm (real-world millimetres) |
| OQ-2 | Bounding-box dimensions | Auto-derived at runtime via `mesh.getBoundingInfo()` — no preprocessing required; authoring constraints apply (see §8.5) |
| OQ-3 | File format | `.glb` only |
| OQ-4 | `ActualAxisX` / `ActualAxisY` axis assignment | `ActualAxisX` = primary axis; `ActualAxisY` = secondary axis for all mount types |
| OQ-6 | Re-read latitude/alignment on each tick? | No — read once at `init`. V2 will add change notification |
| OQ-7 | Stage 6 counterpart tube/body direction | `otaSecondary` uses same `tubeDir` as primary OTA (`Xp_bab.scale(-1)`). `counterWeight` extends along `-Yp_bab` away from `S0` |
| OQ-8 | What triggers camera state save? Is camera state alone sufficient scope? | Save triggered by explicit user **Save View** action. Camera state only (no decoration visibility) is sufficient for V1 |

---

*End of document*
