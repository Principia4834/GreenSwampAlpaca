// telescopeViewer.js — Babylon.js ES module for GreenSwamp Alpaca telescope 3D view
// Phase 2: cylinder prototype wired to real axis data. .glb loading deferred to Phase 3.
// Babylon.js core, materials library, and loaders loaded from CDN via script tags in TelescopeView.razor.
// DO NOT modify the geometric transformation chain — all transforms match the authoritative prototype.

// ── Module-level state ────────────────────────────────────────────────────────

let _engine = null;
let _scene = null;
let _primaryNode = null;   // Stage 3 — rotates by primaryAngle each frame
let _secondaryNode = null;   // Stage 4 — rotates by secondaryAngle each frame

let _primaryAngle = 0;     // radians, updated by setAxes()
let _secondaryAngle = 0;     // radians, updated by setAxes()

let _latitude = 51.5;   // observer latitude in degrees
let _alignmentMode = 'GermanPolar';
let _mountType = 'Simulator';

// Babylon coordinate vectors set during init, shared with render loop
let _Zp_bab = null;   // celestial pole direction in Babylon world space
let _Yp_bab = null;   // secondary axis rotation axis in Babylon world space

let _resizeHandler = null;   // kept so we can remove it on dispose

// ── Prototype default dimensions (mm scale — 1 unit = 1 mm) ──────────────────
// Representative cylinder proportions for the prototype.
// Replaced by bounding-box-derived values in Phase 3.

const PILLAR_HEIGHT = 80;    // Stage 1 support pillar height
const PILLAR_RADIUS = 5;      // Stage 1 support pillar radius

const STRUCT_LENGTH = 20;     // Stage 2 structural axis length
const STRUCT_RADIUS = 1.5;    // Stage 2 structural axis radius

const PRIMARY_LENGTH = 30;    // Stage 3 primary axis span
const PRIMARY_RADIUS = 1.5;   // Stage 3 primary axis radius

const SECONDARY_LENGTH = 25;  // Stage 4 secondary axis span
const SECONDARY_RADIUS = 1.5; // Stage 4 secondary axis radius

const OTA_LENGTH = 40;      // Stage 5 OTA tube length
const OTA_RADIUS = 3;         // Stage 5 OTA tube radius
const OTA_SPHERE_R = 2;       // Stage 5 OTA origin sphere radius

// Stage connection offsets
const o_m0 = PILLAR_HEIGHT;         // M0: top of support pillar
const o_m1 = STRUCT_LENGTH;         // M1: tip of structural axis from M0
const o_s = 15;                  // S0 to telescope origin

// ── Exported public API ───────────────────────────────────────────────────────

/**
 * Initialise the Babylon.js scene.
 * @param {string}      canvasId      - id of the <canvas> element
 * @param {number}      latitude      - observer latitude in degrees (pass 90.0 for AltAz)
 * @param {string}      alignmentMode - 'GermanPolar' | 'Polar' | 'AltAz'
 * @param {string}      mountType     - 'Simulator' | 'SkyWatcher'
 * @param {object|null} models        - model path object from modelsets.json (ignored Phase 2)
 * @param {object|null} camera        - persisted camera state { alpha, beta, radius, target } or null
 */
export function init(canvasId, latitude, alignmentMode, mountType, models, camera) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) {
        console.error('telescopeViewer: canvas not found:', canvasId);
        return;
    }

    _latitude = isFinite(latitude) ? latitude : 51.5;
    _alignmentMode = alignmentMode || 'GermanPolar';
    _mountType = mountType || 'Simulator';

    _engine = new BABYLON.Engine(canvas, true, { preserveDrawingBuffer: true, stencil: true });
    _scene = new BABYLON.Scene(_engine);
    _scene.useRightHandedSystem = true;
    _scene.clearColor = new BABYLON.Color4(0.08, 0.08, 0.12, 1);

    // ── Camera ────────────────────────────────────────────────────────────────
    // Defaults: alpha=135°, beta=60°, radius=300 mm, target=(0,40,0)
    const defAlpha = 3 * Math.PI / 4;
    const defBeta = Math.PI / 3;
    const defRadius = 300;
    const defTarget = new BABYLON.Vector3(0, 40, 0);

    const cam = new BABYLON.ArcRotateCamera('cam', defAlpha, defBeta, defRadius, defTarget, _scene);

    if (camera && isFinite(camera.alpha) && isFinite(camera.beta) && isFinite(camera.radius)) {
        cam.alpha = camera.alpha;
        cam.beta = camera.beta;
        cam.radius = camera.radius;
        if (camera.target) {
            cam.target = new BABYLON.Vector3(
                camera.target.x ?? 0,
                camera.target.y ?? 40,
                camera.target.z ?? 0
            );
        }
    }

    cam.attachControl(canvas, true);
    cam.lowerRadiusLimit = 50;
    cam.upperRadiusLimit = 2000;

    // ── Lighting ──────────────────────────────────────────────────────────────
    new BABYLON.HemisphericLight('hemi', new BABYLON.Vector3(0, 1, 0), _scene);

    // ── Scene decorations ─────────────────────────────────────────────────────
    _buildGrid();
    _buildWorldAxes(120);

    // ── Geometry ──────────────────────────────────────────────────────────────
    _buildScene();

    // ── Render loop ───────────────────────────────────────────────────────────
    _engine.runRenderLoop(() => { if (_scene) _scene.render(); });

    _resizeHandler = () => { if (_engine) _engine.resize(); };
    window.addEventListener('resize', _resizeHandler);
}

/**
 * Called from Blazor on every StateChanged tick.
 * @param {number} primaryDeg   - ActualAxisX in degrees
 * @param {number} secondaryDeg - ActualAxisY in degrees
 */
export function setAxes(primaryDeg, secondaryDeg) {
    if (!isFinite(primaryDeg) || !isFinite(secondaryDeg)) return;
    _primaryAngle = BABYLON.Tools.ToRadians(primaryDeg) * primaryAxisSign(_latitude, _alignmentMode, _mountType);
    _secondaryAngle = BABYLON.Tools.ToRadians(secondaryDeg) * secondaryAxisSign(_latitude, _alignmentMode, _mountType);
}

/**
 * Returns the current ArcRotateCamera state for Save View persistence.
 * @returns {{ alpha, beta, radius, target: { x, y, z } } | null}
 */
export function getCameraState() {
    if (!_scene) return null;
    const cam = _scene.activeCamera;
    if (!cam) return null;
    return {
        alpha: cam.alpha,
        beta: cam.beta,
        radius: cam.radius,
        target: { x: cam.target.x, y: cam.target.y, z: cam.target.z }
    };
}

export function dispose() {
    if (_resizeHandler) {
        window.removeEventListener('resize', _resizeHandler);
        _resizeHandler = null;
    }
    if (_engine) _engine.stopRenderLoop();
    if (_scene) { _scene.dispose(); _scene = null; }
    if (_engine) { _engine.dispose(); _engine = null; }
    _primaryNode = null;
    _secondaryNode = null;
    _Zp_bab = null;
    _Yp_bab = null;
}

// ── Sign convention stubs ─────────────────────────────────────────────────────
// Return +1 until hardware testing determines correct sign for each
// combination of alignment mode, hemisphere, and mount type (OQ-5).

function primaryAxisSign(latitude, alignmentMode, mountType) { return +1; }
function secondaryAxisSign(latitude, alignmentMode, mountType) { return +1; }

// ── Scene construction ────────────────────────────────────────────────────────

function _buildScene() {
    const theta = BABYLON.Tools.ToRadians(90 - _latitude);

    const X_astro = Math.cos(theta);
    const Y_astro = Math.sin(theta);
    const Z_astro = 0;

    const Xp_bab = new BABYLON.Vector3(
        Z_astro,
        Y_astro,
        X_astro
    ).normalize();

    // ── Stage 1 — Support pillar + structural axis ───────────────────────────
    const pillarNode = new BABYLON.TransformNode('pillarNode', _scene);
    pillarNode.position = new BABYLON.Vector3(0, o_m0 / 2, 0);
    const pillarMesh = _makeCylinder('pillar', PILLAR_RADIUS, PILLAR_HEIGHT, pillarNode);
    _setColor(pillarMesh, new BABYLON.Color3(0.4, 0.4, 0.45));

    const M0 = new BABYLON.Vector3(0, o_m0, 0);
    const M1 = M0.add(Xp_bab.scale(o_m1));

    const latNode = new BABYLON.TransformNode('latNode', _scene);
    latNode.position = M0.add(Xp_bab.scale(o_m1 / 2));
    latNode.rotationQuaternion = quaternionAlignTo(Xp_bab);

    const structMesh = _makeCylinder('structural', STRUCT_RADIUS, STRUCT_LENGTH, latNode);
    _setColor(structMesh, new BABYLON.Color3(0.0, 0.0, 0.0));

    // ── Stage 2 — Primary axis (RA/Az) ───────────────────────────────────────
    const Xp_astro_vec = new BABYLON.Vector3(X_astro, Y_astro, Z_astro);
    const Up_astro = new BABYLON.Vector3(0, 1, 0);

    const Zp_astro = Up_astro.subtract(
        Xp_astro_vec.scale(BABYLON.Vector3.Dot(Up_astro, Xp_astro_vec))
    ).normalize();

    _Zp_bab = new BABYLON.Vector3(
        Zp_astro.z,
        Zp_astro.y,
        Zp_astro.x
    ).normalize();

    _primaryNode = new BABYLON.TransformNode('primaryNode', _scene);
    _primaryNode.position = M1;

    const primaryAxisNode = new BABYLON.TransformNode('primaryAxisNode', _scene);
    primaryAxisNode.parent = _primaryNode;
    primaryAxisNode.rotationQuaternion = quaternionAlignTo(_Zp_bab);

    const primaryMesh = _makeCylinder('primaryAxis', PRIMARY_RADIUS, PRIMARY_LENGTH, primaryAxisNode);
    _setColor(primaryMesh, new BABYLON.Color3(1.0, 0.0, 0.0));

    // ── Stage 3 — Secondary axis (Dec/Alt) ───────────────────────────────────
    const Yp_astro = BABYLON.Vector3.Cross(Zp_astro, Xp_astro_vec).normalize();

    _Yp_bab = new BABYLON.Vector3(
        Yp_astro.z,
        Yp_astro.y,
        Yp_astro.x
    ).normalize();

    const S0 = M1.add(_Zp_bab.scale(PRIMARY_LENGTH / 2));

    _secondaryNode = new BABYLON.TransformNode('secondaryNode', _scene);
    _secondaryNode.parent = _primaryNode;
    _secondaryNode.position = S0.subtract(_primaryNode.position);

    const secondaryAxisNode = new BABYLON.TransformNode('secondaryAxisNode', _scene);
    secondaryAxisNode.parent = _secondaryNode;
    secondaryAxisNode.rotationQuaternion = quaternionAlignTo(_Yp_bab);

    const secondaryMesh = _makeCylinder('secondaryAxis', SECONDARY_RADIUS, SECONDARY_LENGTH, secondaryAxisNode);
    _setColor(secondaryMesh, new BABYLON.Color3(0.0, 0.0, 1.0));

    // ── Stage 4 — Telescope origin + tube ─────────────────────────────────────
    const T0 = S0.add(_Yp_bab.scale(o_s));

    const otaSphere = BABYLON.MeshBuilder.CreateSphere('otaSphere',
        { diameter: OTA_SPHERE_R * 2 }, _scene);
    otaSphere.parent = _secondaryNode;
    otaSphere.position = T0.subtract(S0);
    _setColor(otaSphere, new BABYLON.Color3(1, 1, 0));

    const tubeDir = Xp_bab.scale(-1).normalize();
    const tubeCenter = T0;

    const tubeNode = new BABYLON.TransformNode('tubeNode', _scene);
    tubeNode.parent = _secondaryNode;

    // Position at centre
    tubeNode.position = tubeCenter.subtract(S0);
    tubeNode.rotationQuaternion = quaternionAlignTo(tubeDir);

    const otaTube = _makeCylinder('ota', OTA_RADIUS, OTA_LENGTH, tubeNode);
    _setColor(otaTube, new BABYLON.Color3(0.8, 0.8, 0.8));

    // ── Render loop: apply axis rotations via quaternions each frame ──────────
    // Matches prototype's onBeforeRenderObservable pattern exactly.
    _scene.onBeforeRenderObservable.add(() => {
        if (_primaryNode) {
            _primaryNode.rotationQuaternion = BABYLON.Quaternion.RotationAxis(_Zp_bab, _primaryAngle);
        }
        if (_secondaryNode) {
            _secondaryNode.rotationQuaternion = BABYLON.Quaternion.RotationAxis(_Yp_bab, _secondaryAngle);
        }
    });
}

// ── Scene decoration helpers ──────────────────────────────────────────────────

function _buildGrid() {
    const ground = BABYLON.MeshBuilder.CreateGround('grid',
        { width: 400, height: 400, subdivisions: 40 }, _scene);

    const gridMat = new BABYLON.GridMaterial('gridMat', _scene);
    gridMat.majorUnitFrequency = 5;
    gridMat.minorUnitVisibility = 0.45;
    gridMat.gridRatio = 1;
    gridMat.backFaceCulling = false;
    gridMat.mainColor = new BABYLON.Color3(0.7, 0.7, 0.7);
    gridMat.lineColor = new BABYLON.Color3(0.4, 0.4, 0.4);
    gridMat.opacity = 0.7;
    ground.material = gridMat;
    ground.position.y = -1;
}

function _buildWorldAxes(size) {
    _makeAxisLine('axisX', BABYLON.Vector3.Zero(), new BABYLON.Vector3(size, 0, 0),
        new BABYLON.Color3(1, 0.2, 0.2), 'E');
    _makeAxisLine('axisY', BABYLON.Vector3.Zero(), new BABYLON.Vector3(0, size, 0),
        new BABYLON.Color3(0.2, 1, 0.2), 'U');
    _makeAxisLine('axisZ', BABYLON.Vector3.Zero(), new BABYLON.Vector3(0, 0, -size),
        new BABYLON.Color3(0.2, 0.4, 1), 'N');
}

function _makeAxisLine(name, from, to, color, label) {
    const axis = BABYLON.MeshBuilder.CreateLines(name, { points: [from, to] }, _scene);
    axis.color = color;

    const plane = BABYLON.MeshBuilder.CreatePlane(name + 'Label', { size: 18 }, _scene);
    plane.position = to.scale(1.15);
    plane.billboardMode = BABYLON.Mesh.BILLBOARDMODE_ALL;

    const tex = new BABYLON.DynamicTexture(name + 'Tex', { width: 64, height: 64 }, _scene, false);
    tex.drawText(label, null, 48, 'bold 48px Arial',
        `rgb(${Math.round(color.r * 255)},${Math.round(color.g * 255)},${Math.round(color.b * 255)})`,
        'transparent', true);

    const mat = new BABYLON.StandardMaterial(name + 'TexMat', _scene);
    mat.diffuseTexture = tex;
    mat.emissiveTexture = tex;
    mat.backFaceCulling = false;
    mat.disableLighting = true;
    plane.material = mat;
}

// ── Geometry helpers ──────────────────────────────────────────────────────────

function _makeCylinder(name, radius, height, parent) {
    const mesh = BABYLON.MeshBuilder.CreateCylinder(name,
        { diameter: radius * 2, height, tessellation: 16 }, _scene);
    mesh.parent = parent;
    return mesh;
}

function _setColor(mesh, color3) {
    const mat = new BABYLON.StandardMaterial(mesh.name + 'Mat', _scene);
    mat.diffuseColor = color3;
    mesh.material = mat;
}

function quaternionAlignTo(dir) {
    const Y = BABYLON.Vector3.Up();
    const normalized = dir.normalize();
    const dot = BABYLON.Vector3.Dot(Y, normalized);
    const eps = 1e-6;

    if (Math.abs(dot - 1) < eps) {
        return BABYLON.Quaternion.Identity();
    }

    if (Math.abs(dot + 1) < eps) {
        return BABYLON.Quaternion.RotationAxis(
            new BABYLON.Vector3(1, 0, 0), Math.PI
        );
    }

    const axis = Y.cross(normalized);
    const angle = Math.acos(dot);
    return BABYLON.Quaternion.RotationAxis(axis.normalize(), angle);
}
