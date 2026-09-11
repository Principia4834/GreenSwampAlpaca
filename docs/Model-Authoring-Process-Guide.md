# Telescope 3D View — Model Authoring Process Guide
**Document status:** DRAFT v1 — awaiting Andy's review  
**Last updated:** 2026-08-29 18:04  
**Requirements references:** `docs/TelescopeView-3D-Requirements.md` (§8.1–§8.8, §9.3) and `docs/TelescopeView-3D-ImplementationPlan.md` (Pause before Phase 3)

---

## 1. Purpose

This guide defines the production process for creating `.glb` assets for the Telescope 3D View from 2D references. It is a handoff guide focused on **authoring contract compliance** (pivot, orientation, scale, rest pose) so the runtime placement logic can derive geometry from bounding boxes without sidecar metadata.

---

## 2. Scope and Deliverables

### 2.1 Minimum deliverable before Phase 3

- One complete validated five-asset set:
  - `support.glb`
  - `structural.glb`
  - `primaryAxis.glb`
  - `secondaryAxis.glb`
  - `ota.glb`
- Optional sixth asset (choose one):
  - `otaSecondary.glb` **or** `counterWeight.glb`
- `modelsets.json` entry pointing at the generated files.

### 2.2 Out of scope for this guide

- Runtime JavaScript implementation details.
- Sign convention tuning from hardware tests.

---

## 3. Non-Negotiable Authoring Contract

These constraints are mandatory for every exported `.glb`:

1. **Scale:** `1 scene unit = 1 mm`.
2. **Pivot/Origin:** At the defined mechanical rotation or connection point.
3. **Local axis orientation:** Rotation axis aligned with local `+Y` in rest pose.
4. **Rest pose export:** Zero baked rotation at export.
5. **Babylon validation:** File opens cleanly in `https://sandbox.babylonjs.com`.

If any rule fails, the asset is rejected for integration.

---

## 4. Asset Specification Table

| Asset key | Functional role | Required pivot location | Expected extension from pivot |
|---|---|---|---|
| `support` | Stage 1 fixed pillar | Base at ground level | Upward along local `+Y` |
| `structural` | Stage 2 latitude element | Support connection point `M0` | Along intended structural body axis |
| `primaryAxis` | Stage 3 primary axis assembly | Axis intersection `M1` | Across primary axis span |
| `secondaryAxis` | Stage 4 secondary assembly | Midpoint `S0` | Across secondary axis span |
| `ota` | Stage 5 optical tube | Telescope rotation point `T0` | Tube body away from `T0` in designed direction |
| `otaSecondary` / `counterWeight` | Stage 6 counterpart (optional) | Opposite point `W0` | Away from `S0` per counterpart type |

---

## 5. Recommended Toolchain Pattern

Because Andy is already strong in CAD/CAM workflows, use a two-stage flow:

1. **Mechanical authoring stage (CAD or precision modeling tool)**
   - Build from dimensioned references.
   - Keep units in mm from the first sketch.
2. **Export/validation stage (DCC + Babylon sandbox check)**
   - Confirm pivot/orientation/rest-pose compliance.
   - Export `.glb` and validate bounds/origin in Babylon.

Suggested evaluation candidates (pick one as the team baseline):

- Blender (strong glTF export and pivot controls)
- Onshape/Fusion/FreeCAD + glTF-capable conversion path
- Browser-first modeling tools with reliable glTF export and transform control

Selection criteria:

- Explicit mm unit support
- Reliable origin/pivot editing
- Reliable “apply/freeze transforms” workflow
- Deterministic `.glb` export (no hidden axis remap surprises)

---

## 6. 2D Image to 3D Authoring Workflow

### Step A — Prepare references

- Gather front/side/top images where possible.
- Identify at least one trusted physical dimension for scaling.
- Create a per-asset dimension sheet (key lengths, diameters, offsets).

### Step B — Calibrate scale

- Import reference image(s) into modeling space.
- Scale references to mm using known dimension.
- Lock reference planes to avoid accidental scaling later.

### Step C — Blockout geometry first

- Build each asset with simple primitives first.
- Validate proportion and connection points (`M0`, `M1`, `S0`, `T0`, `W0`) before detailing.

### Step D — Place pivot/origin early

- Move origin/pivot to the required connection/rotation point before detail work.
- Keep an explicit construction marker object at pivot point until final QA.

### Step E — Align local axes

- Rotate object in authoring tool so intended rotation axis is local `+Y` at rest.
- Apply/freeze transforms so exported rotation is zero.

### Step F — Detail and clean

- Add detail after compliance is locked.
- Remove hidden helper meshes before export, or isolate export collections cleanly.

---

## 7. Export Profile (Draft Baseline)

Define one shared export profile and reuse it for every asset:

- Format: `.glb`
- Unit system preserved in mm (no scale baking to meters)
- Apply transforms: enabled only when it preserves zero-rotation rest pose
- Include only intended render meshes (exclude helper geometry)
- Materials/textures embedded as required

> Final per-tool option names must be captured after toolchain selection (see Research Backlog).

---

## 8. Babylon Sandbox Verification Procedure

For each exported `.glb`:

1. Open `https://sandbox.babylonjs.com`.
2. Load the asset.
3. Confirm no import error or warning affecting geometry.
4. Verify origin visually (gizmo at required pivot location).
5. Verify orientation by test-rotating around local `+Y`.
6. Verify bounding box is non-degenerate and aligned with expected extents.
7. Record pass/fail in the checklist table.

---

## 9. Acceptance Checklist (Per File)

Use this checklist for handoff:

- [ ] Pivot is at required mechanical point (§4)
- [ ] Local `+Y` is the rotation axis at rest
- [ ] Exported rest pose has zero rotation baked in
- [ ] Scale is mm (`1 unit = 1 mm`)
- [ ] Babylon sandbox import succeeds
- [ ] Bounding box extents are sensible for runtime derivation
- [ ] File name follows required key naming
- [ ] File placed under `wwwroot/models/{ModelSetName}/`

An asset is integration-ready only when all boxes are checked.

---

## 10. Integration Handoff Steps

1. Copy exported files to:
   - `GreenSwamp.Alpaca.Server/wwwroot/models/{ModelSetName}/`
2. Add or update model set entry in `wwwroot/models/modelsets.json`.
3. Set `activeModelSet` for integration testing.
4. Validate with three runtime cases:
   - Prototype all-null fallback
   - Full `.glb` model set
   - Partial null fallback

---

## 11. Research Backlog to Finalize This Guide

Before this guide can be marked READY:

1. **Toolchain decision**
   - Select the official authoring/export stack.
2. **Exporter option mapping**
   - Document exact UI options for pivot handling, transform application, and `.glb` output.
3. **Axis-conversion verification**
   - Confirm exporter does not introduce coordinate flips for Babylon right-handed usage.
4. **Multi-mesh bounds behavior**
   - Define policy for separate meshes vs merged meshes when bounds become ambiguous.
5. **Golden sample set**
   - Produce one fully validated five-asset reference set and keep it for regression checks.

---

## 12. Suggested Next Actions

1. Choose one official toolchain for v1.
2. Produce `support.glb` first as the calibration asset.
3. Run full checklist and refine guide wording from real findings.
4. Repeat for remaining Stage 1–5 assets.
5. Add optional Stage 6 counterpart asset once Phase 3 gate is passed.
